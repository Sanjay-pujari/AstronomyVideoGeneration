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
        var v1Identity = _identityResolver.Resolve(input.EventType, nameof(AstronomyFamilyProfileResolutionInput));
        return ResolveFamilyProfile(v1Identity, input.EventType);
    }

    public AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalEventIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.EventType))
            throw new InvalidOperationException("Canonical event identity missing. " + string.Join("; ", identity.BlockingErrors.DefaultIfEmpty("No event type was present in inspected sources.")));

        var v1Identity = _identityResolver.Resolve(identity.EventType, identity.ResolutionSource);
        return ResolveFamilyProfile(v1Identity, identity.SourceEventType ?? identity.EventType);
    }

    private AstronomyFamilyProfileResolutionResult ResolveFamilyProfile(CanonicalAstronomyEventIdentity v1Identity, string? sourceEventType)
    {
        if (!v1Identity.Supported || string.IsNullOrWhiteSpace(v1Identity.CanonicalProfile))
            throw new InvalidOperationException($"Unsupported astronomy event type: {v1Identity.InputEventType}. V1 diagnostic: {string.Join("; ", v1Identity.DiagnosticMessages)}");

        var familyResolution = _familyCatalog.ResolveEventType(v1Identity.CanonicalProfile);
        if (familyResolution.Status == AstronomyFamilyResolutionStatusV1.FutureFamily)
            throw new InvalidOperationException($"Future astronomy family is not active in current runtime: {familyResolution.CanonicalFamilyId}");
        if (familyResolution.Status != AstronomyFamilyResolutionStatusV1.Resolved || string.IsNullOrWhiteSpace(familyResolution.ProfileId))
            throw new InvalidOperationException($"Unsupported astronomy event type: {v1Identity.InputEventType}. {familyResolution.Diagnostic}");
        if (!_familyCatalog.TryGet(familyResolution.ProfileId, out var v1Profile))
            throw new InvalidOperationException($"V1 astronomy family profile '{familyResolution.ProfileId}' was not registered.");

        var compatibility = _compatibilityAdapter.Convert(v1Profile, new(sourceEventType ?? v1Identity.InputEventType, v1Identity.CanonicalEventType, familyResolution.CanonicalFamilyId, v1Identity.AppliedAliases.Count > 0 || familyResolution.AliasApplied));
        if (!compatibility.Succeeded || compatibility.LegacyProfile is null)
            throw new InvalidOperationException("Astronomy family V1 compatibility failed: " + string.Join("; ", compatibility.BlockingErrors));

        var resolved = new ResolvedFamilyProfile(familyResolution.CanonicalFamilyId!, familyResolution.ProfileId, $"{v1Identity.ResolutionSource}+V1", compatibility.Diagnostics.AliasApplied, compatibility.Diagnostics.AliasApplied ? $"Normalized {compatibility.Diagnostics.InputEventType} to {compatibility.Diagnostics.CanonicalEventType}." : null, v1Profile.ProfileVersion);
        return new AstronomyFamilyProfileResolutionResult(compatibility.LegacyProfile, resolved, compatibility.Diagnostics);
    }
}
