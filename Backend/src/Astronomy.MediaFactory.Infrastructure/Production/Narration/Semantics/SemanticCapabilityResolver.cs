using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

public sealed class SemanticCapabilityResolver(ISemanticCapabilityCatalog catalog, ISemanticCapabilitySourceRegistry registry) : ISemanticCapabilityResolver
{
    public SemanticCapabilityResolution Resolve(string capabilityId, SemanticCapabilitySourceContext context, LanguageProfile languageProfile)
    {
        var def = catalog.GetRequired(capabilityId);
        if (def.CapabilityId.Equals("EventIdentity", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(context.FamilyProfileId))
            return new(def.CapabilityId, "Unresolved", null, null, null, [], ["Capability EventIdentity unresolved: meaningful title or subject identity requires a resolved astronomy family."], "Missing", [], [new("FamilyProfileResolver", "FamilyProfileId", "UnsupportedForFamily")], []);
        var adapters = registry.GetAdapters(def.CapabilityId).Where(a => def.ApprovedSourceAdapterIds.Contains(a.AdapterId, StringComparer.OrdinalIgnoreCase)).ToArray();
        var candidates = new List<(ISemanticCapabilitySourceAdapter Adapter, SemanticCapabilityCandidate Candidate)>();
        var rejections = new List<SemanticCapabilityRejection>();
        foreach (var adapter in adapters)
        {
            if (adapter.TryExtract(context, out var candidate, out var rejection)) candidates.Add((adapter, candidate));
            else if (rejection is not null) rejections.Add(rejection);
        }
        var valid = candidates.Where(c => c.Adapter.Strength >= def.MinimumStrength).OrderBy(c => c.Adapter.Precedence).ThenByDescending(c => c.Adapter.Strength).ToArray();
        if (valid.Length == 0)
        {
            var reason = adapters.Length == 0 ? "NoAdapterRegistered" : candidates.Count == 0 ? string.Join(";", rejections.Select(r => r.Reason).Distinct()) : "VerificationFailed";
            return new(def.CapabilityId, "Unresolved", null, null, null, [], [$"Capability {def.CapabilityId} unresolved: {reason}."], "Missing", candidates.Select(c => c.Candidate).ToArray(), rejections.ToArray(), []);
        }
        var best = valid[0];
        var selected = $"{best.Adapter.SourceArtifact}.{best.Candidate.SourceField}";
        var conversion = best.Adapter.AdapterId.Contains("Utc", StringComparison.OrdinalIgnoreCase) ? [$"UTC value converted using verified timezone for {def.CapabilityId}."] : Array.Empty<string>();
        var substitutions = best.Adapter.SupportedCapabilityId.Equals(def.CapabilityId, StringComparison.OrdinalIgnoreCase) ? conversion : conversion.Concat([$"{def.CapabilityId} substituted with valid source {best.Adapter.SupportedCapabilityId} from {selected}."]).ToArray();
        return new(def.CapabilityId, "Resolved", selected, best.Candidate.Value, best.Candidate.Value?.ToString(), valid.Skip(1).Select(c => $"{c.Adapter.SourceArtifact}.{c.Candidate.SourceField}").ToArray(), [], best.Candidate.Strength, candidates.Select(c => c.Candidate).ToArray(), rejections.ToArray(), substitutions);
    }
}
