using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class NoOpDailySkyGuideVisualAssetConsumer : IDailySkyGuideVisualAssetConsumer
{
    public Task<bool> CanConsumeAsync(DailySkyGuideAssetAwareExecutionContext context, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task ConsumeAsync(DailySkyGuideAssetAwareExecutionContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
