using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Core;

public sealed class AnalyticsAggregationService : IAnalyticsAggregationService
{
    private static readonly Regex TokenSplitRegex = new("[^a-zA-Z0-9]+", RegexOptions.Compiled);
    private const int AggregationWindowSize = 1_500;

    private readonly IServiceScopeFactory _scopeFactory;

    public AnalyticsAggregationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken)
    {
        var boundedTopN = Math.Max(topN, 1);

        // IMPORTANT:
        // - EF Core DbContext is not thread-safe.
        // - Several callers in the pipeline share a scoped repository/DbContext for writes.
        // To avoid any accidental cross-call concurrency on the same DbContext, we query analytics
        // using a dedicated scope (fresh repository + fresh DbContext).
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();

        var topVideos = await repository.GetTopPerformingAnalyticsAsync(from, to, boundedTopN, shortsOnly: false, cancellationToken);
        var topShorts = await repository.GetTopPerformingAnalyticsAsync(from, to, boundedTopN * 3, shortsOnly: true, cancellationToken);
        var analyticsWindow = await repository.GetAnalyticsWindowAsync(from, to, AggregationWindowSize, cancellationToken);

        return new AnalyticsAggregationSummary
        {
            TopVideosByViews = topVideos,
            TopShortsByRetention = topShorts
                .OrderByDescending(ComputeRetention)
                .Take(boundedTopN)
                .ToArray(),
            BestPerformingTitles = ExtractTopKeywords(analyticsWindow, boundedTopN),
            BestPerformingContentTypes = BuildContentTypePerformance(analyticsWindow, boundedTopN)
        };
    }

    private static IReadOnlyCollection<string> ExtractTopKeywords(IReadOnlyCollection<VideoAnalytics> analyticsWindow, int topN)
    {
        return analyticsWindow
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .OrderByDescending(x => x.Views)
            .Take(topN * 4)
            .SelectMany(x => TokenSplitRegex.Split(x.Title!))
            .Where(x => x.Length >= 4)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .Select(x => x.Key)
            .ToArray();
    }

    private static IReadOnlyCollection<ContentTypePerformance> BuildContentTypePerformance(IReadOnlyCollection<VideoAnalytics> analyticsWindow, int topN)
    {
        return analyticsWindow
            .GroupBy(x => x.ContentType)
            .Select(x => new ContentTypePerformance
            {
                ContentType = x.Key,
                AverageViews = x.Average(v => v.Views),
                AverageRetention = x.Average(ComputeRetention),
                Samples = x.Count()
            })
            .OrderByDescending(x => x.AverageViews)
            .Take(topN)
            .ToArray();
    }

    private static double ComputeRetention(VideoAnalytics analytics)
    {
        if (analytics.DurationSeconds <= 0 || !analytics.AverageViewDurationSeconds.HasValue)
            return 0;

        return analytics.AverageViewDurationSeconds.Value / analytics.DurationSeconds;
    }
}
