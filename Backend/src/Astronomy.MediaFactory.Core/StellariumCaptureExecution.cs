namespace Astronomy.MediaFactory.Core;

public sealed record StellariumAssetCaptureExecutionRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? JobIds = null,
    int MaxJobs = 1,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record StellariumCaptureExecutionResult(
    int JobCount,
    int CompletedCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<string> CapturedFiles,
    IReadOnlyList<string> Warnings);

public interface IStellariumCaptureExecutionService
{
    Task<StellariumCaptureExecutionResult> ExecuteCaptureAsync(StellariumAssetCaptureExecutionRequest request, CancellationToken cancellationToken);
}
