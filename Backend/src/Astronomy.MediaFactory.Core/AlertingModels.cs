using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public enum AlertCategory
{
    StageFailed,
    StageSlow,
    PipelineFailed,
    PublishFailed,
    QueueBacklogHigh,
    HealthDegraded,
    PublishSucceeded
}

public sealed record OperationalAlert(
    AlertCategory Category,
    string Message,
    Guid? PipelineRunId = null,
    string? StageName = null,
    ContentType? ContentType = null,
    DateOnly? RunDate = null,
    string? LocationName = null,
    long? DurationMs = null,
    string? ErrorSummary = null,
    Guid? JobId = null,
    int? QueueBacklog = null,
    int? QueueThreshold = null,
    DateTimeOffset? OccurredAt = null);

public interface IOperationalAlertPublisher
{
    Task PublishAsync(OperationalAlert alert, CancellationToken cancellationToken);
}

public interface IOperationalAlertNotifier
{
    Task NotifyAsync(OperationalAlert alert, CancellationToken cancellationToken);
}
