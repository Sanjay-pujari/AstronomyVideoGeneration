using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record OpsDashboardResponse(
    SchedulerOpsSummary SchedulerStatus,
    IReadOnlyCollection<OpsPipelineRunSummary> RecentPipelineRuns,
    PlatformPublishOpsSummary PlatformPublishSummary,
    TokenHealthOpsSummary TokenHealthSummary,
    SystemHealthOpsSummary SystemHealthSummary,
    FailureOpsSummary FailureSummary,
    PerformanceOpsSummary PerformanceSummary,
    AnalyticsDashboardSummary AnalyticsSummary,
    OpsAnalyticsIntelligenceSummary AnalyticsIntelligence,
    OpsDashboardDiagnostics Diagnostics,
    IReadOnlyCollection<string> Warnings);

public sealed record OpsAnalyticsIntelligenceSummary(
    IReadOnlyCollection<PlatformContentAnalytics> TopContent,
    long TotalEngagement,
    double AverageEngagementRate,
    string? BestPlatform,
    int ViralCandidateCount);

public sealed record SchedulerOpsSummary(
    bool Enabled,
    int SchedulesCount,
    DateTimeOffset? NextPlannedRun,
    SchedulerRunRecord? LastSchedulerRun,
    IReadOnlyCollection<string> Warnings);

public sealed record OpsPipelineRunSummary(
    Guid RunId,
    ContentType ContentType,
    string LocationName,
    DateOnly TargetDate,
    PipelineRunStatus Status,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    double? DurationSeconds,
    string? FailedStage,
    string? LastError);

public sealed record OpsPipelineRunDetail(
    OpsPipelineRunSummary Run,
    PlatformPublishOpsSummary PlatformPublishSummary,
    IReadOnlyCollection<PipelineStageExecution> Stages,
    IReadOnlyCollection<string> Warnings);

public sealed record PlatformPublishOpsSummary(
    PlatformPublishStatus YouTubeLong,
    PlatformPublishStatus YouTubeShort,
    PlatformPublishStatus FacebookReel,
    PlatformPublishStatus InstagramReel,
    IReadOnlyCollection<string> Warnings);

public sealed record PlatformPublishStatus(string Status, string? Url);

public sealed record TokenHealthOpsSummary(
    bool? YouTubeValid,
    bool? MetaValid,
    string? ExpiryWarning,
    IReadOnlyCollection<string> Warnings);

public sealed record SystemHealthOpsSummary(
    long? DiskFreeSpaceBytes,
    long? OutputFolderSizeBytes,
    bool FfmpegConfigured,
    bool StellariumConfigured,
    bool SkyfieldSidecarReachable,
    bool AzureBlobStorageConfigured,
    IReadOnlyCollection<string> Warnings);

public sealed record FailureOpsSummary(
    int FailuresLast24Hours,
    int FailuresLast7Days,
    string? MostCommonFailedStage,
    IReadOnlyCollection<OpsPipelineRunSummary> Failures);

public sealed record PerformanceOpsSummary(
    double? AveragePipelineDurationSeconds,
    double? AverageRenderingDurationSeconds,
    double? AveragePublishingDurationSeconds,
    IReadOnlyCollection<string> Warnings);

public sealed record OpsDashboardDiagnostics(
    string FileName,
    bool Exists,
    DateTimeOffset? LastModifiedUtc,
    IReadOnlyCollection<string> Warnings);
