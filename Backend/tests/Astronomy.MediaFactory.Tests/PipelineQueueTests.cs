using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineQueueTests
{
    [Fact]
    public async Task Enqueue_Prevents_Duplicate_Main_Video_Jobs()
    {
        var repo = new QueueRepo { DuplicateMainJob = true };
        var queue = new PipelineJobQueue(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync(
            new EnqueuePipelineJobRequest(PipelineJobType.GenerateMainVideo, DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "Pune"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Processor_Retries_Then_Fails_When_Attempts_Exhausted()
    {
        var repo = new QueueRepo();
        var job = new PipelineJob { JobType = PipelineJobType.PublishVideo, RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.SpaceNews, LocationName = "Pune" };
        repo.Jobs.Add(job);

        var processor = new PipelineJobProcessor(repo, new ThrowingExecutor(), Options.Create(new SchedulingOptions { MaxRetryAttempts = 2, RetryBackoffSeconds = 1 }), NullLogger<PipelineJobProcessor>.Instance);

        await processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(PipelineJobStatus.Retrying, job.Status);

        job.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await processor.ProcessNextAsync(CancellationToken.None);
        Assert.Equal(PipelineJobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task Processor_Transitions_Pending_To_Succeeded()
    {
        var repo = new QueueRepo();
        var job = new PipelineJob { JobType = PipelineJobType.ArchiveAssets, RunDate = DateOnly.FromDateTime(DateTime.UtcNow), ContentType = ContentType.TelescopeTargets, LocationName = "Pune" };
        repo.Jobs.Add(job);

        var processor = new PipelineJobProcessor(repo, new NoopExecutor(), Options.Create(new SchedulingOptions()), NullLogger<PipelineJobProcessor>.Instance);

        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(PipelineJobStatus.Succeeded, job.Status);
        Assert.True(job.StartedAt.HasValue);
        Assert.True(job.FinishedAt.HasValue);
    }

    private sealed class QueueRepo : IPipelineRepository
    {
        public bool DuplicateMainJob { get; set; }
        public List<PipelineJob> Jobs { get; } = [];

        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Jobs.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>(Jobs.OrderByDescending(x => x.CreatedUtc).Take(take).ToList());
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult(Jobs.OrderBy(x => x.ScheduledAt).FirstOrDefault(x => (x.Status == PipelineJobStatus.Pending && x.ScheduledAt <= now) || (x.Status == PipelineJobStatus.Retrying && x.NextAttemptAt <= now)));
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(DuplicateMainJob);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingExecutor : IPipelineJobExecutor
    {
        public Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken) => throw new InvalidOperationException("transient");
    }

    private sealed class NoopExecutor : IPipelineJobExecutor
    {
        public Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
