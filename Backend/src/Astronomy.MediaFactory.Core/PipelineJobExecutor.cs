using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineJobExecutor : IPipelineJobExecutor
{
    private readonly PipelineOrchestrator _orchestrator;
    private readonly IRunOperationsService _operationsService;
    private readonly IPipelineRepository _repository;
    private readonly ILogger<PipelineJobExecutor> _logger;

    public PipelineJobExecutor(PipelineOrchestrator orchestrator, IRunOperationsService operationsService, IPipelineRepository repository, ILogger<PipelineJobExecutor> logger)
    {
        _orchestrator = orchestrator;
        _operationsService = operationsService;
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["jobId"] = job.Id,
            ["jobType"] = job.JobType.ToString(),
            ["pipelineRunId"] = job.ParentPipelineRunId
        });
        _logger.LogInformation("Executing pipeline job {JobId} of type {JobType}.", job.Id, job.JobType);
        switch (job.JobType)
        {
            case PipelineJobType.GenerateMainVideo:
                var run = await _orchestrator.RunAsync(new RunPipelineRequest(job.RunDate, job.ContentType, job.LocationName, job.TimeZone, job.PublishToYouTube, job.UseTopicPlanner, Language: job.Language), cancellationToken);
                job.ParentPipelineRunId = run.Id;
                break;
            case PipelineJobType.GenerateShorts:
                EnsureRunId(job);
                await _operationsService.RegenerateShortsAsync(job.ParentPipelineRunId!.Value, new RegenerateShortsRequest("job-runner", $"Queued GenerateShorts job {job.Id}.", job.PublishToYouTube, Force: true), cancellationToken);
                break;
            case PipelineJobType.PublishVideo:
                EnsureRunId(job);
                await _operationsService.RetryPublishAsync(job.ParentPipelineRunId!.Value, new RetryPublishRequest("job-runner", $"Queued PublishVideo job {job.Id}.", ForceRepublish: false, PublishToYouTube: true), cancellationToken);
                break;
            case PipelineJobType.ArchiveAssets:
                EnsureRunId(job);
                await _operationsService.RetryArchiveAsync(job.ParentPipelineRunId!.Value, new RetryArchiveRequest("job-runner", $"Queued ArchiveAssets job {job.Id}.", Force: true), cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported job type {job.JobType}");
        }

        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureRunId(PipelineJob job)
    {
        if (job.ParentPipelineRunId is null)
            throw new InvalidOperationException($"Job {job.Id} requires ParentPipelineRunId for {job.JobType} work.");
    }
}
