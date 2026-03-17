using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class TopicSelectionServiceTests
{
    [Fact]
    public async Task Ranking_PrefersTimelyAndHighObservabilityEvents()
    {
        var service = BuildService(new[]
        {
            new AstronomyEventModel { Category = "Meteor", ObjectName = "Perseids", Score = 0.95, Details = "Peak tonight" },
            new AstronomyEventModel { Category = "Planet", ObjectName = "Mars", Score = 0.6, Details = "Visible before dawn" }
        });

        var plan = await service.BuildPlanAsync(new TopicSelectionRequest { Date = DateOnly.FromDateTime(DateTime.UtcNow), LocationName = "Pune" }, CancellationToken.None);

        Assert.Equal("Perseids", plan.PrimaryLongForm!.ObjectName);
        Assert.NotEmpty(plan.ShortsCandidates);
    }

    [Fact]
    public async Task RepetitionAvoidance_PenalizesRepeatedTopics()
    {
        var repo = new TopicRepo
        {
            Published = [new PublishedVideo { Title = "Best Thing To Watch Tonight: Jupiter", CreatedAt = DateTimeOffset.UtcNow }],
            Scripts = [new GeneratedScript { Title = "Top Telescope Target Tonight: Jupiter" }]
        };
        var service = BuildService(new[]
        {
            new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", Score = 0.95, Details = "Bright" },
            new AstronomyEventModel { Category = "Moon", ObjectName = "Waxing Moon", Score = 0.8, Details = "Great contrast" }
        }, repo);

        var plan = await service.BuildPlanAsync(new TopicSelectionRequest { Date = DateOnly.FromDateTime(DateTime.UtcNow), LocationName = "Pune" }, CancellationToken.None);

        Assert.NotEqual("Jupiter", plan.PrimaryLongForm!.ObjectName);
    }

    [Fact]
    public async Task Fallback_ReturnsStableOpportunityWhenDataSparse()
    {
        var service = BuildService([]);
        var plan = await service.BuildPlanAsync(new TopicSelectionRequest { Date = DateOnly.FromDateTime(DateTime.UtcNow), LocationName = "Pune" }, CancellationToken.None);

        Assert.Single(plan.RankedOpportunities);
        Assert.Equal("fallback", plan.RankedOpportunities.First().EventType);
    }

    private static TopicSelectionService BuildService(IReadOnlyCollection<AstronomyEventModel> events, TopicRepo? repo = null)
    {
        var contextProvider = new StubContextProvider(events);
        var repository = repo ?? new TopicRepo();
        repository.Analytics =
        [
            new VideoAnalytics { VideoId = "a", Views = 12000, DurationSeconds = 300, AverageViewDurationSeconds = 140, ContentType = ContentType.DailySkyGuide, Title = "Meteor shower tonight" }
        ];

        return new TopicSelectionService(contextProvider, repository,
            Options.Create(new TopicSelectionOptions { RepetitionWindowDays = 7 }));
    }

    private sealed class StubContextProvider : IAstronomyContextProvider
    {
        private readonly IReadOnlyCollection<AstronomyEventModel> _events;
        public StubContextProvider(IReadOnlyCollection<AstronomyEventModel> events) => _events = events;

        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone, Events = _events.ToList() });
    }

    private sealed class TopicRepo : IPipelineRepository
    {
        public IReadOnlyCollection<VideoAnalytics> Analytics { get; set; } = [];
        public IReadOnlyCollection<PublishedVideo> Published { get; set; } = [];
        public IReadOnlyCollection<GeneratedScript> Scripts { get; set; } = [];
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>([]);
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult(Analytics);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult(Published);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult(Scripts);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
