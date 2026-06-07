namespace Astronomy.MediaFactory.Core;

public sealed record VisualAssetGenerationRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    int? MaxPlans = 1,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record VisualAssetGenerationResponse(
    int PlanCount,
    int SceneCount,
    int GeneratedVisualCount,
    int ApprovedVisualCount,
    int FailedVisualCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<VisualAssetGenerationPlanItem> PlannedVisualOutputs);

public sealed record VisualAssetGenerationPlanItem(
    string ContentGenerationPlanId,
    string RegionId,
    int SceneNumber,
    string SceneName,
    string BackgroundPath,
    string OverlayPath,
    string ManifestPath,
    string VisualSourceType,
    string SourcePath,
    IReadOnlyList<string> Objects,
    IReadOnlyList<string> Issues);

public sealed record SceneVisualAssetManifest(
    int SceneNumber,
    string PrimaryVisualPath,
    string OverlayVisualPath,
    string VisualSourceType,
    IReadOnlyList<string> Objects,
    bool IsProductionVisual,
    IReadOnlyList<string> Issues,
    DateTimeOffset GeneratedUtc);

public interface IVisualAssetGenerationService
{
    Task<VisualAssetGenerationResponse> GenerateVisualAssetsAsync(VisualAssetGenerationRequest request, CancellationToken cancellationToken);
}
