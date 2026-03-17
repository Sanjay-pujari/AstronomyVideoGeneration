namespace Astronomy.MediaFactory.Core;

public sealed class AnalyticsFeedbackProvider : IAnalyticsFeedbackProvider
{
    private readonly IAnalyticsAggregationService _aggregationService;

    public AnalyticsFeedbackProvider(IAnalyticsAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    public async Task<FeedbackSignals> GetSignalsAsync(int topN, CancellationToken cancellationToken)
    {
        var summary = await _aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, topN, cancellationToken);

        return new FeedbackSignals
        {
            TopKeywords = summary.BestPerformingTitles,
            BestHooks = summary.TopShortsByRetention
                .Where(x => !string.IsNullOrWhiteSpace(x.HookLine))
                .Select(x => x.HookLine!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(topN)
                .ToArray()
        };
    }
}
