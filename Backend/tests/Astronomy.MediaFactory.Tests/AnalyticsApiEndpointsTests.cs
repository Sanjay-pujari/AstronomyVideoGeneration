using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AnalyticsApiEndpointsTests
{
    [Fact]
    public async Task Endpoints_ReturnExpectedPayloads()
    {
        var repo = new TestRepo();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineRepository>(repo);
        builder.Services.AddSingleton<IAnalyticsAggregationService, AnalyticsAggregationService>();

        var app = builder.Build();
        app.MapGet("/api/analytics/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAnalyticsAsync(50, ct)));
        app.MapGet("/api/analytics/top-performing", async (IAnalyticsAggregationService aggregationService, CancellationToken ct) => Results.Ok(await aggregationService.BuildSummaryAsync(null, null, 10, ct)));
        app.MapGet("/api/analytics/{videoId}", async (string videoId, IPipelineRepository repository, CancellationToken ct) =>
        {
            var items = await repository.GetAnalyticsByVideoIdAsync(videoId, ct);
            return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/recent")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/top-performing")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/analytics/v1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/analytics/missing")).StatusCode);

        var recent = await client.GetFromJsonAsync<List<VideoAnalytics>>("/api/analytics/recent");
        Assert.Single(recent!);
        await app.StopAsync();
    }

    private sealed class TestRepo : IPipelineRepository
    {
        public readonly List<VideoAnalytics> Data = [new VideoAnalytics { VideoId = "v1", Views = 123, RetrievedAt = DateTimeOffset.UtcNow, IsShort = false }];
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
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data.Where(x => x.VideoId == videoId).ToArray());
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>(Data);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
