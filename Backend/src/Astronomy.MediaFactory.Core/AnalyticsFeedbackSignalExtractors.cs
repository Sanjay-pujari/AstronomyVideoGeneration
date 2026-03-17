namespace Astronomy.MediaFactory.Core;

public sealed class TopKeywordSignalExtractor : IFeedbackSignalExtractor
{
    public void Extract(AnalyticsAggregationSummary summary, int topN, FeedbackSignalCollector collector)
    {
        foreach (var keyword in summary.BestPerformingTitles.Take(topN))
            collector.AddKeyword(keyword);
    }
}

public sealed class TopHookSignalExtractor : IFeedbackSignalExtractor
{
    public void Extract(AnalyticsAggregationSummary summary, int topN, FeedbackSignalCollector collector)
    {
        foreach (var hook in summary.TopShortsByRetention
                     .Where(x => !string.IsNullOrWhiteSpace(x.HookLine))
                     .Select(x => x.HookLine)
                     .Take(topN))
        {
            collector.AddHook(hook);
        }
    }
}
