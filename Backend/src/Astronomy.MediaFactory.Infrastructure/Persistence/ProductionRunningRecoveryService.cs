using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ProductionRunningRecoveryService(
    MediaFactoryDbContext db,
    IOptions<ProductionPipelineOptions> options,
    ILogger<ProductionRunningRecoveryService> logger) : IProductionRunningRecoveryService
{
    private const string RecoveryMessage = "Automatically marked failed due to stale running execution.";

    public Task<ProductionRunningRecoveryResult> RecoverStaleRunningExecutionAsync(Guid planId, CancellationToken cancellationToken)
        => RecoverRunningExecutionCoreAsync(planId, force: false, cancellationToken);

    public Task<ProductionRunningRecoveryResult> RecoverRunningExecutionAsync(Guid planId, CancellationToken cancellationToken)
        => RecoverRunningExecutionCoreAsync(planId, force: true, cancellationToken);

    private async Task<ProductionRunningRecoveryResult> RecoverRunningExecutionCoreAsync(Guid planId, bool force, CancellationToken cancellationToken)
    {
        if (!force && !options.Value.StaleRunningRecoveryEnabled)
            return new ProductionRunningRecoveryResult(false, planId, null, null, "Stale running recovery is disabled.");

        var plan = await db.ContentGenerationPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return new ProductionRunningRecoveryResult(false, planId, null, null, "Content generation plan was not found.");

        var execution = await db.ContentPipelineExecutions
            .Where(e => e.ContentGenerationPlanId == planId)
            .OrderByDescending(e => e.StartedUtc ?? e.CreatedUtc)
            .ThenByDescending(e => e.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (execution is null)
            return new ProductionRunningRecoveryResult(false, planId, null, null, "No content pipeline execution was found for the plan.");

        if (!string.Equals(execution.Status, "Running", StringComparison.OrdinalIgnoreCase))
            return new ProductionRunningRecoveryResult(false, planId, execution.Id, null, $"Latest execution status was {execution.Status}.");

        if (execution.FinishedUtc.HasValue)
            return new ProductionRunningRecoveryResult(false, planId, execution.Id, null, "Latest running execution already has FinishedUtc set.");

        var startedUtc = execution.StartedUtc ?? execution.CreatedUtc;
        var threshold = TimeSpan.FromMinutes(Math.Max(1, options.Value.StaleRunningThresholdMinutes));
        if (!force && DateTimeOffset.UtcNow - startedUtc < threshold)
            return new ProductionRunningRecoveryResult(false, planId, execution.Id, null, "Latest running execution is not stale.");

        execution.Status = "Failed";
        execution.FinishedUtc = DateTimeOffset.UtcNow;
        execution.ErrorMessage = RecoveryMessage;
        plan.Status = "ProductionFailed";
        plan.PlanStatus = "ProductionFailed";
        plan.FailureReason = RecoveryMessage;
        plan.CompletedUtc = null;
        plan.Touch();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Recovered running production execution {ExecutionId} for content plan {PlanId}; force={Force}",
            execution.Id,
            planId,
            force);

        var warning = $"Recovered stale running execution for plan {planId:D}. Previous execution {execution.Id:D} marked Failed.";
        return new ProductionRunningRecoveryResult(true, planId, execution.Id, warning, null);
    }
}
