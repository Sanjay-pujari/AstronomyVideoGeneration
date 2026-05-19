using System.Text.Json;
using Astronomy.MediaFactory.AIOptimization;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Optimization;

public interface IAIOptimizationReadService
{
    Task<IReadOnlyCollection<HookOptimizationResultEntity>> GetHooksAsync(Guid pipelineRunId, CancellationToken ct);
    Task<IReadOnlyCollection<TrendSignalEntity>> GetTrendsAsync(DateOnly date, CancellationToken ct);
    Task<PublishingOptimizationResultEntity?> GetPublishingAsync(Guid pipelineRunId, CancellationToken ct);
}

public sealed class AIOptimizationReadService(MediaFactoryDbContext db) : IAIOptimizationReadService
{
    public Task<IReadOnlyCollection<HookOptimizationResultEntity>> GetHooksAsync(Guid pipelineRunId, CancellationToken ct) => db.HookOptimizationResults.Where(x => x.PipelineRunId == pipelineRunId).OrderByDescending(x => x.FinalScore).ToArrayAsync(ct).ContinueWith(t => (IReadOnlyCollection<HookOptimizationResultEntity>)t.Result, ct);
    public Task<IReadOnlyCollection<TrendSignalEntity>> GetTrendsAsync(DateOnly date, CancellationToken ct) => db.TrendSignals.Where(x => x.SignalDate == date).ToArrayAsync(ct).ContinueWith(t => (IReadOnlyCollection<TrendSignalEntity>)t.Result, ct);
    public Task<PublishingOptimizationResultEntity?> GetPublishingAsync(Guid pipelineRunId, CancellationToken ct) => db.PublishingOptimizationResults.OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(x => x.PipelineRunId == pipelineRunId, ct);
}
