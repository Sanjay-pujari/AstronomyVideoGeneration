using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Core;

public sealed class AnalyticsAggregationService : IAnalyticsAggregationService
{
    private static readonly Regex TokenSplitRegex = new("[^a-zA-Z0-9]+", RegexOptions.Compiled);
    private const int AggregationWindowSize = 1_500;

    private readonly IPipelineRepository _repository;

    public AnalyticsAggregationService(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public async Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken)
    {
        var boundedTopN = Math.Max(topN, 1);

        var topVideosTask = _repository.GetTopPerformingAnalyticsAsync(from, to, boundedTopN, shortsOnly: false, cancellationToken);
        var topShortsTask = _repository.GetTopPerformingAnalyticsAsync(from, to, boundedTopN * 3, shortsOnly: true, cancellationToken);
        var analyticsWindowTask = _repository.GetAnalyticsWindowAsync(from, to, AggregationWindowSize, cancellationToken);

        await Task.WhenAll(topVideosTask, topShortsTask, analyticsWindowTask);

        var analyticsWindow = analyticsWindowTask.Result;
        var topShorts = topShortsTask.Result;

        return new AnalyticsAggregationSummary
        {
            TopVideosByViews = topVideosTask.Result,
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
