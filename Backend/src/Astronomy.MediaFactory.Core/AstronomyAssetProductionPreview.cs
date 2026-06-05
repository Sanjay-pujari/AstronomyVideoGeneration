using System.Text.Json.Nodes;

namespace Astronomy.MediaFactory.Core;

public interface IAstronomyAssetProducer
{
    string ProducerName { get; }
    bool CanHandle(AstronomyAssetProductionJob job);
    Task<AssetProducerValidationResult> ValidateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken);
    Task<AssetProducerEstimateResult> EstimateAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken);
    Task<AssetProductionRequestPreview> CreateProductionRequestAsync(AstronomyAssetProductionJob job, CancellationToken cancellationToken);
}

public sealed record AssetProducerValidationResult(
    bool IsValid,
    string Status,
    IReadOnlyList<string> Messages)
{
    public static AssetProducerValidationResult Valid(IReadOnlyList<string>? messages = null) => new(true, "Valid", messages ?? Array.Empty<string>());
    public static AssetProducerValidationResult Invalid(IReadOnlyList<string> messages) => new(false, "Invalid", messages);
}

public sealed record AssetProducerEstimateResult(
    int EstimatedDurationSeconds,
    string EstimatedCostCategory,
    string EstimatedComplexity);

public sealed record AssetProductionRequestPreview(
    string RequestType,
    string Provider,
    bool WillExecute,
    JsonObject Parameters,
    IReadOnlyList<string> SafetyNotes);

public sealed record AstronomyAssetProducerPreviewRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? JobIds = null,
    IReadOnlyList<string>? AssetTypes = null,
    IReadOnlyList<string>? AssetPriorities = null,
    IReadOnlyList<string>? Providers = null,
    IReadOnlyList<string>? Status = null,
    int? MaxJobs = 20);

public sealed record AstronomyAssetProducerPreviewResult(
    int JobCount,
    int ValidJobs,
    int InvalidJobs,
    IReadOnlyList<AssetProducerCoverageResult> ProducerCoverage,
    IReadOnlyList<AstronomyAssetProductionJobPreview> Previews,
    IReadOnlyList<string> Warnings);

public sealed record AssetProducerCoverageResult(
    string AssetType,
    string ProducerName,
    int JobCount,
    bool Covered);

public sealed record AstronomyAssetProductionJobPreview(
    Guid JobId,
    Guid ContentGenerationPlanId,
    string AssetType,
    string AssetPriority,
    string AssetExecutionGroup,
    string PlannedProvider,
    string? ProducerName,
    bool CanProduce,
    string ValidationStatus,
    IReadOnlyList<string> ValidationMessages,
    int? EstimatedDurationSeconds,
    string? EstimatedCostCategory,
    string? EstimatedComplexity,
    AssetProductionRequestPreview? ProductionRequestPreview,
    bool WillExecute);

public interface IAstronomyAssetProducerPreviewService
{
    Task<AstronomyAssetProducerPreviewResult> PreviewAssetProductionAsync(AstronomyAssetProducerPreviewRequest request, CancellationToken cancellationToken);
}
