using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineJobQueue : IPipelineJobQueue
{
    private readonly IPipelineRepository _repository;

    public PipelineJobQueue(IPipelineRepository repository)
    {
        _repository = repository;
    }

    public async Task<PipelineJob> EnqueueAsync(EnqueuePipelineJobRequest request, CancellationToken cancellationToken)
    {
        if (request.JobType == PipelineJobType.GenerateMainVideo)
        {
            var duplicate = await _repository.HasQueuedOrCompletedMainJobAsync(request.RunDate, request.ContentType, cancellationToken);
            if (duplicate)
                throw new InvalidOperationException($"A main video job already exists for {request.ContentType} on {request.RunDate:yyyy-MM-dd}.");
        }

        var job = new PipelineJob
        {
            JobType = request.JobType,
            ParentPipelineRunId = request.ParentPipelineRunId,
            Status = PipelineJobStatus.Pending,
            ScheduledAt = request.ScheduledAt ?? DateTimeOffset.UtcNow,
            RunDate = request.RunDate,
            ContentType = request.ContentType,
            LocationName = request.LocationName,
            TimeZone = request.TimeZone,
            PublishToYouTube = request.PublishToYouTube
        };

        await _repository.AddJobAsync(job, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return job;
    }
}
