namespace Astronomy.MediaFactory.Core;

public sealed record QuestionDrivenNarrationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ProductionContext = null,
    Guid? PlanId = null,
    string? EventType = null,
    string? Title = null,
    string? ShortTitle = null,
    IReadOnlyList<string>? PrimaryObjects = null,
    IReadOnlyList<string>? SecondaryObjects = null,
    string? LocalPeakTime = null,
    string? SkyDirectionHint = null,
    string? BestViewingWindowLocal = null,
    string? StrategyId = null,
    string? SourceOfEventId = null);

public sealed record QuestionDrivenNarrationResponse(
    string EventId,
    int SceneCount,
    int TotalEstimatedDurationSeconds,
    bool IsValid,
    QuestionDrivenNarrationDto Narration,
    QuestionDrivenNarrationReviewDto Review,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record QuestionDrivenNarrationDto(
    string EventId,
    string RegionId,
    string Language,
    IReadOnlyList<QuestionDrivenNarrationSceneDto> Scenes,
    int TotalEstimatedDurationSeconds,
    DateTimeOffset GeneratedUtc);

public sealed record QuestionDrivenNarrationSceneDto(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string ViewerTakeaway,
    string SourceAnswer,
    string NarrationIntent,
    string NarrationText,
    int EstimatedDurationSeconds,
    string VoiceDirection,
    string CaptionText);

public sealed record QuestionDrivenNarrationReviewDto(
    string EventId,
    string RegionId,
    string Language,
    bool IsValid,
    int SceneCount,
    int TotalEstimatedDurationSeconds,
    IReadOnlyList<QuestionDrivenNarrationReviewCheckDto> Checks,
    IReadOnlyList<string> Warnings,
    DateTimeOffset GeneratedUtc);

public sealed record QuestionDrivenNarrationReviewCheckDto(
    string Name,
    bool Passed,
    string Message);

public interface IQuestionDrivenNarrationGenerator
{
    Task<QuestionDrivenNarrationResponse> GenerateQuestionDrivenNarrationAsync(QuestionDrivenNarrationRequest request, CancellationToken cancellationToken);
}
