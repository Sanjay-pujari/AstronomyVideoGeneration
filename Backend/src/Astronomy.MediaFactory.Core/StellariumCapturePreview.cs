namespace Astronomy.MediaFactory.Core;

public sealed record StellariumCapturePreviewRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? JobIds = null,
    int MaxJobs = 50);

public sealed record StellariumCapturePreviewResult(
    int JobCount,
    int Valid,
    int Warnings,
    int Invalid,
    IReadOnlyList<StellariumCapturePreview> CapturePreviews);

public sealed record StellariumCapturePreview(
    Guid JobId,
    Guid ContentGenerationPlanId,
    Guid? AstronomyEventIntelligenceId,
    string SscFile,
    string MetadataFile,
    IReadOnlyList<string> TargetObjects,
    string? ScheduledUtc,
    string? PeakUtc,
    string? Orientation,
    bool RequiresLabels,
    bool RequiresConstellationLines,
    bool RequiresLandscape,
    string ExpectedCapturePath,
    string CaptureCommandPreview,
    string ValidationStatus,
    IReadOnlyList<string> Warnings);

public interface IStellariumCapturePreviewService
{
    Task<StellariumCapturePreviewResult> PreviewCaptureAsync(StellariumCapturePreviewRequest request, CancellationToken cancellationToken);
}
