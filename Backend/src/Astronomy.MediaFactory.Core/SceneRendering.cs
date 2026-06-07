namespace Astronomy.MediaFactory.Core;

public sealed record SceneRenderRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    int? MaxPlans = 1,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record SceneRenderResponse(
    int PlanCount,
    int SceneCount,
    int CompletedCount,
    int FailedCount,
    IReadOnlyList<string> RenderedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SceneRenderingPlanItem> RenderingPlan);

public sealed record SceneRenderingPlanItem(
    string ContentGenerationPlanId,
    string RegionId,
    int SceneNumber,
    string SceneName,
    double DurationSeconds,
    string RecipePath,
    string CapabilityPath,
    string AudioPath,
    string OutputVideoPath,
    string VisualSourcePath,
    string VisualRenderer,
    string MotionRenderer,
    bool ReadyToRender,
    IReadOnlyList<string> Warnings);

public sealed record SceneRenderManifest(
    string ContentGenerationPlanId,
    int SceneCount,
    int CompletedCount,
    int FailedCount,
    IReadOnlyList<SceneRenderManifestOutput> SceneOutputs,
    DateTimeOffset GeneratedUtc);

public sealed record SceneRenderManifestOutput(
    int SceneNumber,
    string SceneName,
    string OutputVideoPath,
    string AudioPath,
    string VisualSourcePath,
    double RecipeDurationSeconds,
    double RenderedDurationSeconds,
    long FileSizeBytes,
    bool HasAudioStream,
    bool HasVideoStream,
    string Status,
    IReadOnlyList<string> Warnings);

public interface ISceneRenderer
{
    Task<SceneRenderResponse> RenderScenesAsync(SceneRenderRequest request, CancellationToken cancellationToken);
}
