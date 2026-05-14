using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PublishingFlowTests
{
    [Fact]
    public async Task AzureBlobStorageService_UploadAsync_ReturnsEmpty_WhenConnectionStringMissing()
    {
        var service = new AzureBlobStorageService(
            Options.Create(new AzureBlobOptions { ConnectionString = "", ContainerName = "astronomy-videos" }),
            NullLogger<AzureBlobStorageService>.Instance);

        var result = await service.UploadAsync(new BlobUploadRequest
        {
            BasePath = "test/path",
            VideoPath = "missing.mp4",
            AudioPath = "missing.mp3"
        }, CancellationToken.None);

        Assert.Null(result.VideoUrl);
        Assert.Null(result.AudioUrl);
        Assert.Null(result.ThumbnailUrl);
    }

    [Fact]
    public async Task YouTubePublishingService_UploadAsync_ReturnsNull_WhenCredentialsMissing()
    {
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "video");

        var service = new YouTubePublishingService(
            Options.Create(new YouTubeOptions { ClientId = "", ClientSecret = "" }),
            NullLogger<YouTubePublishingService>.Instance);

        var videoId = await service.UploadAsync(tempFile, "title", "desc", ["tag"], "private", CancellationToken.None);
        Assert.Null(videoId);

        File.Delete(tempFile);
    }

    [Fact]
    public async Task PipelineOrchestrator_UsesMonetizedDescription_WhenMonetizationSucceeds()
    {
        var repository = new FakePipelineRepository();
        var monetizationService = new ContentMonetizationService(
            Options.Create(new MonetizationOptions
            {
                AffiliateBaseUrl = "https://partners.example.com/products",
                DefaultAffiliateTag = "astro-42",
                EnableAffiliateLinks = true,
                EnablePinnedCommentText = true
            }),
            NullLogger<ContentMonetizationService>.Instance);
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance,
            contentMonetizationService: monetizationService);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.TelescopeTargets, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
        Assert.Contains("Recommended gear:", repository.PublishedVideos[0].OptimizedDescription);
        Assert.Single(repository.MonetizationRecords);
    }

    [Fact]
    public async Task PipelineOrchestrator_FallsBack_WhenMonetizationFails()
    {
        var repository = new FakePipelineRepository();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance,
            contentMonetizationService: new ThrowingMonetizationService());

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: false), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
        Assert.DoesNotContain("Recommended gear:", repository.PublishedVideos[0].OptimizedDescription ?? string.Empty);
        Assert.Empty(repository.MonetizationRecords);
    }

    [Fact]
    public async Task PipelineOrchestrator_Continues_WhenBlobOrYouTubeUploadFails()
    {
        var repository = new FakePipelineRepository();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new ThrowingBlobService(),
            new ThrowingYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
        Assert.Equal("UploadFailed", repository.PublishedVideos[0].Status);
    }

    [Fact]
    public async Task PipelineOrchestrator_UploadsThumbnailToYouTube_WhenVideoUploadSucceeds()
    {
        var repository = new FakePipelineRepository();
        var thumbnailPublisher = new TrackingThumbnailPublisher();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance,
            youTubeThumbnailPublisher: thumbnailPublisher);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.SpaceNews, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.True(thumbnailPublisher.WasCalled);
        Assert.Single(repository.PublishedVideos);
        Assert.True(repository.PublishedVideos[0].ThumbnailUploadedToYouTube);
    }

    [Fact]
    public async Task PipelineOrchestrator_Continues_WhenThumbnailGenerationFails()
    {
        var repository = new FakePipelineRepository();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new ThrowingThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: false), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
    }

    [Fact]
    public async Task PipelineOrchestrator_Continues_WhenThumbnailGenerationReturnsNull()
    {
        var repository = new FakePipelineRepository();
        var orchestrator = CreateOrchestrator(
            repository,
            new TrackingYouTubeService(),
            Options.Create(new PublishingOptions { Enabled = true, Mode = "Disabled" }),
            thumbnailGenerationService: new NullThumbnailGenerationService());

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: false), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Single(repository.PublishedVideos);
        Assert.NotNull(repository.PublishedVideos[0].ThumbnailPath);
    }

    [Fact]
    public async Task PublishingMode_Disabled_SkipsUpload()
    {
        var yt = new TrackingYouTubeService();
        var repository = new FakePipelineRepository();
        var orchestrator = CreateOrchestrator(repository, yt, Options.Create(new PublishingOptions { Enabled = true, Mode = "Disabled" }));
        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);
        Assert.False(yt.WasUploadCalled);
    }

    [Fact]
    public async Task PublishingMode_DryRun_WritesPayloadWithoutUpload()
    {
        var yt = new TrackingYouTubeService();
        var repository = new FakePipelineRepository();
        var orchestrator = CreateOrchestrator(repository, yt, Options.Create(new PublishingOptions { Enabled = true, Mode = "DryRun" }));
        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);
        Assert.False(yt.WasUploadCalled);
        var renderedVideo = Assert.Single(repository.Assets.Where(a => a.AssetType == "Video"));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(renderedVideo.LocalPath)!, "youtube-publish-payload.json")));
    }

    [Fact]
    public async Task PublishingMode_Private_UsesPrivatePrivacyStatus()
    {
        var yt = new TrackingYouTubeService();
        var orchestrator = CreateOrchestrator(new FakePipelineRepository(), yt, Options.Create(new PublishingOptions { Enabled = true, Mode = "Private" }));
        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);
        Assert.Equal("private", yt.LastPrivacyStatus);
    }

    [Fact]
    public async Task PublishingMode_Public_RequiresExplicitPublicMode()
    {
        var yt = new TrackingYouTubeService();
        var orchestrator = CreateOrchestrator(new FakePipelineRepository(), yt, Options.Create(new PublishingOptions { Enabled = true, Mode = "Private", DefaultPrivacyStatus = "public" }));
        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);
        Assert.Equal("private", yt.LastPrivacyStatus);
    }

    [Fact]
    public async Task Publishing_Blocked_WhenValidationFails()
    {
        var yt = new TrackingYouTubeService();
        var orchestrator = CreateOrchestrator(new FakePipelineRepository(), yt, Options.Create(new PublishingOptions { Enabled = true, Mode = "Private" }), new FailingValidationService());
        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);
        Assert.False(yt.WasUploadCalled);
    }


    [Fact]
    public async Task PipelineOrchestrator_SkipsYouTubeShortPublish_WhenPublishToYouTubeFalse()
    {
        var repository = new FakePipelineRepository();
        var contentPublishService = new TrackingContentPublishService();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions { Enabled = true }),
            NullLogger<PipelineOrchestrator>.Instance,
            publishingOptions: Options.Create(new PublishingOptions { Enabled = true, Mode = "Private", PublishShortVideo = true }),
            contentPublishService: contentPublishService);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: false), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.False(contentPublishService.WasCalled);
    }

    [Fact]
    public async Task PipelineOrchestrator_SkipsMetaPublish_WhenValidationFails()
    {
        var repository = new FakePipelineRepository();
        var metaPublishService = new TrackingMetaPublishService();
        var stageExecutor = new PipelineStageExecutor(repository, NullLogger<PipelineStageExecutor>.Instance);
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions { Enabled = true }),
            NullLogger<PipelineOrchestrator>.Instance,
            publishingOptions: Options.Create(new PublishingOptions { Enabled = true, Mode = "Private", PublishShortVideo = true }),
            prePublishValidationService: new FailingValidationService(),
            metaPublishingOptions: Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, PublishInstagramReel = true }),
            metaPublishService: metaPublishService,
            pipelineStageExecutor: stageExecutor);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.False(metaPublishService.WasCalled);
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.FacebookReelPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Pre-publish validation blocked publishing.");
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.InstagramReelPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Pre-publish validation blocked publishing.");
    }

    [Fact]
    public async Task PipelineOrchestrator_SkipsPublishStages_WhenPublishArtifactsAreMissing()
    {
        var repository = new FakePipelineRepository();
        var stageExecutor = new PipelineStageExecutor(repository, NullLogger<PipelineStageExecutor>.Instance);
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new PassThroughBlobService(),
            new SuccessfulYouTubeService(),
            new FakeShortsVideoRenderService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new FakeThumbnailGenerationService(),
            new PassThroughSeoMetadataGeneratorService(),
            repository,
            Options.Create(new YouTubeOptions { PrivacyStatus = "private" }),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions { Enabled = true }),
            NullLogger<PipelineOrchestrator>.Instance,
            publishingOptions: Options.Create(new PublishingOptions { Enabled = true, Mode = "Private", PublishShortVideo = true }),
            metaPublishingOptions: Options.Create(new MetaPublishingOptions { Enabled = true, Mode = "Public", PublishFacebookReel = true, PublishInstagramReel = true }),
            contentPublishService: new MissingArtifactContentPublishService(),
            metaPublishService: new MissingVideoMetaPublishService(),
            pipelineStageExecutor: stageExecutor);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.YouTubeLongPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Required publish artifact is missing: seo-metadata.json");
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.YouTubeShortPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Video file is missing: shorts/short-video.mp4");
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.FacebookReelPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Facebook Reel video is missing: shorts/short-video.mp4.");
        Assert.Contains(repository.StageExecutions, s => s.StageName == PipelineStageNames.InstagramReelPublished && s.Status == PersistentStageStatuses.Skipped && s.LastError == "Instagram Reel video is missing: shorts/short-video.mp4.");
    }



    private static PipelineOrchestrator CreateOrchestrator(
        FakePipelineRepository repository,
        IYouTubePublishingService yt,
        IOptions<PublishingOptions> publishingOptions,
        IPrePublishValidationService? validationService = null,
        IThumbnailGenerationService? thumbnailGenerationService = null)
        => new(new FakeContextProvider(), new FakeTopicRankingService(), new FakeVisualProvider(), new FakeScriptService(), new FakeSpeechService(), new FakeRenderService(), new PassThroughBlobService(), yt, new FakeShortsVideoRenderService(), new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance), thumbnailGenerationService ?? new FakeThumbnailGenerationService(), new PassThroughSeoMetadataGeneratorService(), repository, Options.Create(new YouTubeOptions { PrivacyStatus = "private" }), Options.Create(new RenderingOptions()), Options.Create(new PublishingValidationOptions { Enabled = true }), NullLogger<PipelineOrchestrator>.Instance, publishingOptions: publishingOptions, prePublishValidationService: validationService);


    private sealed class MissingArtifactContentPublishService : IContentPublishService
    {
        public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PublishResult>>([new PublishResult
            {
                Success = false,
                Platform = "YouTube",
                AssetType = "LongVideo",
                Mode = "Private",
                Error = "Required publish artifact is missing: seo-metadata.json"
            }]);

        public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PublishResult>>([new PublishResult
            {
                Success = false,
                Platform = "YouTube",
                AssetType = "ShortVideo",
                IsShort = true,
                Mode = "Private",
                Error = "Video file is missing: shorts/short-video.mp4"
            }]);
    }

    private sealed class MissingVideoMetaPublishService : IMetaPublishService
    {
        public Task<IReadOnlyList<MetaPublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset = "all", CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetaPublishResult>>([
                new MetaPublishResult { Success = false, Platform = "Facebook", Mode = "Public", Error = "Facebook Reel video is missing: shorts/short-video.mp4." },
                new MetaPublishResult { Success = false, Platform = "Instagram", Mode = "Public", Error = "Instagram Reel video is missing: shorts/short-video.mp4." }
            ]);
    }


    private sealed class TrackingContentPublishService : IContentPublishService
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<PublishResult>>([]);
        }

        public Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<PublishResult>>([]);
        }
    }

    private sealed class TrackingMetaPublishService : IMetaPublishService
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<MetaPublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset = "all", CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<MetaPublishResult>>([
                new MetaPublishResult { Success = false, Platform = "Facebook", Mode = "Public", Error = "Facebook should have been skipped." },
                new MetaPublishResult { Success = false, Platform = "Instagram", Mode = "Public", Error = "Instagram should have been skipped." }
            ]);
        }
    }

    private sealed class FakePipelineRepository : IPipelineRepository
    {
        private readonly List<PipelineRun> _runs = [];

        public List<MediaAsset> Assets { get; } = [];
        public List<PublishedVideo> PublishedVideos { get; } = [];
        public List<MonetizationRecord> MonetizationRecords { get; } = [];
        public List<PipelineStageExecution> StageExecutions { get; } = [];

        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken)
        {
            Assets.Add(asset);
            return Task.CompletedTask;
        }
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken)
        {
            PublishedVideos.Add(publishedVideo);
            return Task.CompletedTask;
        }

        public Task AddMonetizationRecordAsync(MonetizationRecord monetizationRecord, CancellationToken cancellationToken)
        {
            MonetizationRecords.Add(monetizationRecord);
            return Task.CompletedTask;
        }

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken)
        {
            _runs.Add(run);
            return Task.FromResult(run);
        }

        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_runs.FirstOrDefault(x => x.Id == id));
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
        public Task<IReadOnlyCollection<PipelineStageExecution>> GetStageExecutionsAsync(Guid pipelineRunId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<PipelineStageExecution>>(StageExecutions.Where(x => x.PipelineRunId == pipelineRunId).ToArray());
        public Task<PipelineStageExecution?> GetLatestStageExecutionAsync(Guid pipelineRunId, string stageName, CancellationToken cancellationToken)
            => Task.FromResult(StageExecutions.LastOrDefault(x => x.PipelineRunId == pipelineRunId && x.StageName == stageName));
        public Task AddStageExecutionAsync(PipelineStageExecution stageExecution, CancellationToken cancellationToken)
        {
            StageExecutions.Add(stageExecution);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken)
            => Task.FromResult(new AstronomyContext { Date = date, LocationName = locationName, TimeZone = timeZone });
    }

    private sealed class FakeTopicRankingService : ITopicRankingService
    {
        public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<RankedTopic>>([]);
    }

    private sealed class FakeVisualProvider : IVisualAssetProvider
    {
        public async Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken)
        {
            var visualPath = Path.Combine(outputDirectory, "scene.png");
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllTextAsync(visualPath, "image", cancellationToken);
            return [visualPath];
        }
    }

    private sealed class FakeScriptService : IScriptGenerationService
    {
        public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptResult { Title = "Sky", Description = "Desc", ScriptBody = "Body", Tags = ["astronomy"], EstimatedDurationSeconds = 30 });
    }

    private sealed class FakeSpeechService : ISpeechSynthesisService
    {
        public async Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            var audioPath = Path.Combine(outputDirectory, "narration.mp3");
            await File.WriteAllTextAsync(audioPath, "audio", cancellationToken);
            return audioPath;
        }
    }

    private sealed class FakeRenderService : IVideoRenderService
    {
        public async Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(manifest.OutputPath, "video", cancellationToken);
            return manifest.OutputPath;
        }
    }



    private sealed class FakeShortsVideoRenderService : IShortsVideoRenderService
    {
        public async Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var shortVideoPath = Path.Combine(outputDirectory, "short-video.mp4");
            var shortAudioPath = Path.Combine(outputDirectory, "short-audio.mp3");
            await File.WriteAllTextAsync(shortVideoPath, "video", cancellationToken);
            await File.WriteAllTextAsync(shortAudioPath, "audio", cancellationToken);
            return new ShortVideoRenderResult
            {
                Script = new ShortScriptResult { Hook = "Hook", ShortScript = "Script", Title = "Short", EstimatedDurationSeconds = 45, Tags = ["shorts", "astronomy"] },
                AudioPath = shortAudioPath,
                VideoPath = shortVideoPath
            };
        }
    }

    private sealed class ThrowingMonetizationService : IContentMonetizationService
    {
        public Task<MonetizationPlan> BuildPlanAsync(MonetizationInput input, CancellationToken cancellationToken)
            => throw new InvalidOperationException("monetization failed");
    }

    private sealed class ThrowingBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("blob fail");
    }

    private sealed class PassThroughBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new BlobUploadResult
            {
                VideoUrl = "https://blob/video.mp4",
                ThumbnailUrl = request.ThumbnailPath is null ? null : "https://blob/thumbnail.png"
            });
    }

    private sealed class SuccessfulYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
            => Task.FromResult<string?>("video-123");
    }

    private sealed class TrackingYouTubeService : IYouTubePublishingService
    {
        public bool WasUploadCalled { get; private set; }
        public string? LastPrivacyStatus { get; private set; }
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
        {
            WasUploadCalled = true;
            LastPrivacyStatus = visibility;
            return Task.FromResult<string?>("video-123");
        }
    }

    private sealed class FailingValidationService : IPrePublishValidationService
    {
        public Task<PrePublishValidationReport> ValidateAsync(PrePublishValidationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PrePublishValidationReport
            {
                PipelineRunId = request.PipelineRunId,
                ContentType = request.ContentType,
                IsShort = request.IsShort,
                FinalVideoPath = request.FinalVideoPath,
                Passed = false,
                Errors = ["validation failed"]
            });
    }

    private sealed class TrackingThumbnailPublisher : IYouTubeThumbnailPublisher
    {
        public bool WasCalled { get; private set; }

        public Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingYouTubeService : IYouTubePublishingService, IYouTubeThumbnailPublisher
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
            => throw new InvalidOperationException("youtube fail");

        public Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken)
            => throw new InvalidOperationException("youtube thumb fail");
    }

    private sealed class FakeThumbnailGenerationService : IThumbnailGenerationService
    {
        public async Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
        {
            var path = Path.Combine(request.OutputDirectory, "thumbnail.png");
            await File.WriteAllTextAsync(path, "thumb", cancellationToken);
            return new ThumbnailPlan
            {
                PrimaryThumbnailText = "TONIGHT'S SKY",
                AlternateThumbnailTexts = ["ASTRONOMY"],
                SelectedVisualPath = request.AvailableVisuals.FirstOrDefault(),
                ThumbnailPath = path,
                LayoutType = ThumbnailLayoutType.TopBanner
            };
        }
    }

    private sealed class ThrowingThumbnailGenerationService : IThumbnailGenerationService
    {
        public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("thumbnail fail");
    }

    private sealed class NullThumbnailGenerationService : IThumbnailGenerationService
    {
        public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult<ThumbnailPlan>(null!);
    }
}
