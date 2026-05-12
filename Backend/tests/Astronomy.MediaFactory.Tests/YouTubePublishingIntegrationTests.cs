using System.Net;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class YouTubePublishingIntegrationTests
{
    [Fact]
    public async Task DryRun_WritesPayload_AndDoesNotCallYouTube()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "DryRun", RequirePrePublishValidation = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("DryRun", result.Mode);
        Assert.False(api.UploadCalled);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-payload.json")));
    }

    [Fact]
    public async Task MissingRefreshToken_FailsClearly()
    {
        var auth = new YouTubeAuthService(new HttpClient(new StaticHandler(HttpStatusCode.OK, "{}")), Options.Create(new YouTubeOptions { ClientId = "id", ClientSecret = "secret", RefreshToken = "" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal("YouTube refresh token is missing. Complete one-time OAuth setup first.", ex.Message);
    }

    [Fact]
    public async Task ChannelVerificationFailure_BlocksUpload()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient { ChannelFailure = "No YouTube channel found for authenticated account." };
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.False(api.UploadCalled);
        Assert.Contains("No YouTube channel found", result.Error);
    }

    [Fact]
    public async Task ValidationFailure_BlocksPublish()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: false);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", RequirePrePublishValidation = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.False(result.Success);
        Assert.False(api.UploadCalled);
        Assert.Contains("Pre-publish validation failed", result.Error);
    }

    [Fact]
    public async Task PrivateMode_SetsPrivacyStatusPrivate()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });

        _ = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.Equal("private", api.LastRequest?.PrivacyStatus);
    }

    [Fact]
    public async Task PublicMode_OnlyWorksWhenExplicitlyConfigured()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "DryRun", DefaultPrivacyStatus = "public" });

        _ = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);
        var payload = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-payload.json"));

        Assert.Contains("\"privacyStatus\": \"private\"", payload);
    }

    [Fact]
    public async Task ThumbnailMissing_DoesNotFailVideoUpload()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createThumbnail: false);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", UploadThumbnail = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.True(api.UploadCalled);
        Assert.Contains("Thumbnail file is missing", result.Error);
    }


    [Fact]
    public async Task ThumbnailTransientFailure_IsRetried_AndStillSucceedsWithoutError()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient { ThumbnailFailuresBeforeSuccess = 1 };
        var service = CreateContentService(
            workspace,
            repository,
            api,
            new PublishingOptions { Enabled = true, Mode = "Private", UploadThumbnail = true },
            new YouTubeOptions { DefaultPrivacyStatus = "private", CategoryId = "28", UploadRetryAttempts = 2, RetryBaseDelaySeconds = 0, MaxRetryDelaySeconds = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(2, api.ThumbnailUploadCalls);
    }

    [Fact]
    public async Task SuccessfulRealMode_SavesYouTubeVideoId()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient { VideoId = "abc123" };
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("abc123", run.YouTubeVideoId);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-result.json")));
    }



    [Fact]
    public async Task LongVideoAsset_IsDetected()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "DryRun" });

        _ = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);
        var assetsJson = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-assets.json"));

        Assert.Contains("LongVideo", assetsJson);
        Assert.Contains("final-video.mp4", assetsJson);
    }

    [Fact]
    public async Task ShortVideoAsset_IsDetected_FromShortsShortVideo()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "DryRun" });

        _ = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);
        var assetsJson = await File.ReadAllTextAsync(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-assets.json"));

        Assert.Contains("ShortVideo", assetsJson);
        Assert.Contains(Path.Combine("shorts", "short-video.mp4"), assetsJson);
        Assert.Contains("\"willUpload\": true", assetsJson);
    }

    [Fact]
    public async Task ShortVideoAsset_DoesNotRequireShortsFinalVideo()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var shortFinalVideoPath = Path.Combine(workspace.OutputDirectory(run), "shorts", "final-video.mp4");
        Assert.False(File.Exists(shortFinalVideoPath));
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", PublishLongVideo = false, PublishShortVideo = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None)).Single(x => x.AssetType == "ShortVideo");

        Assert.True(result.Success);
        Assert.Single(api.Requests);
        Assert.Equal(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4"), api.Requests[0].VideoPath);
    }


    [Fact]
    public async Task ShortOnlyPublish_DoesNotRequireRootSeoMetadata_WhenShortMetadataExists()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true, createRootMetadata: false);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", PublishLongVideo = false, PublishShortVideo = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, "short", CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal("ShortVideo", result.AssetType);
        Assert.Single(api.Requests);
        Assert.Equal("Short Title #Shorts", api.Requests[0].Title);
        Assert.Equal(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4"), api.Requests[0].VideoPath);
    }

    [Fact]
    public async Task BothAssetsUploaded_WhenEnabled()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", PublishLongVideo = true, PublishShortVideo = true });

        var results = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.Equal(2, api.UploadCalls);
        Assert.Contains(results, x => x.AssetType == "LongVideo" && x.Success);
        Assert.Contains(results, x => x.AssetType == "ShortVideo" && x.Success);
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-result-long.json")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-result-short.json")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory(run), "youtube-publish-results.json")));
    }

    [Fact]
    public async Task ShortSkipped_WhenPublishShortVideoFalse()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", PublishLongVideo = true, PublishShortVideo = false });

        var results = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.Single(api.Requests);
        Assert.Equal("LongVideo", api.Requests[0].AssetType);
        Assert.Contains(results, x => x.AssetType == "ShortVideo" && x.Error!.Contains("PublishShortVideo is false"));
    }

    [Fact]
    public async Task ValidLongThumbnail_IsUploaded()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", UploadThumbnail = true }, new YouTubeOptions { DefaultPrivacyStatus = "private", CategoryId = "28", UploadThumbnailForLongVideos = true });

        _ = await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None);

        Assert.Equal(1, api.ThumbnailUploadCalls);
    }

    [Fact]
    public async Task ThumbnailOverTwoMb_IsSkippedWithWarning()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, largeThumbnail: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", UploadThumbnail = true });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.Equal(0, api.ThumbnailUploadCalls);
        Assert.Contains("larger than 2MB", result.Warnings.Single());
    }

    [Fact]
    public async Task ThumbnailFailure_DoesNotFailVideoUpload()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient { ThumbnailFailuresBeforeSuccess = 10 };
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private", UploadThumbnail = true }, new YouTubeOptions { DefaultPrivacyStatus = "private", CategoryId = "28", UploadRetryAttempts = 1, RetryBaseDelaySeconds = 0, MaxRetryDelaySeconds = 1 });

        var result = (await service.PublishForPipelineRunAsync(run.Id, CancellationToken.None)).Single();

        Assert.True(result.Success);
        Assert.True(api.UploadCalled);
        Assert.Contains("Video uploaded but thumbnail upload failed", result.Warnings.Single());
    }

    [Theory]
    [InlineData("long", "LongVideo")]
    [InlineData("short", "ShortVideo")]
    public async Task ManualAssetSelector_UploadsOnlyRequestedAsset(string selector, string expectedAssetType)
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineRepository>(repository);
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapPost("/api/youtubepublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IContentPublishService publishService, IPipelineRepository repo, CancellationToken ct) =>
        {
            var existingRun = await repo.GetAsync(pipelineRunId, ct);
            if (existingRun is null) return Results.NotFound();
            return Results.Ok(await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct));
        });

        await app.StartAsync();
        var response = await app.GetTestClient().PostAsync($"/api/youtubepublish/{run.Id}?asset={selector}", null);
        response.EnsureSuccessStatusCode();
        await app.StopAsync();

        Assert.Single(api.Requests);
        Assert.Equal(expectedAssetType, api.Requests[0].AssetType);
        if (selector == "short")
        {
            Assert.Equal(Path.Combine(workspace.OutputDirectory(run), "shorts", "short-video.mp4"), api.Requests[0].VideoPath);
        }
    }

    [Fact]
    public async Task ManualAssetSelectorAll_UploadsBothAssets()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true, createShort: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineRepository>(repository);
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapPost("/api/youtubepublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IContentPublishService publishService, IPipelineRepository repo, CancellationToken ct) =>
        {
            var existingRun = await repo.GetAsync(pipelineRunId, ct);
            if (existingRun is null) return Results.NotFound();
            return Results.Ok(await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct));
        });

        await app.StartAsync();
        var response = await app.GetTestClient().PostAsync($"/api/youtubepublish/{run.Id}?asset=all", null);
        response.EnsureSuccessStatusCode();
        await app.StopAsync();

        Assert.Equal(2, api.UploadCalls);
        Assert.Contains(api.Requests, x => x.AssetType == "LongVideo");
        Assert.Contains(api.Requests, x => x.AssetType == "ShortVideo");
    }

    [Fact]
    public async Task ManualEndpoint_PublishesExistingRun()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateRepositoryWithRun(out var run, passedValidation: true);
        var api = new TrackingYouTubeApiClient();
        var service = CreateContentService(workspace, repository, api, new PublishingOptions { Enabled = true, Mode = "Private" });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineRepository>(repository);
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapPost("/api/youtubepublish/{pipelineRunId:guid}", async (Guid pipelineRunId, IContentPublishService publishService, IPipelineRepository repo, CancellationToken ct) =>
        {
            var existingRun = await repo.GetAsync(pipelineRunId, ct);
            if (existingRun is null) return Results.NotFound();
            var results = await publishService.PublishForPipelineRunAsync(pipelineRunId, ct);
            return Results.Ok(results.First());
        });

        await app.StartAsync();
        var response = await app.GetTestClient().PostAsync($"/api/youtubepublish/{run.Id}", null);

        response.EnsureSuccessStatusCode();
        Assert.True(api.UploadCalled);
        await app.StopAsync();
    }

    [Fact]
    public async Task AutoPublish_UsesContentPublishService_WhenPipelinePublishingEnabled()
    {
        var contentPublisher = new TrackingContentPublishService();
        var orchestrator = new PipelineOrchestrator(
            new AutoFakeContextProvider(),
            new AutoFakeTopicRankingService(),
            new AutoFakeVisualProvider(),
            new AutoFakeScriptService(),
            new AutoFakeSpeechService(),
            new AutoFakeRenderService(),
            new AutoBlobService(),
            new AutoYouTubeService(),
            new AutoShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new AutoThumbnailGenerationService(),
            new AutoSeoMetadataGeneratorService(),
            new InMemoryRepository(new PipelineRun()),
            Options.Create(new YouTubeOptions()),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions { Enabled = false }),
            NullLogger<PipelineOrchestrator>.Instance,
            publishingOptions: Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun", RequirePrePublishValidation = false }),
            maintenanceOptions: Options.Create(new MaintenanceOptions { WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) }),
            contentPublishService: contentPublisher);

        var run = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune"), CancellationToken.None);

        Assert.Equal(run.Id, contentPublisher.PublishedRunId);
    }

    private static IContentPublishService CreateContentService(
        TempWorkspace workspace,
        InMemoryRepository repository,
        TrackingYouTubeApiClient api,
        PublishingOptions publishingOptions,
        YouTubeOptions? youTubeOptions = null)
    {
        youTubeOptions ??= new YouTubeOptions { DefaultPrivacyStatus = "private", CategoryId = "28" };
        var publishService = new YouTubePublishService(
            new StaticAuthService(),
            api,
            Options.Create(publishingOptions),
            Options.Create(youTubeOptions),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            repository,
            NullLogger<YouTubePublishService>.Instance);

        return new ContentPublishService(
            repository,
            publishService,
            new FixedTokenHealthService(),
            Options.Create(publishingOptions),
            Options.Create(youTubeOptions),
            Options.Create(new TokenHealthOptions { Enabled = false }),
            Options.Create(new MaintenanceOptions { WorkingDirectory = workspace.Root }),
            NullLogger<ContentPublishService>.Instance);
    }

    private sealed class StaticAuthService : IYouTubeAuthService
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("access-token");
    }

    private sealed class TrackingYouTubeApiClient : IYouTubeApiClient
    {
        public bool UploadCalled { get; private set; }
        public int UploadCalls { get; private set; }
        public List<PublishRequest> Requests { get; } = [];
        public int ThumbnailUploadCalls { get; private set; }
        public int ThumbnailFailuresBeforeSuccess { get; init; }
        public PublishRequest? LastRequest { get; private set; }
        public string? ChannelFailure { get; init; }
        public string VideoId { get; init; } = "video-123";

        public Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string accessToken, CancellationToken cancellationToken)
            => ChannelFailure is null
                ? Task.FromResult(new YouTubeChannelInfo { ChannelId = "channel-1", ChannelTitle = "Astronomy" })
                : throw new InvalidOperationException(ChannelFailure);

        public Task<string> UploadVideoAsync(PublishRequest request, string accessToken, CancellationToken cancellationToken)
        {
            UploadCalled = true;
            UploadCalls++;
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(VideoId);
        }

        public Task UploadThumbnailAsync(string videoId, string thumbnailPath, string accessToken, CancellationToken cancellationToken)
        {
            ThumbnailUploadCalls++;
            if (ThumbnailUploadCalls <= ThumbnailFailuresBeforeSuccess)
            {
                throw new InvalidOperationException("YouTube thumbnail upload did not complete successfully. Status: Failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        public StaticHandler(HttpStatusCode statusCode, string content) { _statusCode = statusCode; _content = content; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_content) });
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        public string OutputDirectory(PipelineRun run) => Path.Combine(Root, run.ContentType.ToString(), run.RunDate.ToString("yyyy-MM-dd"), run.Id.ToString("N"));

        public InMemoryRepository CreateRepositoryWithRun(out PipelineRun run, bool passedValidation, bool createThumbnail = true, bool createShort = false, bool largeThumbnail = false, bool createRootMetadata = true)
        {
            run = new PipelineRun { ContentType = ContentType.DailySkyGuide, RunDate = DateOnly.FromDateTime(DateTime.UtcNow), LocationName = "Pune" };
            var output = OutputDirectory(run);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "final-video.mp4"), "video");
            if (createThumbnail)
            {
                if (largeThumbnail)
                {
                    File.WriteAllBytes(Path.Combine(output, "thumbnail-1.png"), new byte[(2 * 1024 * 1024) + 1]);
                }
                else
                {
                    File.WriteAllText(Path.Combine(output, "thumbnail-1.png"), "thumb");
                }
            }
            if (createShort)
            {
                var shorts = Path.Combine(output, "shorts");
                Directory.CreateDirectory(shorts);
                File.WriteAllText(Path.Combine(shorts, "short-video.mp4"), "short video");
                File.WriteAllText(Path.Combine(shorts, "seo-metadata.json"), "{\"title\":\"Short Title #Shorts\",\"description\":\"Short Description\",\"tagsCsv\":\"shorts,astronomy\"}");
                File.WriteAllText(Path.Combine(shorts, "pre-publish-validation-report.json"), passedValidation
                    ? "{\"passed\":true,\"errors\":[]}"
                    : "{\"passed\":false,\"errors\":[\"short validation failed\"]}");
            }
            if (createRootMetadata)
            {
                File.WriteAllText(Path.Combine(output, "seo-metadata.json"), "{\"title\":\"Title\",\"description\":\"Description\",\"tagsCsv\":\"astronomy,night sky\"}");
            }
            File.WriteAllText(Path.Combine(output, "pre-publish-validation-report.json"), passedValidation
                ? "{\"passed\":true,\"errors\":[]}"
                : "{\"passed\":false,\"errors\":[\"validation failed\"]}");
            return new InMemoryRepository(run);
        }
    }

    private sealed class InMemoryRepository : IPipelineRepository
    {
        private readonly PipelineRun _run;
        public InMemoryRepository(PipelineRun run) => _run = run;
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == _run.Id ? _run : null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([_run]);
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
    }

    private sealed class TrackingContentPublishService : IContentPublishService
    {
        public Guid? PublishedRunId { get; private set; }
        public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        {
            PublishedRunId = pipelineRunId;
            return Task.FromResult<IReadOnlyList<PublishResult>>([new PublishResult { Success = true, Platform = "YouTube", Mode = "DryRun" }]);
        }
    }

    private sealed class AutoFakeContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext { Date = date, LocationName = locationName });
    }

    private sealed class AutoFakeTopicRankingService : ITopicRankingService
    {
        public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<RankedTopic>>([]);
    }

    private sealed class AutoFakeVisualProvider : IVisualAssetProvider
    {
        public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "visual.png");
            await File.WriteAllTextAsync(path, "visual", cancellationToken);
            return [path];
        }
    }

    private sealed class AutoFakeScriptService : IScriptGenerationService
    {
        public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptResult { Title = "Title", Description = "Description", ScriptBody = "Script", EstimatedDurationSeconds = 90, Tags = ["astronomy"] });
    }

    private sealed class AutoFakeSpeechService : ISpeechSynthesisService
    {
        public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "audio.mp3");
            await File.WriteAllTextAsync(path, "audio", cancellationToken);
            return path;
        }
    }

    private sealed class AutoFakeRenderService : IVideoRenderService
    {
        public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(manifest.OutputPath, "video", cancellationToken);
            return manifest.OutputPath;
        }
    }

    private sealed class AutoBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken) => Task.FromResult(new BlobUploadResult());
    }

    private sealed class AutoYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class AutoShortsVideoRenderService : IShortsVideoRenderService
    {
        public Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
            => Task.FromResult<ShortVideoRenderResult>(null!);
    }

    private sealed class AutoThumbnailGenerationService : IThumbnailGenerationService
    {
        public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ThumbnailPlan { ThumbnailPath = request.AvailableVisuals.FirstOrDefault(), SelectedVisualPath = request.AvailableVisuals.FirstOrDefault() });
    }

    private sealed class AutoSeoMetadataGeneratorService : ISeoMetadataGeneratorService
    {
        public async Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken)
        {
            var result = new SeoMetadataResult { Title = "Title", Description = "Description", TagsCsv = "astronomy" };
            await SeoMetadataGeneratorService.WriteToFileAsync(result, Path.GetDirectoryName(request.ThumbnailVariants.FirstOrDefault() ?? Path.GetTempPath()) ?? Path.GetTempPath(), cancellationToken);
            return result;
        }
    }

}
