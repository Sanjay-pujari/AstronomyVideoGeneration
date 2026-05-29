using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.NasaAssets;

public interface INasaAssetRealizationService
{
    bool IsConfigured { get; }

    Task<NasaAssetRealizationResult> RealizeAsync(
        string rootPath,
        string weeklyVisualAssetPlanPath,
        string weeklyProductionAssetManifestPath,
        string? weeklyAssetRealizationReportPath,
        bool continueOnFailure,
        CancellationToken cancellationToken);
}

public sealed class NasaAssetRealizationService(
    INasaImagesClient imagesClient,
    INasaAssetSelector selector,
    INasaAssetDownloader downloader,
    ILogger<NasaAssetRealizationService> logger) : INasaAssetRealizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private const long MinimumProductionBytes = 50L * 1024L;
    private const int MinimumProductionWidth = 1024;
    private const int MinimumProductionHeight = 720;

    public bool IsConfigured => imagesClient.IsConfigured;

    public async Task<NasaAssetRealizationResult> RealizeAsync(
        string rootPath,
        string weeklyVisualAssetPlanPath,
        string weeklyProductionAssetManifestPath,
        string? weeklyAssetRealizationReportPath,
        bool continueOnFailure,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("NASA_ASSET_REALIZATION_START root={RootPath} visualPlanPath={VisualPlanPath} manifestPath={ManifestPath}", rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "episode"));
        var pipelineRunId = Guid.Empty;
        if (!IsConfigured)
        {
            var empty = NasaAssetRealizationResult.Empty(rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, pipelineRunId, configured: false, ["NASA Images provider is not configured."]);
            await PersistAsync(empty, cancellationToken);
            logger.LogWarning("NASA_ASSET_REALIZATION_COMPLETE status=ProviderUnavailable root={RootPath}", rootPath);
            return empty;
        }

        WeeklyVisualAssetPlan visualPlan;
        WeeklyProductionAssetManifest? manifest = null;
        try
        {
            visualPlan = await ReadJsonAsync<WeeklyVisualAssetPlan>(weeklyVisualAssetPlanPath, cancellationToken);
            pipelineRunId = visualPlan.PipelineRunId;
            if (File.Exists(weeklyProductionAssetManifestPath))
                manifest = await ReadJsonAsync<WeeklyProductionAssetManifest>(weeklyProductionAssetManifestPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!continueOnFailure) throw;
            var empty = NasaAssetRealizationResult.Empty(rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, pipelineRunId, configured: true, [$"NASA asset input read failed: {ex.Message}"]);
            await PersistAsync(empty, cancellationToken);
            logger.LogWarning(ex, "NASA_ASSET_REALIZATION_COMPLETE status=FailedToReadInputs visualPlanPath={VisualPlanPath} manifestPath={ManifestPath}", weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath);
            return empty;
        }

        var planWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(weeklyAssetRealizationReportPath) && File.Exists(weeklyAssetRealizationReportPath))
            planWarnings.Add($"Previous asset realization report used as planning context: {weeklyAssetRealizationReportPath}");
        var requirements = BuildRequirements(visualPlan, manifest).ToList();
        var plan = new NasaAssetPlan(pipelineRunId, DateTime.UtcNow, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, weeklyAssetRealizationReportPath, requirements.Count, requirements, planWarnings);
        var results = new List<NasaAssetResult>();
        var warnings = new List<string>(planWarnings);

        foreach (var requirement in requirements)
        {
            logger.LogInformation("NASA_ASSET_REQUIREMENT_CREATED assetCode={AssetCode} segmentId={SegmentId} segmentType={SegmentType} query={SearchQuery}", requirement.AssetCode, requirement.SegmentId, requirement.SegmentType, requirement.SearchQuery);
            try
            {
                var candidates = await imagesClient.SearchAsync(requirement, requirement.SearchQuery, cancellationToken);
                var best = selector.SelectBest(requirement, candidates);
                var usedQuery = requirement.SearchQuery;
                if (best is null && !requirement.FallbackSearchQuery.Equals(requirement.SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    candidates = await imagesClient.SearchAsync(requirement, requirement.FallbackSearchQuery, cancellationToken);
                    best = selector.SelectBest(requirement, candidates);
                    usedQuery = requirement.FallbackSearchQuery;
                }

                if (best is null)
                {
                    results.Add(Failed(requirement, usedQuery, "NoCandidateFound", "NASA Images search returned no usable image candidates."));
                    continue;
                }

                var choices = await imagesClient.GetAssetDownloadChoicesAsync(best, cancellationToken);
                if (choices.Count == 0)
                {
                    results.Add(Failed(requirement, usedQuery, "NoCandidateFound", "NASA Images candidate did not expose downloadable JPG/PNG assets."));
                    continue;
                }

                var targetPath = Path.Combine(rootPath, "nasa-assets", NormalizePathSegment(requirement.EpisodeType), NormalizePathSegment(requirement.SegmentType), $"{requirement.AssetCode}.jpg");
                var download = await downloader.DownloadAsync(choices[0].Url, targetPath, cancellationToken);
                var validationWarnings = Validate(download.Path, download.FileSizeBytes, download.Width, download.Height).ToList();
                var productionReady = validationWarnings.Count == 0;
                if (productionReady)
                    logger.LogInformation("NASA_IMAGE_VALIDATION_PASSED assetCode={AssetCode} width={Width} height={Height} size={Size}", requirement.AssetCode, download.Width, download.Height, download.FileSizeBytes);
                else
                    logger.LogWarning("NASA_IMAGE_VALIDATION_FAILED assetCode={AssetCode} width={Width} height={Height} size={Size} warnings={Warnings}", requirement.AssetCode, download.Width, download.Height, download.FileSizeBytes, string.Join(" | ", validationWarnings));

                var result = new NasaAssetResult(
                    requirement.AssetCode,
                    requirement.SegmentId,
                    requirement.SegmentType,
                    usedQuery,
                    best.NasaId,
                    best.Title,
                    best.Description,
                    best.DateCreated,
                    best.Center,
                    download.SourceUrl,
                    download.Path,
                    download.Width,
                    download.Height,
                    download.FileSizeBytes,
                    productionReady ? "Generated" : "GeneratedButInvalid",
                    productionReady,
                    validationWarnings,
                    []);
                results.Add(result);
                logger.LogInformation("NASA_ASSET_RESULT_WRITTEN assetCode={AssetCode} status={Status} productionReady={ProductionReady}", result.AssetCode, result.GenerationStatus, result.ProductionReady);
            }
            catch (NasaProviderUnavailableException ex)
            {
                results.Add(Failed(requirement, requirement.SearchQuery, "ProviderUnavailable", ex.Message));
                warnings.Add(ex.Message);
                logger.LogWarning(ex, "NASA_ASSET_RESULT_WRITTEN assetCode={AssetCode} status=ProviderUnavailable", requirement.AssetCode);
                if (!continueOnFailure) throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(Failed(requirement, requirement.SearchQuery, "Failed", ex.Message));
                logger.LogWarning(ex, "NASA_ASSET_RESULT_WRITTEN assetCode={AssetCode} status=Failed", requirement.AssetCode);
                if (!continueOnFailure) throw;
            }
        }

        var readyPaths = results.Where(x => x.ProductionReady && !string.IsNullOrWhiteSpace(x.DownloadedImagePath)).Select(x => x.DownloadedImagePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var report = new NasaAssetRealizationReport(
            pipelineRunId,
            DateTime.UtcNow,
            true,
            requirements.Count,
            results.Count,
            results.Count(x => !string.IsNullOrWhiteSpace(x.DownloadedImagePath) && File.Exists(x.DownloadedImagePath)),
            results.Count(x => x.ProductionReady),
            results.Count(x => !x.ProductionReady),
            readyPaths,
            results,
            warnings.Concat(results.SelectMany(x => x.Warnings)).Concat(results.SelectMany(x => x.ValidationWarnings)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var realization = new NasaAssetRealizationResult(plan, results, report, Path.Combine(rootPath, "episode", "nasa-asset-plan.json"), Path.Combine(rootPath, "episode", "nasa-asset-results.json"), Path.Combine(rootPath, "episode", "nasa-asset-realization-report.json"));
        await PersistAsync(realization, cancellationToken);
        logger.LogInformation("NASA_ASSET_REALIZATION_COMPLETE planned={Planned} attempted={Attempted} generated={Generated} productionReady={ProductionReady} failed={Failed}", report.PlannedNASAAssetCount, report.AttemptedNASAAssetCount, report.GeneratedNASAAssetCount, report.ProductionReadyNASAAssetCount, report.FailedNASAAssetCount);
        return realization;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? throw new InvalidOperationException($"Unable to deserialize {path}.");
    }

    private static IEnumerable<NasaAssetRequirement> BuildRequirements(WeeklyVisualAssetPlan visualPlan, WeeklyProductionAssetManifest? manifest)
    {
        var segmentEpisodeTypes = manifest?.SegmentBundles.GroupBy(x => x.SegmentId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().EpisodeType, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segmentPlan in visualPlan.LongformSegmentVisualPlans.Concat(visualPlan.ShortformSegmentVisualPlans))
        {
            var nasaPlans = segmentPlan.SourcePlans.Where(x => x.SourceType == VisualAssetSourceType.NASA).ToList();
            for (var index = 0; index < nasaPlans.Count; index++)
            {
                var sourcePlan = nasaPlans[index];
                var objects = segmentPlan.AssignedObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var (query, fallback, keyword, codeHint) = BuildQuery(segmentPlan.SegmentType, objects, sourcePlan.TargetNasaAssetCategory);
                var assetCode = Sanitize($"nasa-{codeHint}-context-{index + 1:00}");
                var episodeType = NormalizeEpisodeType(segmentEpisodeTypes.GetValueOrDefault(segmentPlan.SegmentId, "longform"));
                yield return new NasaAssetRequirement(assetCode, segmentPlan.SegmentId, segmentPlan.SegmentType, episodeType, sourcePlan.TargetNasaAssetCategory ?? "NASA contextual support image", objects, query, fallback, keyword, sourcePlan.UsageRole);
            }
        }
    }

    private static (string Query, string Fallback, string Keyword, string CodeHint) BuildQuery(string segmentType, IReadOnlyList<string> assignedObjects, string? category)
    {
        if (segmentType.Equals("WeeklySkyOverview", StringComparison.OrdinalIgnoreCase)) return ("Earth night sky stars NASA", "Earth from space NASA", "Earth", "earth");
        if (segmentType.Equals("MoonHighlights", StringComparison.OrdinalIgnoreCase)) return ("Moon surface NASA", "lunar surface NASA", "Moon", "moon");
        if (segmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase))
        {
            var planet = assignedObjects.FirstOrDefault(x => IsKnownPlanet(x));
            if (!string.IsNullOrWhiteSpace(planet)) return ($"{NormalizeObjectName(planet)} NASA", "planet NASA", NormalizeObjectName(planet), NormalizeObjectName(planet));
            return ("planet NASA", "planet NASA", "planet", "planet");
        }
        if (segmentType.Equals("DeepSky", StringComparison.OrdinalIgnoreCase)) return ("deep sky NASA", "nebula galaxy NASA", "nebula", "deep-sky");
        var objectText = assignedObjects.Count == 0 ? null : NormalizeObjectName(assignedObjects[0]);
        var keyword = objectText ?? FirstWord(category) ?? "NASA";
        return ($"{keyword} NASA", category is null ? "astronomy NASA" : $"{category} NASA", keyword, keyword);
    }

    private static IEnumerable<string> Validate(string path, long fileSizeBytes, int width, int height)
    {
        if (!File.Exists(path)) yield return "Downloaded image file does not exist.";
        if (fileSizeBytes <= MinimumProductionBytes) yield return $"File size {fileSizeBytes} bytes is not greater than 50KB.";
        if (width < MinimumProductionWidth) yield return $"Image width {width} is less than {MinimumProductionWidth}.";
        if (height < MinimumProductionHeight) yield return $"Image height {height} is less than {MinimumProductionHeight}.";
    }

    private static NasaAssetResult Failed(NasaAssetRequirement requirement, string query, string status, string warning) => new(requirement.AssetCode, requirement.SegmentId, requirement.SegmentType, query, null, null, null, null, null, null, null, 0, 0, 0, status, false, [], [warning]);

    private static async Task PersistAsync(NasaAssetRealizationResult realization, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(realization.PlanPath)!);
        await File.WriteAllTextAsync(realization.PlanPath, JsonSerializer.Serialize(realization.Plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.ResultsPath, JsonSerializer.Serialize(realization.Results, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.ReportPath, JsonSerializer.Serialize(realization.Report, JsonOptions), cancellationToken);
    }

    private static bool IsKnownPlanet(string value)
    {
        var normalized = NormalizeObjectName(value);
        return normalized.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Venus", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Saturn", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Mars", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Mercury", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Uranus", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Neptune", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEpisodeType(string value)
    {
        if (value.Contains("ShortForm", StringComparison.OrdinalIgnoreCase) || value.Contains("short", StringComparison.OrdinalIgnoreCase)) return "shortform";
        if (value.Contains("LongForm", StringComparison.OrdinalIgnoreCase) || value.Contains("long", StringComparison.OrdinalIgnoreCase)) return "longform";
        return value;
    }

    private static string NormalizeObjectName(string value) => value.Replace('_', ' ').Replace('-', ' ').Trim();
    private static string? FirstWord(string? value) => value?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    private static string NormalizePathSegment(string value) => Sanitize(value).Replace('-', '_');
    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.ToLowerInvariant().Select(ch => invalid.Contains(ch) ? '-' : char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
