using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeAuthorityBuilder : IPhase7KnowledgeAuthorityBuilder
{
    public Phase7KnowledgeAuthority Build(Phase7CommittedInputAuthority input, CertifiedKnowledgePayload payload,
        ResolvedNarrationKnowledge knowledge, FamilyNarrationProfile profile,
        IReadOnlyDictionary<string, string> runtimeCompatibilityEvidence)
    {
        var phase6=input.StoryFrameAuthority;
        var authorityId="p7k-"+Phase7Determinism.Hash(new {
            phase6.Authority.ExecutionId, phase6.Authority.PlanId, phase6.Authority.EventId,
            language=input.Language, profileId=profile.ProfileId,
            sourcePhase6AuthorityChecksum=phase6.Authority.SemanticChecksum,
            eventKnowledgeChecksum=payload.PayloadChecksum, evergreenChecksum=payload.EvergreenChecksum ?? "",
            sourceRegistryChecksum=knowledge.SourceRegistryChecksum, contractVersion=Phase7KnowledgeContract.Version
        })[..32];
        var claims=knowledge.Domains.SelectMany(x=>x.Claims).OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray();
        var draft=new Phase7KnowledgeAuthority(Phase7KnowledgeContract.Version,authorityId,
            phase6.Authority.ExecutionId,phase6.Authority.PlanId,phase6.Authority.EventId,input.EventFamily,input.EventType,
            input.Language,profile.ProfileId,profile.ContractVersion,phase6.Authority.AuthorityId,
            phase6.Authority.SemanticChecksum,phase6.Index.IndexId,phase6.Index.Checksum,
            phase6.SourcePhase4AggregateId,phase6.SourcePhase4Checksum,phase6.SourcePhase5PublicationId,
            payload.PayloadId,payload.PayloadChecksum,payload.VerificationStatus,payload.EvergreenPayloadId ?? "",
            payload.EvergreenChecksum ?? "",payload.EvergreenPayloadId is null ? "NotLoaded" : "Reviewed",
            payload.EvergreenRelativePath ?? "",knowledge.SourceRegistryId,knowledge.SourceRegistryChecksum,
            profile.MandatoryKnowledgeDomains.Concat(profile.OptionalKnowledgeDomains).Distinct(StringComparer.Ordinal).Order().ToArray(),
            knowledge.KnowledgeEntities,claims,payload.AllResolvedSources.OrderBy(x=>x.SourceId,StringComparer.Ordinal).ToArray(),
            knowledge.ClaimSupportEvidence,knowledge.AdapterDiagnostics,knowledge.MergeDecisions,knowledge.SourceAuditSummary,
            knowledge.UnknownSections,knowledge.UnknownProperties,knowledge.Warnings,knowledge.BlockingIssues,"",
            new SortedDictionary<string,string>(runtimeCompatibilityEvidence,StringComparer.Ordinal));
        return draft with { SemanticChecksum=Phase7Determinism.Hash(draft) };
    }
}
