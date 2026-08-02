using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public interface IPhase6InputAuthorityEvaluator
{
    Task<Phase6InputAuthorityEvaluation> EvaluateAsync(Phase6InputAuthorityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Joins the two physical committed-authority boundaries without consulting pipeline status.</summary>
public sealed class Phase6InputAuthorityEvaluator(IPhase4CommittedAuthorityEvaluator phase4Evaluator,
    IPhase5CommittedAuthorityEvaluator phase5Evaluator) : IPhase6InputAuthorityEvaluator
{
    private static readonly HashSet<string> SupportedVariants = new(["Long", "Short"], StringComparer.OrdinalIgnoreCase);

    public async Task<Phase6InputAuthorityEvaluation> EvaluateAsync(Phase6InputAuthorityRequest request, CancellationToken token = default)
    {
        Phase6InputAuthorityEvaluation Invalid(string code, string message) => new(false, code, [message], null);
        var p4 = await phase4Evaluator.EvaluateAsync(request.ExecutionRoot, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, token);
        if (!p4.IsValid || p4.PublishedAuthority is null) return Invalid("P6INPUT_PHASE4_INVALID", p4.ReasonCode);
        var aggregate = p4.PublishedAuthority;
        var p5 = await phase5Evaluator.EvaluateAsync(request.ExecutionRoot, request.ExecutionId, request.PlanId,
            request.EventId, request.Language, Phase5ExpectedPhase4Authority.From(aggregate), token);
        if (!p5.IsValid || p5.PublishedAuthority is null)
        {
            var detail = string.Join("; ", p5.Errors);
            var code = detail.Contains("Long projection lineage", StringComparison.OrdinalIgnoreCase) ? "P6INPUT_LONG_LINEAGE_MISMATCH"
                : detail.Contains("Short projection lineage", StringComparison.OrdinalIgnoreCase) ? "P6INPUT_SHORT_LINEAGE_MISMATCH"
                : p5.ReasonCode == "P5REUSE_SOURCE_PHASE4_MISMATCH" ? "P6INPUT_PHASE4_LINEAGE_MISMATCH"
                : "P6INPUT_PHASE5_INVALID";
            return Invalid(code, $"{p5.ReasonCode}: {detail}");
        }
        var published = p5.PublishedAuthority;
        var certification = published.Certification;
        if (published.SourceAggregateId != aggregate.AggregateId || published.SourceAggregateChecksum != aggregate.DeterministicChecksum ||
            certification.SourcePhase4Checksum != aggregate.DeterministicChecksum)
            return Invalid("P6INPUT_PHASE4_LINEAGE_MISMATCH", "Phase 5 aggregate lineage differs from committed Phase 4.");
        if (certification.SourceLongBlueprintChecksum != aggregate.LongProjectionChecksum)
            return Invalid("P6INPUT_LONG_LINEAGE_MISMATCH", "Phase 5 Long lineage differs from committed Phase 4.");
        if (certification.SourceShortBlueprintChecksum != aggregate.ShortProjectionChecksum)
            return Invalid("P6INPUT_SHORT_LINEAGE_MISMATCH", "Phase 5 Short lineage differs from committed Phase 4.");
        if (!certification.Passed || certification.CertificationStatus == DocumentaryBlueprintCertificationStatus.Rejected)
            return Invalid("P6INPUT_CERTIFICATION_REJECTED", "Phase 5 certification was rejected.");
        if (!published.EditorialContract.StoryFrameEligible)
            return Invalid("P6INPUT_STORY_FRAME_NOT_ELIGIBLE", "Phase 5 does not authorize Story Frame generation.");
        if (!published.Validation.OverallValid || !published.Coverage.IsValid || !published.Transitions.IsValid || !published.PauseTest.IsValid)
            return Invalid("P6INPUT_PHASE5_INVALID", "A required Phase 5 validation gate is invalid.");
        var requested = request.RequestedVariants.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Count == 0 || requested.Any(v => !SupportedVariants.Contains(v)))
            return Invalid("P6INPUT_VARIANT_INVALID", "A requested variant is empty or unsupported.");
        if (requested.Any(v => !published.EditorialContract.AllowedVariants.Contains(v, StringComparer.OrdinalIgnoreCase)))
            return Invalid("P6INPUT_VARIANT_NOT_ALLOWED", "A requested variant is not authorized by Phase 5.");

        var longResult = BuildScenes("Long", aggregate.LongVariant, published);
        if (!longResult.Valid) return Invalid("P6INPUT_SCENE_EVIDENCE_INVALID", longResult.Error!);
        var shortResult = BuildScenes("Short", aggregate.ShortVariant, published);
        if (!shortResult.Valid) return Invalid("P6INPUT_SCENE_EVIDENCE_INVALID", shortResult.Error!);
        if (requested.Contains("Long", StringComparer.OrdinalIgnoreCase) && longResult.Scenes!.Count == 0)
            return Invalid("P6INPUT_VARIANT_INVALID", "Requested Long projection is empty.");
        if (requested.Contains("Short", StringComparer.OrdinalIgnoreCase) && shortResult.Scenes!.Count == 0)
            return Invalid("P6INPUT_VARIANT_INVALID", "Requested Short projection is empty.");

        var authority = new Phase6CommittedInputAuthority(aggregate, aggregate.AggregateId, aggregate.DeterministicChecksum,
            aggregate.LongProjectionChecksum, aggregate.ShortProjectionChecksum, aggregate.ProfileId, aggregate.ProfileVersion,
            p4.ArtifactPaths.Where(x => x.Contains("validation", StringComparison.OrdinalIgnoreCase)).ToArray(),
            p4.ArtifactPaths.Where(x => x.Contains("manifest", StringComparison.OrdinalIgnoreCase)).ToArray(), published,
            certification.CertificationId, certification.SemanticChecksum, published.EditorialContract.Checksum,
            published.EditorialContract.ContractId, ["validation/phase-05-validation.json"], p5.Artifacts.ToArray(), true,
            published.EditorialContract.AllowedVariants.ToArray(), requested, true, true, published.Coverage.IsValid,
            published.Transitions.IsValid, published.PauseTest.IsValid, true, true, longResult.Scenes!, shortResult.Scenes!);
        return new(true, "P6INPUT_VALID", [], authority);
    }

    private static (bool Valid, IReadOnlyList<CertifiedStoryFrameSceneAuthority>? Scenes, string? Error) BuildScenes(
        string variant, DocumentaryBlueprintVariantArtifact projection, PublishedBlueprintCertification p5)
    {
        var scenes = projection.Blueprint.Scenes;
        if (scenes.Select(x => x.SceneId).Distinct(StringComparer.Ordinal).Count() != scenes.Count)
            return (false, null, $"Duplicate {variant} source scene IDs.");
        var intents = p5.SceneIntents.Scenes.Where(x => x.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (intents.Any(x => scenes.All(s => s.SceneId != x.SceneId)) || scenes.Any(s => intents.Count(x => x.SceneId == s.SceneId) != 1))
            return (false, null, $"{variant} Phase 4 scenes and Phase 5 intents do not reconcile one-to-one.");
        var trace = projection.SceneTraceability.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var output = new List<CertifiedStoryFrameSceneAuthority>();
        foreach (var scene in scenes.OrderBy(x => x.SceneNumber))
        {
            var intent = intents.Single(x => x.SceneId == scene.SceneId);
            if (!trace.TryGetValue(scene.SceneId, out var evidence) || string.IsNullOrWhiteSpace(evidence.PrimaryViewerQuestionId) ||
                string.IsNullOrWhiteSpace(scene.ViewerQuestion.Text) || string.IsNullOrWhiteSpace(intent.LearningObjectiveId) ||
                string.IsNullOrWhiteSpace(scene.SceneObjective.LearningGoal) || string.IsNullOrWhiteSpace(scene.EditorialOutcome.ViewerTakeaway) ||
                string.IsNullOrWhiteSpace(scene.Transition.TransitionIntent) || scene.KnowledgeReferences.Count == 0 ||
                scene.KnowledgeReferences.Count(x => x.IsPrimary) != 1 || evidence.MinimumDurationSeconds <= 0 ||
                evidence.MaximumDurationSeconds < evidence.MinimumDurationSeconds || scene.EstimatedDurationSeconds < evidence.MinimumDurationSeconds ||
                scene.EstimatedDurationSeconds > evidence.MaximumDurationSeconds)
                return (false, null, $"Invalid certified evidence for {variant} scene '{scene.SceneId}'.");
            output.Add(new(scene.SceneId, variant, scene.SceneNumber, scene.NarrativeStage, scene.SceneRole,
                evidence.PrimaryViewerQuestionId, scene.ViewerQuestion.Text, intent.LearningObjectiveId, scene.SceneObjective.LearningGoal,
                scene.EditorialOutcome, scene.Transition, scene.KnowledgeReferences.ToArray(), evidence.MinimumDurationSeconds,
                scene.EstimatedDurationSeconds, evidence.MaximumDurationSeconds, scene.VisualOpportunities.FirstOrDefault(),
                Phase5SemanticChecksum.Calculate(scene)));
        }
        return (true, output.ToArray(), null);
    }
}
