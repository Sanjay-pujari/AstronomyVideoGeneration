using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class AnalyticsIntelligenceService : IAnalyticsIntelligenceService
{
    private static readonly string[] SupportedPlatforms = ["YouTube", "Facebook", "Instagram"];
    private static readonly string[] SupportedContentTypes = ["Long videos", "Shorts", "Reels"];
    private static readonly string[] AstronomyTopics = ["Moon", "Venus", "Mars", "Jupiter", "Saturn", "Orion Nebula", "Meteor Shower", "Eclipse", "Comet", "Galaxy", "Cluster"];
    private static readonly Regex TokenCleanupRegex = new("[^a-zA-Z0-9 #:-]+", RegexOptions.Compiled);

    private readonly MediaFactoryDbContext _db;
    private readonly AnalyticsOptions _analyticsOptions;
    private readonly MaintenanceOptions _maintenanceOptions;

    public AnalyticsIntelligenceService(MediaFactoryDbContext db, IOptions<AnalyticsOptions> analyticsOptions, IOptions<MaintenanceOptions> maintenanceOptions)
    {
        _db = db;
        _analyticsOptions = analyticsOptions.Value;
        _maintenanceOptions = maintenanceOptions.Value;
    }

    public async Task<AnalyticsDashboardResponse> BuildDashboardAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(request, cancellationToken);
        ApplyPerformanceScores(context.Items);
        await PersistPerformanceScoresAsync(context.Items, cancellationToken);

        var topContent = BuildTopContent(context, request.Limit);
        var objectPerformance = BuildAstronomyPerformance(context);
        var durationBuckets = BuildDurationBuckets(context);
        var thumbnailSummary = BuildThumbnailIntelligence(context);
        var trends = BuildTrends(context, topContent);
        var insights = BuildInsights(context, objectPerformance, durationBuckets, thumbnailSummary, trends);
        await StoreInsightsAsync(insights, cancellationToken);

        return new AnalyticsDashboardResponse(
            BuildOverallSummary(context),
            BuildPlatformBreakdown(context),
            BuildContentTypeBreakdown(context),
            BuildTimeIntelligence(context),
            BuildAstronomySummary(objectPerformance),
            BuildReelIntelligence(context, durationBuckets),
            thumbnailSummary,
            trends,
            BuildChartData(context, objectPerformance, durationBuckets),
            insights);
    }

    public async Task<IReadOnlyCollection<AnalyticsTopContentItem>> GetTopContentAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(request, cancellationToken);
        ApplyPerformanceScores(context.Items);
        await PersistPerformanceScoresAsync(context.Items, cancellationToken);
        return BuildTopContent(context, request.Limit);
    }

    public async Task<IReadOnlyCollection<AnalyticsInsight>> GetInsightsAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
        => (await BuildDashboardAsync(request, cancellationToken)).Insights;

    public async Task<IReadOnlyCollection<AnalyticsPlatformBreakdown>> GetPlatformSummaryAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
        => BuildPlatformBreakdown(await LoadContextAsync(request, cancellationToken));

    public async Task<IReadOnlyCollection<AnalyticsContentTypeBreakdown>> GetContentPerformanceAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(request, cancellationToken);
        ApplyPerformanceScores(context.Items);
        return BuildContentTypeBreakdown(context);
    }

    private async Task<IntelligenceContext> LoadContextAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(request.Days <= 0 ? 14 : request.Days, 1, 365);
        var take = Math.Clamp(request.Limit <= 0 ? _analyticsOptions.TopN : request.Limit, 1, 500);
        var from = DateTimeOffset.UtcNow.AddDays(-days);
        var query = _db.PlatformContentAnalytics.AsNoTracking().Where(x => x.CollectedUtc >= from && x.IsAnalyticsAvailable);

        if (!string.IsNullOrWhiteSpace(request.Platform))
            query = query.Where(x => x.Platform == request.Platform);
        if (!string.IsNullOrWhiteSpace(request.Location))
            query = query.Where(x => x.LocationName != null && x.LocationName.Contains(request.Location));
        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            if (Enum.TryParse<ContentType>(request.ContentType, true, out var contentType))
                query = query.Where(x => x.ContentCategory == contentType);
            else
            {
                var normalizedContentType = request.ContentType.Trim();
                if (normalizedContentType.Equals("Long videos", StringComparison.OrdinalIgnoreCase) || normalizedContentType.Equals("Long", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.PlatformContentType.Contains("Long") || x.PlatformContentType.Contains("Video"));
                else if (normalizedContentType.Equals("Shorts", StringComparison.OrdinalIgnoreCase) || normalizedContentType.Equals("Short", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.PlatformContentType.Contains("Short"));
                else if (normalizedContentType.Equals("Reels", StringComparison.OrdinalIgnoreCase) || normalizedContentType.Equals("Reel", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.PlatformContentType.Contains("Reel"));
                else
                    query = query.Where(x => x.PlatformContentType == normalizedContentType || x.PlatformContentType.Contains(normalizedContentType));
            }
        }

        var items = await query.OrderByDescending(x => x.CollectedUtc).Take(Math.Max(take * 10, 100)).ToListAsync(cancellationToken);
        var runIds = items.Select(x => x.PipelineRunId).OfType<Guid>().Distinct().ToArray();
        var scripts = runIds.Length == 0
            ? new List<GeneratedScript>()
            : await _db.GeneratedScripts.AsNoTracking().Where(x => runIds.Contains(x.PipelineRunId)).ToListAsync(cancellationToken);
        var runs = runIds.Length == 0
            ? new List<PipelineRun>()
            : await _db.PipelineRuns.AsNoTracking().Where(x => runIds.Contains(x.Id)).ToListAsync(cancellationToken);

        return new IntelligenceContext(items, scripts.ToDictionary(x => x.PipelineRunId), runs.ToDictionary(x => x.Id), days);
    }

    private void ApplyPerformanceScores(IReadOnlyCollection<PlatformContentAnalytics> items)
    {
        if (items.Count == 0)
            return;

        var maxViews = Math.Max(1, items.Max(x => x.Views ?? 0));
        var maxEngagement = Math.Max(1, items.Max(EngagementValue));
        var maxWatch = Math.Max(1, items.Max(x => x.WatchTimeMinutes ?? 0));
        var maxShares = Math.Max(1, items.Max(x => x.Shares ?? 0));
        var weights = GetWeights();

        foreach (var item in items)
        {
            var score =
                weights.Views * Normalize(item.Views ?? 0, maxViews) +
                weights.Engagement * Normalize(EngagementValue(item), maxEngagement) +
                weights.WatchTime * Normalize(item.WatchTimeMinutes ?? 0, maxWatch) +
                weights.Shares * Normalize(item.Shares ?? 0, maxShares) +
                weights.Retention * Math.Clamp(Retention(item) ?? 0, 0, 1);
            item.PerformanceScore = Math.Round(score * 100, 2);
        }
    }

    private async Task PersistPerformanceScoresAsync(IReadOnlyCollection<PlatformContentAnalytics> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var ids = items.Select(x => x.Id).ToArray();
        var scores = items.ToDictionary(x => x.Id, x => x.PerformanceScore);
        var tracked = await _db.PlatformContentAnalytics.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var item in tracked)
        {
            if (scores.TryGetValue(item.Id, out var score))
                item.PerformanceScore = score;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private AnalyticsPerformanceWeights GetWeights() => new()
    {
        Views = _analyticsOptions.PerformanceScoreViewsWeight,
        Engagement = _analyticsOptions.PerformanceScoreEngagementWeight,
        WatchTime = _analyticsOptions.PerformanceScoreWatchTimeWeight,
        Shares = _analyticsOptions.PerformanceScoreSharesWeight,
        Retention = _analyticsOptions.PerformanceScoreRetentionWeight
    };

    private static AnalyticsOverallSummary BuildOverallSummary(IntelligenceContext context)
    {
        var items = context.Items;
        var bestPlatform = items.GroupBy(x => x.Platform).OrderByDescending(g => g.Average(x => x.PerformanceScore ?? 0)).Select(g => g.Key).FirstOrDefault();
        var bestType = items.GroupBy(ContentTypeLabel).OrderByDescending(g => g.Average(x => x.PerformanceScore ?? 0)).Select(g => g.Key).FirstOrDefault();
        return new AnalyticsOverallSummary(
            items.Select(IdentityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            items.Sum(x => x.Views ?? 0),
            items.Sum(EngagementValue),
            AverageOrZero(items, EngagementRate),
            AverageOrZero(items, x => x.AverageViewDurationSeconds ?? 0),
            bestPlatform,
            bestType);
    }

    private static IReadOnlyCollection<AnalyticsPlatformBreakdown> BuildPlatformBreakdown(IntelligenceContext context)
        => SupportedPlatforms.Select(platform =>
        {
            var items = context.Items.Where(x => x.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase)).ToArray();
            var top = items.OrderByDescending(x => x.PerformanceScore ?? 0).ThenByDescending(x => x.Views ?? 0).FirstOrDefault();
            return new AnalyticsPlatformBreakdown(platform, items.Length, items.Sum(x => x.Views ?? 0), AverageOrZero(items, x => EngagementValue(x)), AverageOrZero(items, x => x.AverageViewDurationSeconds ?? 0), top is null ? null : ToTopContentItem(context, top));
        }).ToArray();

    private static IReadOnlyCollection<AnalyticsContentTypeBreakdown> BuildContentTypeBreakdown(IntelligenceContext context)
        => SupportedContentTypes.Select(type =>
        {
            var items = context.Items.Where(x => ContentTypeLabel(x).Equals(type, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new AnalyticsContentTypeBreakdown(type, items.Length, items.Sum(x => x.Views ?? 0), AverageOrZero(items, EngagementRate), AverageOrZero(items, x => x.AverageViewDurationSeconds ?? 0), AverageOrZero(items, x => x.PerformanceScore ?? 0));
        }).ToArray();

    private static AnalyticsTimeIntelligence BuildTimeIntelligence(IntelligenceContext context)
    {
        var withPublishTime = context.Items.Where(x => x.PublishedUtc.HasValue).ToArray();
        var bestHour = withPublishTime.GroupBy(x => x.PublishedUtc!.Value.Hour).OrderByDescending(g => g.Average(EngagementRate)).Select(g => (int?)g.Key).FirstOrDefault();
        var bestWeekday = withPublishTime.GroupBy(x => x.PublishedUtc!.Value.DayOfWeek).OrderByDescending(g => g.Average(EngagementRate)).Select(g => g.Key.ToString()).FirstOrDefault();
        var bestTimezone = context.Runs.Values.GroupBy(x => x.TimeZone).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var window = bestHour.HasValue ? $"{bestWeekday ?? "Best day"} {bestHour.Value:00}:00-{(bestHour.Value + 1) % 24:00}:00 {bestTimezone ?? "UTC"}" : null;
        return new AnalyticsTimeIntelligence(bestHour, bestWeekday, bestTimezone, window);
    }

    private static IReadOnlyCollection<AstronomyObjectPerformance> BuildAstronomyPerformance(IntelligenceContext context)
        => AstronomyTopics.Select(topic =>
        {
            var items = context.Items.Where(x => ExtractAstronomyObjects(context, x).Contains(topic, StringComparer.OrdinalIgnoreCase)).ToArray();
            return new AstronomyObjectPerformance(topic, items.Length, items.Sum(x => x.Views ?? 0), items.Sum(EngagementValue), AverageOrZero(items, EngagementRate), GrowthRate(items, context.Days));
        }).Where(x => x.ContentCount > 0).OrderByDescending(x => x.TotalViews).ToArray();

    private static AstronomyIntelligenceSummary BuildAstronomySummary(IReadOnlyCollection<AstronomyObjectPerformance> objects)
        => new(
            objects.OrderByDescending(x => x.TotalViews).FirstOrDefault()?.ObjectName,
            objects.OrderByDescending(x => x.TotalEngagement).FirstOrDefault()?.ObjectName,
            objects.OrderByDescending(x => x.GrowthRate).FirstOrDefault()?.ObjectName,
            objects);

    private static ReelIntelligenceSummary BuildReelIntelligence(IntelligenceContext context, IReadOnlyCollection<DurationBucketPerformance> buckets)
    {
        var reels = context.Items.Where(IsShortOrReel).ToArray();
        var bestBucket = buckets.OrderByDescending(x => x.AveragePerformanceScore).FirstOrDefault();
        var bestHook = reels.Select(x => ResolveHook(context, x)).Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault();
        var bestEngagement = reels.Length == 0 ? null : EngagementRange(AverageOrZero(reels, EngagementRate));
        var retentionSamples = reels.Select(Retention).OfType<double>().ToArray();
        return new ReelIntelligenceSummary(bestBucket?.Range, bestHook, bestEngagement, retentionSamples.Length == 0 ? null : retentionSamples.Average(), buckets);
    }

    private static IReadOnlyCollection<DurationBucketPerformance> BuildDurationBuckets(IntelligenceContext context)
        => new[] { ("0-15 sec", 0, 15), ("15-30 sec", 16, 30), ("30-45 sec", 31, 45), ("45-60 sec", 46, 60) }
            .Select(bucket =>
            {
                var items = context.Items.Where(IsShortOrReel).Where(x => (x.DurationSeconds ?? 0) >= bucket.Item2 && (x.DurationSeconds ?? 0) <= bucket.Item3).ToArray();
                return new DurationBucketPerformance(bucket.Item1, items.Length, items.Sum(x => x.Views ?? 0), AverageOrZero(items, EngagementRate), AverageOrZero(items, x => Retention(x) ?? 0), AverageOrZero(items, x => x.PerformanceScore ?? 0));
            }).ToArray();

    private static ThumbnailIntelligenceSummary BuildThumbnailIntelligence(IntelligenceContext context)
    {
        var variants = context.Items.GroupBy(ResolveThumbnailVariant, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ThumbnailVariantPerformance(g.Key, g.Count(), NullableAverage(g.Select(x => x.Ctr)), g.Sum(x => x.Views ?? 0), g.Average(x => x.PerformanceScore ?? 0), g.OrderByDescending(x => x.PerformanceScore ?? 0).Select(x => x.ThumbnailPath).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))))
            .OrderByDescending(x => x.AveragePerformanceScore).ToArray();
        var best = variants.FirstOrDefault();
        return new ThumbnailIntelligenceSummary(best?.Variant, best?.AverageCtr, best?.TopImage, variants);
    }

    private static AnalyticsTrendSummary BuildTrends(IntelligenceContext context, IReadOnlyCollection<AnalyticsTopContentItem> topContent)
    {
        var items = context.Items;
        var avgScore = AverageOrZero(items, x => x.PerformanceScore ?? 0);
        var fastest = items.OrderByDescending(x => GrowthVelocity(x, context.Days)).FirstOrDefault();
        var spikes = items.Where(x => GrowthVelocity(x, context.Days) >= 1.5 && (x.Views ?? 0) > 0).OrderByDescending(x => GrowthVelocity(x, context.Days)).Take(10).Select(x => ToTopContentItem(context, x)).ToArray();
        var under = items.Where(x => (x.PerformanceScore ?? 0) < avgScore * 0.5 && (x.Views ?? 0) > 0).OrderBy(x => x.PerformanceScore ?? 0).Take(10).Select(x => ToTopContentItem(context, x)).ToArray();
        var viral = items.Where(x => EngagementRate(x) >= 0.08 && GrowthVelocity(x, context.Days) >= 1.2 && ((x.Shares ?? 0) >= Math.Max(3, EngagementValue(x) * 0.15))).OrderByDescending(x => x.PerformanceScore ?? 0).Take(10).Select(x => ToTopContentItem(context, x)).ToArray();
        return new AnalyticsTrendSummary(fastest is null ? null : ToTopContentItem(context, fastest), spikes, under, viral);
    }

    private static IReadOnlyCollection<AnalyticsInsight> BuildInsights(IntelligenceContext context, IReadOnlyCollection<AstronomyObjectPerformance> objects, IReadOnlyCollection<DurationBucketPerformance> buckets, ThumbnailIntelligenceSummary thumbnails, AnalyticsTrendSummary trends)
    {
        var insights = new List<AnalyticsInsight>();
        var objectPlatform = BestObjectPlatformInsight(context);
        if (objectPlatform is not null) insights.Add(objectPlatform);
        var bestDuration = buckets.Where(x => x.ContentCount > 0).OrderByDescending(x => x.AverageRetention).FirstOrDefault();
        if (bestDuration is not null) insights.Add(new AnalyticsInsight("Reels", $"{bestDuration.Range} reels have highest retention.", bestDuration.AverageRetention));
        var time = BuildTimeIntelligence(context);
        if (time.BestWeekday is not null && time.BestPublishHour.HasValue) insights.Add(new AnalyticsInsight("Timing", $"{time.BestWeekday} {time.BestPublishHour.Value:00}:00 {time.BestTimezone ?? "UTC"} gives best engagement."));
        var topObject = objects.OrderByDescending(x => x.TotalViews).FirstOrDefault();
        if (topObject is not null) insights.Add(new AnalyticsInsight("Astronomy", $"{topObject.ObjectName} is the top astronomy topic by views.", topObject.TotalViews));
        if (thumbnails.BestThumbnailVariant is not null) insights.Add(new AnalyticsInsight("Thumbnails", $"{thumbnails.BestThumbnailVariant} is the best thumbnail variant.", thumbnails.AverageCtrForBestVariant));
        if (trends.ViralCandidates.Count > 0) insights.Add(new AnalyticsInsight("Trends", $"{trends.ViralCandidates.Count} viral candidate(s) detected from recent analytics."));
        if (insights.Count == 0) insights.Add(new AnalyticsInsight("Analytics", "Not enough analytics data is available to generate reliable intelligence yet."));
        return insights;
    }

    private async Task StoreInsightsAsync(IReadOnlyCollection<AnalyticsInsight> insights, CancellationToken cancellationToken)
    {
        var directory = string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory) ? Directory.GetCurrentDirectory() : _maintenanceOptions.WorkingDirectory;
        Directory.CreateDirectory(directory);
        var payload = JsonSerializer.Serialize(new { generatedUtc = DateTimeOffset.UtcNow, insights }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "analytics-insights.json"), payload, cancellationToken);
    }

    private static AnalyticsChartData BuildChartData(IntelligenceContext context, IReadOnlyCollection<AstronomyObjectPerformance> objects, IReadOnlyCollection<DurationBucketPerformance> buckets)
    {
        var daily = context.Items.GroupBy(x => DateOnly.FromDateTime((x.PublishedUtc ?? x.CollectedUtc).UtcDateTime.Date)).OrderBy(g => g.Key).Select(g => new DailyViewsPoint(g.Key, g.Sum(x => x.Views ?? 0), g.Count())).ToArray();
        var platform = context.Items.GroupBy(x => x.Platform).OrderBy(g => g.Key).Select(g => new PlatformComparisonPoint(g.Key, g.Sum(x => x.Views ?? 0), g.Sum(EngagementValue), AverageOrZero(g, x => x.AverageViewDurationSeconds ?? 0), AverageOrZero(g, x => x.PerformanceScore ?? 0))).ToArray();
        var objectPoints = objects.Select(x => new ObjectPerformancePoint(x.ObjectName, x.TotalViews, x.TotalEngagement, x.AverageEngagementRate)).ToArray();
        var duration = buckets.Select(x => new DurationPerformancePoint(x.Range, x.TotalViews, x.AverageRetention, x.AverageEngagementRate)).ToArray();
        var engagement = context.Items.GroupBy(x => DateOnly.FromDateTime((x.PublishedUtc ?? x.CollectedUtc).UtcDateTime.Date)).OrderBy(g => g.Key).Select(g => new EngagementTrendPoint(g.Key, AverageOrZero(g, EngagementRate), g.Sum(EngagementValue))).ToArray();
        return new AnalyticsChartData(daily, platform, objectPoints, duration, engagement);
    }

    private static IReadOnlyCollection<AnalyticsTopContentItem> BuildTopContent(IntelligenceContext context, int limit)
        => context.Items.OrderByDescending(x => x.PerformanceScore ?? 0).ThenByDescending(x => x.Views ?? 0).Take(Math.Clamp(limit <= 0 ? 10 : limit, 1, 100)).Select(x => ToTopContentItem(context, x)).ToArray();

    private static AnalyticsTopContentItem ToTopContentItem(IntelligenceContext context, PlatformContentAnalytics item)
        => new(item.Id, item.PipelineRunId, item.Platform, ContentTypeLabel(item), item.PlatformMediaId, item.Title, item.PlatformUrl, item.Views ?? 0, EngagementValue(item), EngagementRate(item), item.AverageViewDurationSeconds, Retention(item), item.Shares ?? 0, 0, item.PerformanceScore ?? 0, item.PublishedUtc, item.CollectedUtc, item.ThumbnailPath, ExtractAstronomyObjects(context, item));

    private static IReadOnlyCollection<string> ExtractAstronomyObjects(IntelligenceContext context, PlatformContentAnalytics item)
    {
        var text = BuildSearchText(context, item);
        return AstronomyTopics.Where(topic => ContainsTopic(text, topic)).ToArray();
    }

    private static string BuildSearchText(IntelligenceContext context, PlatformContentAnalytics item)
    {
        context.Scripts.TryGetValue(item.PipelineRunId ?? Guid.Empty, out var script);
        var parts = new[] { item.Title, item.Hashtags, script?.Title, script?.Description, script?.TagsCsv, script?.OptimizedTitle, script?.OptimizedDescription, script?.OptimizedTagsCsv, script?.OptimizedHashtagsCsv, script?.ScriptBody };
        return TokenCleanupRegex.Replace(string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))), " ");
    }

    private static string? ResolveHook(IntelligenceContext context, PlatformContentAnalytics item)
    {
        context.Scripts.TryGetValue(item.PipelineRunId ?? Guid.Empty, out var script);
        return string.IsNullOrWhiteSpace(script?.HookLine) ? item.Title : script.HookLine;
    }

    private static AnalyticsInsight? BestObjectPlatformInsight(IntelligenceContext context)
    {
        foreach (var topic in AstronomyTopics)
        {
            var byPlatform = context.Items.Where(x => ExtractAstronomyObjects(context, x).Contains(topic, StringComparer.OrdinalIgnoreCase)).GroupBy(x => x.Platform).Select(g => new { Platform = g.Key, Avg = g.Average(x => x.PerformanceScore ?? 0) }).OrderByDescending(x => x.Avg).ToArray();
            if (byPlatform.Length < 2 || byPlatform[1].Avg <= 0) continue;
            var lift = ((byPlatform[0].Avg - byPlatform[1].Avg) / byPlatform[1].Avg) * 100;
            return new AnalyticsInsight("Platform", $"{topic} {ContentTypeLabel(context.Items.First(x => ExtractAstronomyObjects(context, x).Contains(topic, StringComparer.OrdinalIgnoreCase))).ToLowerInvariant()} perform {lift:0}% better on {byPlatform[0].Platform}.", lift);
        }
        return null;
    }

    private static bool ContainsTopic(string text, string topic)
    {
        if (topic.Equals("Meteor Shower", StringComparison.OrdinalIgnoreCase))
            return text.Contains("meteor", StringComparison.OrdinalIgnoreCase);
        if (topic.Equals("Orion Nebula", StringComparison.OrdinalIgnoreCase))
            return text.Contains("orion nebula", StringComparison.OrdinalIgnoreCase) || text.Contains("m42", StringComparison.OrdinalIgnoreCase);
        return text.Contains(topic, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveThumbnailVariant(PlatformContentAnalytics item)
    {
        var file = Path.GetFileNameWithoutExtension(item.ThumbnailPath ?? "");
        var match = Regex.Match(file, @"(?:thumbnail|short-cover)-(?<n>\d+)", RegexOptions.IgnoreCase);
        if (match.Success) return $"variant-{match.Groups["n"].Value}";

        var selectionPath = FindThumbnailSelectionPath(item.ThumbnailPath);
        if (selectionPath is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(selectionPath));
                if (doc.RootElement.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array && variants.GetArrayLength() > 0)
                    return "selection-variant-1";
                if (doc.RootElement.TryGetProperty("selectedThumbnailPath", out var selected) && selected.GetString() is { Length: > 0 } selectedPath)
                    return Path.GetFileNameWithoutExtension(selectedPath);
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return string.IsNullOrWhiteSpace(item.ThumbnailPath) ? "unknown" : file;
    }

    private static string? FindThumbnailSelectionPath(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath)) return null;
        var directory = Path.GetDirectoryName(thumbnailPath);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var direct = Path.Combine(directory, "thumbnail-selection.json");
        if (File.Exists(direct)) return direct;
        var nested = Path.Combine(directory, "thumbnails", "thumbnail-selection.json");
        return File.Exists(nested) ? nested : null;
    }

    private static string ContentTypeLabel(PlatformContentAnalytics item)
    {
        if (item.PlatformContentType.Contains("reel", StringComparison.OrdinalIgnoreCase)) return "Reels";
        if (item.PlatformContentType.Contains("short", StringComparison.OrdinalIgnoreCase)) return "Shorts";
        return "Long videos";
    }

    private static string IdentityKey(PlatformContentAnalytics x) => !string.IsNullOrWhiteSpace(x.PlatformMediaId) ? $"{x.Platform}:{x.PlatformMediaId}" : x.Id.ToString("N");
    private static bool IsShortOrReel(PlatformContentAnalytics x) => ContentTypeLabel(x) is "Shorts" or "Reels";
    private static long EngagementValue(PlatformContentAnalytics x) => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0);
    private static double EngagementRate(PlatformContentAnalytics x) => x.EngagementRate ?? ((x.Views ?? 0) <= 0 ? 0 : (double)EngagementValue(x) / (x.Views ?? 1));
    private static double? Retention(PlatformContentAnalytics x) => x.DurationSeconds is > 0 && x.AverageViewDurationSeconds.HasValue ? x.AverageViewDurationSeconds.Value / x.DurationSeconds.Value : null;
    private static double Normalize(double value, double max) => max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);
    private static double AverageOrZero(IEnumerable<PlatformContentAnalytics> items, Func<PlatformContentAnalytics, double> selector) { var arr = items.ToArray(); return arr.Length == 0 ? 0 : arr.Average(selector); }
    private static double? NullableAverage(IEnumerable<double?> values) { var arr = values.OfType<double>().ToArray(); return arr.Length == 0 ? null : arr.Average(); }
    private static double GrowthVelocity(PlatformContentAnalytics x, int days) => (x.Views ?? 0) / Math.Max(1.0, ((DateTimeOffset.UtcNow - x.CollectedUtc).TotalDays + 1) / Math.Max(1, days));
    private static double GrowthRate(IReadOnlyCollection<PlatformContentAnalytics> items, int days) => items.Count == 0 ? 0 : items.Average(x => GrowthVelocity(x, days));
    private static string EngagementRange(double rate) => rate switch { >= 0.12 => "12%+", >= 0.08 => "8-12%", >= 0.04 => "4-8%", _ => "0-4%" };

    private sealed record IntelligenceContext(IReadOnlyCollection<PlatformContentAnalytics> Items, IReadOnlyDictionary<Guid, GeneratedScript> Scripts, IReadOnlyDictionary<Guid, PipelineRun> Runs, int Days);
}
