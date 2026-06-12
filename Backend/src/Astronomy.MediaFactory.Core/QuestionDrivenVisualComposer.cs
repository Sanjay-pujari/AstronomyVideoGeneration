namespace Astronomy.MediaFactory.Core;

public sealed record QuestionDrivenVisualGenerationRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    bool OverwriteExisting = false,
    ProductionPipelineExecutionContext? ProductionContext = null);

public sealed record QuestionDrivenVisualGenerationResponse(
    string EventId,
    int SceneCount,
    int FinalImageCount,
    int SrtCount,
    int ApprovedSceneCount,
    int FailedSceneCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    string CompositionMode = "SceneInfographic",
    bool UsesSharedAstronomyVisualComposer = true,
    bool HeroAssetRulesApplied = false,
    bool DuplicateObjectRenderingDetected = false,
    int PlannedImageCount = 0,
    int PlannedSrtCount = 0,
    int PlannedReviewCount = 0,
    IReadOnlyList<QuestionDrivenPlannedScene>? PlannedScenes = null,
    int QuestionIsolationScore = 100,
    bool CrossSceneLeakageDetected = false,
    IReadOnlyList<SceneQuestionIsolationValidation>? SceneValidation = null,
    string AstronomySceneEngineV1Status = "FROZEN",
    string SharedAstronomyVisualComposerStatus = "FROZEN");

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
    string ReviewPath,
    QuestionDrivenPresentationVariants? PresentationVariants = null);

public sealed record QuestionDrivenPresentationVariants(
    string LongFormFinalImagePath,
    string ShortFormFinalImagePath);

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
    DateTimeOffset GeneratedUtc,
    string EventType,
    bool UsesLocalPlanetAssets,
    string? BestViewingWindowLocal = null,
    IReadOnlyDictionary<string, string>? StrategyValidationFacts = null,
    IReadOnlyList<SceneDrawableVisualObject>? DrawableVisualObjects = null,
    IReadOnlyList<string>? RequiredVisualObjects = null,
    VisualSourceResolutionResult? VisualSourceResolution = null);

public sealed record SceneDrawableVisualObject(
    string ObjectType,
    string? Phase = null,
    string? Size = null,
    bool Glow = false,
    string? Label = null,
    string? Placement = null,
    string? ObjectVisualSource = null,
    string? AssetKey = null,
    string? GeneratedRealisticPrompt = null,
    bool PrimitivePlaceholderUsed = false,
    CelestialObjectQuality CelestialObjectQuality = CelestialObjectQuality.Realistic);


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
    int BackgroundRealismScore,
    int AstronomyPhotographyScore,
    int ClickabilityScore,
    int AtmosphericDepthScore,
    int EditorialQualityScore,
    int ShareabilityScore,
    int TwilightQualityScore,
    int StarfieldRealismScore,
    bool VisibleHorizontalBanding,
    bool SmoothSkyGradient,
    bool DecorativeCircleDetected,
    bool AtmosphericBackgroundUsed,
    bool LargeTemplateShapeDetected,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public sealed record AstronomyInfographicRenderVariant(
    string VariantName,
    int Width,
    int Height,
    float TextScale,
    float InformationDensity,
    string SafeAreaIntent,
    string VisualEmphasisIntent)
{
    public static AstronomyInfographicRenderVariant LongForm { get; } = new(
        "LongForm",
        1920,
        1080,
        1.0f,
        1.0f,
        "16:9 editorial safe area with horizon/planet labels separated from frame edges",
        "approved landscape editorial infographic composition");

    public static AstronomyInfographicRenderVariant ShortForm { get; } = new(
        "ShortForm",
        1080,
        1920,
        1.22f,
        0.72f,
        "9:16 short-form safe area with top/bottom platform UI margins and centered astronomy action",
        "vertical emphasis on the same question answer, planets, timing, direction, and viewer takeaway");
}

public interface IAstronomyInfographicRenderer
{
    Task RenderAsync(string finalPath, QuestionDrivenVisualSpec spec, string venusAssetPath, string jupiterAssetPath, CancellationToken cancellationToken, AstronomyInfographicRenderVariant? variant = null);
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
    IReadOnlyList<AstronomyInfographicDesignTemplate>? DesignSpecs = null,
    string CompositionMode = "SceneInfographic",
    bool UsesSharedAstronomyVisualComposer = true,
    int QuestionIsolationScore = 100,
    bool CrossSceneLeakageDetected = false,
    IReadOnlyList<SceneQuestionIsolationValidation>? SceneValidation = null,
    string AstronomySceneEngineV1Status = "FROZEN",
    string SharedAstronomyVisualComposerStatus = "FROZEN",
    bool HeroAssetRulesApplied = false,
    bool DuplicateObjectRenderingDetected = false,
    SceneVariantFinalImagesResponse? SceneVariantFinalImages = null,
    SceneVariantGenerationDiagnostics? Diagnostics = null,
    ShortFormValidation? ShortFormValidation = null);

public sealed record SceneVariantFinalImagesResponse(
    SceneVariantFinalImageSet LongForm,
    SceneVariantFinalImageSet ShortForm);

public sealed record SceneVariantFinalImageSet(
    string Profile,
    string BaseDirectory,
    int Width,
    int Height,
    IReadOnlyDictionary<string, string> Images);

public sealed record SceneVariantGenerationDiagnostics(
    bool SceneVariantGenerationEnabled,
    bool LongFormGenerated,
    bool ShortFormGenerated,
    int LongFormImageCount,
    int ShortFormImageCount);

public sealed record ShortFormValidation(
    bool NativeShortFormComposerUsed,
    bool EmbeddedLongFormImageDetected,
    bool InnerFrameDetected,
    int ShortFormImageCount,
    int ShortFormWidth,
    int ShortFormHeight,
    int ShortFormReadabilityScore,
    int ShortFormReelSuitabilityScore);

public sealed record SceneQuestionIsolationValidation(
    int SceneNumber,
    string ExpectedQuestion,
    int IsolationScore,
    IReadOnlyList<string> LeakageWarnings);

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
