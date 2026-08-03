using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7InputAuthorityEvaluator(IPhase6CommittedAuthorityEvaluator phase6Evaluator,
    IPhase7CertifiedKnowledgeSource knowledgeSource, IFamilyNarrationProfileResolver profileResolver,
    IPhase7KnowledgeResolver knowledgeResolver, ILogger<Phase7InputAuthorityEvaluator> logger) : IPhase7InputAuthorityEvaluator
{
    private static readonly HashSet<string> Variants = new(["Long","Short"], StringComparer.OrdinalIgnoreCase);
    public async Task<Phase7InputAuthorityEvaluation> EvaluateAsync(Phase7InputAuthorityRequest request, CancellationToken token = default)
    {
        static Phase7InputAuthorityEvaluation Bad(string code, string error) => new(false, null, code, [error], []);
        token.ThrowIfCancellationRequested();
        var p6 = await phase6Evaluator.EvaluateAsync(new(request.ExecutionRoot, request.ExecutionId, request.PlanId, request.EventId, request.Language), token);
        token.ThrowIfCancellationRequested();
        if (!p6.IsValid || p6.Authority is null)
            return new(false, null, "P7INPUT_PHASE6_INVALID",
                new[] { $"Phase 6 evaluator reason: {p6.ReasonCode}." }.Concat(p6.Errors).Distinct(StringComparer.Ordinal).ToArray(),
                p6.Warnings);
        var published = p6.Authority;
        var authority = published.Authority;
        if (authority.ExecutionId != request.ExecutionId || authority.PlanId != request.PlanId || authority.EventId != request.EventId
            || published.Index.SourceStoryFramesAuthorityId != authority.AuthorityId
            || published.Index.SourceStoryFramesChecksum != authority.SemanticChecksum)
            return Bad("P7INPUT_PHASE6_LINEAGE_MISMATCH", "Phase 6 authority identity or index lineage does not match the request.");
        if (!string.Equals(authority.Language, request.Language, StringComparison.OrdinalIgnoreCase))
            return Bad("P7INPUT_LANGUAGE_INVALID", "Phase 6 authority language does not match the Phase 7 request.");
        if (request.ExpectedVariants.Count != 2 || request.ExpectedVariants.Any(x => !Variants.Contains(x))
            || Variants.Any(x => !request.ExpectedVariants.Contains(x, StringComparer.OrdinalIgnoreCase))
            || Variants.Any(x => !authority.RequestedVariants.Contains(x, StringComparer.OrdinalIgnoreCase)))
            return Bad("P7INPUT_VARIANT_INVALID", "Independent Long and Short authority variants are required.");
        var sourceResult = await knowledgeSource.ResolveResultAsync(request.EventId, request.Language, token);
        token.ThrowIfCancellationRequested();
        if (!sourceResult.IsValid) return new(false,null,sourceResult.ReasonCode,sourceResult.Errors,sourceResult.Warnings);
        var payload = sourceResult.Payload;
        if (payload is null) return Bad("P7INPUT_EVENT_INTELLIGENCE_MISSING", "Certified event intelligence was not resolved.");
        if (string.IsNullOrWhiteSpace(payload.RawDataJson) || string.IsNullOrWhiteSpace(payload.EvergreenJson))
            return Bad("P7INPUT_KNOWLEDGE_PAYLOAD_MISSING", "Both certified raw event and evergreen knowledge are required.");
        if (string.IsNullOrWhiteSpace(payload.SourceRegistryId) || payload.ReviewedSourceIds.Count == 0)
            return Bad("P7INPUT_SOURCE_REGISTRY_INVALID", "A certified reviewed source registry is required.");
        var profileResult = profileResolver.Resolve(payload.EventFamily, request.Language);
        if (!profileResult.IsValid || profileResult.Profile is null)
            return Bad(profileResult.ReasonCode, profileResult.Errors.FirstOrDefault() ?? "Narration profile resolution failed.");
        var profile = profileResult.Profile;
        logger.LogInformation("Phase7ProfileIdentityComparison ExpectedProfileId={ExpectedProfileId} ExpectedProfileVersion={ExpectedProfileVersion} ResolvedFamilyProfileId={ResolvedFamilyProfileId} ResolvedFamilyProfileVersion={ResolvedFamilyProfileVersion} PublishedProfileId={PublishedProfileId} PublishedProfileVersion={PublishedProfileVersion} Phase6AuthorityProfileId={Phase6AuthorityProfileId} Phase6AuthorityProfileVersion={Phase6AuthorityProfileVersion} Phase4AuthorityProfileId={Phase4AuthorityProfileId} Phase4AuthorityProfileVersion={Phase4AuthorityProfileVersion} ProductionPipelineRequestEventType={ProductionPipelineRequestEventType} ProductionPipelineRequestContentCategory={ProductionPipelineRequestContentCategory}",
            request.ExpectedProfile, request.ExpectedProfileVersion, profile.ProfileId, profile.ContractVersion,
            published.ProfileId, published.ProfileVersion, authority.Profile, published.ProfileVersion,
            request.ExpectedProfile, request.ExpectedProfileVersion, request.EventType, request.ContentCategory);
        if (!ProfileIdentityMatches(request.ExpectedProfile, request.ExpectedProfileVersion,
                published.ProfileId, published.ProfileVersion))
            return Bad("P7INPUT_PROFILE_INVALID", "Expected profile does not match canonical published profile authority.");
        var knowledge = knowledgeResolver.Resolve(payload, profile);
        if (knowledge.BlockingIssues.Count > 0) return Bad("P7INPUT_KNOWLEDGE_PAYLOAD_INVALID", string.Join("; ", knowledge.BlockingIssues));
        var longFrames = authority.Frames.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        var shortFrames = authority.Frames.Where(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        if (longFrames.Length == 0 || shortFrames.Length == 0) return Bad("P7INPUT_VARIANT_INVALID", "A requested authority variant contains no Story Frames.");
        var paths = published.ArtifactPaths.Where(SafePath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length != published.ArtifactPaths.Count) return Bad("P7INPUT_PHASE6_LINEAGE_MISMATCH", "Phase 6 supplied an unsafe artifact path.");
        var committed = new Phase7CommittedInputAuthority(published, payload.EventFamily, payload.EventType, request.Language,
            profile.ProfileId, profile.ContractVersion, payload.EventId, knowledge.PayloadId, knowledge.PayloadChecksum,
            knowledge.SourceRegistryId, knowledge.SourceRegistryChecksum, payload.EvergreenPayloadId,
            payload.EvergreenChecksum, profile, longFrames, shortFrames,
            published.Index.Scenes.Where(x => x.Variant.Equals("Long", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneNumber).ToArray(),
            published.Index.Scenes.Where(x => x.Variant.Equals("Short", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.SceneNumber).ToArray(),
            published.ManifestEvidence.Concat(published.ValidationEvidence).Distinct(StringComparer.Ordinal).ToArray(), paths,
            published.RuntimeCompatibilityEvidence, knowledge);
        return new(true, committed, "P7INPUT_VALID", [], p6.Warnings);
    }
    public static bool ProfileIdentityMatches(string expectedProfileId, string expectedProfileVersion,
        string publishedProfileId, string publishedProfileVersion) =>
        !string.IsNullOrWhiteSpace(expectedProfileId)
        && !string.IsNullOrWhiteSpace(expectedProfileVersion)
        && string.Equals(expectedProfileId, publishedProfileId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expectedProfileVersion, publishedProfileVersion, StringComparison.OrdinalIgnoreCase);
    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains('\\')
        && !path.Split('/').Any(x => x is "" or "." or "..") && !path.Contains("staging", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("backup", StringComparison.OrdinalIgnoreCase);
}
