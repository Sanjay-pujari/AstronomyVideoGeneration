using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class PipelineMonitoringService : IPipelineMonitoringService
{
    private readonly MediaFactoryDbContext _db;
    private readonly OperationsOptions _options;

    public PipelineMonitoringService(MediaFactoryDbContext db, IOptions<OperationsOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<PipelineOpsSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RetainDays));
        var runs = await _db.PipelineRuns.AsNoTracking().Where(x => x.CreatedUtc >= from).ToListAsync(cancellationToken);
        var stages = await _db.PipelineStageExecutions.AsNoTracking().Where(x => x.CreatedUtc >= from).ToListAsync(cancellationToken);
        var latestPublish = await _db.PublishedVideos.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(5)
            .Select(x => new PublishedVideoStatusSnapshot(x.Title, x.Status, x.CreatedAt, x.YouTubeVideoId)).ToListAsync(cancellationToken);

        var failedByStage = stages.Where(x => x.Status.StartsWith("Failed")).GroupBy(x => x.StageName)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var avgDuration = runs.Where(x => x.StartedUtc.HasValue && x.FinishedUtc.HasValue)
            .Select(x => (x.FinishedUtc!.Value - x.StartedUtc!.Value).TotalMilliseconds).DefaultIfEmpty(0).Average();
        var slow = stages.Where(x => x.DurationMs.HasValue && x.DurationMs.Value >= _options.SlowStageThresholdMs)
            .OrderByDescending(x => x.DurationMs)
            .Take(20)
            .Select(x => new SlowStageSnapshot(x.PipelineRunId, x.StageName, x.DurationMs!.Value, x.StartedAt, x.FinishedAt))
            .ToList();

        var jobs = await _db.PipelineJobs.AsNoTracking().Where(x => x.CreatedUtc >= from).ToListAsync(cancellationToken);
        var queue = new QueueHealthSnapshot(jobs.Count(x => x.Status == PipelineJobStatus.Pending), jobs.Count(x => x.Status == PipelineJobStatus.Running), jobs.Count(x => x.Status == PipelineJobStatus.Retrying), jobs.Count(x => x.Status == PipelineJobStatus.Failed));
        return new PipelineOpsSummary(runs.Count, runs.Count(x => x.Status == PipelineRunStatus.Succeeded), runs.Count(x => x.Status == PipelineRunStatus.Failed), avgDuration, failedByStage, latestPublish, queue, slow);
    }

    public async Task<IReadOnlyCollection<PipelineRun>> GetRecentPipelinesAsync(int take, CancellationToken cancellationToken)
        => await _db.PipelineRuns.AsNoTracking().OrderByDescending(x => x.CreatedUtc).Take(Math.Max(1, take)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PipelineStageExecution>> GetPipelineStagesAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await _db.PipelineStageExecutions.AsNoTracking().Where(x => x.PipelineRunId == pipelineRunId).OrderBy(x => x.StartedAt).ToListAsync(cancellationToken);

    public async Task<RecentFailuresSnapshot> GetRecentFailuresAsync(int take, CancellationToken cancellationToken)
    {
        var failedRuns = await _db.PipelineRuns.AsNoTracking().Where(x => x.Status == PipelineRunStatus.Failed).OrderByDescending(x => x.FinishedUtc).Take(take).ToListAsync(cancellationToken);
        var failedJobs = await _db.PipelineJobs.AsNoTracking().Where(x => x.Status == PipelineJobStatus.Failed).OrderByDescending(x => x.FinishedAt).Take(take).ToListAsync(cancellationToken);
        var failedStages = await _db.PipelineStageExecutions.AsNoTracking().Where(x => x.Status.StartsWith("Failed")).OrderByDescending(x => x.FinishedAt).Take(take).ToListAsync(cancellationToken);
        var latestByStage = failedStages
            .GroupBy(x => x.StageName)
            .Select(g => g.OrderByDescending(x => x.FinishedAt).First())
            .Select(x => new StageFailureDigest(x.StageName, x.ErrorMessage ?? "Unknown", x.FinishedAt ?? x.StartedAt, x.PipelineRunId))
            .ToList();
        return new RecentFailuresSnapshot(failedRuns, failedJobs, failedStages, latestByStage);
    }

    public async Task<JobOpsSummary> GetJobSummaryAsync(CancellationToken cancellationToken)
    {
        var jobs = await _db.PipelineJobs.AsNoTracking().ToListAsync(cancellationToken);
        return new JobOpsSummary(jobs.Count, jobs.Count(x => x.Status == PipelineJobStatus.Pending), jobs.Count(x => x.Status == PipelineJobStatus.Running), jobs.Count(x => x.Status == PipelineJobStatus.Retrying), jobs.Count(x => x.Status == PipelineJobStatus.Failed), jobs.Count(x => x.Status == PipelineJobStatus.Succeeded));
    }
}
