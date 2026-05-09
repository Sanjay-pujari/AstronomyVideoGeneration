namespace Astronomy.MediaFactory.Core;

public sealed record AnalyticsIntelligenceRequest(
    int Days = 14,
    string? Platform = null,
    string? ContentType = null,
    string? Location = null,
    int Limit = 10);

public sealed class AnalyticsPerformanceWeights
{
    public double Views { get; set; } = 0.35;
    public double Engagement { get; set; } = 0.25;
    public double WatchTime { get; set; } = 0.20;
    public double Shares { get; set; } = 0.10;
    public double Retention { get; set; } = 0.10;
}

public sealed record AnalyticsDashboardResponse(
    AnalyticsOverallSummary OverallSummary,
    IReadOnlyCollection<AnalyticsPlatformBreakdown> PlatformBreakdown,
    IReadOnlyCollection<AnalyticsContentTypeBreakdown> ContentTypeBreakdown,
    AnalyticsTimeIntelligence TimeIntelligence,
    AstronomyIntelligenceSummary AstronomyIntelligence,
    ReelIntelligenceSummary ReelIntelligence,
    ThumbnailIntelligenceSummary ThumbnailIntelligence,
    AnalyticsTrendSummary Trends,
    AnalyticsChartData Charts,
    IReadOnlyCollection<AnalyticsInsight> Insights);

public sealed record AnalyticsOverallSummary(
    int TotalContentPublished,
    long TotalViews,
    long TotalEngagement,
    double AverageEngagementRate,
    double AverageWatchDurationSeconds,
    string? BestPerformingPlatform,
    string? BestPerformingContentType);

public sealed record AnalyticsPlatformBreakdown(
    string Platform,
    int ContentCount,
    long TotalViews,
    double AverageEngagement,
    double AverageWatchDurationSeconds,
    AnalyticsTopContentItem? TopPost);

public sealed record AnalyticsContentTypeBreakdown(
    string ContentType,
    int ContentCount,
    long TotalViews,
    double AverageEngagementRate,
    double AverageWatchDurationSeconds,
    double AveragePerformanceScore);

public sealed record AnalyticsTimeIntelligence(
    int? BestPublishHour,
    string? BestWeekday,
    string? BestTimezone,
    string? HighestEngagementWindow);

public sealed record AstronomyIntelligenceSummary(
    string? TopObjectByViews,
    string? TopObjectByEngagement,
    string? FastestGrowingTopic,
    IReadOnlyCollection<AstronomyObjectPerformance> Objects);

public sealed record AstronomyObjectPerformance(
    string ObjectName,
    int ContentCount,
    long TotalViews,
    long TotalEngagement,
    double AverageEngagementRate,
    double GrowthRate);

public sealed record ReelIntelligenceSummary(
    string? BestDurationRange,
    string? BestPerformingHook,
    string? BestEngagementRange,
    double? AverageRetention,
    IReadOnlyCollection<DurationBucketPerformance> DurationBuckets);

public sealed record DurationBucketPerformance(
    string Range,
    int ContentCount,
    long TotalViews,
    double AverageEngagementRate,
    double AverageRetention,
    double AveragePerformanceScore);

public sealed record ThumbnailIntelligenceSummary(
    string? BestThumbnailVariant,
    double? AverageCtrForBestVariant,
    string? TopThumbnailImage,
    IReadOnlyCollection<ThumbnailVariantPerformance> Variants);

public sealed record ThumbnailVariantPerformance(
    string Variant,
    int ContentCount,
    double? AverageCtr,
    long TotalViews,
    double AveragePerformanceScore,
    string? TopImage);

public sealed record AnalyticsTopContentItem(
    Guid Id,
    Guid? PipelineRunId,
    string Platform,
    string ContentType,
    string MediaId,
    string? Title,
    string? Url,
    long Views,
    long Engagement,
    double EngagementRate,
    double? WatchDurationSeconds,
    double? Retention,
    long Shares,
    long Saves,
    double PerformanceScore,
    DateTimeOffset? PublishedUtc,
    DateTimeOffset CollectedUtc,
    string? ThumbnailPath,
    IReadOnlyCollection<string> AstronomyObjects);

public sealed record AnalyticsTrendSummary(
    AnalyticsTopContentItem? FastestGrowingContent,
    IReadOnlyCollection<AnalyticsTopContentItem> RecentSpikes,
    IReadOnlyCollection<AnalyticsTopContentItem> UnderperformingContent,
    IReadOnlyCollection<AnalyticsTopContentItem> ViralCandidates);

public sealed record AnalyticsInsight(string Category, string Message, double? Value = null);

public sealed record AnalyticsChartData(
    IReadOnlyCollection<DailyViewsPoint> DailyViews,
    IReadOnlyCollection<PlatformComparisonPoint> PlatformComparison,
    IReadOnlyCollection<ObjectPerformancePoint> ObjectPerformance,
    IReadOnlyCollection<DurationPerformancePoint> DurationPerformance,
    IReadOnlyCollection<EngagementTrendPoint> EngagementTrends);

public sealed record DailyViewsPoint(DateOnly Date, long Views, int ContentCount);
public sealed record PlatformComparisonPoint(string Platform, long Views, long Engagement, double AverageWatchDurationSeconds, double AveragePerformanceScore);
public sealed record ObjectPerformancePoint(string ObjectName, long Views, long Engagement, double AverageEngagementRate);
public sealed record DurationPerformancePoint(string Range, long Views, double AverageRetention, double AverageEngagementRate);
public sealed record EngagementTrendPoint(DateOnly Date, double EngagementRate, long Engagement);
