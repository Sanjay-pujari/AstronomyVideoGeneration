using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineAlertEmissionTests
{
    [Fact]
    public async Task RunAsync_EmitsPipelineFailedAlert()
    {
        var notifier = new RecordingNotifier();
        var orchestrator = new PipelineOrchestrator(
            new ThrowingContextProvider(),
            new NoOpTopicService(),
            new NoOpVisualProvider(),
            new NoOpScriptService(),
            new NoOpSpeechService(),
            new NoOpRenderService(),
            new NoOpBlobService(),
            new NoOpYouTubeService(),
            new NoOpShortsService(),
            new MetadataOptimizationService(NullLogger<MetadataOptimizationService>.Instance),
            new NoOpThumbnailService(),
            new InMemoryRepo(),
            Options.Create(new YouTubeOptions()),
            Options.Create(new RenderingOptions()),
            Options.Create(new PublishingValidationOptions()),
            NullLogger<PipelineOrchestrator>.Instance,
            operationalAlertNotifier: notifier);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune"), CancellationToken.None));

        Assert.Contains(notifier.Alerts, x => x.Category == AlertCategory.PipelineFailed);
    }

    private sealed class RecordingNotifier : IOperationalAlertNotifier
    {
        public List<OperationalAlert> Alerts { get; } = [];
        public Task NotifyAsync(OperationalAlert alert, CancellationToken cancellationToken) { Alerts.Add(alert); return Task.CompletedTask; }
    }

    private sealed class ThrowingContextProvider : IAstronomyContextProvider
    {
        public Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken) => throw new InvalidOperationException("context failed");
    }

    private sealed class NoOpTopicService : ITopicRankingService { public Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<RankedTopic>>([]); }
    private sealed class NoOpVisualProvider : IVisualAssetProvider { public Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]); }
    private sealed class NoOpScriptService : IScriptGenerationService { public Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken) => Task.FromResult(new ScriptResult()); }
    private sealed class NoOpSpeechService : ISpeechSynthesisService { public Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken) => Task.FromResult("x"); }
    private sealed class NoOpRenderService : IVideoRenderService { public Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken) => Task.FromResult("x"); }
    private sealed class NoOpBlobService : IAzureBlobStorageService { public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken) => Task.FromResult(new BlobUploadResult()); }
    private sealed class NoOpYouTubeService : IYouTubePublishingService { public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken) => Task.FromResult<string?>(null); }
    private sealed class NoOpShortsService : IShortsVideoRenderService { public Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken) => Task.FromResult(new ShortVideoRenderResult{ Script=new ShortScriptResult(), AudioPath = "audio.mp3", VideoPath = "video.mp4"}); }
    private sealed class NoOpThumbnailService : IThumbnailGenerationService { public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken) => Task.FromResult(new ThumbnailPlan()); }

    private sealed class InMemoryRepo : IPipelineRepository
    {
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
    }
}
