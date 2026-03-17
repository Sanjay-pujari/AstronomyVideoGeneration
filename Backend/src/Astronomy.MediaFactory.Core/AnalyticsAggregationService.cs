using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Core;

public sealed class AnalyticsAggregationService : IAnalyticsAggregationService
{
    private readonly IPipelineRepository _repository;

    public AnalyticsAggregationService(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public async Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken)
    {
        var topVideos = await _repository.GetTopPerformingAnalyticsAsync(from, to, topN, shortsOnly: false, cancellationToken);
        var topShorts = await _repository.GetTopPerformingAnalyticsAsync(from, to, topN, shortsOnly: true, cancellationToken);
        var all = await _repository.GetRecentAnalyticsAsync(500, cancellationToken);

        if (from.HasValue)
            all = all.Where(x => x.RetrievedAt >= from.Value).ToArray();
        if (to.HasValue)
            all = all.Where(x => x.RetrievedAt <= to.Value).ToArray();

        var keywords = all
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .OrderByDescending(x => x.Views)
            .Take(topN)
            .SelectMany(x => Regex.Split(x.Title!, "[^a-zA-Z0-9]+"))
            .Where(x => x.Length >= 4)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .Take(topN)
            .Select(x => x.Key)
            .ToArray();

        var typePerf = all
            .GroupBy(x => x.ContentType)
            .Select(x => new ContentTypePerformance
            {
                ContentType = x.Key,
                AverageViews = x.Average(v => v.Views)
            })
            .OrderByDescending(x => x.AverageViews)
            .Take(topN)
            .ToArray();

        return new AnalyticsAggregationSummary
        {
            TopVideosByViews = topVideos,
            TopShortsByRetention = topShorts
                .OrderByDescending(x => ComputeRetention(x))
                .Take(topN)
                .ToArray(),
            BestPerformingTitles = keywords,
            BestPerformingContentTypes = typePerf
        };
    }

    private static double ComputeRetention(VideoAnalytics analytics)
    {
        if (analytics.DurationSeconds <= 0 || !analytics.AverageViewDurationSeconds.HasValue)
            return 0;

        return analytics.AverageViewDurationSeconds.Value / analytics.DurationSeconds;
    }
}
