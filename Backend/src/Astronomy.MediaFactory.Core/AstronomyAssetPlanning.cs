namespace Astronomy.MediaFactory.Core;

public sealed record AstronomyAssetPlanningRequest(
    string? RegionId = null,
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    decimal? MinPriorityScore = null,
    int? MaxPlans = null,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record AstronomyAssetPlanningResult(
    int PlanCount,
    int AssetRequirementCount,
    int SavedCount,
    int SkippedDuplicates,
    bool DryRun,
    IReadOnlyList<AstronomyAssetPlanDto> AssetPlans,
    IReadOnlyList<string> Warnings);

public sealed record AstronomyAssetPlanDto(
    Guid ContentGenerationPlanId,
    Guid? AstronomyContentOpportunityId,
    Guid? AstronomyEventIntelligenceId,
    string ContentCategory,
    string? PlannedFormat,
    string RegionId,
    string? LocationName,
    DateTimeOffset? ScheduledUtc,
    DateTimeOffset? PeakUtc,
    string PlanStatus,
    string AssetPlanStatus,
    int SceneGroupCount,
    int AssetRequirementCount,
    IReadOnlyList<string> ObjectNames,
    IReadOnlyList<AstronomySceneAssetGroupDto> SceneAssetGroups,
    IReadOnlyList<AstronomyAssetRequirementDto> AssetRequirements,
    object? MetadataJson);

public sealed record AstronomySceneAssetGroupDto(
    int SceneNumber,
    string SceneName,
    IReadOnlyList<AstronomyAssetRequirementDto> AssetRequirements);

public sealed record AstronomyAssetRequirementDto(
    int SceneNumber,
    string SceneName,
    string AssetType,
    string AssetPurpose,
    IReadOnlyList<string> ObjectNames,
    string PlannedProvider,
    string PromptOrInstruction,
    string ExpectedOutputType,
    int Priority,
    string Status,
    IReadOnlyList<string> DependsOn,
    object MetadataJson);

public interface IAstronomyAssetPlanningService
{
    Task<AstronomyAssetPlanningResult> GenerateAssetPlansAsync(AstronomyAssetPlanningRequest request, CancellationToken cancellationToken);
}
