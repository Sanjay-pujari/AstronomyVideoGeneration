namespace Astronomy.MediaFactory.Core;

public sealed record AssetExecutionRequest(
    IReadOnlyList<Guid>? JobIds = null,
    IReadOnlyList<string>? AssetTypes = null,
    string? RegionId = null,
    int MaxJobs = 50,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record AssetExecutionResult(
    int JobCount,
    int CompletedCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> Warnings);

public interface IAssetExecutionService
{
    Task<AssetExecutionResult> ExecuteRequiredAssetsAsync(AssetExecutionRequest request, CancellationToken cancellationToken);
}
