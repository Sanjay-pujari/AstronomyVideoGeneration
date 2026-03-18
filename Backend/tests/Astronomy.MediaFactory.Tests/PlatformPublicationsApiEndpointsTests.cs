using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PlatformPublicationsApiEndpointsTests
{
    [Fact]
    public async Task Endpoints_ReturnPlatformPublicationPayloads()
    {
        var repo = new TestRepo();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineRepository>(repo);

        var app = builder.Build();
        app.MapGet("/api/platform-publications/recent", async (int? take, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentPlatformPublicationRecordsAsync(take ?? 20, ct)));
        app.MapGet("/api/platform-publications/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
        {
            var item = await repository.GetPlatformPublicationRecordAsync(id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        app.MapGet("/api/platform-publications/by-short/{shortId:guid}", async (Guid shortId, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetPlatformPublicationRecordsByShortIdAsync(shortId, ct)));

        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform-publications/recent")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/platform-publications/{repo.Record.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/platform-publications/by-short/{repo.Record.ParentShortVideoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/platform-publications/{Guid.NewGuid()}")).StatusCode);

        var recent = await client.GetFromJsonAsync<List<PlatformPublicationRecord>>("/api/platform-publications/recent");
        Assert.Single(recent!);
        await app.StopAsync();
    }

    private sealed class TestRepo : IPipelineRepository
    {
        public PlatformPublicationRecord Record { get; } = new()
        {
            ParentShortVideoId = Guid.NewGuid(),
            Platform = ShortFormPlatform.YouTubeShorts,
            Status = PlatformPublicationStatus.Published,
            ExternalPostId = "yt-1"
        };

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
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task<PlatformPublicationRecord?> GetPlatformPublicationRecordAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == Record.Id ? Record : null);
        public Task<IReadOnlyCollection<PlatformPublicationRecord>> GetRecentPlatformPublicationRecordsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>([Record]);
        public Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByShortIdAsync(Guid shortVideoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>(shortVideoId == Record.ParentShortVideoId ? [Record] : []);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
