using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Read-only join of the two committed authorities consumed by P7.1B.</summary>
public sealed class Phase7ScenePacketInputAuthorityEvaluator(
    IPhase7KnowledgeCommittedStateEvaluator knowledgeEvaluator,
    IPhase6CommittedAuthorityEvaluator phase6Evaluator,
    IFamilyNarrationProfileResolver profileResolver) : IPhase7ScenePacketInputAuthorityEvaluator
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
        var requirements = longs.Concat(shorts).ToDictionary(frame => frame.FrameId, frame =>
            (IReadOnlyList<Phase7SceneReferenceRequirement>)frame.KnowledgeReferenceIds.Select((id, index) =>
                new Phase7SceneReferenceRequirement(id, frame.Variant, index == 0, true,
                    "Phase6StoryFramesAuthority", $"frames/{frame.FrameId}/knowledgeReferenceIds/{index}" )).ToArray(), StringComparer.Ordinal);
        var authority = new Phase7ScenePacketInputAuthority(knowledge.Authority, p, profile.Profile,
            request.ExecutionId, request.PlanId, request.EventId, k.EventFamily, k.EventType, request.Language,
            k.ProfileId, k.ProfileVersion, longs, shorts,
            p.Index.Scenes.Where(x => x.Variant == "Long").OrderBy(x => x.SceneNumber).ToArray(),
            p.Index.Scenes.Where(x => x.Variant == "Short").OrderBy(x => x.SceneNumber).ToArray(), lineage, runtime)
            { ReferenceRequirements = requirements };
        return new(true, authority, "P7PACKET_INPUT_VALID", [], knowledge.Warnings.Concat(phase6.Warnings).Distinct().ToArray());
    }

    private static Phase7ScenePacketInputAuthorityEvaluation Bad(string code, string detail,
        IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null) =>
        new(false, null, code, new[] { detail }.Concat(errors ?? []).Distinct().ToArray(), (warnings ?? []).ToArray());
    private static bool Safe(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Contains('\\') && !path.Split('/').Any(x => x is "" or "." or "..") &&
        !path.Contains("staging", StringComparison.OrdinalIgnoreCase) && !path.Contains("backup", StringComparison.OrdinalIgnoreCase);
}
