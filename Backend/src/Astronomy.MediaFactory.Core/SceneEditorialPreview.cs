namespace Astronomy.MediaFactory.Core;

public sealed record SceneEditorialPreviewRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    int? MaxPlans = 1,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record SceneEditorialPreviewResponse(
    int PlanCount,
    int SceneCount,
    int ApprovedSceneCount,
    int FailedSceneCount,
    int ApprovedPlanCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SceneEditorialPreviewOutput> PlannedOutputs);

public sealed record SceneEditorialPreviewOutput(
    string ContentGenerationPlanId,
    string RegionId,
    int SceneNumber,
    string CardPath,
    string SrtPath,
    string ReviewPath,
    bool VisualApproved,
    bool NarrationApproved,
    bool AlignmentApproved,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public sealed record SceneEditorialReview(
    int SceneNumber,
    bool VisualApproved,
    bool NarrationApproved,
    bool AlignmentApproved,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations);

public interface ISceneEditorialPreviewService
{
    Task<SceneEditorialPreviewResponse> GenerateSceneEditorialPreviewAsync(SceneEditorialPreviewRequest request, CancellationToken cancellationToken);
}
