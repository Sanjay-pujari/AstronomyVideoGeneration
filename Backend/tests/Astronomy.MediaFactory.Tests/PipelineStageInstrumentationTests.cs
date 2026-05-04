using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineStageInstrumentationTests
{
    [Fact]
    public async Task RunAsync_RecordsStages_AndFallbackFailure()
    {
        var repository = new FakePipelineRepository();
        var recorder = new RecordingStageRecorder();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new ThrowingBlobService(),
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
            pipelineStageRecorder: recorder);

        var result = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, result.Status);
        Assert.Contains(recorder.Stages, s => s.StageName == "AstronomyData" && s.Status == PipelineStageStatuses.Succeeded);
        Assert.Contains(recorder.Stages, s => s.StageName == "BlobUpload" && s.Status == PipelineStageStatuses.FailedWithFallback);
        Assert.All(recorder.Stages, s => Assert.True(s.DurationMs >= 0));
    }


    [Fact]
    public async Task RunAsync_PublishesStageAlerts_ForSlowAndFailedStages()
    {
        var repository = new FakePipelineRepository();
        var recorder = new RecordingStageRecorder();
        var alerts = new RecordingStageAlertPublisher();
        var orchestrator = new PipelineOrchestrator(
            new FakeContextProvider(),
            new FakeTopicRankingService(),
            new FakeVisualProvider(),
            new FakeScriptService(),
            new FakeSpeechService(),
            new FakeRenderService(),
            new ThrowingBlobService(),
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
            pipelineStageRecorder: recorder,
            stageAlertPublisher: alerts,
            operationsOptions: Options.Create(new OperationsOptions { SlowStageThresholdMs = 0 }));

        _ = await orchestrator.RunAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune", PublishToYouTube: true), CancellationToken.None);

        Assert.NotEmpty(alerts.SlowStages);
        Assert.Contains(alerts.Failures, x => x.StageName == "BlobUpload" && x.Status == PipelineStageStatuses.FailedWithFallback);
    }

    private sealed class RecordingStageRecorder : IPipelineStageRecorder
    {
        public List<PipelineStageExecution> Stages { get; } = [];
        public Task<PipelineStageExecution> StartStageAsync(Guid pipelineRunId, string stageName, string? metadataJson, CancellationToken cancellationToken)
        {
            var stage = new PipelineStageExecution { PipelineRunId = pipelineRunId, StageName = stageName, Status = PipelineStageStatuses.Running, StartedAt = DateTimeOffset.UtcNow, MetadataJson = metadataJson };
            Stages.Add(stage);
            return Task.FromResult(stage);
        }

        public Task CompleteStageAsync(PipelineStageExecution stageExecution, string? metadataJson, CancellationToken cancellationToken)
        {
            stageExecution.FinishedAt = DateTimeOffset.UtcNow;
            stageExecution.DurationMs = (long)Math.Max(0, (stageExecution.FinishedAt.Value - stageExecution.StartedAt).TotalMilliseconds);
            stageExecution.Status = PipelineStageStatuses.Succeeded;
            return Task.CompletedTask;
        }

        public Task FailStageAsync(PipelineStageExecution stageExecution, string errorMessage, bool continuedWithFallback, string? metadataJson, CancellationToken cancellationToken)
        {
            stageExecution.FinishedAt = DateTimeOffset.UtcNow;
            stageExecution.DurationMs = (long)Math.Max(0, (stageExecution.FinishedAt.Value - stageExecution.StartedAt).TotalMilliseconds);
            stageExecution.Status = continuedWithFallback ? PipelineStageStatuses.FailedWithFallback : PipelineStageStatuses.Failed;
            stageExecution.ErrorMessage = errorMessage;
            return Task.CompletedTask;
        }
    }


    private sealed class RecordingStageAlertPublisher : IStageAlertPublisher
    {
        public List<StageAlertContext> SlowStages { get; } = [];
        public List<StageAlertContext> Failures { get; } = [];

        public Task PublishSlowStageAsync(StageAlertContext context, CancellationToken cancellationToken)
        {
            SlowStages.Add(context);
            return Task.CompletedTask;
        }

        public Task PublishStageFailureAsync(StageAlertContext context, CancellationToken cancellationToken)
        {
            Failures.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePipelineRepository : IPipelineRepository
    {
        public List<PublishedVideo> PublishedVideos { get; } = [];
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) { PublishedVideos.Add(publishedVideo); return Task.CompletedTask; }
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
            Directory.CreateDirectory(outputDirectory);
            var visualPath = Path.Combine(outputDirectory, "scene.png");
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

    private sealed class ThrowingBlobService : IAzureBlobStorageService
    {
        public Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("blob fail");
    }

    private sealed class SuccessfulYouTubeService : IYouTubePublishingService
    {
        public Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken)
            => Task.FromResult<string?>("video-123");
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

    private sealed class FakeThumbnailGenerationService : IThumbnailGenerationService
    {
        public Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ThumbnailPlan
            {
                PrimaryThumbnailText = "SKY",
                AlternateThumbnailTexts = ["SKY"],
                SelectedVisualPath = request.AvailableVisuals.FirstOrDefault(),
                ThumbnailPath = request.AvailableVisuals.FirstOrDefault(),
                LayoutType = ThumbnailLayoutType.CenteredTitleOverlay
            });
    }
}
