namespace Astronomy.MediaFactory.Core;

public sealed record QuestionScenePlanRequest(
    string RegionId,
    string EventId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ProductionContext = null);

public sealed record QuestionScenePlanResponse(
    string EventId,
    int SceneCount,
    bool IsValid,
    QuestionDrivenScenePlanDto ScenePlan,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record QuestionDrivenScenePlanDto(
    string EventId,
    string RegionId,
    string Language,
    IReadOnlyList<QuestionDrivenSceneDto> Scenes,
    DateTimeOffset GeneratedUtc);

public sealed record QuestionDrivenSceneDto(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string ViewerTakeaway,
    string SourceAnswer,
    string VisualIntent,
    string NarrationIntent,
    bool IsRequired);

public interface IQuestionScenePlanner
{
    Task<QuestionScenePlanResponse> GenerateQuestionScenePlanAsync(QuestionScenePlanRequest request, CancellationToken cancellationToken);
}
