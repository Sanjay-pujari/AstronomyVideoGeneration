using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Optimization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AIOptimizationServiceTests
{
    [Fact]
    public async Task Insufficient_Data_Returns_Low_Confidence()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(3), temp.Path, useAzure: false, minimumRows: 20);

        var result = await service.GenerateNowAsync(CancellationToken.None);

        Assert.True(result.ConfidenceScore <= 0.3);
        Assert.Contains(result.RiskWarnings, x => x.Contains("Insufficient analytics rows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("analyticsRows=3", result.ReasoningSummary);
    }

    [Fact]
    public async Task Generated_Json_Schema_Is_Valid()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: false, minimumRows: 20);

        var result = await service.GenerateNowAsync(CancellationToken.None);
        var path = System.IO.Path.Combine(temp.Path, "ai-optimization-recommendations.json");
        var json = await File.ReadAllTextAsync(path);
        var reparsed = JsonSerializer.Deserialize<AIOptimizationRecommendations>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(reparsed);
        Assert.NotEmpty(result.RecommendedHooks);
        Assert.InRange(reparsed!.ConfidenceScore, 0, 1);
        Assert.False(string.IsNullOrWhiteSpace(reparsed.ReasoningSummary));
    }

    [Fact]
    public async Task Recommendations_Do_Not_Mutate_Scheduler_Or_Pipeline()
    {
        using var temp = new TempOutput();
        var repository = new FakeRepository(CreateAnalytics(24));
        var scheduler = SchedulerOptions();
        var originalScheduleTime = scheduler.Schedules.Single().LocalRunTime;
        var service = CreateService(repository, new FakeAnalyticsIntelligenceService(repository.Analytics), new FakeOptimizationService(), temp.Path, useAzure: false, minimumRows: 20, schedulerOptions: scheduler);

        await service.GenerateNowAsync(CancellationToken.None);

        Assert.False(repository.CreateCalled);
        Assert.False(repository.SaveChangesCalled);
        Assert.Equal(originalScheduleTime, scheduler.Schedules.Single().LocalRunTime);
    }


    [Fact]
    public async Task Low_Confidence_Recommendation_Is_Not_Applied()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: false, minimumRows: 20, mode: OptimizationMode.ApplySafeRecommendations);

        var result = await service.ApplyApprovedAsync(new AIOptimizationApplyRequest { ApprovedBy = "reviewer", Recommendations = Recommendation(confidence: 0.5) }, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "ai-optimization-applied.json")));
    }

    [Fact]
    public async Task Unapproved_Recommendation_Is_Not_Applied()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: false, minimumRows: 20, mode: OptimizationMode.ApplySafeRecommendations);

        var result = await service.ApplyApprovedAsync(new AIOptimizationApplyRequest { Recommendations = Recommendation(confidence: 0.9) }, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Contains("Human approval", result.Reason);
    }

    [Fact]
    public async Task Disallowed_Field_Is_Ignored()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: false, minimumRows: 20, mode: OptimizationMode.ApplySafeRecommendations);

        var result = await service.ApplyApprovedAsync(new AIOptimizationApplyRequest
        {
            ApprovedBy = "reviewer",
            AllowedApplyFields = ["recommendedHooks", "recommendedVideoIdeas"],
            Recommendations = Recommendation(confidence: 0.9)
        }, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Contains("recommendedHooks", result.AppliedFields);
        Assert.DoesNotContain("recommendedVideoIdeas", result.AppliedFields);
        Assert.Contains(nameof(AIOptimizationRecommendations.RecommendedVideoIdeas), result.IgnoredFields);
        Assert.Empty(result.Profile!.AppliedValues.RecommendedThumbnailText);
    }

    [Fact]
    public async Task Approved_Safe_Recommendation_Is_Applied_With_Rollback_Data()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: false, minimumRows: 20, mode: OptimizationMode.ApplySafeRecommendations);

        var first = await service.ApplyApprovedAsync(new AIOptimizationApplyRequest { ApprovedBy = "reviewer", Recommendations = Recommendation(confidence: 0.9, hook: "First hook") }, CancellationToken.None);
        var second = await service.ApplyApprovedAsync(new AIOptimizationApplyRequest { ApprovedBy = "reviewer", Recommendations = Recommendation(confidence: 0.95, hook: "Second hook") }, CancellationToken.None);
        var json = await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "ai-optimization-applied.json"));
        var profile = JsonSerializer.Deserialize<AIOptimizationAppliedProfile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(first.Applied);
        Assert.True(second.Applied);
        Assert.Equal("Second hook", second.Profile!.AppliedValues.RecommendedHooks.Single());
        Assert.Equal("First hook", second.Profile.PreviousValues.RecommendedHooks.Single());
        Assert.Equal("First hook", profile!.PreviousValues.RecommendedHooks.Single());
    }

    [Fact]
    public async Task Missing_Azure_OpenAI_Config_Fails_Gracefully()
    {
        using var temp = new TempOutput();
        var service = CreateService(CreateAnalytics(24), temp.Path, useAzure: true, minimumRows: 20, azureOptions: new AzureOpenAiOptions());

        var result = await service.GenerateNowAsync(CancellationToken.None);

        Assert.True(result.ConfidenceScore <= 0.3);
        Assert.Contains(result.RiskWarnings, x => x.Contains("Azure OpenAI configuration is missing", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "ai-optimization-recommendations.json")));
    }

    private static AIOptimizationService CreateService(
        IReadOnlyCollection<PlatformContentAnalytics> analytics,
        string outputPath,
        bool useAzure,
        int minimumRows,
        AzureOpenAiOptions? azureOptions = null,
        OptimizationMode mode = OptimizationMode.RecommendOnly)
        => CreateService(new FakeRepository(analytics), new FakeAnalyticsIntelligenceService(analytics), new FakeOptimizationService(), outputPath, useAzure, minimumRows, azureOptions, mode: mode);

    private static AIOptimizationService CreateService(
        FakeRepository repository,
        IAnalyticsIntelligenceService analytics,
        IOptimizationService optimization,
        string outputPath,
        bool useAzure,
        int minimumRows,
        AzureOpenAiOptions? azureOptions = null,
        SchedulerOptions? schedulerOptions = null,
        OptimizationMode mode = OptimizationMode.RecommendOnly)
        => new(
            repository,
            analytics,
            optimization,
            new HttpClient(),
            Options.Create(new AIOptimizationOptions { Enabled = true, Mode = mode, UseAzureOpenAI = useAzure, MinimumAnalyticsRows = minimumRows }),
            Options.Create(azureOptions ?? new AzureOpenAiOptions { Endpoint = "https://example.openai.azure.com", ChatDeployment = "gpt-4o", ApiKey = "test" }),
            Options.Create(schedulerOptions ?? SchedulerOptions()),
            Options.Create(new MaintenanceOptions { WorkingDirectory = outputPath }),
            NullLogger<AIOptimizationService>.Instance);


    private static AIOptimizationRecommendations Recommendation(double confidence, string hook = "Question-led hook")
        => new()
        {
            RecommendedHooks = [hook],
            RecommendedVideoIdeas = ["Unsafe free-form video idea should remain advisory only"],
            RecommendedThumbnailText = ["LOOK UP"],
            RecommendedPublishTimes = ["19:00"],
            RecommendedObjectsToBoost = ["Jupiter"],
            RecommendedObjectsToAvoid = ["Moon"],
            RecommendedHashtagSets = [["#space", "#astronomy"]],
            ConfidenceScore = confidence,
            ReasoningSummary = "Enough local analytics rows support these scheduler-safe recommendations."
        };

    private static SchedulerOptions SchedulerOptions()
        => new()
        {
            Schedules =
            [
                new SchedulerScheduleOptions
                {
                    Name = "Udaipur Daily Sky",
                    Enabled = true,
                    LocationName = "Udaipur, India",
                    Latitude = 24.5854,
                    Longitude = 73.7125,
                    Timezone = "Asia/Kolkata",
                    LocalRunTime = "18:00",
                    PublishEnabled = true
                }
            ]
        };

    private static IReadOnlyCollection<PlatformContentAnalytics> CreateAnalytics(int count)
        => Enumerable.Range(0, count).Select(i => new PlatformContentAnalytics
        {
            Platform = "YouTube",
            PlatformContentType = "Shorts",
            PlatformMediaId = $"video-{i}",
            Title = i % 2 == 0 ? "Tonight: Moon over Jupiter?" : "Mars in the morning sky",
            PublishedUtc = new DateTimeOffset(2026, 5, 1, 18 + i % 3, 0, 0, TimeSpan.Zero),
            CollectedUtc = DateTimeOffset.UtcNow,
            Views = 1000 + i * 100,
            Likes = 100 + i,
            Comments = 10,
            Shares = 5 + i,
            EngagementRate = 0.12 + i * 0.001,
            Ctr = 0.08,
            DurationSeconds = 30,
            Hashtags = i % 2 == 0 ? "#Moon #Jupiter #TonightSky" : "#Mars #Astronomy",
            PerformanceScore = 10 + i,
            IsAnalyticsAvailable = true
        }).ToArray();

    private sealed class FakeAnalyticsIntelligenceService : IAnalyticsIntelligenceService
    {
        private readonly IReadOnlyCollection<PlatformContentAnalytics> _analytics;
        public FakeAnalyticsIntelligenceService(IReadOnlyCollection<PlatformContentAnalytics> analytics) => _analytics = analytics;

        public Task<AnalyticsDashboardResponse> BuildDashboardAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
        {
            var top = _analytics.OrderByDescending(x => x.PerformanceScore ?? 0).FirstOrDefault();
            var item = top is null ? null : ToTopItem(top);
            return Task.FromResult(new AnalyticsDashboardResponse(
                new AnalyticsOverallSummary(_analytics.Count, _analytics.Sum(x => x.Views ?? 0), _analytics.Sum(x => (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0)), 0.12, 18, "YouTube", "Shorts"),
                [new AnalyticsPlatformBreakdown("YouTube", _analytics.Count, _analytics.Sum(x => x.Views ?? 0), 0.12, 18, item)],
                [new AnalyticsContentTypeBreakdown("Shorts", _analytics.Count, _analytics.Sum(x => x.Views ?? 0), 0.12, 18, 20)],
                new AnalyticsTimeIntelligence(18, "Friday", "UTC", "Friday 18:00 UTC"),
                new AstronomyIntelligenceSummary("Moon", "Moon", "Jupiter", [new AstronomyObjectPerformance("Moon", _analytics.Count / 2, 5000, 400, 0.12, 0.4)]),
                new ReelIntelligenceSummary("15-30 sec", "Question-led hook", "12%+", 0.8, [new DurationBucketPerformance("15-30 sec", _analytics.Count, _analytics.Sum(x => x.Views ?? 0), 0.12, 0.8, 20)]),
                new ThumbnailIntelligenceSummary("bright-moon", 0.08, null, [new ThumbnailVariantPerformance("bright-moon", _analytics.Count, 0.08, _analytics.Sum(x => x.Views ?? 0), 20, null)]),
                new AnalyticsTrendSummary(item, [], [], []),
                new AnalyticsChartData([], [], [], [], []),
                [new AnalyticsInsight("Timing", "18 UTC leads local performance", 18)]));
        }

        public Task<IReadOnlyCollection<AnalyticsTopContentItem>> GetTopContentAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<AnalyticsTopContentItem>>(_analytics.OrderByDescending(x => x.PerformanceScore ?? 0).Take(request.Limit).Select(ToTopItem).ToArray());

        public Task<IReadOnlyCollection<AnalyticsInsight>> GetInsightsAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<AnalyticsInsight>>([new AnalyticsInsight("Timing", "18 UTC leads local performance", 18)]);
        public Task<IReadOnlyCollection<AnalyticsPlatformBreakdown>> GetPlatformSummaryAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AnalyticsPlatformBreakdown>>([]);
        public Task<IReadOnlyCollection<AnalyticsContentTypeBreakdown>> GetContentPerformanceAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<AnalyticsContentTypeBreakdown>>([]);

        private static AnalyticsTopContentItem ToTopItem(PlatformContentAnalytics x)
            => new(x.Id, x.PipelineRunId, x.Platform, x.PlatformContentType, x.PlatformMediaId, x.Title, x.PlatformUrl, x.Views ?? 0, (x.Likes ?? 0) + (x.Comments ?? 0) + (x.Shares ?? 0), x.EngagementRate ?? 0, x.AverageViewDurationSeconds, 0.8, x.Shares ?? 0, 0, x.PerformanceScore ?? 0, x.PublishedUtc, x.CollectedUtc, x.ThumbnailPath, ["Moon"]);
    }

    private sealed class FakeOptimizationService : IOptimizationService
    {
        public Task<OptimizationPlan> BuildPlanAsync(string locationName, string platform, CancellationToken cancellationToken)
            => Task.FromResult(new OptimizationPlan
            {
                LocationName = locationName,
                Platform = platform,
                RecommendedPublishTimeLocal = "18:00",
                PreferredContentObjects = ["Moon", "Jupiter"],
                AvoidedContentObjects = ["Low altitude Mercury"],
                RecommendedHookStyle = "Question-led hook",
                RecommendedHashtags = ["#Moon", "#Jupiter", "#TonightSky"],
                ConfidenceScore = 0.82,
                Reasons = ["Local rules found a timing lift."],
                AppliedRules = ["PublishTimeRule", "ObjectRankingRule"]
            });

        public Task<RunPipelineRequest> ApplyPlanAsync(RunPipelineRequest request, OptimizationPlan plan, CancellationToken cancellationToken) => Task.FromResult(request);
    }

    private sealed class FakeRepository : IPipelineRepository
    {
        public FakeRepository(IReadOnlyCollection<PlatformContentAnalytics> analytics) => Analytics = analytics;
        public IReadOnlyCollection<PlatformContentAnalytics> Analytics { get; }
        public bool CreateCalled { get; private set; }
        public bool SaveChangesCalled { get; private set; }
        public Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsAsync(PlatformAnalyticsQuery query, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) { CreateCalled = true; return Task.FromResult(run); }
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([new PipelineRun { RunDate = new DateOnly(2026, 5, 9), ContentType = ContentType.DailySkyGuide, LocationName = "Udaipur, India", Status = PipelineRunStatus.Succeeded }]);
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
        public Task SaveChangesAsync(CancellationToken cancellationToken) { SaveChangesCalled = true; return Task.CompletedTask; }
    }

    private sealed class TempOutput : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ai-optimization-tests-" + Guid.NewGuid().ToString("N"));
        public TempOutput() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
