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
    IReadOnlyList<string> Warnings,
    int PlannedImageCount = 0,
    int PlannedSrtCount = 0,
    int PlannedReviewCount = 0,
    IReadOnlyList<QuestionDrivenPlannedScene>? PlannedScenes = null);

public sealed record QuestionDrivenPlannedScene(
    int SceneNumber,
    string QuestionType,
    string ScenePurpose,
    string ViewerQuestion,
    string ViewerTakeaway,
    string NarrationText,
    string CaptionText,
    string VisualIntent,
    string ImagePromptIntent,
    string OverlayIntent,
    string AccessibilityIntent,
    string AiBackgroundPrompt,
    QuestionDrivenProgrammaticOverlayPlan ProgrammaticOverlayPlan,
    QuestionDrivenPlannedOutputs PlannedOutputs,
    QuestionDrivenValidationPreview ValidationPreview);

public sealed record QuestionDrivenProgrammaticOverlayPlan(
    string Title,
    string Subtitle,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Arrows,
    IReadOnlyList<string> LocalAssetObjects,
    IReadOnlyList<string> DirectionMarkers,
    IReadOnlyList<string> TimingMarkers,
    IReadOnlyList<string> Steps);

public sealed record QuestionDrivenPlannedOutputs(
    string FinalImagePath,
    string SrtPath,
    string NarrationTextPath,
    string VisualSpecPath,
    string ImagePromptPath,
    string ReviewPath);

public sealed record QuestionDrivenValidationPreview(
    bool ImageSceneSpecific,
    bool NarrationAligned,
    bool SrtReady,
    bool AccessibilityReady,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

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


public sealed record AstronomyInfographicDesignRequest(
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
    string VisualIntent,
    string ImagePromptIntent,
    string OverlayIntent,
    string AccessibilityIntent,
    bool VenusAssetAvailable,
    bool JupiterAssetAvailable);

public sealed record AstronomyInfographicDesignTemplate(
    string LayoutKey,
    string TemplateName,
    string ProfessionalInfographicIntent,
    double MaximumTextCoverage,
    double MinimumVisualInformationCoverage,
    IReadOnlyList<string> RequiredVisualAnswers,
    IReadOnlyList<string> ForbiddenPatterns,
    QuestionDrivenProgrammaticOverlayPlan OverlayPlan,
    IReadOnlyList<string> ProgrammaticLayers,
    IReadOnlyList<string> AccessibilityCues);

public interface IAstronomyInfographicDesignSystem
{
    AstronomyInfographicDesignTemplate CreateTemplate(AstronomyInfographicDesignRequest request);
}

public sealed record QuestionDrivenSceneReview(
    int SceneNumber,
    string QuestionType,
    string LayoutTemplate,
    bool ImageApproved,
    bool NarrationApproved,
    bool SrtApproved,
    bool AlignmentApproved,
    bool AccessibilityApproved,
    bool UsesLocalPlanetAssets,
    bool UsesFakeCirclePlanets,
    bool UsesCardLayout,
    int TextCoveragePercent,
    int VisualCoveragePercent,
    bool TextCollisionDetected,
    bool TextCollisionResolved,
    bool LabelOverPlanetDetected,
    bool UsesSolidPlanetBackingCircle,
    bool BlueprintZonesRespected,
    bool SignificanceLayerRendered,
    bool EnvironmentalBackgroundDistinct,
    bool UsesCardOrPanelBox,
    bool UsesHelperLayoutBox,
    bool PlanetAssetsIntegratedIntoSky,
    bool ConstellationLayerRendered,
    bool ReferenceStarLayerRendered,
    string SceneMood,
    bool ThumbnailQuality,
    bool PosterQuality,
    int VisualUniquenessScore,
    int HumanInterestScore,
    bool DecorativeCircleDetected,
    bool AtmosphericBackgroundUsed,
    bool LargeTemplateShapeDetected,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public interface IAstronomyInfographicRenderer
{
    Task RenderAsync(string finalPath, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken);
}


public sealed record EditorialAstronomyInfographicGenerationResponse(
    string EventId,
    int SceneCount,
    int PlannedInfographicCount,
    int FinalImageCount,
    int SrtCount,
    int ApprovedSceneCount,
    int FailedSceneCount,
    IReadOnlyList<QuestionDrivenPlannedScene> PlannedScenes,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AstronomyInfographicDesignTemplate>? DesignSpecs = null);

public interface IEditorialAstronomyInfographicComposer
{
    Task<EditorialAstronomyInfographicGenerationResponse> GenerateEditorialAstronomyInfographicsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken);
}

public interface IQuestionDrivenImagePromptGenerator
{
    string GeneratePrompt(QuestionDrivenImagePromptRequest request);
}

public interface IQuestionDrivenVisualComposer
{
    Task<QuestionDrivenVisualGenerationResponse> GenerateQuestionDrivenVisualsAsync(QuestionDrivenVisualGenerationRequest request, CancellationToken cancellationToken);
}
