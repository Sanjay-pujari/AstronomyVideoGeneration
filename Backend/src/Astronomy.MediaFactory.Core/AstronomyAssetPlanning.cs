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
    string AssetPriority,
    string AssetExecutionGroup,
    object MetadataJson);

public sealed record AstronomyAssetProductionJobRequest(
    IReadOnlyList<Guid>? PlanIds = null,
    IReadOnlyList<string>? ContentCategories = null,
    IReadOnlyList<string>? PlannedFormats = null,
    string? RegionId = null,
    int? MaxPlans = null,
    bool DryRun = true);

public sealed record AstronomyAssetProductionJobResult(
    int JobCount,
    int RequiredJobs,
    int PreferredJobs,
    int OptionalJobs,
    IReadOnlyList<AstronomyAssetProductionJobDto> Jobs,
    IReadOnlyList<string> Warnings);

public sealed record AstronomyAssetProductionJobDto(
    Guid ContentGenerationPlanId,
    Guid? AstronomyContentOpportunityId,
    Guid? AstronomyEventIntelligenceId,
    string ContentCategory,
    string? PlannedFormat,
    string RegionId,
    string? LocationName,
    DateTimeOffset? ScheduledUtc,
    DateTimeOffset? PeakUtc,
    int SceneNumber,
    string SceneName,
    string AssetType,
    string AssetPurpose,
    IReadOnlyList<string> ObjectNames,
    string PlannedProvider,
    string PromptOrInstruction,
    string ExpectedOutputType,
    int Priority,
    string AssetPriority,
    string AssetExecutionGroup,
    IReadOnlyList<string> DependsOn,
    object? MetadataJson,
    bool DryRun);

public interface IAstronomyAssetPlanningService
{
    Task<AstronomyAssetPlanningResult> GenerateAssetPlansAsync(AstronomyAssetPlanningRequest request, CancellationToken cancellationToken);
}

public interface IAstronomyAssetProductionJobService
{
    Task<AstronomyAssetProductionJobResult> CreateAssetProductionJobsAsync(AstronomyAssetProductionJobRequest request, CancellationToken cancellationToken);
}

public static class AstronomyAssetClassificationRules
{
    public const string Required = "Required";
    public const string Preferred = "Preferred";
    public const string Optional = "Optional";

    public const string Core = "Core";
    public const string AstronomyVisualization = "AstronomyVisualization";
    public const string Cinematic = "Cinematic";
    public const string Educational = "Educational";
    public const string Thumbnail = "Thumbnail";

    public static string ResolvePriority(string? contentCategory, string? assetType)
    {
        var normalizedAssetType = NormalizeAssetType(assetType);
        var normalizedCategory = contentCategory ?? string.Empty;

        if (normalizedCategory.Equals("MoonSpecials", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedAssetType == "nasaasset") return Preferred;
            if (normalizedAssetType == "stellariumscreenshot") return Optional;
        }

        if (normalizedCategory.Equals("AstroExplainer", StringComparison.OrdinalIgnoreCase) && normalizedAssetType == "stellariumscreenshot")
            return Optional;

        if ((normalizedCategory.Equals("PlanetConjunction", StringComparison.OrdinalIgnoreCase) ||
             normalizedCategory.Equals("PlanetGrouping", StringComparison.OrdinalIgnoreCase)) &&
            (normalizedAssetType == "stellariumscreenshot" || normalizedAssetType == "constellationguide"))
            return Preferred;

        if (normalizedCategory.Equals("WeeklySkyForecast", StringComparison.OrdinalIgnoreCase) && normalizedAssetType == "stellariumscreenshot")
            return Preferred;

        return normalizedAssetType switch
        {
            "narrationscriptplaceholder" or "textoverlaycard" or "thumbnailconcept" => Required,
            "stellariumscreenshot" or "constellationguide" or "skymapcard" => Preferred,
            "aiheroimage" or "aicinematicimage" or "nasaasset" => Optional,
            _ => Optional
        };
    }

    public static string ResolveExecutionGroup(string? assetType) => NormalizeAssetType(assetType) switch
    {
        "narrationscriptplaceholder" or "textoverlaycard" => Core,
        "stellariumscreenshot" or "skymapcard" => AstronomyVisualization,
        "aiheroimage" or "aicinematicimage" => Cinematic,
        "constellationguide" or "nasaasset" => Educational,
        "thumbnailconcept" => Thumbnail,
        _ => Educational
    };

    private static string NormalizeAssetType(string? assetType) => (assetType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
}
