using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

public sealed record SemanticSourcePolicyRegistryConsistencyIssueV1(string CapabilityId, string SourceId, string Code, string Message);
public sealed record SemanticSourcePolicyRegistryConsistencyReportV1(ImmutableArray<SemanticSourcePolicyRegistryConsistencyIssueV1> Issues)
{
    public bool Succeeded => Issues.Length == 0;
    public void ThrowIfFailed()
    {
        if (!Succeeded) throw new InvalidOperationException("Semantic source policy/registry consistency failed: " + string.Join(" | ", Issues.Select(i => $"{i.CapabilityId}:{i.SourceId}:{i.Code}")));
    }
}

public sealed class SemanticSourcePolicyRegistryConsistencyValidatorV1(ISemanticSourcePolicyCatalogV1 policies, ISemanticSourceAdapterRegistryV1 registry)
{
    public SemanticSourcePolicyRegistryConsistencyReportV1 Validate()
    {
        var issues = new List<SemanticSourcePolicyRegistryConsistencyIssueV1>();
        foreach (var policy in policies.Policies)
        {
            var approvedSources = policy.ApprovedSources.Where(s => s.ActiveInV1 && s.SourceId != SemanticSourcePolicyVocabularyV1.LegacyRawJsonScanner).ToArray();
            var capabilityAdapters = registry.Adapters.Where(a => a.SupportedCapabilityId.Equals(policy.SemanticCapabilityId) && approvedSources.Any(s => s.SourceId == a.SourceId)).ToArray();
            if (capabilityAdapters.Length == 0 && approvedSources.Length > 0)
            {
                issues.Add(new(policy.SemanticCapabilityId.Value, string.Join(",", approvedSources.Select(s => s.SourceId)), "NoRegisteredProductionAdapter", "Approved production source has no registered adapter for the canonical capability."));
                continue;
            }
            foreach (var adapter in capabilityAdapters)
            {
                var source = approvedSources.First(s => s.SourceId == adapter.SourceId);
                if (!adapter.SupportedCapabilityId.Equals(policy.SemanticCapabilityId))
                    issues.Add(new(policy.SemanticCapabilityId.Value, source.SourceId, "CapabilityMismatch", "Registered adapter source id matches policy but declares a different capability."));
                if (!adapter.EventSpecific && source.EventSpecific)
                    issues.Add(new(policy.SemanticCapabilityId.Value, source.SourceId, "CompatibilityOnlyAdapter", "A production policy source may not be satisfied by a compatibility-only adapter."));
            }
        }
        return new(issues.ToImmutableArray());
    }
}
