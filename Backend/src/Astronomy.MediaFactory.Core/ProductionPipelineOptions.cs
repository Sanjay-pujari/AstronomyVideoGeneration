namespace Astronomy.MediaFactory.Core;

public sealed class ProductionPipelineOptions
{
    public const string SectionName = "ProductionPipeline";

    public bool StaleRunningRecoveryEnabled { get; set; } = true;
    public int StaleRunningThresholdMinutes { get; set; } = 30;
}

public sealed record ProductionRunningRecoveryResult(
    bool Recovered,
    Guid PlanId,
    Guid? ExecutionId,
    string? Warning,
    string? Reason);

public interface IProductionRunningRecoveryService
{
    Task<ProductionRunningRecoveryResult> RecoverStaleRunningExecutionAsync(Guid planId, CancellationToken cancellationToken);
    Task<ProductionRunningRecoveryResult> RecoverRunningExecutionAsync(Guid planId, CancellationToken cancellationToken);
}
