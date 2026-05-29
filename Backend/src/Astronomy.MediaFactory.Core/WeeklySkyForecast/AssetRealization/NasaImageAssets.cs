using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.VisualAssetPlanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;

public interface INasaImageAssetProvider
{
    bool IsConfigured { get; }

    Task<NasaAssetRealizationResult> RealizeAsync(
        string rootPath,
        string weeklyVisualAssetPlanPath,
        string weeklyProductionAssetManifestPath,
        bool continueOnFailure,
        CancellationToken cancellationToken);
}

public sealed record NasaAssetRequirement(
    string AssetCode,
    string SegmentId,
    string SegmentType,
    string EpisodeType,
    string TargetNasaAssetCategory,
    IReadOnlyList<string> AssignedObjects,
    string SearchQuery);

public sealed record NasaAssetResult(
    string AssetCode,
    string SegmentId,
    string SegmentType,
    string SearchQuery,
    string? NasaId,
    string? Title,
    string? Description,
    string? DateCreated,
    string? Photographer,
    string? SourceUrl,
    string? DownloadedImagePath,
    int Width,
    int Height,
    long FileSizeBytes,
    string GenerationStatus,
    bool ProductionReady,
    IReadOnlyList<string> Warnings);

public sealed record NasaAssetPlanDocument(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    string WeeklyVisualAssetPlanPath,
    string WeeklyProductionAssetManifestPath,
    int PlannedNASAAssetCount,
    IReadOnlyList<NasaAssetRequirement> Requirements);

public sealed record NasaAssetResultsDocument(
    Guid PipelineRunId,
    DateTime GeneratedAtUtc,
    bool ProviderConfigured,
    int PlannedNASAAssetCount,
    int GeneratedNASAAssetCount,
    int ProductionReadyNASAAssetCount,
    IReadOnlyList<string> NasaImagePaths,
    IReadOnlyList<NasaAssetResult> Results,
    IReadOnlyList<string> Warnings);

public sealed record NasaAssetRealizationResult(
    NasaAssetPlanDocument Plan,
    NasaAssetResultsDocument Results,
    string PlanPath,
    string ResultsPath)
{
    public static NasaAssetRealizationResult Empty(string rootPath, string visualPlanPath, string manifestPath, Guid pipelineRunId, bool configured, IReadOnlyList<string> warnings)
    {
        var episodeDirectory = Path.Combine(rootPath, "episode");
        var planPath = Path.Combine(episodeDirectory, "nasa-asset-plan.json");
        var resultsPath = Path.Combine(episodeDirectory, "nasa-asset-results.json");
        var plan = new NasaAssetPlanDocument(pipelineRunId, DateTime.UtcNow, visualPlanPath, manifestPath, 0, []);
        var results = new NasaAssetResultsDocument(pipelineRunId, DateTime.UtcNow, configured, 0, 0, 0, [], [], warnings);
        return new NasaAssetRealizationResult(plan, results, planPath, resultsPath);
    }
}

public sealed class NasaImageAssetProvider(
    NasaImageSearchClient searchClient,
    NasaImageAssetDownloader downloader,
    IOptions<NasaImagesOptions> nasaImagesOptions,
    ILogger<NasaImageAssetProvider> logger) : INasaImageAssetProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private const long MinimumProductionBytes = 50L * 1024L;
    private const int MinimumProductionWidth = 1024;
    private const int MinimumProductionHeight = 720;

    public bool IsConfigured => Uri.TryCreate(nasaImagesOptions.Value.SearchBaseUrl, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(nasaImagesOptions.Value.SearchEndpoint)
        && !string.IsNullOrWhiteSpace(nasaImagesOptions.Value.AssetEndpoint);

    public async Task<NasaAssetRealizationResult> RealizeAsync(
        string rootPath,
        string weeklyVisualAssetPlanPath,
        string weeklyProductionAssetManifestPath,
        bool continueOnFailure,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("NASA_ASSET_REALIZATION_START root={RootPath} visualPlanPath={VisualPlanPath} manifestPath={ManifestPath}", rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "episode"));
        var pipelineRunId = Guid.Empty;
        if (!IsConfigured)
        {
            logger.LogWarning("NASA_ASSET_REALIZATION_COMPLETE status=ProviderUnavailable root={RootPath}", rootPath);
            var empty = NasaAssetRealizationResult.Empty(rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, pipelineRunId, configured: false, ["NASA Images provider is not configured."]);
            await PersistAsync(empty, cancellationToken);
            return empty;
        }

        WeeklyVisualAssetPlan? visualPlan;
        WeeklyProductionAssetManifest? manifest;
        try
        {
            visualPlan = await ReadJsonAsync<WeeklyVisualAssetPlan>(weeklyVisualAssetPlanPath, cancellationToken);
            manifest = await ReadJsonAsync<WeeklyProductionAssetManifest>(weeklyProductionAssetManifestPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "NASA_ASSET_REALIZATION_COMPLETE status=FailedToReadInputs visualPlanPath={VisualPlanPath} manifestPath={ManifestPath}", weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath);
            if (!continueOnFailure) throw;
            var empty = NasaAssetRealizationResult.Empty(rootPath, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, pipelineRunId, configured: true, [$"NASA asset input read failed: {ex.Message}"]);
            await PersistAsync(empty, cancellationToken);
            return empty;
        }

        pipelineRunId = visualPlan.PipelineRunId;
        var requirements = BuildRequirements(visualPlan, manifest).ToList();
        var plan = new NasaAssetPlanDocument(pipelineRunId, DateTime.UtcNow, weeklyVisualAssetPlanPath, weeklyProductionAssetManifestPath, requirements.Count, requirements);
        var results = new List<NasaAssetResult>();
        var warnings = new List<string>();

        foreach (var requirement in requirements)
        {
            logger.LogInformation("NASA_ASSET_REQUIREMENT_SELECTED assetCode={AssetCode} segmentId={SegmentId} segmentType={SegmentType} query={SearchQuery}", requirement.AssetCode, requirement.SegmentId, requirement.SegmentType, requirement.SearchQuery);
            try
            {
                var searchResults = await searchClient.SearchAsync(requirement, cancellationToken);
                if (searchResults.Count == 0)
                {
                    results.Add(Failed(requirement, "Failed", "NASA Images search returned no image candidates."));
                    continue;
                }

                var best = searchResults.OrderByDescending(x => x.RelevanceScore).ThenByDescending(x => x.PixelHint).First();
                logger.LogInformation("NASA_IMAGE_DOWNLOAD_START assetCode={AssetCode} nasaId={NasaId} sourceUrl={SourceUrl}", requirement.AssetCode, best.NasaId, best.ImageUrl);
                var targetPath = Path.Combine(rootPath, "nasa-assets", requirement.EpisodeType, requirement.SegmentType, $"{requirement.AssetCode}.jpg");
                var download = await downloader.DownloadAsync(best.ImageUrl, targetPath, cancellationToken);
                logger.LogInformation("NASA_IMAGE_DOWNLOAD_COMPLETE assetCode={AssetCode} path={Path} size={Size}", requirement.AssetCode, download.Path, download.FileSizeBytes);

                var validationWarnings = Validate(download.FileSizeBytes, download.Width, download.Height).ToList();
                var productionReady = validationWarnings.Count == 0;
                logger.LogInformation(productionReady ? "NASA_IMAGE_VALIDATION_PASSED assetCode={AssetCode} width={Width} height={Height} size={Size}" : "NASA_IMAGE_VALIDATION_FAILED assetCode={AssetCode} width={Width} height={Height} size={Size} warnings={Warnings}", requirement.AssetCode, download.Width, download.Height, download.FileSizeBytes, string.Join(" | ", validationWarnings));
                results.Add(new NasaAssetResult(
                    requirement.AssetCode,
                    requirement.SegmentId,
                    requirement.SegmentType,
                    requirement.SearchQuery,
                    best.NasaId,
                    best.Title,
                    best.Description,
                    best.DateCreated,
                    best.Photographer,
                    best.ImageUrl,
                    download.Path,
                    download.Width,
                    download.Height,
                    download.FileSizeBytes,
                    productionReady ? "Downloaded" : "ValidationFailed",
                    productionReady,
                    validationWarnings));
            }
            catch (NasaProviderUnavailableException ex)
            {
                logger.LogWarning(ex, "NASA_ASSET_PROVIDER_UNAVAILABLE assetCode={AssetCode}", requirement.AssetCode);
                results.Add(Failed(requirement, "ProviderUnavailable", ex.Message));
                warnings.Add(ex.Message);
                if (!continueOnFailure) throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NASA_ASSET_REALIZATION_FAILED assetCode={AssetCode}", requirement.AssetCode);
                results.Add(Failed(requirement, "Failed", ex.Message));
                if (!continueOnFailure) throw;
            }
        }

        var readyPaths = results.Where(x => x.ProductionReady && !string.IsNullOrWhiteSpace(x.DownloadedImagePath)).Select(x => x.DownloadedImagePath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resultDocument = new NasaAssetResultsDocument(
            pipelineRunId,
            DateTime.UtcNow,
            ProviderConfigured: true,
            requirements.Count,
            results.Count(x => !string.IsNullOrWhiteSpace(x.DownloadedImagePath) && File.Exists(x.DownloadedImagePath)),
            results.Count(x => x.ProductionReady),
            readyPaths,
            results,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var realization = new NasaAssetRealizationResult(plan, resultDocument, Path.Combine(rootPath, "episode", "nasa-asset-plan.json"), Path.Combine(rootPath, "episode", "nasa-asset-results.json"));
        await PersistAsync(realization, cancellationToken);
        logger.LogInformation("NASA_ASSET_REALIZATION_COMPLETE planned={Planned} generated={Generated} productionReady={ProductionReady}", resultDocument.PlannedNASAAssetCount, resultDocument.GeneratedNASAAssetCount, resultDocument.ProductionReadyNASAAssetCount);
        return realization;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? throw new InvalidOperationException($"Unable to deserialize {path}.");
    }

    private static IEnumerable<NasaAssetRequirement> BuildRequirements(WeeklyVisualAssetPlan visualPlan, WeeklyProductionAssetManifest manifest)
    {
        var segmentEpisodeTypes = manifest.SegmentBundles.GroupBy(x => x.SegmentId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().EpisodeType, StringComparer.OrdinalIgnoreCase);
        foreach (var plan in visualPlan.LongformSegmentVisualPlans.Concat(visualPlan.ShortformSegmentVisualPlans))
        {
            var nasaPlans = plan.SourcePlans.Where(x => x.SourceType == VisualAssetSourceType.NASA).ToList();
            for (var index = 0; index < nasaPlans.Count; index++)
            {
                var nasa = nasaPlans[index];
                var category = string.IsNullOrWhiteSpace(nasa.TargetNasaAssetCategory) ? "NASA astronomy image" : nasa.TargetNasaAssetCategory!;
                var objects = plan.AssignedObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var assetCode = Sanitize($"nasa-{plan.SegmentId}-{index + 1:00}");
                var query = BuildSearchQuery(category, objects, plan.SegmentType);
                yield return new NasaAssetRequirement(assetCode, plan.SegmentId, plan.SegmentType, segmentEpisodeTypes.GetValueOrDefault(plan.SegmentId, "WeeklySkyForecast"), category, objects, query);
            }
        }
    }

    private static string BuildSearchQuery(string category, IReadOnlyList<string> objects, string segmentType)
    {
        var objectText = objects.Count == 0 ? string.Empty : string.Join(' ', objects.Select(NormalizeObjectName));
        return string.Join(' ', new[] { category, objectText, SegmentQueryText(segmentType), "NASA" }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }

    private static string NormalizeObjectName(string value) => value.Replace('_', ' ').Replace('-', ' ');

    private static string SegmentQueryText(string segmentType) => segmentType switch
    {
        "MoonHighlights" => "Moon lunar surface crater",
        "PlanetHighlights" => "planet space image",
        "AstrophotographyTip" => "night sky astrophotography reference",
        _ => "astronomy image"
    };

    private static IEnumerable<string> Validate(long fileSizeBytes, int width, int height)
    {
        if (fileSizeBytes <= MinimumProductionBytes) yield return $"File size {fileSizeBytes} bytes is not greater than 50KB.";
        if (width < MinimumProductionWidth) yield return $"Image width {width} is less than {MinimumProductionWidth}.";
        if (height < MinimumProductionHeight) yield return $"Image height {height} is less than {MinimumProductionHeight}.";
    }

    private static NasaAssetResult Failed(NasaAssetRequirement requirement, string status, string warning) => new(
        requirement.AssetCode,
        requirement.SegmentId,
        requirement.SegmentType,
        requirement.SearchQuery,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        status,
        false,
        [warning]);

    private static async Task PersistAsync(NasaAssetRealizationResult realization, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(realization.PlanPath)!);
        await File.WriteAllTextAsync(realization.PlanPath, JsonSerializer.Serialize(realization.Plan, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(realization.ResultsPath, JsonSerializer.Serialize(realization.Results, JsonOptions), cancellationToken);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.ToLowerInvariant().Select(ch => invalid.Contains(ch) ? '-' : char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed record NasaImageSearchResult(
    string NasaId,
    string Title,
    string Description,
    string? DateCreated,
    string? Photographer,
    string ImageUrl,
    int RelevanceScore,
    long PixelHint);

public sealed class NasaImageSearchClient(HttpClient httpClient, IOptions<NasaImagesOptions> options, ILogger<NasaImageSearchClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NasaImageSearchResult>> SearchAsync(NasaAssetRequirement requirement, CancellationToken cancellationToken)
    {
        var endpoint = options.Value.SearchEndpoint.StartsWith('/') ? options.Value.SearchEndpoint : $"/{options.Value.SearchEndpoint}";
        var url = $"{options.Value.SearchBaseUrl.TrimEnd('/')}{endpoint}?q={Uri.EscapeDataString(requirement.SearchQuery)}&media_type=image";
        logger.LogInformation("NASA_IMAGE_SEARCH_START assetCode={AssetCode} query={SearchQuery}", requirement.AssetCode, requirement.SearchQuery);
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new NasaProviderUnavailableException($"NASA Images search returned HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<NasaSearchResponse>(JsonOptions, cancellationToken);
        var results = new List<NasaImageSearchResult>();
        foreach (var item in payload?.Collection?.Items ?? [])
        {
            var data = item.Data?.FirstOrDefault();
            if (data is null) continue;
            var links = new List<string>();
            if (item.Links is not null) links.AddRange(item.Links.Select(x => x.Href));
            links.AddRange(await GetAssetLinksAsync(data.NasaId, cancellationToken));
            foreach (var link in links.Where(IsImageUrl).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                results.Add(new NasaImageSearchResult(
                    data.NasaId ?? string.Empty,
                    data.Title ?? requirement.TargetNasaAssetCategory,
                    data.Description ?? string.Empty,
                    data.DateCreated,
                    data.Photographer,
                    link,
                    Score(requirement, data, link),
                    PixelHint(link)));
            }
        }

        var ranked = results.OrderByDescending(x => x.RelevanceScore).ThenByDescending(x => x.PixelHint).Take(20).ToList();
        logger.LogInformation("NASA_IMAGE_SEARCH_COMPLETE assetCode={AssetCode} candidates={CandidateCount}", requirement.AssetCode, ranked.Count);
        return ranked;
    }

    private async Task<IReadOnlyList<string>> GetAssetLinksAsync(string? nasaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nasaId)) return [];
        try
        {
            var endpoint = options.Value.AssetEndpoint.Replace("{nasaId}", Uri.EscapeDataString(nasaId));
            endpoint = endpoint.StartsWith('/') ? endpoint : $"/{endpoint}";
            using var response = await httpClient.GetAsync($"{options.Value.SearchBaseUrl.TrimEnd('/')}{endpoint}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];
            var payload = await response.Content.ReadFromJsonAsync<NasaAssetResponse>(JsonOptions, cancellationToken);
            return payload?.Collection?.Items?.Select(x => x.Href).Where(IsImageUrl).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "NASA asset endpoint failed for nasaId={NasaId}", nasaId);
            return [];
        }
    }

    private static bool IsImageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static int Score(NasaAssetRequirement requirement, NasaSearchData data, string link)
    {
        var keywords = data.Keywords is null ? string.Empty : string.Join(' ', data.Keywords);
        var haystack = $"{data.Title} {data.Description} {keywords} {link}";
        var score = 0;
        foreach (var term in requirement.TargetNasaAssetCategory.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
        foreach (var obj in requirement.AssignedObjects)
            if (haystack.Contains(obj, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (haystack.Contains(requirement.SegmentType, StringComparison.OrdinalIgnoreCase)) score += 5;
        if (link.Contains("~orig", StringComparison.OrdinalIgnoreCase) || link.Contains("~large", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (link.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || link.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (haystack.Contains("NASA", StringComparison.OrdinalIgnoreCase)) score += 5;
        return score;
    }

    private static long PixelHint(string link)
    {
        if (link.Contains("~orig", StringComparison.OrdinalIgnoreCase)) return 10_000_000;
        if (link.Contains("~large", StringComparison.OrdinalIgnoreCase)) return 5_000_000;
        if (link.Contains("~medium", StringComparison.OrdinalIgnoreCase)) return 1_000_000;
        return 0;
    }
}

public sealed record NasaImageDownloadResult(string Path, long FileSizeBytes, int Width, int Height);

public sealed class NasaImageAssetDownloader(HttpClient httpClient, ILogger<NasaImageAssetDownloader> logger)
{
    private const long MaximumDownloadBytes = 50L * 1024L * 1024L;

    public async Task<NasaImageDownloadResult> DownloadAsync(string sourceUrl, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var response = await httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"NASA image download returned HTTP {(int)response.StatusCode}.");
        }

        var length = response.Content.Headers.ContentLength;
        if (length > MaximumDownloadBytes)
            throw new InvalidOperationException($"NASA image download exceeds maximum size of {MaximumDownloadBytes} bytes.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
        await target.FlushAsync(cancellationToken);
        var info = new FileInfo(targetPath);
        if (info.Length > MaximumDownloadBytes)
        {
            File.Delete(targetPath);
            throw new InvalidOperationException($"NASA image download exceeded maximum size of {MaximumDownloadBytes} bytes.");
        }

        var dimensions = ImageDimensionReader.Read(targetPath);
        logger.LogDebug("NASA image downloaded sourceUrl={SourceUrl} targetPath={TargetPath} width={Width} height={Height} size={Size}", sourceUrl, targetPath, dimensions.Width, dimensions.Height, info.Length);
        return new NasaImageDownloadResult(targetPath, info.Length, dimensions.Width, dimensions.Height);
    }
}

public sealed class NasaProviderUnavailableException(string message) : Exception(message);

file sealed record NasaSearchResponse(NasaSearchCollection? Collection);
file sealed record NasaSearchCollection(IReadOnlyList<NasaSearchItem>? Items);
file sealed record NasaSearchItem(IReadOnlyList<NasaSearchData>? Data, IReadOnlyList<NasaSearchLink>? Links);
file sealed record NasaSearchData(
    [property: JsonPropertyName("nasa_id")] string? NasaId,
    string? Title,
    string? Description,
    [property: JsonPropertyName("date_created")] string? DateCreated,
    [property: JsonPropertyName("photographer")] string? Photographer,
    IReadOnlyList<string>? Keywords);
file sealed record NasaSearchLink(string Href);
file sealed record NasaAssetResponse(NasaAssetCollection? Collection);
file sealed record NasaAssetCollection(IReadOnlyList<NasaAssetItem>? Items);
file sealed record NasaAssetItem(string Href);
