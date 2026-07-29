namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed record DocumentaryBlueprintValidationContext(
    string ExecutionId,
    string EventId,
    string Language,
    string Profile,
    string ExpectedVariant,
    string SourcePhase3Checksum,
    IReadOnlySet<string> ViewerQuestionIds,
    IReadOnlySet<string> HighPriorityViewerQuestionIds,
    IReadOnlySet<string> LearningObjectiveIds,
    IReadOnlySet<string> KnowledgeReferenceIds,
    IReadOnlySet<string> RequestedVariants);

public sealed record DocumentaryBlueprintArtifactValidationError(
    string Artifact, string? SceneId, string Field, string? Expected, string? Actual);

public sealed record DocumentaryBlueprintArtifactValidationResult(
    IReadOnlyList<DocumentaryBlueprintArtifactValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>One validation policy shared by Phase 4 generation, staging, and resume.</summary>
public static class DocumentaryBlueprintArtifactValidator
{
    public static DocumentaryBlueprintArtifactValidationResult Validate(
        DocumentaryBlueprintArtifact artifact,
        DocumentaryBlueprintValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<DocumentaryBlueprintArtifactValidationError>();
        void Add(string field, object? expected, object? actual, string? scene = null) =>
            errors.Add(new(context.ExpectedVariant, scene, field, expected?.ToString(), actual?.ToString()));

        if (artifact.Metadata.ExecutionId != context.ExecutionId) Add("Metadata.ExecutionId", context.ExecutionId, artifact.Metadata.ExecutionId);
        if (artifact.Metadata.EventId != context.EventId) Add("Metadata.EventId", context.EventId, artifact.Metadata.EventId);
        if (!string.Equals(artifact.Metadata.Language, context.Language, StringComparison.OrdinalIgnoreCase)) Add("Metadata.Language", context.Language, artifact.Metadata.Language);
        if (artifact.Metadata.Profile != context.Profile) Add("Metadata.Profile", context.Profile, artifact.Metadata.Profile);
        if (artifact.Metadata.Variant != context.ExpectedVariant) Add("Metadata.Variant", context.ExpectedVariant, artifact.Metadata.Variant);
        if (artifact.Metadata.SourcePhase3Checksum != context.SourcePhase3Checksum) Add("Metadata.SourcePhase3Checksum", context.SourcePhase3Checksum, artifact.Metadata.SourcePhase3Checksum);
        if (string.IsNullOrWhiteSpace(artifact.Blueprint.BlueprintId)) Add("Blueprint.BlueprintId", "non-empty", artifact.Blueprint.BlueprintId);
        if (artifact.Blueprint.Scenes.Count == 0) Add("Blueprint.Scenes", "non-empty", "empty");
        if (!DocumentaryBlueprintChecksum.HasValidChecksum(artifact)) Add("Metadata.Checksum", DocumentaryBlueprintChecksum.Calculate(artifact), artifact.Metadata.Checksum);
        if (context.ExpectedVariant != "Master" && !context.RequestedVariants.Contains(context.ExpectedVariant)) Add("Metadata.Variant", "requested variant", context.ExpectedVariant);

        var duplicateIds = artifact.Blueprint.Scenes.GroupBy(x => x.SceneId, StringComparer.Ordinal).Where(x => x.Count() > 1);
        foreach (var duplicate in duplicateIds) Add("SceneId", "unique", duplicate.Key, duplicate.Key);
        for (var i = 0; i < artifact.Blueprint.Scenes.Count; i++)
        {
            var scene = artifact.Blueprint.Scenes[i];
            if (scene.SceneNumber != i + 1) Add("SceneNumber", i + 1, scene.SceneNumber, scene.SceneId);
            if (string.IsNullOrWhiteSpace(scene.Title)) Add("Title", "non-empty", scene.Title, scene.SceneId);
            foreach (var reference in scene.KnowledgeReferences)
                if (string.IsNullOrWhiteSpace(reference.KnowledgeEntryId) || !context.KnowledgeReferenceIds.Contains(reference.KnowledgeEntryId))
                    Add("KnowledgeReferences", "known non-empty reference", reference.KnowledgeEntryId, scene.SceneId);
            if (artifact.Coverage.SectionQuestionMap.TryGetValue(scene.SceneId, out var questions))
                foreach (var question in questions.Where(q => !context.ViewerQuestionIds.Contains(q))) Add("Coverage.ViewerQuestionId", "known question", question, scene.SceneId);
        }
        foreach (var objective in artifact.Coverage.CoveredLearningObjectiveIds.Concat(artifact.Coverage.DeferredLearningObjectiveIds).Where(x => !context.LearningObjectiveIds.Contains(x)))
            Add("Coverage.LearningObjectiveId", "known objective", objective);
        foreach (var question in context.HighPriorityViewerQuestionIds.Where(q => !artifact.Coverage.CoveredViewerQuestionIds.Contains(q)))
            Add("Coverage.HighPriorityViewerQuestionId", "covered", question);
        foreach (var deferred in artifact.Coverage.DeferredViewerQuestionIds.Where(q => !artifact.Coverage.DeferralReasons.TryGetValue(q, out var reason) || string.IsNullOrWhiteSpace(reason)))
            Add("Coverage.DeferralReason", "non-empty", null, deferred);
        return new(errors);
    }
}
