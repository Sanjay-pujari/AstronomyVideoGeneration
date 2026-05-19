using Astronomy.MediaFactory.Core.Common;

namespace Astronomy.MediaFactory.Analytics;

public abstract class AnalyticsRecordBase : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string Platform { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Language { get; set; } = "en";
    public string RegionId { get; set; } = "";
    public DateTimeOffset PublishedAtUtc { get; set; }
    public long Impressions { get; set; }
    public long Views { get; set; }
    public double Ctr { get; set; }
    public double AverageWatchDuration { get; set; }
    public double WatchTimeMinutes { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public long SubscribersGained { get; set; }
}

public sealed class PlatformVideoAnalytics : AnalyticsRecordBase;

public sealed class PlatformPostAnalytics : AnalyticsRecordBase;

public sealed class AudienceAnalytics : AnalyticsRecordBase;

public sealed class ThumbnailPerformance : AnalyticsRecordBase;

public sealed class HookPerformance : AnalyticsRecordBase;

public sealed class DailyPerformanceSummary : AnalyticsRecordBase
{
    public DateOnly SummaryDate { get; set; }
}

public sealed record AnalyticsIngestionDto(
    Guid PipelineRunId,
    long Impressions,
    long Views,
    double Ctr,
    double AverageWatchDuration,
    double WatchTimeMinutes,
    long Likes,
    long Comments,
    long Shares,
    long SubscribersGained,
    string Platform,
    string ContentType,
    string Language,
    string RegionId,
    DateTimeOffset PublishedAtUtc);

public sealed record AnalyticsSummaryDto(
    long Impressions,
    long Views,
    double Ctr,
    double WatchTimeMinutes,
    long Likes,
    long Comments,
    long Shares,
    long SubscribersGained);
