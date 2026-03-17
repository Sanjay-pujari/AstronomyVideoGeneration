namespace Astronomy.MediaFactory.Core;

public sealed class AnalyticsFeedbackProvider : IAnalyticsFeedbackProvider
{
    private readonly IAnalyticsAggregationService _aggregationService;
    private readonly IReadOnlyCollection<IFeedbackSignalExtractor> _extractors;

    public AnalyticsFeedbackProvider(
        IAnalyticsAggregationService aggregationService,
        IEnumerable<IFeedbackSignalExtractor> extractors)
    {
        _aggregationService = aggregationService;
        _extractors = extractors.ToArray();
    }

    public async Task<FeedbackSignals> GetSignalsAsync(int topN, CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(topN, cancellationToken);
        var collector = new FeedbackSignalCollector();

        foreach (var extractor in _extractors)
            extractor.Extract(summary, topN, collector);

        return collector.Build(topN);
    }

    public Task<AnalyticsAggregationSummary> GetSummaryAsync(int topN, CancellationToken cancellationToken)
        => _aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, topN, cancellationToken);
}
