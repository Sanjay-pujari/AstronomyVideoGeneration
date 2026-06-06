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
    IReadOnlyList<string> Warnings,
    IReadOnlyList<StellariumCaptureValidationSummary>? ValidationResults = null,
    string? ValidationStatus = null,
    long? FileSizeBytes = null,
    int? ImageWidth = null,
    int? ImageHeight = null,
    int RetryCount = 0);

public sealed record StellariumCaptureValidationSummary(
    Guid JobId,
    string SscPath,
    string CapturePath,
    int CaptureAttemptCount,
    string ValidationStatus,
    long? FileSizeBytes,
    int? ImageWidth,
    int? ImageHeight,
    int RetryCount,
    IReadOnlyList<string> Warnings);

public interface IStellariumCaptureExecutionService
{
    Task<StellariumCaptureExecutionResult> ExecuteCaptureAsync(StellariumAssetCaptureExecutionRequest request, CancellationToken cancellationToken);
}
