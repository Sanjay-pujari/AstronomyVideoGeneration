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
            logger.LogInformation("{Provider}_ASSET_REQUIREMENT_CREATED assetCode={AssetCode} segmentId={SegmentId} segmentType={SegmentType} query={SearchQuery}", requirement.ProviderName, requirement.AssetCode, requirement.SegmentId, requirement.SegmentType, requirement.SearchQuery);
            try
            {
                const int minimumCandidatesToTry = 5;
                var usedQuery = requirement.SearchQuery;
                var primaryCandidates = await imagesClient.SearchAsync(requirement, requirement.SearchQuery, cancellationToken);
                var rankedCandidates = selector.SelectCandidates(requirement, primaryCandidates, minimumCandidatesToTry).ToList();
                if (rankedCandidates.Count < minimumCandidatesToTry && !requirement.FallbackSearchQuery.Equals(requirement.SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackCandidates = await imagesClient.SearchAsync(requirement, requirement.FallbackSearchQuery, cancellationToken);
                    rankedCandidates.AddRange(selector.SelectCandidates(requirement, fallbackCandidates, minimumCandidatesToTry)
                        .Where(candidate => rankedCandidates.All(existing => !existing.NasaId.Equals(candidate.NasaId, StringComparison.OrdinalIgnoreCase))));
                    if (rankedCandidates.Count == 0) usedQuery = requirement.FallbackSearchQuery;
                }

                if (rankedCandidates.Count == 0)
                {
                    results.Add(Failed(requirement, usedQuery, "NoCandidateFound", "NASA Images search returned no usable image candidates."));
                    continue;
                }

                var providerFolder = requirement.ProviderName.Equals("JWST", StringComparison.OrdinalIgnoreCase) ? "jwst" : "nasa";
                var targetPath = Path.Combine(rootPath, "assets", providerFolder, NormalizePathSegment(requirement.EpisodeType), NormalizePathSegment(requirement.SegmentType), $"{requirement.AssetCode}.jpg");
                NasaAssetResult? result = null;
                var attemptWarnings = new List<string>();
                foreach (var candidate in rankedCandidates.Take(Math.Max(minimumCandidatesToTry, rankedCandidates.Count)))
                {
                    var choices = await imagesClient.GetAssetDownloadChoicesAsync(candidate, cancellationToken);
                    if (choices.Count == 0)
                    {
                        attemptWarnings.Add($"NASA Images candidate {candidate.NasaId} did not expose downloadable JPG/PNG assets.");
                        continue;
                    }

                    foreach (var choice in choices)
                    {
                        try
                        {
                            logger.LogInformation("{Provider}_IMAGE_DOWNLOAD_ATTEMPT assetCode={AssetCode} nasaId={NasaId} sourceUrl={SourceUrl} fromAssetEndpoint={FromAssetEndpoint}", requirement.ProviderName, requirement.AssetCode, candidate.NasaId, choice.Url, choice.FromAssetEndpoint);
                            var download = await downloader.DownloadAsync(choice.Url, targetPath, requirement.ProviderName, cancellationToken);
                            var validationWarnings = Validate(download.Path, download.FileSizeBytes, download.Width, download.Height).ToList();
                            var productionReady = validationWarnings.Count == 0;
                            if (productionReady)
                                logger.LogInformation("{Provider}_IMAGE_VALIDATION_PASSED path={Path} assetCode={AssetCode} width={Width} height={Height} size={Size}", requirement.ProviderName, download.Path, requirement.AssetCode, download.Width, download.Height, download.FileSizeBytes);
                            else
                            {
                                attemptWarnings.AddRange(validationWarnings.Select(warning => $"{candidate.NasaId}: {warning}"));
                                logger.LogWarning("{Provider}_IMAGE_VALIDATION_FAILED path={Path} assetCode={AssetCode} width={Width} height={Height} size={Size} warnings={Warnings}", requirement.ProviderName, download.Path, requirement.AssetCode, download.Width, download.Height, download.FileSizeBytes, string.Join(" | ", validationWarnings));
                            }

                            result = new NasaAssetResult(
                                requirement.ProviderName,
                                requirement.AssetCode,
                                requirement.SegmentId,
                                requirement.SegmentType,
                                usedQuery,
                                candidate.NasaId,
                                candidate.Title,
                                candidate.Description,
                                candidate.DateCreated,
                                candidate.Center,
                                download.SourceUrl,
                                download.Path,
                                download.Width,
                                download.Height,
                                download.FileSizeBytes,
                                productionReady ? "Generated" : "GeneratedButInvalid",
                                productionReady,
                                validationWarnings,
                                attemptWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                            if (productionReady) break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            attemptWarnings.Add($"{candidate.NasaId}: download failed for {choice.Url}: {ex.Message}");
                            logger.LogWarning(ex, "{Provider}_IMAGE_DOWNLOAD_ATTEMPT_FAILED assetCode={AssetCode} nasaId={NasaId} sourceUrl={SourceUrl}", requirement.ProviderName, requirement.AssetCode, candidate.NasaId, choice.Url);
                        }
                    }

                    if (result?.ProductionReady == true) break;
                }

                if (result is null)
                {
                    results.Add(Failed(requirement, usedQuery, "NoCandidateDownloaded", string.Join(" | ", attemptWarnings.DefaultIfEmpty("NASA Images candidates did not produce a downloadable production-ready image."))));
                    continue;
                }

                if (!result.ProductionReady && rankedCandidates.Count >= minimumCandidatesToTry)
                {
                    results.Add(Failed(requirement, usedQuery, "ValidationFailed", string.Join(" | ", attemptWarnings.DefaultIfEmpty("NASA Images candidates failed production image validation."))));
                    continue;
                }

                results.Add(result);
                logger.LogInformation("{Provider}_ASSET_RESULT_WRITTEN assetCode={AssetCode} status={Status} productionReady={ProductionReady}", requirement.ProviderName, result.AssetCode, result.GenerationStatus, result.ProductionReady);
                if (result.ProductionReady) logger.LogInformation("{Provider}_ASSET_REGISTERED_IN_PRODUCTION_MANIFEST path={Path}", requirement.ProviderName, result.DownloadedImagePath);
            }
            catch (NasaProviderUnavailableException ex)
            {
                results.Add(Failed(requirement, requirement.SearchQuery, "ProviderUnavailable", ex.Message));
                warnings.Add(ex.Message);
                logger.LogWarning(ex, "{Provider}_ASSET_RESULT_WRITTEN assetCode={AssetCode} status=ProviderUnavailable", requirement.ProviderName, requirement.AssetCode);
                if (!continueOnFailure) throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(Failed(requirement, requirement.SearchQuery, "Failed", ex.Message));
                logger.LogWarning(ex, "{Provider}_ASSET_RESULT_WRITTEN assetCode={AssetCode} status=Failed", requirement.ProviderName, requirement.AssetCode);
                if (!continueOnFailure) throw;
            }
        }

        var nasaReadyPaths = results.Where(x => x.ProviderName.Equals("NASA", StringComparison.OrdinalIgnoreCase) && x.ProductionReady && !string.IsNullOrWhiteSpace(x.DownloadedImagePath)).Select(x => x.DownloadedImagePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var jwstReadyPaths = results.Where(x => x.ProviderName.Equals("JWST", StringComparison.OrdinalIgnoreCase) && x.ProductionReady && !string.IsNullOrWhiteSpace(x.DownloadedImagePath)).Select(x => x.DownloadedImagePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var nasaResults = results.Where(x => x.ProviderName.Equals("NASA", StringComparison.OrdinalIgnoreCase)).ToList();
        var jwstResults = results.Where(x => x.ProviderName.Equals("JWST", StringComparison.OrdinalIgnoreCase)).ToList();
        var report = new NasaAssetRealizationReport(
            pipelineRunId,
            DateTime.UtcNow,
            true,
            nasaResults.Count,
            nasaResults.Count,
            nasaResults.Count(x => !string.IsNullOrWhiteSpace(x.DownloadedImagePath) && File.Exists(x.DownloadedImagePath)),
            nasaResults.Count(x => x.ProductionReady),
            nasaResults.Count(x => !x.ProductionReady),
            nasaReadyPaths,
            jwstResults.Count,
            jwstResults.Count,
            jwstResults.Count(x => !string.IsNullOrWhiteSpace(x.DownloadedImagePath) && File.Exists(x.DownloadedImagePath)),
            jwstResults.Count(x => x.ProductionReady),
            jwstResults.Count(x => !x.ProductionReady),
            jwstReadyPaths,
            results,
            warnings.Concat(results.SelectMany(x => x.Warnings)).Concat(results.SelectMany(x => x.ValidationWarnings)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var realization = new NasaAssetRealizationResult(plan, results, report, Path.Combine(rootPath, "episode", "nasa-asset-plan.json"), Path.Combine(rootPath, "episode", "nasa-asset-results.json"), Path.Combine(rootPath, "episode", "nasa-asset-realization-report.json"), Path.Combine(rootPath, "episode", "jwst-asset-plan.json"), Path.Combine(rootPath, "episode", "jwst-asset-results.json"), Path.Combine(rootPath, "episode", "jwst-asset-realization-report.json"));
        await PersistAsync(realization, cancellationToken);
        logger.LogInformation("NASA_ASSET_REALIZATION_COMPLETE planned={Planned} attempted={Attempted} generated={Generated} productionReady={ProductionReady} failed={Failed} jwstPlanned={JwstPlanned} jwstGenerated={JwstGenerated} jwstProductionReady={JwstProductionReady}", report.PlannedNASAAssetCount, report.AttemptedNASAAssetCount, report.GeneratedNASAAssetCount, report.ProductionReadyNASAAssetCount, report.FailedNASAAssetCount, report.PlannedJWSTAssetCount, report.GeneratedJWSTAssetCount, report.ProductionReadyJWSTAssetCount);
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
            var sourcePlans = segmentPlan.SourcePlans.Where(x => x.SourceType is VisualAssetSourceType.NASA or VisualAssetSourceType.JWST).ToList();
            for (var index = 0; index < sourcePlans.Count; index++)
            {
                var sourcePlan = sourcePlans[index];
                var objects = segmentPlan.AssignedObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var providerName = sourcePlan.SourceType == VisualAssetSourceType.JWST ? "JWST" : "NASA";
                var category = providerName == "JWST" ? sourcePlan.TargetJwstAssetCategory : sourcePlan.TargetNasaAssetCategory;
                var (query, fallback, keyword, codeHint) = BuildQuery(segmentPlan.SegmentType, objects, category, providerName);
                var assetCode = Sanitize($"{providerName.ToLowerInvariant()}-{codeHint}-context-{index + 1:00}");
                var episodeType = NormalizeEpisodeType(segmentEpisodeTypes.GetValueOrDefault(segmentPlan.SegmentId, "longform"));
                yield return new NasaAssetRequirement(providerName, assetCode, segmentPlan.SegmentId, segmentPlan.SegmentType, episodeType, category ?? $"{providerName} contextual support image", objects, query, fallback, keyword, sourcePlan.UsageRole);
            }
        }
    }

    private static (string Query, string Fallback, string Keyword, string CodeHint) BuildQuery(string segmentType, IReadOnlyList<string> assignedObjects, string? category, string providerName)
    {
        var providerKeyword = providerName.Equals("JWST", StringComparison.OrdinalIgnoreCase) ? "JWST" : "NASA";
        if (segmentType.Equals("WeeklySkyOverview", StringComparison.OrdinalIgnoreCase)) return ($"Earth night sky stars {providerKeyword}", $"Earth from space {providerKeyword}", "Earth", "earth");
        if (segmentType.Equals("MoonHighlights", StringComparison.OrdinalIgnoreCase)) return ($"Moon surface {providerKeyword}", $"lunar surface {providerKeyword}", "Moon", "moon");
        if (segmentType.Equals("PlanetHighlights", StringComparison.OrdinalIgnoreCase))
        {
            var planet = assignedObjects.FirstOrDefault(x => IsKnownPlanet(x));
            if (!string.IsNullOrWhiteSpace(planet)) return ($"{NormalizeObjectName(planet)} {providerKeyword}", $"planet {providerKeyword}", NormalizeObjectName(planet), NormalizeObjectName(planet));
            return ($"planet {providerKeyword}", $"planet {providerKeyword}", "planet", "planet");
        }
        if (segmentType.Equals("DeepSky", StringComparison.OrdinalIgnoreCase)) return ($"deep sky {providerKeyword}", $"nebula galaxy {providerKeyword}", "nebula", "deep-sky");
        var objectText = assignedObjects.Count == 0 ? null : NormalizeObjectName(assignedObjects[0]);
        var keyword = objectText ?? FirstWord(category) ?? providerKeyword;
        return ($"{keyword} {providerKeyword}", category is null ? $"astronomy {providerKeyword}" : $"{category} {providerKeyword}", keyword, keyword);
    }

    private static IEnumerable<string> Validate(string path, long fileSizeBytes, int width, int height)
    {
        if (!File.Exists(path)) yield return "Downloaded image file does not exist.";
        if (fileSizeBytes <= MinimumProductionBytes) yield return $"File size {fileSizeBytes} bytes is not greater than 50KB.";
        if (width < MinimumProductionWidth) yield return $"Image width {width} is less than {MinimumProductionWidth}.";
        if (height < MinimumProductionHeight) yield return $"Image height {height} is less than {MinimumProductionHeight}.";
    }

    private static NasaAssetResult Failed(NasaAssetRequirement requirement, string query, string status, string warning) => new(requirement.ProviderName, requirement.AssetCode, requirement.SegmentId, requirement.SegmentType, query, null, null, null, null, null, null, null, 0, 0, 0, status, false, [], [warning]);

    private static async Task PersistAsync(NasaAssetRealizationResult realization, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(realization.PlanPath)!);
        var nasaRequirements = realization.Plan.Requirements.Where(x => x.ProviderName.Equals("NASA", StringComparison.OrdinalIgnoreCase)).ToList();
        var jwstRequirements = realization.Plan.Requirements.Where(x => x.ProviderName.Equals("JWST", StringComparison.OrdinalIgnoreCase)).ToList();
        var nasaResults = realization.Results.Where(x => x.ProviderName.Equals("NASA", StringComparison.OrdinalIgnoreCase)).ToList();
        var jwstResults = realization.Results.Where(x => x.ProviderName.Equals("JWST", StringComparison.OrdinalIgnoreCase)).ToList();
        var nasaPlan = realization.Plan with { PlannedNASAAssetCount = nasaRequirements.Count, Requirements = nasaRequirements };
        var jwstPlan = realization.Plan with { PlannedNASAAssetCount = jwstRequirements.Count, Requirements = jwstRequirements };
        await File.WriteAllTextAsync(realization.PlanPath, JsonSerializer.Serialize(nasaPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.ResultsPath, JsonSerializer.Serialize(nasaResults, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.ReportPath, JsonSerializer.Serialize(realization.Report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.JwstPlanPath, JsonSerializer.Serialize(jwstPlan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.JwstResultsPath, JsonSerializer.Serialize(jwstResults, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.JwstReportPath, JsonSerializer.Serialize(realization.Report, JsonOptions), cancellationToken);
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
