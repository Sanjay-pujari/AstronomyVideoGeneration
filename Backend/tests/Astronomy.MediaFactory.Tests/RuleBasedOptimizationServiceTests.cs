using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Optimization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class RuleBasedOptimizationServiceTests
{
    [Fact]
    public async Task Insufficient_Data_Returns_Low_Confidence()
    {
        using var temp = new TempOutput();
        var service = CreateService([Sample("a", 18, 0.10, "Moon")], temp.Path);

        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);

        Assert.True(plan.ConfidenceScore < 0.5);
        Assert.Contains("at least", plan.Reasons.Single());
    }

    [Fact]
    public async Task Recommend_Only_Does_Not_Mutate_Request()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path, mode: OptimizationMode.RecommendOnly);
        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);
        var request = Request();

        var result = await service.ApplyPlanAsync(request, plan, CancellationToken.None);

        Assert.Equal(request, result);
    }

    [Fact]
    public async Task Apply_Safe_Rules_Mutates_Only_Allowed_Fields()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path, mode: OptimizationMode.ApplySafeRules);
        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);
        var request = Request();

        var result = await service.ApplyPlanAsync(request, plan, CancellationToken.None);

        Assert.True(result.UseTopicPlanner);
        Assert.Equal(request with { UseTopicPlanner = true }, result);
    }

    [Fact]
    public async Task Publish_Time_Rule_Works()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path);

        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);

        Assert.Equal("18:00", plan.RecommendedPublishTimeLocal);
        Assert.Contains("PublishTimeRule", plan.AppliedRules);
    }

    [Fact]
    public async Task Object_Boost_Rule_Works()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path);

        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);

        Assert.Contains("Moon", plan.PreferredContentObjects);
        Assert.Contains("ObjectRankingRule", plan.AppliedRules);
    }

    [Fact]
    public async Task Disabled_Optimization_Changes_Nothing()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path, enabled: false, mode: OptimizationMode.Disabled);
        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);
        var request = Request();

        var result = await service.ApplyPlanAsync(request, plan, CancellationToken.None);

        Assert.Equal(request, result);
        Assert.Equal(0, plan.ConfidenceScore);
    }

    [Fact]
    public async Task Audit_Files_Are_Created()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(), temp.Path, mode: OptimizationMode.ApplySafeRules);
        var plan = await service.BuildPlanAsync("Udaipur, India", "YouTube", CancellationToken.None);

        await service.ApplyPlanAsync(Request(), plan, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "optimization-plan.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "optimization-applied.json")));
    }

    private static RuleBasedOptimizationService CreateService(IReadOnlyCollection<PlatformContentAnalytics> analytics, string outputPath, bool enabled = true, OptimizationMode mode = OptimizationMode.RecommendOnly)
    {
        var repository = new FakeRepository { Analytics = analytics };
        var intelligence = new FakeAnalyticsIntelligenceService(analytics);
        var options = Options.Create(new OptimizationOptions { Enabled = enabled, Mode = mode, MinimumDataPoints = 10, ConfidenceThreshold = 0.6 });
        var maintenance = Options.Create(new MaintenanceOptions { WorkingDirectory = outputPath });
        return new RuleBasedOptimizationService(repository, intelligence, options, maintenance);
    }

    private static RunPipelineRequest Request() => new(new DateOnly(2026, 5, 9), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", PublishToYouTube: true);

    private static IReadOnlyCollection<PlatformContentAnalytics> CreateAnalytics()
        => Enumerable.Range(0, 8).Select(i => Sample($"moon-{i}", 18, 0.30, "Moon", duration: 25, ctr: 0.12, thumbnail: $"/tmp/thumbnail-1-{i}.png", hashtags: "#Moon #TonightSky"))
            .Concat(Enumerable.Range(0, 4).Select(i => Sample($"mars-{i}", 20, 0.10, "Mars", duration: 45, ctr: 0.04, thumbnail: $"/tmp/thumbnail-2-{i}.png", hashtags: "#Mars")))
            .ToArray();

    private static PlatformContentAnalytics Sample(string id, int hour, double engagementRate, string topic, int duration = 30, double ctr = 0.05, string? thumbnail = null, string hashtags = "#Sky")
        => new()
        {
            Platform = "YouTube",
            PlatformContentType = "Shorts",
            PlatformMediaId = id,
            Title = $"Tonight: {topic}?",
            PublishedUtc = new DateTimeOffset(2026, 5, 1, hour, 0, 0, TimeSpan.Zero),
            CollectedUtc = DateTimeOffset.UtcNow,
            Views = 1000,
            Likes = (long)(1000 * engagementRate),
            Comments = 0,
            Shares = 0,
            EngagementRate = engagementRate,
            DurationSeconds = duration,
            AverageViewDurationSeconds = duration * 0.8,
            Ctr = ctr,
            ThumbnailPath = thumbnail,
            Hashtags = hashtags,
            LocationName = "Udaipur, India",
            PerformanceScore = engagementRate * 100
        };

    private sealed class FakeAnalyticsIntelligenceService : IAnalyticsIntelligenceService
    {
        private readonly IReadOnlyCollection<PlatformContentAnalytics> _analytics;
        public FakeAnalyticsIntelligenceService(IReadOnlyCollection<PlatformContentAnalytics> analytics) => _analytics = analytics;
        public Task<AnalyticsDashboardResponse> BuildDashboardAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
        {
            var buckets = new[]
            {
                new DurationBucketPerformance("15-30 sec", _analytics.Count(x => x.DurationSeconds is >= 16 and <= 30), 8000, 0.30, 0.8, 30),
                new DurationBucketPerformance("45-60 sec", _analytics.Count(x => x.DurationSeconds is >= 46 and <= 60), 4000, 0.10, 0.5, 10)
            };
            var thumbs = new[]
            {
                new ThumbnailVariantPerformance("variant-1", 8, 0.12, 8000, 30, null),
                new ThumbnailVariantPerformance("variant-2", 4, 0.04, 4000, 10, null)
            };
            var response = new AnalyticsDashboardResponse(
                new AnalyticsOverallSummary(_analytics.Count, 0, 0, 0, 0, "YouTube", "Shorts"), [], [], [],
                new AnalyticsTimeIntelligence(18, "Friday", "UTC", "Friday 18:00-19:00 UTC"),
                new AstronomyIntelligenceSummary("Moon", "Moon", "Moon", []),
                new ReelIntelligenceSummary("15-30 sec", "Question-led hook", "12%+", 0.8, buckets),
                new ThumbnailIntelligenceSummary("variant-1", 0.12, null, thumbs),
                new AnalyticsTrendSummary(null, [], [], []),
                new AnalyticsChartData([], [], [], [], []),
                [new AnalyticsInsight("Timing", "Friday 18:00 UTC gives best engagement.")]);
            return Task.FromResult(response);
        }
        public Task<IReadOnlyCollection<AnalyticsTopContentItem>> GetTopContentAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<AnalyticsTopContentItem>>(_analytics.OrderByDescending(x => x.PerformanceScore).Take(10).Select(x => new AnalyticsTopContentItem(x.Id, x.PipelineRunId, x.Platform, "Shorts", x.PlatformMediaId, x.Title, null, x.Views ?? 0, 0, x.EngagementRate ?? 0, x.AverageViewDurationSeconds, 0.8, x.Shares ?? 0, 0, x.PerformanceScore ?? 0, x.PublishedUtc, x.CollectedUtc, x.ThumbnailPath, [])).ToArray());
        public Task<IReadOnlyCollection<AnalyticsInsight>> GetInsightsAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AnalyticsInsight>>([new AnalyticsInsight("Timing", "ok")]);
        public Task<IReadOnlyCollection<AnalyticsPlatformBreakdown>> GetPlatformSummaryAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AnalyticsPlatformBreakdown>>([]);
        public Task<IReadOnlyCollection<AnalyticsContentTypeBreakdown>> GetContentPerformanceAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AnalyticsContentTypeBreakdown>>([]);
    }

    private sealed class FakeRepository : IPipelineRepository
    {
        public IReadOnlyCollection<PlatformContentAnalytics> Analytics { get; init; } = [];
        public Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsAsync(PlatformAnalyticsQuery query, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>([]);
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TempOutput : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "optimization-tests-" + Guid.NewGuid().ToString("N"));
        public TempOutput() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
