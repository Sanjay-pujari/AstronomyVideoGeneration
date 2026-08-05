using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Read-only join of the two committed authorities consumed by P7.1B.</summary>
public sealed class Phase7ScenePacketInputAuthorityEvaluator(
    IPhase7KnowledgeCommittedStateEvaluator knowledgeEvaluator,
    IPhase6CommittedAuthorityEvaluator phase6Evaluator,
    IFamilyNarrationProfileResolver profileResolver,
    IPhase7SceneReferenceCompatibilityPolicy referenceCompatibilityPolicy) : IPhase7ScenePacketInputAuthorityEvaluator
{
    public async Task<Phase7ScenePacketInputAuthorityEvaluation> EvaluateAsync(
        Phase7ScenePacketInputAuthorityRequest request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var knowledge = await knowledgeEvaluator.EvaluateAsync(
            new(request.ExecutionRoot, request.ExecutionId, request.PlanId, request.EventId, request.Language), token);
        token.ThrowIfCancellationRequested();
        if (!knowledge.IsValid || knowledge.Authority is null)
            return Bad("P7PACKET_INPUT_KNOWLEDGE_AUTHORITY_INVALID", knowledge.ReasonCode, knowledge.Errors, knowledge.Warnings);

        var phase6 = await phase6Evaluator.EvaluateAsync(
            new(request.ExecutionRoot, request.ExecutionId, request.PlanId, request.EventId, request.Language), token);
        token.ThrowIfCancellationRequested();
        if (!phase6.IsValid || phase6.Authority is null)
            return Bad("P7PACKET_INPUT_PHASE6_AUTHORITY_INVALID", phase6.ReasonCode, phase6.Errors, phase6.Warnings);

        var k = knowledge.Authority.KnowledgeAuthority;
        var resolution = knowledge.Authority.ResolvedNarrationKnowledge;
        if (resolution is null || knowledge.Authority.KnowledgeDiagnostics is null)
            return Bad("P7PACKET_INPUT_RESOLUTION_REPORT_MISSING", "Committed P7.1A did not carry its physically validated resolution report and diagnostics.");
        if (resolution.PayloadId != k.EventKnowledgePayloadId || resolution.PayloadChecksum != k.EventKnowledgeChecksum ||
            resolution.SourceRegistryId != k.SourceRegistryId || resolution.SourceRegistryChecksum != k.SourceRegistryChecksum ||
            resolution.Language != k.Language || resolution.DeterministicChecksum != Phase7Determinism.Hash(resolution with { DeterministicChecksum = "" }))
            return Bad("P7PACKET_INPUT_RESOLUTION_REPORT_MISMATCH", "Committed resolution-report identity or checksum differs from the knowledge authority.");
        var p = phase6.Authority;
        var p6 = p.Authority;
        if (k.ExecutionId != request.ExecutionId || k.PlanId != request.PlanId || k.EventId != request.EventId ||
            p6.ExecutionId != request.ExecutionId || p6.PlanId != request.PlanId || p6.EventId != request.EventId)
            return Bad("P7PACKET_INPUT_IDENTITY_MISMATCH", "Committed authority identity differs from the request.");
        if (!string.Equals(k.Language, request.Language, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(p6.Language, request.Language, StringComparison.OrdinalIgnoreCase))
            return Bad("P7PACKET_INPUT_LANGUAGE_MISMATCH", "Committed authority language differs from the request.");
        var profile = profileResolver.Resolve(k.EventFamily, request.Language);
        if (!profile.IsValid || profile.Profile is null || k.ProfileId != request.ProfileId ||
            k.ProfileVersion != request.ProfileVersion || profile.Profile.ProfileId != k.ProfileId ||
            profile.Profile.ContractVersion != k.ProfileVersion)
            return Bad("P7PACKET_INPUT_PROFILE_MISMATCH", profile.ReasonCode, profile.Errors);
        if (k.SourcePhase6AuthorityId != p6.AuthorityId || k.SourcePhase6AuthorityChecksum != p6.SemanticChecksum ||
            k.SourcePhase6IndexId != p.Index.IndexId || k.SourcePhase6IndexChecksum != p.Index.Checksum ||
            p.Index.SourceStoryFramesAuthorityId != p6.AuthorityId || p.Index.SourceStoryFramesChecksum != p6.SemanticChecksum)
            return Bad("P7PACKET_INPUT_LINEAGE_MISMATCH", "P7.1A does not bind the evaluated Phase 6 authority and index.");
        var longs = p6.Frames.Where(x => x.Variant == "Long").OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        var shorts = p6.Frames.Where(x => x.Variant == "Short").OrderBy(x => x.SceneNumber).ThenBy(x => x.FrameNumber).ToArray();
        if (longs.Length == 0 || shorts.Length == 0)
            return Bad("P7PACKET_INPUT_VARIANT_MISSING", "Both Long and Short Story Frame variants are required.");
        var allFrames = longs.Concat(shorts).ToArray();
        if (allFrames.GroupBy(x => x.FrameId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            return Bad("P7PACKET_INPUT_DUPLICATE_FRAME_ID", "The committed authority contains a duplicate FrameId.");
        if (allFrames.GroupBy(x => (x.Variant, x.SceneId, x.SceneNumber, x.FrameNumber)).Any(x => x.Count() > 1))
            return Bad("P7PACKET_INPUT_DUPLICATE_SCENE_IDENTITY", "The committed authority contains a duplicate scene/frame identity.");
        foreach (var frame in allFrames)
        {
            var sameIdentity = p.Index.Scenes.Where(x => x.SceneId == frame.SceneId && x.SceneNumber == frame.SceneNumber).ToArray();
            var exact = sameIdentity.Where(x => x.Variant == frame.Variant).ToArray();
            if (exact.Length > 1)
                return Bad("P7PACKET_INPUT_SOURCE_SCENE_AMBIGUOUS", $"Multiple source-scene rows exist for '{frame.FrameId}'.");
            if (exact.Length == 0 && sameIdentity.Length > 0)
                return Bad("P7PACKET_INPUT_SOURCE_SCENE_VARIANT_MISMATCH", $"The source-scene row for '{frame.FrameId}' belongs to another variant.");
            if (exact.Length == 0)
                return Bad("P7PACKET_INPUT_SOURCE_SCENE_MISSING", $"No source-scene row exists for '{frame.FrameId}'.");
        }
        if (knowledge.Authority.ArtifactPaths.Concat(p.ArtifactPaths).Any(x => !Safe(x)))
            return Bad("P7PACKET_INPUT_UNSAFE_PATH", "A committed authority contains an unsafe artifact path.");
        var lineage = new SortedDictionary<string,string>(StringComparer.Ordinal) {
            ["phase4AggregateId"] = k.SourcePhase4AggregateId, ["phase4Checksum"] = k.SourcePhase4Checksum,
            ["phase5PublicationId"] = k.SourcePhase5PublicationId, ["phase6AuthorityId"] = p6.AuthorityId,
            ["phase6AuthorityChecksum"] = p6.SemanticChecksum, ["phase6IndexId"] = p.Index.IndexId,
            ["phase6IndexChecksum"] = p.Index.Checksum, ["phase7KnowledgeAuthorityId"] = k.AuthorityId,
            ["phase7KnowledgeAuthorityChecksum"] = k.SemanticChecksum };
        var runtime = k.RuntimeCompatibilityEvidence.Concat(p.RuntimeCompatibilityEvidence)
            .GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.Ordinal);
        var projections = allFrames.Select(frame => (frame, result: referenceCompatibilityPolicy.Project(frame))).ToArray();
        if (projections.Any(x => !x.result.IsValid))
            return Bad("P7PACKET_REFERENCE_REQUIREMENTS_UNRESOLVED", "The governed compatibility policy could not classify every scene reference.",
                projections.SelectMany(x => x.result.Errors), projections.SelectMany(x => x.result.Warnings));
        var requirements = projections.ToDictionary(x => x.frame.FrameId,
            x => x.result.Requirements.Select(r => r with { IsRequired = HasRequiredBinding(k, r.ReferenceId) }).ToArray(), StringComparer.Ordinal);
        var authority = new Phase7ScenePacketInputAuthority(knowledge.Authority, p, profile.Profile,
            request.ExecutionId, request.PlanId, request.EventId, k.EventFamily, k.EventType, request.Language,
            k.ProfileId, k.ProfileVersion, longs, shorts,
            p.Index.Scenes.Where(x => x.Variant == "Long").OrderBy(x => x.SceneNumber).ToArray(),
            p.Index.Scenes.Where(x => x.Variant == "Short").OrderBy(x => x.SceneNumber).ToArray(), lineage, runtime)
            { ReferenceRequirements = requirements };
        return new(true, authority, "P7PACKET_INPUT_VALID", [], knowledge.Warnings.Concat(phase6.Warnings)
            .Concat(projections.SelectMany(x => x.result.Warnings)).Distinct().ToArray());
    }

    private static bool HasRequiredBinding(PublishedPhase7KnowledgeAuthority authority, string referenceId) =>
        authority.KnowledgeAuthority.Claims.Any(c => c.KnowledgeReferenceIds.Contains(referenceId, StringComparer.Ordinal) &&
            c.Disposition == Phase7ClaimDisposition.Required && !c.RequiresHumanReview &&
            authority.KnowledgeAuthority.ClaimSupportEvidence.Any(e => e.ClaimId == c.ClaimId &&
                e.SemanticIdentity == c.SemanticIdentity && c.SourceIds.Contains(e.SourceId, StringComparer.Ordinal) &&
                e.SourceEligibility == Phase7SourceEligibility.EligibleForRequiredClaim && !e.RequiresHumanReview &&
                e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField) &&
            (!(c.IsLocationDependent || c.IsDateTimeDependent) || Phase7KnowledgePolicyFacts.Scoped(authority.KnowledgeAuthority, c) || Phase7KnowledgePolicyFacts.Qualified(authority.KnowledgeAuthority, c)));

    private static Phase7ScenePacketInputAuthorityEvaluation Bad(string code, string detail,
        IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null) =>
        new(false, null, code, new[] { detail }.Concat(errors ?? []).Distinct().ToArray(), (warnings ?? []).ToArray());
    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Contains('\\') && !path.Split('/').Any(x => x is "" or "." or "..") &&
        !path.Contains("staging", StringComparison.OrdinalIgnoreCase) && !path.Contains("backup", StringComparison.OrdinalIgnoreCase);
}
