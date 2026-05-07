using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class MetaPublishingTests
{
    [Fact]
    public async Task DryRun_WritesFacebookPayload_AndDoesNotCallApi()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("DryRun", result.Mode);
        Assert.Empty(handler.Requests);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-payload.json")));
    }

    [Fact]
    public async Task MissingMetaTokenFile_FailsClearly()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: false);
        var service = CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Equal(FacebookReelPublishService.IncompleteTokenMessage, result.Error);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json")));
    }

    [Fact]
    public async Task MissingShortVideo_FailsClearly_InRealMode()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: false, createToken: true);
        var service = CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("shorts/short-video.mp4", result.Error);
    }

    [Fact]
    public async Task Caption_IncludesHookLocationObjectsAndHashtags()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var service = CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, CaptionHashtagSuffix = "#Astronomy #NightSky #Stargazing" });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);
        var caption = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-caption.txt"));

        Assert.Contains("Amazing Saturn Tonight", caption);
        Assert.Contains("Location: Denver", caption);
        Assert.Contains("Saturn", caption);
        Assert.Contains("Moon", caption);
        Assert.Contains("#Astronomy #NightSky #Stargazing", caption);
    }

    [Fact]
    public async Task PublicMode_CallsStartUploadFinishInOrder()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { BaseAddress = "https://upload.example.test" };
        var service = CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("video-123", result.VideoId);
        Assert.Equal("post-456", result.PostId);
        Assert.Equal(["start", "upload", "finish"], handler.Requests.Select(x => x.Phase).ToArray());
        Assert.Contains("Authorization", handler.Requests[1].Headers.Keys);
        Assert.Equal("0", handler.Requests[1].Headers["offset"]);
        Assert.Equal(new FileInfo(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4")).Length.ToString(System.Globalization.CultureInfo.InvariantCulture), handler.Requests[1].Headers["file_size"]);
        Assert.Equal("application/octet-stream", handler.Requests[1].ContentHeaders["Content-Type"]);
    }

    [Fact]
    public async Task UploadFailure_IncludesResponseBody()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler { FailUpload = true };
        var service = CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("Facebook Reel binary upload failed with status 400", result.Error);
        Assert.Contains("missing offset", result.Error);
    }

    [Fact]
    public async Task ResultJson_IsWritten()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var service = CreateMetaService(workspace, repository, new TrackingMetaHandler(), new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true });

        _ = await service.PublishForPipelineRunAsync(run.Id, "facebook-reel", CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "facebook-reel-publish-result.json")));
    }

    [Fact]
    public async Task UnsupportedAsset_DoesNotInvokeFacebookOrInstagram()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var handler = new TrackingMetaHandler();
        var service = CreateMetaService(workspace, repository, handler, new MetaPublishingOptions { Enabled = true, Mode = "DryRun", PublishFacebookReel = true, PublishInstagramReel = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "instagram-reel", CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.Contains("Only facebook-reel is implemented", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task YouTubePublishing_RemainsUnaffected()
    {
        using var workspace = new TempMetaWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, createVideo: true, createToken: true);
        var youtube = new TrackingYouTubePublishService();
        var content = new ContentPublishService(
            repository,
            youtube,
            Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun" }),
            Options.Create(new YouTubeOptions()),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<ContentPublishService>.Instance);

        var results = await content.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None);

        Assert.All(results, result => Assert.Equal("YouTube", result.Platform));
        Assert.True(youtube.Called);
    }

    private static MetaPublishService CreateMetaService(TempMetaWorkspace workspace, IPipelineRepository repository, TrackingMetaHandler handler, MetaPublishingOptions options)
    {
        var facebook = new FacebookReelPublishService(
            new HttpClient(handler),
            Options.Create(new MetaOptions { TokenFilePath = workspace.TokenPath }),
            Options.Create(options),
            Options.Create(new RenderingOptions { FfprobePath = "missing-ffprobe-for-tests" }),
            NullLogger<FacebookReelPublishService>.Instance);

        return new MetaPublishService(
            repository,
            facebook,
            Options.Create(options),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<MetaPublishService>.Instance);
    }

    private sealed class TempMetaWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meta-publish-tests", Guid.NewGuid().ToString("N"));
        public string TokenPath => Path.Combine(Root, "meta-oauth-token.json");

        public TempMetaWorkspace() => Directory.CreateDirectory(Root);

        public InMemoryPipelineRepository CreateRepositoryWithRun(out PipelineRun run, bool createVideo, bool createToken)
        {
            run = new PipelineRun { RunDate = new DateOnly(2026, 5, 7), ContentType = ContentType.DailySkyGuide, LocationName = "Denver", TimeZone = "America/Denver", Status = PipelineRunStatus.Succeeded };
            var output = OutputDirectory(run);
            Directory.CreateDirectory(Path.Combine(output, "shorts"));
            File.WriteAllText(Path.Combine(output, "seo-metadata.json"), JsonSerializer.Serialize(new SeoMetadataResult { Title = "Root Title", Description = "Root description", TagsCsv = "astronomy" }));
            File.WriteAllText(Path.Combine(output, "shorts", "seo-metadata.json"), JsonSerializer.Serialize(new SeoMetadataResult { Title = "Amazing Saturn Tonight", Description = "Look up for a fast tour of tonight's best targets.", TagsCsv = "saturn,moon" }));
            File.WriteAllText(Path.Combine(output, "selected-visible-objects.json"), "[{\"objectName\":\"Saturn\"},{\"objectName\":\"Moon\"}]");
            File.WriteAllText(Path.Combine(output, "pre-publish-validation-report.json"), "{\"passed\":true,\"errors\":[],\"warnings\":[]}");
            File.WriteAllText(Path.Combine(output, "thumbnail-1.png"), "thumb");
            File.WriteAllText(Path.Combine(output, "final-video.mp4"), "video");
            if (createVideo)
            {
                File.WriteAllText(Path.Combine(output, "shorts", "short-video.mp4"), "not-empty-mp4");
            }

            if (createToken)
            {
                File.WriteAllText(TokenPath, JsonSerializer.Serialize(new MetaOAuthTokenFile("1135323479659435", "AstroPulse", "page-token-secret", "17841433998640998", "astro", "user-token-secret", DateTimeOffset.UtcNow)));
            }

            return new InMemoryPipelineRepository(run);
        }

        public string OutputDirectory(PipelineRun run) => Path.Combine(Root, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed class TrackingMetaHandler : HttpMessageHandler
    {
        public string BaseAddress { get; set; } = "https://upload.example.test";
        public bool FailUpload { get; set; }
        public List<(string Phase, Dictionary<string, string> Headers, Dictionary<string, string> ContentHeaders)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/video_reels", StringComparison.OrdinalIgnoreCase) && body.Contains("upload_phase=start", StringComparison.Ordinal))
            {
                Requests.Add(("start", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request)));
                return JsonResponse(new { video_id = "video-123", upload_url = BaseAddress + "/upload/video-123" });
            }

            if (request.RequestUri!.Host == "upload.example.test")
            {
                Requests.Add(("upload", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request)));
                return FailUpload
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = new { message = "missing offset" } }) }
                    : JsonResponse(new { success = true });
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/video_reels", StringComparison.OrdinalIgnoreCase) && body.Contains("upload_phase=finish", StringComparison.Ordinal))
            {
                Requests.Add(("finish", request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)), ContentHeaders(request)));
                return JsonResponse(new { post_id = "post-456", id = "post-456" });
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        private static HttpResponseMessage JsonResponse(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

        private static Dictionary<string, string> ContentHeaders(HttpRequestMessage request)
            => request.Content?.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)) ?? [];
    }

    private sealed class TrackingYouTubePublishService : IYouTubePublishService
    {
        public string PlatformName => "YouTube";
        public bool Called { get; private set; }
        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new PublishResult { Success = true, Platform = "YouTube", Mode = "DryRun", AssetType = request.AssetType, IsShort = request.IsShort });
        }
    }

    private sealed class InMemoryPipelineRepository : IPipelineRepository
    {
        private readonly PipelineRun _run;
        public InMemoryPipelineRepository(PipelineRun run) => _run = run;
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == _run.Id ? _run : null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([_run]);
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
}
