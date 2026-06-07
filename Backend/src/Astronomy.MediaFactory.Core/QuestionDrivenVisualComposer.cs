namespace Astronomy.MediaFactory.Core;

public sealed record QuestionDrivenVisualGenerationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record QuestionDrivenVisualGenerationResponse(
    string EventId,
    int SceneCount,
    int FinalImageCount,
    int SrtCount,
    int ApprovedSceneCount,
    int FailedSceneCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public sealed record QuestionDrivenImagePromptRequest(
    string EventId,
    string RegionId,
    string Language,
    int SceneNumber,
    string QuestionType,
    string VisualIntent,
    string ImagePromptIntent,
    bool LocalPlanetAssetsAvailable);

public sealed record QuestionDrivenVisualSpec(
    string EventId,
    string RegionId,
    string Language,
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string ViewerTakeaway,
    string NarrationText,
    string CaptionText,
    int EstimatedDurationSeconds,
    string BackgroundPrompt,
    IReadOnlyList<string> OverlayText,
    IReadOnlyList<string> ProgrammaticLayers,
    IReadOnlyList<string> AccessibilityCues,
    DateTimeOffset GeneratedUtc);

public sealed record QuestionDrivenSceneReview(
    int SceneNumber,
    string QuestionType,
    bool ImageApproved,
    bool NarrationApproved,
    bool SrtApproved,
    bool AlignmentApproved,
    bool AccessibilityApproved,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public interface IQuestionDrivenImagePromptGenerator
{
    string GeneratePrompt(QuestionDrivenImagePromptRequest request);
}

public interface IQuestionDrivenVisualComposer
{
    Task<QuestionDrivenVisualGenerationResponse> GenerateQuestionDrivenVisualsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken);
}
