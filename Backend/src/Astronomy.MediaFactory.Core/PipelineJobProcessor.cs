using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineJobProcessor
{
    private readonly IPipelineRepository _repository;
    private readonly IPipelineJobExecutor _executor;
    private readonly SchedulingOptions _options;
    private readonly ILogger<PipelineJobProcessor> _logger;

    public PipelineJobProcessor(IPipelineRepository repository, IPipelineJobExecutor executor, IOptions<SchedulingOptions> options, ILogger<PipelineJobProcessor> logger)
    {
        _repository = repository;
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await _repository.GetNextRunnableJobAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (job is null)
            return false;

        var started = DateTimeOffset.UtcNow;
        job.Status = PipelineJobStatus.Running;
        job.StartedAt = started;
        job.AttemptCount += 1;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            await _executor.ExecuteAsync(job, cancellationToken);
            job.Status = PipelineJobStatus.Succeeded;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = null;
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Job {JobId} ({JobType}) finished in {ElapsedMs} ms.", job.Id, job.JobType, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var attemptsRemaining = job.AttemptCount < Math.Max(1, _options.MaxRetryAttempts);
            job.ErrorMessage = ex.Message;
            if (attemptsRemaining)
            {
                job.Status = PipelineJobStatus.Retrying;
                job.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _options.RetryBackoffSeconds) * job.AttemptCount);
                _logger.LogWarning(ex, "Job {JobId} failed attempt {AttemptCount}; retrying at {NextAttemptAt}.", job.Id, job.AttemptCount, job.NextAttemptAt);
            }
            else
            {
                job.Status = PipelineJobStatus.Failed;
                job.FinishedAt = DateTimeOffset.UtcNow;
                _logger.LogError(ex, "Job {JobId} failed after {AttemptCount} attempts.", job.Id, job.AttemptCount);
            }

            await _repository.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
