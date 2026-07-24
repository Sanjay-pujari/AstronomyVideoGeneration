using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public interface IAstronomyFamilyProfileResolver
{
    AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input);
    AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity);
}

public sealed class AstronomyFamilyProfileResolver : IAstronomyFamilyProfileResolver
{
    private readonly ICanonicalAstronomyEventIdentityResolverV1 _identityResolver;
    private readonly IAstronomyFamilyProfileCatalogV1 _familyCatalog;
    private readonly IAstronomyFamilyProfileV1CompatibilityAdapter _compatibilityAdapter;

    public AstronomyFamilyProfileResolver()
        : this(new CanonicalAstronomyEventIdentityResolverV1(), new AstronomyFamilyProfileCatalogV1(), new AstronomyFamilyProfileV1CompatibilityAdapter()) { }

    public AstronomyFamilyProfileResolver(ICanonicalAstronomyEventIdentityResolverV1 identityResolver, IAstronomyFamilyProfileCatalogV1 familyCatalog, IAstronomyFamilyProfileV1CompatibilityAdapter compatibilityAdapter)
    { _identityResolver = identityResolver; _familyCatalog = familyCatalog; _compatibilityAdapter = compatibilityAdapter; }

    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(AstronomyFamilyProfileResolutionInput input)
    {
        var bridge = ResolveAuthoritativeEventType(input);
        if (string.IsNullOrWhiteSpace(bridge.EventType))
            throw new InvalidOperationException("Canonical astronomy event identity input is missing. Inspected source fields: " + string.Join("; ", bridge.InspectedSources.Select(s => $"{s.Key}={(string.IsNullOrWhiteSpace(s.Value) ? "<missing>" : s.Value)}")));

        var v1Identity = _identityResolver.Resolve(bridge.EventType, bridge.Source);
        return ResolveFamilyProfile(v1Identity, bridge.EventType);
    }

    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity)
    {
        if (identity.BlockingErrors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", identity.BlockingErrors));
        if (string.IsNullOrWhiteSpace(identity.EventType))
            throw new InvalidOperationException("Canonical event identity missing. No event type was present in inspected sources.");

        var hasCanonicalIdentityFields = !string.IsNullOrWhiteSpace(identity.EventFamily) && !string.IsNullOrWhiteSpace(identity.StrategyId);
        var v1Identity = hasCanonicalIdentityFields
            ? new CanonicalAstronomyEventIdentity(
                identity.SourceEventType ?? identity.EventType,
                identity.EventType,
                identity.EventFamily,
                identity.StrategyId,
                identity.ResolutionSource,
                identity.AliasApplied && !string.IsNullOrWhiteSpace(identity.SourceEventType) ? [identity.SourceEventType] : [],
                true,
                [])
            : _identityResolver.Resolve(identity.EventType, identity.ResolutionSource);
        return ResolveFamilyProfile(v1Identity, identity.SourceEventType ?? identity.EventType);
    }

    private AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalAstronomyEventIdentity v1Identity, string? sourceEventType)
    {
        if (!v1Identity.Supported)
            throw new InvalidOperationException(string.Join("; ", v1Identity.DiagnosticMessages.DefaultIfEmpty($"Unsupported astronomy event type '{v1Identity.InputEventType}'.")));
        if (string.IsNullOrWhiteSpace(v1Identity.CanonicalEventType))
            throw new InvalidOperationException($"Canonical astronomy event type is missing for source event type '{sourceEventType ?? v1Identity.InputEventType}'.");

        var familyResolution = _familyCatalog.ResolveEventType(v1Identity.CanonicalEventType);
        if (familyResolution.Status == AstronomyFamilyResolutionStatusV1.FutureFamily)
            throw new InvalidOperationException($"Future astronomy family is not active in current runtime: {familyResolution.CanonicalFamilyId}");
        if (familyResolution.Status != AstronomyFamilyResolutionStatusV1.Resolved || string.IsNullOrWhiteSpace(familyResolution.ProfileId))
            throw new InvalidOperationException($"V1 astronomy family profile not found: {v1Identity.CanonicalFamily}");
        if (!_familyCatalog.TryGet(familyResolution.ProfileId, out var v1Profile))
            throw new InvalidOperationException($"V1 astronomy family profile not found: {familyResolution.CanonicalFamilyId}");

        var compatibility = _compatibilityAdapter.Convert(v1Profile, new(sourceEventType ?? v1Identity.InputEventType, v1Identity.CanonicalEventType, familyResolution.CanonicalFamilyId, v1Identity.AppliedAliases.Count > 0 || familyResolution.AliasApplied));
        if (!compatibility.Succeeded || compatibility.LegacyProfile is null)
            throw new InvalidOperationException($"Unable to convert V1 family profile '{v1Profile.FamilyId}' to legacy runtime profile:\n{string.Join("; ", compatibility.BlockingErrors)}");

        var resolved = new ResolvedFamilyProfile(familyResolution.CanonicalFamilyId!, familyResolution.ProfileId, $"{v1Identity.ResolutionSource}+V1", compatibility.Diagnostics.AliasApplied, compatibility.Diagnostics.AliasApplied ? $"Normalized {compatibility.Diagnostics.InputEventType} to {compatibility.Diagnostics.CanonicalEventType}." : null, v1Profile.ProfileVersion);
        return new AstronomyFamilyProfileResolutionResult(compatibility.LegacyProfile, resolved, compatibility.Diagnostics);
    }

    private static FamilyProfileInputBridgeResult ResolveAuthoritativeEventType(AstronomyFamilyProfileResolutionInput input)
    {
        var inspected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(AstronomyFamilyProfileResolutionInput.EventType)] = Clean(input.EventType),
            ["ProductionEventIntelligence.eventType"] = Clean(GetString(input.ProductionEventIntelligence, "eventType")),
            ["EditorialContract.eventType"] = Clean(GetString(input.EditorialContract, "eventType")),
            ["CreativeStoryboard.eventType"] = Clean(GetString(input.CreativeStoryboard, "eventType")),
            ["LongDocumentaryContract.eventType"] = Clean(GetString(input.LongDocumentaryContract, "eventType")),
            ["ShortDocumentaryContract.eventType"] = Clean(GetString(input.ShortDocumentaryContract, "eventType")),
            ["EditorialContract.family"] = Clean(GetString(input.EditorialContract, "family")),
            [nameof(AstronomyFamilyProfileResolutionInput.ContentCategory)] = Clean(input.ContentCategory),
            [nameof(AstronomyFamilyProfileResolutionInput.DocumentaryArchetype)] = Clean(input.DocumentaryArchetype),
            [nameof(AstronomyFamilyProfileResolutionInput.ObservationMode)] = Clean(input.ObservationMode)
        };

        foreach (var key in new[] { nameof(AstronomyFamilyProfileResolutionInput.EventType), "ProductionEventIntelligence.eventType", "EditorialContract.eventType", "CreativeStoryboard.eventType", "LongDocumentaryContract.eventType", "ShortDocumentaryContract.eventType", "EditorialContract.family", nameof(AstronomyFamilyProfileResolutionInput.ContentCategory), nameof(AstronomyFamilyProfileResolutionInput.DocumentaryArchetype), nameof(AstronomyFamilyProfileResolutionInput.ObservationMode) })
        {
            if (!string.IsNullOrWhiteSpace(inspected[key]))
                return new(inspected[key], key, inspected);
        }

        return new(null, "Missing", inspected);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? GetString(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e) return null;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? p.Value.GetRawText() : null;
        return null;
    }

    private sealed record FamilyProfileInputBridgeResult(string? EventType, string Source, IReadOnlyDictionary<string, string?> InspectedSources);
}
