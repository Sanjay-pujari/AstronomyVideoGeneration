namespace Astronomy.MediaFactory.Core;

public sealed record ProductionVisualGenerationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    int? MaxPlans = 1,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record ProductionVisualGenerationResponse(
    int PlanCount,
    int SceneCount,
    int VisualSpecCount,
    int AiImageCount,
    int FinalImageCount,
    int ApprovedImageCount,
    int FailedImageCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProductionVisualPlanItem> PlannedVisuals);

public sealed record ProductionVisualPlanItem(
    string ContentGenerationPlanId,
    string RegionId,
    int SceneNumber,
    string VisualSpecPath,
    string AiBackgroundPath,
    string FinalImagePath,
    string ImagePrompt,
    IReadOnlyList<string> OverlayText,
    IReadOnlyList<string> LocalAssetObjects,
    IReadOnlyList<string> Warnings);

public sealed record SceneVisualSpec(
    int SceneNumber,
    string ScenePurpose,
    string EventTitle,
    IReadOnlyList<string> Objects,
    string Location,
    string Direction,
    string BestViewingTime,
    string VisualStyle,
    string ImagePrompt,
    IReadOnlyList<string> OverlayText,
    IReadOnlyList<string> LocalAssetObjects,
    bool RequiresAiBackground,
    bool RequiresSkyOverlay,
    bool RequiresObjectOverlay);

public interface IProductionVisualComposerService
{
    Task<ProductionVisualGenerationResponse> GenerateProductionVisualsAsync(ProductionVisualGenerationRequest request, CancellationToken cancellationToken);
}
