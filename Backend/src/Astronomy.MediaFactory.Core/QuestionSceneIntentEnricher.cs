namespace Astronomy.MediaFactory.Core;

public sealed record QuestionSceneIntentEnrichmentRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record QuestionSceneIntentEnrichmentResponse(
    string EventId,
    int SceneCount,
    bool IsValid,
    EnrichedQuestionScenePlanDto EnrichedScenePlan,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record EnrichedQuestionScenePlanDto(
    string EventId,
    string RegionId,
    string Language,
    IReadOnlyList<EnrichedQuestionSceneDto> Scenes,
    bool IsValid,
    DateTimeOffset GeneratedUtc);

public sealed record EnrichedQuestionSceneDto(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string SourceAnswer,
    string ViewerTakeaway,
    string NarrationIntent,
    string VisualIntent,
    string ImagePromptIntent,
    string OverlayIntent,
    string AccessibilityIntent,
    bool IsRequired);

public interface IQuestionSceneIntentEnricher
{
    Task<QuestionSceneIntentEnrichmentResponse> EnrichQuestionScenePlanAsync(QuestionSceneIntentEnrichmentRequest request, CancellationToken cancellationToken);
}
