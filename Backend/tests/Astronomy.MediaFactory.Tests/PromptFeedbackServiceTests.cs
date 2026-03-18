using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PromptFeedbackServiceTests
{
    [Fact]
    public async Task BuildContext_UsesAnalyticsAndTopicPlanSignals()
    {
        var service = BuildService(
            new StubAnalyticsFeedbackProvider(),
            new StubRepository
            {
                Scripts =
                [
                    new GeneratedScript { Title = "Jupiter tonight for beginners" },
                    new GeneratedScript { Title = "Jupiter tonight skywatch" }
                ]
            });

        var plan = new TopicSelectionPlan
        {
            PrimaryLongForm = new ContentOpportunity
            {
                ObjectName = "Jupiter",
                PriorityScore = 0.93,
                ObservabilityScore = 0.91,
                TimelinessScore = 0.84,
                SignificanceScore = 0.78,
                Rationale = "Visible early evening and beginner-accessible."
            },
            SchedulingHints = new TopicSelectionSchedulingHints { Notes = "Publish before sunset local time." }
        };

        var context = await service.BuildContextAsync(new PromptFeedbackRequest
        {
            ContentType = ContentType.DailySkyGuide,
            TopicSelectionPlan = plan
        }, CancellationToken.None);

        Assert.Contains("meteor shower", context.RecommendedKeywords);
        Assert.Contains(context.RecentOverusedTopics, x => x.Contains("Jupiter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Selected because score=", context.TopicSelectionRationale);
        Assert.False(context.UsedFallbackDefaults);
    }


    [Fact]
    public async Task BuildContext_AppliesContentTypeSpecificToneAndShortHooks()
    {
        var service = BuildService(new StubAnalyticsFeedbackProvider(), new StubRepository());

        var context = await service.BuildContextAsync(new PromptFeedbackRequest
        {
            ContentType = ContentType.AstrophotographyTips,
            IsShortForm = true
        }, CancellationToken.None);

        Assert.Contains(context.RecommendedToneNotes, x => x.Contains("practical settings", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(context.ShortsHookSuggestions);
    }


    [Fact]
    public async Task BuildContext_UsesStructuredExperimentInsightsInHints()
    {
        var service = BuildService(
            new StubAnalyticsFeedbackProvider(),
            new StubRepository(),
            new StubExperimentService());

        var context = await service.BuildContextAsync(new PromptFeedbackRequest
        {
            ContentType = ContentType.DailySkyGuide
        }, CancellationToken.None);

        Assert.Contains(context.MetadataOptimizationHints, x => x.Contains("Latest title winner delivered CTR", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.ThumbnailStrategyHints, x => x.Contains("Most recent thumbnail winner reached", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildContext_FallsBack_WhenAnalyticsUnavailable()
    {
        var service = BuildService(new ThrowingAnalyticsProvider(), new StubRepository());

        var context = await service.BuildContextAsync(new PromptFeedbackRequest
        {
            ContentType = ContentType.SpaceNews,
            IsShortForm = true
        }, CancellationToken.None);

        Assert.True(context.UsedFallbackDefaults);
        Assert.NotEmpty(context.RecommendedToneNotes);
        Assert.NotEmpty(context.RecommendedTitlePatterns);
    }

    private static PromptFeedbackService BuildService(IAnalyticsFeedbackProvider analyticsFeedbackProvider, StubRepository repository, IContentExperimentService? contentExperimentService = null)
        => new(analyticsFeedbackProvider, repository, NullLogger<PromptFeedbackService>.Instance, contentExperimentService);

    private sealed class StubAnalyticsFeedbackProvider : IAnalyticsFeedbackProvider
    {
        public Task<FeedbackSignals> GetSignalsAsync(int topN, CancellationToken cancellationToken)
            => Task.FromResult(new FeedbackSignals
            {
                TopKeywords = ["meteor shower", "jupiter", "tonight"],
                BestHooks = ["Stop scrolling — meteor peak tonight", "Tonight Jupiter looks huge"]
            });

        public Task<AnalyticsAggregationSummary> GetSummaryAsync(int topN, CancellationToken cancellationToken)
            => Task.FromResult(new AnalyticsAggregationSummary
            {
                BestPerformingTitles = ["Meteor Shower Tonight: 3 Viewing Tips", "Jupiter Tonight for Beginners"],
                TopVideosByViews = [new VideoAnalytics { Title = "Meteor Shower Tonight" }],
                TopShortsByRetention = [new VideoAnalytics { Title = "Jupiter Tonight in 60 Seconds" }]
            });
    }

    private sealed class ThrowingAnalyticsProvider : IAnalyticsFeedbackProvider
    {
        public Task<FeedbackSignals> GetSignalsAsync(int topN, CancellationToken cancellationToken) => throw new InvalidOperationException("broken");
        public Task<AnalyticsAggregationSummary> GetSummaryAsync(int topN, CancellationToken cancellationToken) => throw new InvalidOperationException("broken");
    }

    private sealed class StubRepository : IPipelineRepository
    {
        public IReadOnlyCollection<GeneratedScript> Scripts { get; init; } = [];
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken)
            => Task.FromResult(Scripts);

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubExperimentService : IContentExperimentService
    {
        public Task InitializeExperimentsAsync(PublishedVideo publishedVideo, OptimizedVideoMetadata metadata, ThumbnailPlan thumbnailPlan, MonetizationPlan? monetizationPlan, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ExperimentVariantAssignment> ResolveAssignmentsAsync(Guid videoId, CancellationToken cancellationToken) => Task.FromResult(new ExperimentVariantAssignment());
        public Task EvaluateRecentExperimentsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<ContentExperiment>> GetRecentExperimentsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ContentExperiment>>([]);
        public Task<ContentExperiment?> GetExperimentAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ContentExperiment?>(null);
        public Task<IReadOnlyCollection<ContentExperiment>> GetTopPerformingExperimentsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ContentExperiment>>([]);
        public Task<ExperimentFeedbackSnapshot> GetFeedbackSnapshotAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ExperimentFeedbackSnapshot
            {
                WinningTitlePatterns = ["Meteor Tonight: <N> Tips"],
                WinningHooks = ["Meteor peak tonight"],
                WinningThumbnailPatterns = ["TopBanner: METEOR PEAK"],
                Insights =
                [
                    new ExperimentFeedbackInsight
                    {
                        ExperimentType = ContentExperimentType.Title,
                        WinningValue = "Meteor Tonight: 3 Tips",
                        WinningPattern = "Meteor Tonight: <N> Tips",
                        WinningHook = "Meteor peak tonight",
                        Metrics = new VariantPerformanceMetrics { Views = 420, Ctr = 6.3, EngagementScore = 58.2 }
                    },
                    new ExperimentFeedbackInsight
                    {
                        ExperimentType = ContentExperimentType.Thumbnail,
                        WinningValue = "TopBanner: METEOR PEAK",
                        WinningPattern = "TopBanner: METEOR PEAK",
                        Metrics = new VariantPerformanceMetrics { Views = 510, Ctr = 4.9, EngagementScore = 55.4 }
                    }
                ]
            });
    }

}
