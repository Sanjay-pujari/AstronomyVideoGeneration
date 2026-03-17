namespace Astronomy.MediaFactory.Core;

public sealed class NullStageAlertPublisher : IStageAlertPublisher
{
    public Task PublishSlowStageAsync(StageAlertContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishStageFailureAsync(StageAlertContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}
