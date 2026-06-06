namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyProductionMonitoringRequest(
    string? RegionId = null,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndUtc = null);

public sealed record AstronomyProductionSummary(
    DateTimeOffset GeneratedUtc,
    string? RegionId,
    ProductionEventCounts Events,
    ProductionOpportunityCounts Opportunities,
    ProductionVideoPlanCounts VideoPlans,
    ProductionAssetJobCounts AssetJobs,
    IReadOnlyList<ProductionAssetTypeBreakdownItem> AssetTypeBreakdown,
    ProductionPriorityBreakdown PriorityBreakdown,
    IReadOnlyList<ProductionProducerCoverageItem> ProducerCoverage,
    IReadOnlyList<ProductionPendingAssetItem> TopPendingAssets,
    decimal CompletionPercent,
    IReadOnlyList<string> Warnings);

public sealed record ProductionEventCounts(
    int Total,
    int Candidate,
    int Planned,
    int Completed);

public sealed record ProductionOpportunityCounts(
    int Total,
    int Proposed,
    int Planned,
    int Completed);

public sealed record ProductionVideoPlanCounts(
    int Total,
    int Planned,
    int Completed,
    int Failed);

public sealed record ProductionAssetJobCounts(
    int Total,
    int Pending,
    int Completed,
    int Failed,
    int InProgress);

public sealed record ProductionAssetTypeBreakdownItem(
    string AssetType,
    int Total,
    int Completed,
    int Pending);

public sealed record ProductionPriorityBreakdown(
    int Required,
    int Preferred,
    int Optional);

public sealed record ProductionProducerCoverageItem(
    string AssetType,
    string Producer,
    bool Covered);

public sealed record ProductionPendingAssetItem(
    string AssetType,
    string AssetPriority,
    string ContentCategory,
    Guid ContentGenerationPlanId);

public interface IAstronomyProductionMonitoringService
{
    Task<AstronomyProductionSummary> GetProductionSummaryAsync(AstronomyProductionMonitoringRequest request, CancellationToken cancellationToken);
}
