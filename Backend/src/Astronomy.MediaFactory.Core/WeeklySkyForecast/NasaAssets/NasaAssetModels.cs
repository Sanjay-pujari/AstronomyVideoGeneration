using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public sealed class NasaAssetOptions
{
    public string SearchBaseUrl { get; set; } = string.Empty;
    public string SearchEndpoint { get; set; } = string.Empty;
    public string AssetEndpoint { get; set; } = string.Empty;
    public string NasaApiKey { get; set; } = string.Empty;
    public string NasaBaseUrl { get; set; } = string.Empty;

    public bool ProviderConfigured => !string.IsNullOrWhiteSpace(SearchBaseUrl) && !string.IsNullOrWhiteSpace(SearchEndpoint);
}

public sealed record NasaAssetPlan(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string WeeklyVisualAssetPlanPath,
    string WeeklyProductionAssetManifestPath,
    string? PreviousAssetRealizationReportPath,
    int PlannedNASAAssetCount,
    IReadOnlyList<NasaAssetRequirement> Requirements,
    IReadOnlyList<string> Warnings);

public sealed record NasaAssetRequirement(
    string AssetCode,
    string SegmentId,
    string SegmentType,
    string EpisodeType,
    string TargetNasaAssetCategory,
    IReadOnlyList<string> AssignedObjects,
    string SearchQuery,
    string FallbackSearchQuery,
    string PrimaryKeyword,
    string UsageRole);

public sealed record NasaAssetResult(
    string AssetCode,
    string SegmentId,
    string SegmentType,
    string SearchQuery,
    string? NasaId,
    string? Title,
    string? Description,
    string? DateCreated,
    string? Center,
    string? SourceUrl,
    string? DownloadedImagePath,
    int Width,
    int Height,
    long FileSizeBytes,
    string GenerationStatus,
    bool ProductionReady,
    IReadOnlyList<string> ValidationWarnings,
    IReadOnlyList<string> Warnings);

public sealed record NasaAssetRealizationReport(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    bool NasaProviderConfigured,
    int PlannedNASAAssetCount,
    int AttemptedNASAAssetCount,
    int GeneratedNASAAssetCount,
    int ProductionReadyNASAAssetCount,
    int FailedNASAAssetCount,
    IReadOnlyList<string> NasaImagePaths,
    IReadOnlyList<NasaAssetResult> Results,
    IReadOnlyList<string> Warnings);

public sealed record NasaAssetRealizationResult(
    NasaAssetPlan Plan,
    IReadOnlyList<NasaAssetResult> Results,
    NasaAssetRealizationReport Report,
    string PlanPath,
    string ResultsPath,
    string ReportPath)
{
    public static NasaAssetRealizationResult Empty(string rootPath, string visualPlanPath, string manifestPath, Guid pipelineRunId, bool configured, IReadOnlyList<string> warnings)
    {
        var episodeDirectory = Path.Combine(rootPath, "episode");
        var planPath = Path.Combine(episodeDirectory, "nasa-asset-plan.json");
        var resultsPath = Path.Combine(episodeDirectory, "nasa-asset-results.json");
        var reportPath = Path.Combine(episodeDirectory, "nasa-asset-realization-report.json");
        var plan = new NasaAssetPlan(pipelineRunId, DateTime.UtcNow, visualPlanPath, manifestPath, null, 0, [], warnings);
        var report = new NasaAssetRealizationReport(pipelineRunId, DateTime.UtcNow, configured, 0, 0, 0, 0, 0, [], [], warnings);
        return new NasaAssetRealizationResult(plan, [], report, planPath, resultsPath, reportPath);
    }
}

public sealed record NasaImageCandidate(
    string NasaId,
    string Title,
    string Description,
    string? DateCreated,
    IReadOnlyList<string> Keywords,
    string? Center,
    IReadOnlyList<string> PreviewLinks,
    string MediaType,
    int Score = 0,
    long PixelHint = 0);

public sealed record NasaImageDownloadChoice(string Url, bool FromAssetEndpoint, long PixelHint);
public sealed record NasaImageDownloadResult(string Path, long FileSizeBytes, int Width, int Height, string SourceUrl);

public sealed class NasaProviderUnavailableException(string message) : Exception(message);

internal sealed record NasaSearchResponse(NasaSearchCollection? Collection);
internal sealed record NasaSearchCollection(IReadOnlyList<NasaSearchItem>? Items);
internal sealed record NasaSearchItem(IReadOnlyList<NasaSearchData>? Data, IReadOnlyList<NasaSearchLink>? Links);
internal sealed record NasaSearchData(
    [property: JsonPropertyName("nasa_id")] string? NasaId,
    string? Title,
    string? Description,
    [property: JsonPropertyName("date_created")] string? DateCreated,
    IReadOnlyList<string>? Keywords,
    string? Center,
    [property: JsonPropertyName("media_type")] string? MediaType);
internal sealed record NasaSearchLink(string Href, string? Rel, string? Render);
internal sealed record NasaAssetResponse(NasaAssetCollection? Collection);
internal sealed record NasaAssetCollection(IReadOnlyList<NasaAssetItem>? Items);
internal sealed record NasaAssetItem(string Href);
