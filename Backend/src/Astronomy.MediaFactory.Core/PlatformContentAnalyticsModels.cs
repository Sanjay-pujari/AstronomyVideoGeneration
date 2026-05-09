using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class PlatformContentAnalytics : Common.EntityBase
{
    public Guid? PipelineRunId { get; set; }
    public string Platform { get; set; } = "";
    public string PlatformContentType { get; set; } = "";
    public string PlatformMediaId { get; set; } = "";
    public string? PlatformUrl { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset? PublishedUtc { get; set; }
    public DateTimeOffset CollectedUtc { get; set; } = DateTimeOffset.UtcNow;
    public long? Views { get; set; }
    public long? Likes { get; set; }
    public long? Comments { get; set; }
    public long? Shares { get; set; }
    public long? Reach { get; set; }
    public long? Impressions { get; set; }
    public double? WatchTimeMinutes { get; set; }
    public double? AverageViewDurationSeconds { get; set; }
    public double? Ctr { get; set; }
    public double? EngagementRate { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Hashtags { get; set; }
    public string? LocationName { get; set; }
    public DateOnly? TargetDate { get; set; }
    public ContentType? ContentCategory { get; set; }
    public string? ThumbnailPath { get; set; }
    public double? PerformanceScore { get; set; }
    public bool IsAnalyticsAvailable { get; set; } = true;
    public string? LastError { get; set; }
}

public sealed record PlatformAnalyticsQuery(
    int Days = 14,
    string? Platform = null,
    string? Location = null,
    string? ContentType = null,
    int Take = 100);

public sealed record PlatformAnalyticsCollectionContext(
    Guid? PipelineRunId,
    string Platform,
    string PlatformContentType,
    string PlatformMediaId,
    string? PlatformUrl,
    string? Title,
    DateTimeOffset? PublishedUtc,
    int? DurationSeconds,
    string? Hashtags,
    string? LocationName,
    DateOnly? TargetDate,
    ContentType? ContentCategory,
    string? ThumbnailPath,
    string? OutputDirectory);

public sealed record AnalyticsCollectionReport(
    string Platform,
    int MediaCount,
    int SuccessCount,
    int FailureCount,
    DateTimeOffset CollectedUtc,
    IReadOnlyCollection<string> Warnings);

public sealed record AnalyticsCollectionResult(IReadOnlyCollection<AnalyticsCollectionReport> PlatformReports);

public sealed record AnalyticsDashboardSummary(
    IReadOnlyCollection<PlatformContentAnalytics> TopPerformingContent,
    long TotalViews,
    long TotalEngagement,
    string? BestPerformingPlatform,
    PlatformContentAnalytics? BestPerformingReel,
    int? BestPerformingPublishHourUtc);
