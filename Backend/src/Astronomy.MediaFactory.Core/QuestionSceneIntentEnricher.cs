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
    DateTimeOffset GeneratedUtc,
    QuestionSceneEnrichmentDiagnostics? Diagnostics = null);

public sealed record QuestionSceneEnrichmentDiagnostics(
    string StrategyId,
    IReadOnlyList<string> RequiredVisualObjects,
    IReadOnlyList<string> ForbiddenObjectNames,
    IReadOnlyList<string> EnrichedFieldsScanned,
    IReadOnlyList<string> LeakageTermsFound,
    string EnrichmentSource,
    IReadOnlyList<string>? AllowedContextTerms = null,
    IReadOnlyList<string>? PrimaryObjects = null,
    IReadOnlyList<string>? SecondaryObjects = null,
    IReadOnlyList<ObjectValidationDiagnostic>? ObjectValidationDiagnostics = null);

public sealed record ObjectValidationDiagnostic(
    string ObjectName,
    string OccurrenceSource,
    string OccurrenceRole,
    string AllowedBecause,
    string ValidationResult);

public sealed record SceneVisualVariantDto(
    int VariantNo,
    string VariantType,
    string Purpose,
    double RecommendedDurationSeconds,
    string CameraStyle,
    string CompositionHint,
    string MotionHint,
    string OverlayHint,
    string RendererHint,
    string OutputFileNameSuggestion);

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
    bool IsRequired,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RequiredVisualObjects = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? StrategyId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SceneVisualVariantDto>? VisualVariants = null);

public interface IQuestionSceneIntentEnricher
{
    Task<QuestionSceneIntentEnrichmentResponse> EnrichQuestionScenePlanAsync(QuestionSceneIntentEnrichmentRequest request, CancellationToken cancellationToken);
}
