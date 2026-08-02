using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public interface IPhase6InputAuthorityEvaluator
{
    Task<Phase6InputAuthorityEvaluation> EvaluateAsync(Phase6InputAuthorityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Joins the typed, physically validated Phase 4 and Phase 5 committed authorities.</summary>
public sealed class Phase6InputAuthorityEvaluator(IPhase4CommittedAuthorityEvaluator phase4Evaluator,
    IPhase5CommittedAuthorityEvaluator phase5Evaluator) : IPhase6InputAuthorityEvaluator
{
    private static readonly string[] CanonicalVariants = ["Long", "Short"];
    private static readonly HashSet<string> SupportedVariants = new(CanonicalVariants, StringComparer.OrdinalIgnoreCase);

    public async Task<Phase6InputAuthorityEvaluation> EvaluateAsync(Phase6InputAuthorityRequest request,
        CancellationToken token = default)
    {
        static Phase6InputAuthorityEvaluation Invalid(string code, string message) => new(false, code, [message], null);
        Phase4CommittedAuthorityEvaluation p4;
        token.ThrowIfCancellationRequested();
        try
        {
            p4 = await phase4Evaluator.EvaluateAsync(request.ExecutionRoot, request.ExecutionId, request.PlanId,
                request.EventId, request.Language, token);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return Invalid("P6INPUT_PHASE4_INVALID", $"Phase 4 committed-authority evaluation failed: {ex.Message}");
        }
        token.ThrowIfCancellationRequested();
        if (!p4.IsValid || p4.PublishedAuthority is null)
            return Invalid("P6INPUT_PHASE4_INVALID", p4.ReasonCode);
        if (!ValidEvidence(p4.CommittedValidationEvidence, "validation/phase-04-validation.json", p4.ArtifactPaths))
            return Invalid("P6INPUT_PHASE4_INVALID", "Committed Phase 4 validation evidence is missing or unsafe.");
        if (!ValidEvidence(p4.ManifestEvidence, "phase-manifest.json", p4.ArtifactPaths))
            return Invalid("P6INPUT_PHASE4_INVALID", "Committed Phase 4 manifest evidence is missing or unsafe.");

        var aggregate = p4.PublishedAuthority;
        Phase5CommittedStateEvaluation p5;
        try
        {
            p5 = await phase5Evaluator.EvaluateAsync(request.ExecutionRoot, request.ExecutionId, request.PlanId,
                request.EventId, request.Language, Phase5ExpectedPhase4Authority.From(aggregate), token);
        }
        catch (Exception ex) when (Expected(ex))
        {
            return Invalid("P6INPUT_PHASE5_INVALID", $"Phase 5 committed-authority evaluation failed: {ex.Message}");
        }
        token.ThrowIfCancellationRequested();
        if (!p5.IsValid || p5.PublishedAuthority is null)
        {
            var detail = string.Join("; ", p5.Errors);
            var code = detail.Contains("Long projection lineage", StringComparison.OrdinalIgnoreCase) ? "P6INPUT_LONG_LINEAGE_MISMATCH"
                : detail.Contains("Short projection lineage", StringComparison.OrdinalIgnoreCase) ? "P6INPUT_SHORT_LINEAGE_MISMATCH"
                : p5.ReasonCode == "P5REUSE_SOURCE_PHASE4_MISMATCH" ? "P6INPUT_PHASE4_LINEAGE_MISMATCH"
                : "P6INPUT_PHASE5_INVALID";
            return Invalid(code, $"{p5.ReasonCode}: {detail}");
        }
        if (!ValidEvidence(p5.CommittedValidationEvidence, "validation/phase-05-validation.json", p5.CommittedValidationEvidence))
            return Invalid("P6INPUT_PHASE5_INVALID", "Committed Phase 5 validation evidence is missing or unsafe.");
        if (p5.ManifestEvidence.Count == 0 || p5.ManifestEvidence.Any(x => !SafeRelative(x)) ||
            !p5.ManifestEvidence.Contains("phase-manifest.json", StringComparer.Ordinal))
            return Invalid("P6INPUT_PHASE5_INVALID", "Committed Phase 5 manifest evidence is missing or unsafe.");
        if (p5.Artifacts.Count == 0 || p5.Artifacts.Any(x => !SafeRelative(x.RelativePath)))
            return Invalid("P6INPUT_PHASE5_INVALID", "Committed Phase 5 artifact inventory is empty or unsafe.");
        if (string.IsNullOrWhiteSpace(p5.PublicationTransactionId))
            return Invalid("P6INPUT_PHASE5_INVALID", "Committed Phase 5 publication identity is missing.");
        if (!p5.PublicationCommitted)
            return Invalid("P6INPUT_PHASE5_INVALID", "Phase 5 publication-committed gate failed.");
        if (!p5.CommittedStateValidationPassed)
            return Invalid("P6INPUT_PHASE5_INVALID", "Phase 5 committed-state validation gate failed.");

        var published = p5.PublishedAuthority;
        var certification = published.Certification;
        var lineageMatched = published.SourceAggregateId == aggregate.AggregateId &&
            published.SourceAggregateChecksum == aggregate.DeterministicChecksum &&
            certification.SourcePhase4Checksum == aggregate.DeterministicChecksum;
        if (!lineageMatched) return Invalid("P6INPUT_PHASE4_LINEAGE_MISMATCH", "Phase 5 aggregate lineage differs from committed Phase 4.");
        if (certification.SourceLongBlueprintChecksum != aggregate.LongProjectionChecksum)
            return Invalid("P6INPUT_LONG_LINEAGE_MISMATCH", "Phase 5 Long lineage differs from committed Phase 4.");
        if (certification.SourceShortBlueprintChecksum != aggregate.ShortProjectionChecksum)
            return Invalid("P6INPUT_SHORT_LINEAGE_MISMATCH", "Phase 5 Short lineage differs from committed Phase 4.");
        var certificationAccepted = certification.Passed && certification.CertificationStatus != DocumentaryBlueprintCertificationStatus.Rejected;
        if (!certificationAccepted) return Invalid("P6INPUT_CERTIFICATION_REJECTED", "Phase 5 certification was rejected.");
        if (!published.EditorialContract.StoryFrameEligible)
            return Invalid("P6INPUT_STORY_FRAME_NOT_ELIGIBLE", "Phase 5 does not authorize Story Frame generation.");
        if (!published.Validation.OverallValid || !published.Coverage.IsValid || !published.Transitions.IsValid || !published.PauseTest.IsValid)
            return Invalid("P6INPUT_PHASE5_INVALID", "A required Phase 5 validation gate is invalid.");

        var allowed = published.EditorialContract.AllowedVariants;
        if (allowed.Count == 0 || allowed.Any(string.IsNullOrWhiteSpace) || allowed.Any(v => !SupportedVariants.Contains(v)) ||
            allowed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allowed.Count)
            return Invalid("P6INPUT_PHASE5_INVALID", "Phase 5 allowed variants are empty, duplicated, blank, or unsupported.");
        if (published.SceneIntents.Scenes.Any(x => string.IsNullOrWhiteSpace(x.Variant) || !SupportedVariants.Contains(x.Variant)))
            return Invalid("P6INPUT_VARIANT_INVALID", "Phase 5 scene-intent evidence contains an unsupported variant.");
        if (request.RequestedVariants.Count == 0 || request.RequestedVariants.Any(string.IsNullOrWhiteSpace) ||
            request.RequestedVariants.Any(v => !SupportedVariants.Contains(v)))
            return Invalid("P6INPUT_VARIANT_INVALID", "A requested variant is empty or unsupported.");
        var requested = CanonicalVariants.Where(v => request.RequestedVariants.Contains(v, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (requested.Any(v => !allowed.Contains(v, StringComparer.OrdinalIgnoreCase)))
            return Invalid("P6INPUT_VARIANT_NOT_ALLOWED", "A requested variant is not authorized by Phase 5.");

        var longResult = BuildScenes("Long", aggregate.LongVariant, published);
        if (!longResult.Valid) return Invalid("P6INPUT_SCENE_EVIDENCE_INVALID", longResult.Error!);
        var shortResult = BuildScenes("Short", aggregate.ShortVariant, published);
        if (!shortResult.Valid) return Invalid("P6INPUT_SCENE_EVIDENCE_INVALID", shortResult.Error!);
        if (requested.Contains("Long", StringComparer.Ordinal) && longResult.Scenes!.Count == 0 ||
            requested.Contains("Short", StringComparer.Ordinal) && shortResult.Scenes!.Count == 0)
            return Invalid("P6INPUT_VARIANT_INVALID", "A requested committed projection is empty.");
        token.ThrowIfCancellationRequested();

        var authority = new Phase6CommittedInputAuthority(aggregate, aggregate.AggregateId, aggregate.DeterministicChecksum,
            aggregate.LongProjectionChecksum, aggregate.ShortProjectionChecksum, aggregate.ProfileId, aggregate.ProfileVersion,
            p4.CommittedValidationEvidence.ToArray(), p4.ManifestEvidence.ToArray(), published,
            certification.CertificationId, certification.SemanticChecksum, published.EditorialContract.ContractId,
            published.EditorialContract.Checksum, p5.PublicationTransactionId,
            p5.CommittedValidationEvidence.ToArray(), p5.Artifacts.ToArray(), published.EditorialContract.StoryFrameEligible,
            allowed.Select(Canonical).OrderBy(v => Array.IndexOf(CanonicalVariants, v)).ToArray(), requested,
            lineageMatched, certificationAccepted, published.Coverage.IsValid, published.Transitions.IsValid,
            published.PauseTest.IsValid, p5.PublicationCommitted, p5.CommittedStateValidationPassed,
            longResult.Scenes!, shortResult.Scenes!);
        return new(true, "P6INPUT_VALID", [], authority);
    }

    private static (bool Valid, IReadOnlyList<CertifiedStoryFrameSceneAuthority>? Scenes, string? Error) BuildScenes(
        string variant, DocumentaryBlueprintVariantArtifact projection, PublishedBlueprintCertification p5)
    {
        var scenes = projection.Blueprint.Scenes;
        if (scenes.Any(s => string.IsNullOrWhiteSpace(s.SceneId) || s.SceneNumber <= 0) ||
            scenes.Select(x => x.SceneId).Distinct(StringComparer.Ordinal).Count() != scenes.Count ||
            !scenes.Select(x => x.SceneNumber).Order().SequenceEqual(Enumerable.Range(1, scenes.Count)))
            return (false, null, $"Invalid or duplicate {variant} source scene IDs/sequences.");
        if (projection.SceneTraceability.Any(x => string.IsNullOrWhiteSpace(x.SceneId)) ||
            projection.SceneTraceability.GroupBy(x => x.SceneId, StringComparer.Ordinal).Any(g => g.Count() != 1))
            return (false, null, $"Invalid {variant} traceability evidence.");
        var trace = projection.SceneTraceability.ToDictionary(x => x.SceneId, StringComparer.Ordinal);
        var intents = p5.SceneIntents.Scenes.Where(x => string.Equals(x.Variant, variant, StringComparison.OrdinalIgnoreCase)).ToArray();
        var intentGroups = intents.GroupBy(x => x.SceneId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var sceneIds = scenes.Select(s => s.SceneId).ToHashSet(StringComparer.Ordinal);
        if (intentGroups.Keys.Any(id => !sceneIds.Contains(id)) || scenes.Any(s => !intentGroups.TryGetValue(s.SceneId, out var matches) || matches.Length != 1))
            return (false, null, $"{variant} Phase 4 scenes and Phase 5 intents do not reconcile one-to-one using ordinal IDs.");
        var output = new List<CertifiedStoryFrameSceneAuthority>();
        foreach (var scene in scenes.OrderBy(x => x.SceneNumber))
        {
            var intent = intentGroups[scene.SceneId][0];
            if (!trace.TryGetValue(scene.SceneId, out var evidence) || string.IsNullOrWhiteSpace(evidence.PrimaryViewerQuestionId) ||
                string.IsNullOrWhiteSpace(scene.ViewerQuestion.Text) || string.IsNullOrWhiteSpace(intent.LearningObjectiveId) ||
                string.IsNullOrWhiteSpace(scene.SceneObjective.LearningGoal) || string.IsNullOrWhiteSpace(scene.EditorialOutcome.ViewerTakeaway) ||
                string.IsNullOrWhiteSpace(scene.Transition.TransitionIntent) || scene.KnowledgeReferences.Count == 0 ||
                scene.KnowledgeReferences.Count(x => x.IsPrimary) != 1 || evidence.MinimumDurationSeconds <= 0 ||
                evidence.MaximumDurationSeconds < evidence.MinimumDurationSeconds || scene.EstimatedDurationSeconds < evidence.MinimumDurationSeconds ||
                scene.EstimatedDurationSeconds > evidence.MaximumDurationSeconds || intent.Sequence != scene.SceneNumber ||
                intent.NarrativeStage != scene.NarrativeStage || intent.SceneRole != scene.SceneRole ||
                !string.Equals(intent.ViewerQuestionId, evidence.PrimaryViewerQuestionId, StringComparison.Ordinal) ||
                !string.Equals(intent.LearningObjectiveId, evidence.LearningObjectiveId, StringComparison.Ordinal) ||
                intent.EstimatedDurationSeconds != scene.EstimatedDurationSeconds ||
                !intent.KnowledgeReferenceIds.Order(StringComparer.Ordinal).SequenceEqual(
                    scene.KnowledgeReferences.Select(x => x.KnowledgeEntryId).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                intent.EditorialOutcome != scene.EditorialOutcome || intent.TransitionIntent != scene.Transition)
                return (false, null, $"Invalid certified evidence for {variant} scene '{scene.SceneId}'.");
            var checksum = Phase5SemanticChecksum.Calculate(scene);
            if (string.IsNullOrWhiteSpace(checksum)) return (false, null, $"Missing semantic checksum for {variant} scene '{scene.SceneId}'.");
            output.Add(new(scene.SceneId, variant, scene.SceneNumber, scene.NarrativeStage, scene.SceneRole,
                evidence.PrimaryViewerQuestionId, scene.ViewerQuestion.Text, intent.LearningObjectiveId, scene.SceneObjective.LearningGoal,
                scene.EditorialOutcome, scene.Transition, scene.KnowledgeReferences.ToArray(), evidence.MinimumDurationSeconds,
                scene.EstimatedDurationSeconds, evidence.MaximumDurationSeconds, scene.VisualOpportunities.FirstOrDefault(), checksum));
        }
        return (true, output.ToArray(), null);
    }

    private static bool Expected(Exception ex) => ex is IOException or JsonException or InvalidDataException or
        InvalidOperationException or NotSupportedException or ArgumentException;
    private static bool ValidEvidence(IReadOnlyList<string> evidence, string required, IReadOnlyList<string> inventory) =>
        evidence.Count != 0 && evidence.All(SafeRelative) && evidence.Contains(required, StringComparer.Ordinal) &&
        evidence.All(x => inventory.Contains(x, StringComparer.Ordinal));
    private static bool SafeRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Contains('\\') && !path.Split('/').Any(x => x is "" or "." or "..") &&
        !path.Contains("staging", StringComparison.OrdinalIgnoreCase) && !path.Contains("backup", StringComparison.OrdinalIgnoreCase);
    private static string Canonical(string value) => value.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "Long" : "Short";
}
