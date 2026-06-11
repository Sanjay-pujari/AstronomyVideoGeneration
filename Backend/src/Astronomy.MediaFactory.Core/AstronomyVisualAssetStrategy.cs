namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyVisualAssetStrategyRequest(
    string EventId,
    string RegionId,
    string Language = "en",
    bool DryRun = true,
    ProductionPipelineExecutionContext? ProductionContext = null);

public sealed record AstronomyVisualAssetStrategyResponse(
    string EventId,
    string RegionId,
    string Language,
    bool IsReadyForInfographicGeneration,
    AstronomyVisualAssetStrategy AssetStrategy,
    IReadOnlyList<AstronomySceneAssetPlan> SceneAssetPlans,
    IReadOnlyList<AstronomyMissingAsset> MissingAssets,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);

public sealed record AstronomyVisualAssetStrategy(
    VisualLayerPlan BackgroundLayer,
    VisualLayerPlan CelestialObjectLayer,
    VisualLayerPlan ConstellationLayer,
    VisualLayerPlan SkyGuidanceLayer,
    VisualLayerPlan EducationalLayer,
    VisualLayerPlan AnnotationLayer);

public sealed record VisualLayerPlan(
    string Purpose,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> MustNotProvide,
    IReadOnlyList<string> AvailableAssets,
    IReadOnlyList<string> MissingAssets,
    IReadOnlyList<string> ValidationRules,
    bool IsAvailable,
    bool IsProductionApproved);

public sealed record AstronomySceneAssetPlan(
    int SceneNumber,
    string SceneKey,
    string ScenePurpose,
    IReadOnlyList<string> BackgroundRequirements,
    IReadOnlyList<string> CelestialObjects,
    IReadOnlyList<string> ConstellationOrReferenceRequirements,
    IReadOnlyList<string> SkyGuidanceElements,
    IReadOnlyList<string> EducationalElements,
    IReadOnlyList<string> AnnotationElements,
    bool UsesCardLayout,
    bool UsesFakeCirclePlanets,
    bool IsNonCardComposition,
    bool IsReadyForInfographicGeneration,
    IReadOnlyList<string> MissingAssets,
    IReadOnlyList<string> Warnings);

public sealed record AstronomyMissingAsset(
    string AssetType,
    string AssetKey,
    string ExpectedPath,
    string Reason,
    bool BlocksInfographicGeneration);

public interface IAstronomyVisualAssetStrategyService
{
    Task<AstronomyVisualAssetStrategyResponse> ResolveAstronomyVisualAssetStrategyAsync(AstronomyVisualAssetStrategyRequest request, CancellationToken cancellationToken);
}
