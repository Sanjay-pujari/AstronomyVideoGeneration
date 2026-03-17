using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AnalyticsRepositoryAndAggregationTests
{
    [Fact]
    public async Task Repository_StoresAndQueries_Analytics()
    {
        await using var db = new MediaFactoryDbContext(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repo = new EfPipelineRepository(db);

        await repo.AddVideoAnalyticsAsync(new VideoAnalytics { VideoId = "v1", Views = 200, Likes = 20, Comments = 5, DurationSeconds = 120, ContentType = ContentType.SpaceNews, RetrievedAt = DateTimeOffset.UtcNow, IsShort = false }, CancellationToken.None);
        await repo.AddVideoAnalyticsAsync(new VideoAnalytics { VideoId = "v2", Views = 500, Likes = 50, Comments = 6, DurationSeconds = 40, AverageViewDurationSeconds = 30, ContentType = ContentType.DailySkyGuide, RetrievedAt = DateTimeOffset.UtcNow, IsShort = true }, CancellationToken.None);
        await repo.SaveChangesAsync(CancellationToken.None);

        var recent = await repo.GetRecentAnalyticsAsync(10, CancellationToken.None);
        var byVideo = await repo.GetAnalyticsByVideoIdAsync("v1", CancellationToken.None);
        var top = await repo.GetTopPerformingAnalyticsAsync(null, null, 1, shortsOnly: false, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.Single(byVideo);
        Assert.Equal("v1", top.First().VideoId);
    }

    [Fact]
    public async Task Aggregation_Computes_TopShortsAndTitles()
    {
        var repo = new InMemoryAnalyticsRepo
        {
            Data =
            [
                new VideoAnalytics { VideoId = "v1", Views = 1000, ContentType = ContentType.SpaceNews, RetrievedAt = DateTimeOffset.UtcNow, IsShort = false, Title = "Mars Mission Update" },
                new VideoAnalytics { VideoId = "s1", Views = 800, ContentType = ContentType.SpaceNews, RetrievedAt = DateTimeOffset.UtcNow, IsShort = true, Title = "Mars in 60 seconds", DurationSeconds = 60, AverageViewDurationSeconds = 45, HookLine = "Wait for this Mars fact" }
            ]
        };

        var service = new AnalyticsAggregationService(repo);
        var summary = await service.BuildSummaryAsync(null, null, 5, CancellationToken.None);

        Assert.NotEmpty(summary.BestPerformingTitles);
        Assert.Single(summary.TopShortsByRetention);
        Assert.All(summary.BestPerformingContentTypes, x => Assert.True(x.Samples > 0));
    }

    private sealed class InMemoryAnalyticsRepo : IPipelineRepository
    {
        public List<VideoAnalytics> Data { get; set; } = [];
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
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) { Data.Add(analytics); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data.OrderByDescending(x => x.RetrievedAt).Take(take).ToArray());
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken)
        {
            var query = Data.AsEnumerable();
            if (from.HasValue)
                query = query.Where(x => x.RetrievedAt >= from.Value);
            if (to.HasValue)
                query = query.Where(x => x.RetrievedAt <= to.Value);

            return Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(query.OrderByDescending(x => x.RetrievedAt).Take(take).ToArray());
        }
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data.Where(x => x.VideoId == videoId).ToArray());
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data.Where(x => x.ContentType == contentType).Take(take).ToArray());
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data.Where(x => x.IsShort == shortsOnly).OrderByDescending(x => x.Views).Take(take).ToArray());
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
