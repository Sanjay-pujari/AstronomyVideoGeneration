using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
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
    public void ValidationFailure_BlocksPublish_AndIsNonRetryable()
    {
        Assert.False(PipelineRetryClassifier.IsRetryable(new InvalidOperationException("validation failed: missing required artifact")));
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
