using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AnalyticsFeedbackProviderTests
{
    [Fact]
    public async Task Provider_CombinesSignals_FromAllExtractors()
    {
        var aggregationService = new StubAggregationService
        {
            Summary = new AnalyticsAggregationSummary
            {
                BestPerformingTitles = ["mars", "jupiter"],
                TopShortsByRetention =
                [
                    new VideoAnalytics { HookLine = "Wait for this Mars fact", ContentType = ContentType.SpaceNews },
                    new VideoAnalytics { HookLine = "Watch Jupiter vanish", ContentType = ContentType.SpaceNews }
                ]
            }
        };

        var provider = new AnalyticsFeedbackProvider(
            aggregationService,
            [new TopKeywordSignalExtractor(), new TopHookSignalExtractor()]);

        var signals = await provider.GetSignalsAsync(2, CancellationToken.None);

        Assert.Equal(2, signals.TopKeywords.Count);
        Assert.Equal(2, signals.BestHooks.Count);
    }

    private sealed class StubAggregationService : IAnalyticsAggregationService
    {
        public AnalyticsAggregationSummary Summary { get; set; } = new();

        public Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken)
            => Task.FromResult(Summary);
    }
}
