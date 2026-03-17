using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Infrastructure.Operations;

public sealed class PipelineStageRecorder : IPipelineStageRecorder
{
    private readonly MediaFactoryDbContext _db;

    public PipelineStageRecorder(MediaFactoryDbContext db)
    {
        _db = db;
    }

    public async Task<PipelineStageExecution> StartStageAsync(Guid pipelineRunId, string stageName, string? metadataJson, CancellationToken cancellationToken)
    {
        var stage = new PipelineStageExecution
        {
            PipelineRunId = pipelineRunId,
            StageName = stageName,
            Status = PipelineStageStatuses.Running,
            StartedAt = DateTimeOffset.UtcNow,
            MetadataJson = metadataJson
        };

        await _db.PipelineStageExecutions.AddAsync(stage, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return stage;
    }

    public async Task CompleteStageAsync(PipelineStageExecution stageExecution, string? metadataJson, CancellationToken cancellationToken)
    {
        ApplyCompletion(stageExecution, PipelineStageStatuses.Succeeded, metadataJson);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailStageAsync(PipelineStageExecution stageExecution, string errorMessage, bool continuedWithFallback, string? metadataJson, CancellationToken cancellationToken)
    {
        var failureStatus = continuedWithFallback ? PipelineStageStatuses.FailedWithFallback : PipelineStageStatuses.Failed;
        ApplyCompletion(stageExecution, failureStatus, metadataJson, errorMessage);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyCompletion(PipelineStageExecution stageExecution, string status, string? metadataJson, string? errorMessage = null)
    {
        stageExecution.FinishedAt = DateTimeOffset.UtcNow;
        stageExecution.DurationMs = (long)Math.Max(0, (stageExecution.FinishedAt.Value - stageExecution.StartedAt).TotalMilliseconds);
        stageExecution.Status = status;
        stageExecution.ErrorMessage = errorMessage;
        stageExecution.MetadataJson = metadataJson;
        stageExecution.Touch();
    }
}
