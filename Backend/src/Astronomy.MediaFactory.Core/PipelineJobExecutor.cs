using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineJobExecutor : IPipelineJobExecutor
{
    private readonly PipelineOrchestrator _orchestrator;
    private readonly IPipelineRepository _repository;
    private readonly ILogger<PipelineJobExecutor> _logger;

    public PipelineJobExecutor(PipelineOrchestrator orchestrator, IPipelineRepository repository, ILogger<PipelineJobExecutor> logger)
    {
        _orchestrator = orchestrator;
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken)
    {
        switch (job.JobType)
        {
            case PipelineJobType.GenerateMainVideo:
                var run = await _orchestrator.RunAsync(new RunPipelineRequest(job.RunDate, job.ContentType, job.LocationName, job.TimeZone, job.PublishToYouTube), cancellationToken);
                job.ParentPipelineRunId = run.Id;
                break;
            case PipelineJobType.GenerateShorts:
            case PipelineJobType.PublishVideo:
            case PipelineJobType.ArchiveAssets:
                _logger.LogInformation("Executed queued job {JobType} for pipeline run {PipelineRunId}.", job.JobType, job.ParentPipelineRunId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported job type {job.JobType}");
        }

        await _repository.SaveChangesAsync(cancellationToken);
    }
}
