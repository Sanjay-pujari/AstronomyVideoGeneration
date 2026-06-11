namespace Astronomy.MediaFactory.Core;

public sealed record QuestionSceneIntentEnrichmentRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    string ViewerPersona = "CasualSkyWatcher",
    string KnowledgeLevel = "Beginner",
    bool DryRun = true,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ProductionContext = null);

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
    string ViewerPersona,
    string KnowledgeLevel,
    IReadOnlyList<EnrichedQuestionSceneDto> Scenes,
    bool IsValid,
    DateTimeOffset GeneratedUtc);

public sealed record EnrichedQuestionSceneDto(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string SourceAnswer,
    string ViewerPersona,
    string KnowledgeLevel,
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
