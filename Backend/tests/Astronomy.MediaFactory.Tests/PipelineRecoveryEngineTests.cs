using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineRecoveryEngineTests
{
    [Fact]
    public async Task StageSuccess_IsPersisted()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);

        var result = await executor.ExecuteStageAsync(run.Id, PipelineStageNames.RenderingCompleted, _ => Task.FromResult("ok"), new StageExecutionOptions { MaxAttempts = 1 }, CancellationToken.None);

        Assert.Equal("ok", result);
        var stage = Assert.Single(await repo.GetStageExecutionsAsync(run.Id, CancellationToken.None));
        Assert.Equal(PersistentStageStatuses.Succeeded, stage.Status);
        Assert.Equal(1, stage.AttemptCount);
    }

    [Fact]
    public async Task RetryableFailure_Retries()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);
        var calls = 0;

        var result = await executor.ExecuteStageAsync(run.Id, PipelineStageNames.YouTubeLongPublished, _ =>
        {
            calls++;
            if (calls == 1)
                throw new TimeoutException("network timeout");
            return Task.FromResult("uploaded");
        }, new StageExecutionOptions { MaxAttempts = 2, RetryDelaySeconds = 0 }, CancellationToken.None);

        Assert.Equal("uploaded", result);
        Assert.Equal(2, calls);
        Assert.Equal(2, (await repo.GetLatestStageExecutionAsync(run.Id, PipelineStageNames.YouTubeLongPublished, CancellationToken.None))!.AttemptCount);
    }

    [Fact]
    public async Task NonRetryableFailure_DoesNotRetry()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteStageAsync<bool>(run.Id, PipelineStageNames.ValidationCompleted, _ =>
        {
            calls++;
            throw new InvalidOperationException("validation failed: scene mismatch");
        }, new StageExecutionOptions { MaxAttempts = 3, RetryDelaySeconds = 0 }, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Resume_SkipsCompletedStages()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.RenderingCompleted, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.InstagramReelPublished, Status = PersistentStageStatuses.Failed, LastError = "Meta processing timeout", AttemptCount = 1, MaxAttempts = 3 }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        var status = await service.ResumeAsync(run.Id, null, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Contains(status!.Stages, s => s.StageName == PipelineStageNames.RenderingCompleted && s.Status == PersistentStageStatuses.Succeeded);
        Assert.Contains(status.Stages, s => s.StageName == PipelineStageNames.InstagramReelPublished && s.Status == PersistentStageStatuses.Failed);
    }

    [Fact]
    public async Task Resume_RetriesFailedInstagramOnly()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.YouTubeLongPublished, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.InstagramReelPublished, Status = PersistentStageStatuses.Failed }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        var status = await service.RetryPublishAsync(run.Id, "instagram", CancellationToken.None);

        Assert.Contains(status!.Stages, s => s.StageName == PipelineStageNames.YouTubeLongPublished && s.Status == PersistentStageStatuses.Succeeded);
        Assert.Contains(status.Stages, s => s.StageName == PipelineStageNames.InstagramReelPublished && s.Status == PersistentStageStatuses.Pending);
    }

    [Fact]
    public async Task SuccessfulYouTubeUpload_IsNotDuplicated()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.YouTubeLongPublished, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        await repo.AddPublishedVideoAsync(new PublishedVideo { PipelineRunId = run.Id, YouTubeVideoId = "yt-1", Status = "Published" }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        await service.RetryPublishAsync(run.Id, "youtube", CancellationToken.None);

        Assert.Single(repo.PublishedVideos);
        Assert.True(File.Exists(Path.Combine(run.OutputFolder!, "publish-idempotency-check.json")));
    }

    [Fact]
    public async Task ForceStage_RerunsSelectedStage()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.InstagramReelPublished, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        var status = await service.ResumeAsync(run.Id, PipelineStageNames.InstagramReelPublished, CancellationToken.None);

        Assert.Contains(status!.Stages, s => s.StageName == PipelineStageNames.InstagramReelPublished && s.Status == PersistentStageStatuses.Pending);
    }

    [Fact]
    public async Task PipelineStateJson_IsGenerated()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);

        await executor.ExecuteStageAsync(run.Id, PipelineStageNames.Created, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1 }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(run.OutputFolder!, "pipeline-state.json")));
    }

    [Fact]
    public async Task StatusApiModel_ReturnsCorrectStages()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.SpeechCompleted, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        var status = await service.GetStatusAsync(run.Id, CancellationToken.None);

        Assert.Equal(run.Id, status!.RunId);
        Assert.Contains(status.Stages, s => s.StageName == PipelineStageNames.SpeechCompleted);
    }



    [Fact]
    public async Task OutputPath_IsPersisted_ForMainArtifactStages()
    {
        var expectedOutputs = new Dictionary<string, string>
        {
            [PipelineStageNames.ObservationWindowCompleted] = "observation-window.json",
            [PipelineStageNames.SkyfieldCompleted] = "skyfield-night-plan-response.json",
            [PipelineStageNames.SceneContextCompleted] = "scene-observation-context.json",
            [PipelineStageNames.NarrationCompleted] = "narration-context.json",
            [PipelineStageNames.SpeechCompleted] = "narration.mp3",
            [PipelineStageNames.StellariumCompleted] = "stellarium",
            [PipelineStageNames.RenderingCompleted] = "final-video.mp4",
            [PipelineStageNames.ThumbnailCompleted] = "thumbnail-selection.json",
            [PipelineStageNames.SeoCompleted] = "seo-metadata.json",
            ["BlobUpload"] = "public-media-upload-result.json",
            [PipelineStageNames.ValidationCompleted] = "pre-publish-validation-report.json",
            [PipelineStageNames.YouTubeLongPublished] = "youtube-publish-result-long.json",
            [PipelineStageNames.YouTubeShortPublished] = "youtube-publish-result-short.json",
            [PipelineStageNames.FacebookReelPublished] = "facebook-reel-publish-result.json",
            [PipelineStageNames.InstagramReelPublished] = "instagram-reel-publish-result.json",
            [PipelineStageNames.Completed] = "output"
        };
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);

        foreach (var (stageName, fileName) in expectedOutputs)
        {
            var outputPath = Path.Combine(run.OutputFolder!, fileName);
            await executor.ExecuteStageAsync(run.Id, stageName, _ => Task.FromResult(true), new StageExecutionOptions { MaxAttempts = 1, OutputPath = outputPath }, CancellationToken.None);
        }

        foreach (var (stageName, fileName) in expectedOutputs)
        {
            var stage = await repo.GetLatestStageExecutionAsync(run.Id, stageName, CancellationToken.None);
            Assert.Equal(Path.Combine(run.OutputFolder!, fileName), stage!.OutputPath);
        }
    }

    [Fact]
    public async Task SucceededStage_WithMissingOutputPath_Reruns()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.RenderingCompleted, Status = PersistentStageStatuses.Succeeded, OutputPath = Path.Combine(run.OutputFolder!, "missing.mp4"), AttemptCount = 1, MaxAttempts = 1 }, CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);
        var calls = 0;

        var result = await executor.ExecuteStageAsync(run.Id, PipelineStageNames.RenderingCompleted, _ =>
        {
            calls++;
            return Task.FromResult("rerendered");
        }, new StageExecutionOptions { MaxAttempts = 1 }, CancellationToken.None);

        Assert.Equal("rerendered", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SucceededStringStage_WithExistingOutputPath_ReturnsPersistedPath()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        var outputPath = Path.Combine(run.OutputFolder!, "final-video.mp4");
        await File.WriteAllTextAsync(outputPath, "video");
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.RenderingCompleted, Status = PersistentStageStatuses.Succeeded, OutputPath = outputPath, AttemptCount = 1, MaxAttempts = 1 }, CancellationToken.None);
        var executor = new PipelineStageExecutor(repo, NullLogger<PipelineStageExecutor>.Instance);

        var result = await executor.ExecuteStageAsync(run.Id, PipelineStageNames.RenderingCompleted, _ => Task.FromResult("should-not-run"), new StageExecutionOptions { MaxAttempts = 1 }, CancellationToken.None);

        Assert.Equal(outputPath, result);
    }

    [Fact]
    public async Task StatusApi_HidesInternalStagesByDefault_AndShowsWhenRequested()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = PipelineStageNames.SpeechCompleted, Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        await repo.AddStageExecutionAsync(new PipelineStageExecution { PipelineRunId = run.Id, StageName = "PromptGeneration", Status = PersistentStageStatuses.Succeeded }, CancellationToken.None);
        var service = new PipelineRecoveryService(repo);

        var defaultStatus = await service.GetStatusAsync(run.Id, CancellationToken.None);
        var internalStatus = await service.GetStatusAsync(run.Id, CancellationToken.None, includeInternal: true);

        Assert.DoesNotContain(defaultStatus!.Stages, s => s.StageName == "PromptGeneration");
        Assert.Contains(internalStatus!.Stages, s => s.StageName == "PromptGeneration");
    }

    [Fact]
    public async Task PublishedUrls_IncludesAllPlatformUrls_WhenPublished()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "youtube-publish-result-long.json"), new PublishResult { Success = true, Platform = "YouTube", VideoId = "yt-long", IsShort = false, Mode = "Public" });
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "youtube-publish-result-short.json"), new PublishResult { Success = true, Platform = "YouTube", VideoId = "yt-short", IsShort = true, Mode = "Public" });
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "facebook-reel-publish-result.json"), new MetaPublishResult { Success = true, Platform = "Facebook", VideoId = "fb-reel-1", Url = "https://www.facebook.com/reel/fb-reel-1/", Mode = "Public" });
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "instagram-reel-publish-result.json"), new MetaPublishResult { Success = true, Platform = "Instagram", VideoId = "ig-reel-1", Url = "https://www.instagram.com/reel/ig-reel-1/", Mode = "Public" });
        var service = new PipelineRecoveryService(repo);

        var status = await service.GetStatusAsync(run.Id, CancellationToken.None);

        Assert.Contains("https://www.youtube.com/watch?v=yt-long", status!.PublishedUrls);
        Assert.Contains("https://www.youtube.com/shorts/yt-short", status.PublishedUrls);
        Assert.Contains("https://www.facebook.com/reel/fb-reel-1/", status.PublishedUrls);
        Assert.Contains("https://www.instagram.com/reel/ig-reel-1/", status.PublishedUrls);
    }

    [Fact]
    public async Task PublishedUrls_ExcludesStorageOnlyUrls()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await repo.AddPublishedVideoAsync(new PublishedVideo { PipelineRunId = run.Id, YouTubeVideoId = "yt-long", BlobUrl = "https://storage.example/001-sky.png", ThumbnailUrl = "https://storage.example/thumb.png", Status = "Published" }, CancellationToken.None);
        repo.PlatformPublications.Add(new PlatformPublicationRecord { ParentShortVideoId = Guid.NewGuid(), Platform = ShortFormPlatform.Facebook, Status = PlatformPublicationStatus.Published, ExternalUrl = "https://storage.example/final-short.mp4" });
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "facebook-reel-publish-result.json"), new MetaPublishResult { Success = true, Platform = "Facebook", VideoId = "fb-reel-2", Url = "https://storage.example/fb-reel-2.mp4", Mode = "Public" });
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "instagram-reel-publish-result.json"), new MetaPublishResult { Success = true, Platform = "Instagram", VideoId = "ig-reel-2", Url = "https://storage.example/ig-thumb.png", Mode = "Public" });
        var service = new PipelineRecoveryService(repo);

        var status = await service.GetStatusAsync(run.Id, CancellationToken.None);

        Assert.Contains("https://www.youtube.com/watch?v=yt-long", status!.PublishedUrls);
        Assert.Contains("https://www.facebook.com/reel/fb-reel-2/", status.PublishedUrls);
        Assert.DoesNotContain("https://storage.example/001-sky.png", status.PublishedUrls);
        Assert.DoesNotContain("https://storage.example/thumb.png", status.PublishedUrls);
        Assert.DoesNotContain("https://storage.example/final-short.mp4", status.PublishedUrls);
        Assert.DoesNotContain("https://storage.example/ig-thumb.png", status.PublishedUrls);
    }

    [Fact]
    public async Task PublishedUrls_HandlesMissingInstagramPermalinkSafely()
    {
        var repo = new MemoryPipelineRepository();
        var run = await repo.CreateAsync(NewRun(), CancellationToken.None);
        await WriteJsonAsync(Path.Combine(run.OutputFolder!, "instagram-reel-publish-result.json"), new MetaPublishResult { Success = true, Platform = "Instagram", VideoId = "ig-reel-without-permalink", Mode = "Public" });
        var service = new PipelineRecoveryService(repo);

        var status = await service.GetStatusAsync(run.Id, CancellationToken.None);

        Assert.Empty(status!.PublishedUrls);
        Assert.Contains(status.Warnings, warning => warning.Contains("Instagram Reel publish result contained an id but no permalink URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidationFailure_BlocksPublish_AndIsNonRetryable()
    {
        Assert.False(PipelineRetryClassifier.IsRetryable(new InvalidOperationException("validation failed: missing required artifact")));
    }


    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value));
    }

    private static PipelineRun NewRun()
    {
        var output = Path.Combine(Path.GetTempPath(), "pipeline-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        return new PipelineRun
        {
            ContentType = ContentType.DailySkyGuide,
            RunDate = DateOnly.FromDateTime(DateTime.UtcNow),
            LocationName = "Pune",
            Status = PipelineRunStatus.Failed,
            OutputFolder = output,
            ResumeSupported = true
        };
    }

    private sealed class MemoryPipelineRepository : IPipelineRepository
    {
        public List<PipelineRun> Runs { get; } = [];
        public List<PipelineStageExecution> Stages { get; } = [];
        public List<PublishedVideo> PublishedVideos { get; } = [];
        public List<PlatformPublicationRecord> PlatformPublications { get; } = [];

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) { Runs.Add(run); return Task.FromResult(run); }
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Runs.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>(Runs.Take(take).ToArray());
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
        public Task<IReadOnlyCollection<PipelineStageExecution>> GetStageExecutionsAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineStageExecution>>(Stages.Where(x => x.PipelineRunId == pipelineRunId).ToArray());
        public Task<PipelineStageExecution?> GetLatestStageExecutionAsync(Guid pipelineRunId, string stageName, CancellationToken cancellationToken) => Task.FromResult(Stages.LastOrDefault(x => x.PipelineRunId == pipelineRunId && x.StageName == stageName));
        public Task AddStageExecutionAsync(PipelineStageExecution stageExecution, CancellationToken cancellationToken) { Stages.Add(stageExecution); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>(PublishedVideos.Where(x => x.PipelineRunId == pipelineRunId).ToArray());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
