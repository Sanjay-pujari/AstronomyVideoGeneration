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
        var summary = await _aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, topN, cancellationToken);
        var collector = new FeedbackSignalCollector();

        foreach (var extractor in _extractors)
            extractor.Extract(summary, topN, collector);

        return collector.Build(topN);
    }
}
