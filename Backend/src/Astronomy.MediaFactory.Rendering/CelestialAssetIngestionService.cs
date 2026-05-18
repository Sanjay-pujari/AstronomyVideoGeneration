using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace Astronomy.MediaFactory.Rendering;

public sealed class CelestialAssetIngestionService : ICelestialAssetIngestionService
{
    private const long MaximumDownloadBytes = 25L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, string> QueryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["jupiter"] = "Jupiter planet",
        ["saturn"] = "Saturn rings planet",
        ["mars"] = "Mars planet",
        ["venus"] = "Venus planet",
        ["mercury"] = "Mercury planet",
        ["uranus"] = "Uranus planet",
        ["neptune"] = "Neptune planet",
        ["moon"] = "Moon lunar surface",
        ["meteor-shower"] = "meteor shower night sky",
        ["lunar-eclipse"] = "lunar eclipse moon",
        ["solar-eclipse"] = "solar eclipse",
        ["orion-nebula"] = "Orion Nebula Hubble",
        ["andromeda-galaxy"] = "Andromeda Galaxy",
        ["ring-nebula"] = "Ring Nebula Hubble",
        ["earth"] = "Earth planet",
        ["sun"] = "Sun solar disk",
        ["milky-way"] = "Milky Way night sky"
    };

    private readonly HttpClient _httpClient;
    private readonly CelestialAssetsOptions _assetOptions;
    private readonly AstronomyApiOptions _astronomyApiOptions;
    private readonly NasaImagesOptions _nasaImagesOptions;
    private readonly ILogger<CelestialAssetIngestionService> _logger;
    private readonly IRuntimeAssetPathResolver _assetPathResolver;

    public CelestialAssetIngestionService(
        HttpClient httpClient,
        IOptions<CelestialAssetsOptions> assetOptions,
        IOptions<AstronomyApiOptions> astronomyApiOptions,
        IOptions<NasaImagesOptions> nasaImagesOptions,
        ILogger<CelestialAssetIngestionService> logger,
        IRuntimeAssetPathResolver? assetPathResolver = null)
    {
        _httpClient = httpClient;
        _assetOptions = assetOptions.Value;
        _astronomyApiOptions = astronomyApiOptions.Value;
        _nasaImagesOptions = nasaImagesOptions.Value;
        _logger = logger;
        _assetPathResolver = assetPathResolver ?? new RuntimeAssetPathResolver();
    }

    public async Task<CelestialAssetIngestionReport> RefreshAsync(CancellationToken cancellationToken)
    {
        EnsureObjectDirectories();
        var results = new List<CelestialObjectIngestionResult>();
        foreach (var objectKey in RequiredObjectKeys())
        {
            results.Add(await RefreshObjectAsync(objectKey, cancellationToken));
        }

        var report = new CelestialAssetIngestionReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Objects = results
        };
        await WriteReportAsync(report, cancellationToken);
        return report;
    }

    public async Task<CelestialObjectIngestionResult> RefreshObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        objectKey = NormalizeObjectKey(objectKey);
        var errors = new List<string>();
        var directory = GetObjectDirectory(objectKey);
        Directory.CreateDirectory(directory);

        var existing = await ReadOrCreateLocalMetadataAsync(objectKey, directory, cancellationToken);
        var requiredCount = Math.Max(1, _assetOptions.MaxImagesPerObject);
        if (!_assetOptions.Enabled)
        {
            return BuildResult(objectKey, existing, 0, skippedBecauseCached: true, errors);
        }

        if (_assetOptions.PreferLocalCache && !_assetOptions.RefreshExistingAssets && existing.Count >= requiredCount)
        {
            _logger.LogInformation("Celestial asset cache for {ObjectKey} already has {Count} images.", objectKey, existing.Count);
            return BuildResult(objectKey, existing, 0, skippedBecauseCached: true, errors);
        }

        if (!_assetOptions.DownloadIfMissing)
        {
            return BuildResult(objectKey, existing, 0, skippedBecauseCached: existing.Count > 0, errors);
        }

        var downloaded = 0;
        try
        {
            var candidates = await SearchNasaImagesAsync(objectKey, cancellationToken);
            foreach (var candidate in candidates)
            {
                if (existing.Count >= requiredCount && !_assetOptions.RefreshExistingAssets)
                    break;

                if (existing.Any(x => x.OriginalUrl.Equals(candidate.Url, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(candidate.NasaId) && x.NasaId == candidate.NasaId)))
                    continue;

                var metadata = await TryDownloadCandidateAsync(objectKey, directory, candidate, cancellationToken);
                if (metadata is null)
                    continue;

                existing.Add(metadata);
                downloaded++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex.Message);
            _logger.LogWarning(ex, "NASA image search failed for celestial asset {ObjectKey}.", objectKey);
        }

        if (existing.Count < requiredCount)
        {
            try
            {
                var apod = await TryDownloadApodSupplementAsync(objectKey, directory, existing, cancellationToken);
                if (apod is not null)
                {
                    existing.Add(apod);
                    downloaded++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"APOD supplemental source failed: {ex.Message}");
                _logger.LogWarning(ex, "NASA APOD supplemental asset lookup failed for {ObjectKey}.", objectKey);
            }
        }

        await WriteObjectMetadataAsync(directory, existing, cancellationToken);
        var result = BuildResult(objectKey, existing, downloaded, skippedBecauseCached: false, errors);
        await MergeReportObjectAsync(result, cancellationToken);
        return result;
    }

    public async Task<CelestialAssetStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        EnsureObjectDirectories();
        var objects = new List<CelestialAssetObjectStatus>();
        foreach (var objectKey in RequiredObjectKeys())
        {
            var status = await GetObjectAsync(objectKey, cancellationToken);
            if (status is not null)
                objects.Add(status);
        }

        return new CelestialAssetStatusResponse
        {
            Enabled = _assetOptions.Enabled,
            RootPath = GetRootPath(),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Objects = objects
        };
    }

    public async Task<CelestialAssetObjectStatus?> GetObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        objectKey = NormalizeObjectKey(objectKey);
        var allowed = RequiredObjectKeys().Contains(objectKey, StringComparer.OrdinalIgnoreCase);
        if (!allowed)
            return null;

        var directory = GetObjectDirectory(objectKey);
        Directory.CreateDirectory(directory);
        var metadata = await ReadOrCreateLocalMetadataAsync(objectKey, directory, cancellationToken);
        var primary = SelectPrimary(metadata)?.LocalPath ?? string.Empty;
        var requiredCount = Math.Max(1, _assetOptions.MaxImagesPerObject);
        return new CelestialAssetObjectStatus
        {
            ObjectKey = objectKey,
            Directory = directory,
            ImagesFound = metadata.Count,
            RequiredImages = requiredCount,
            IsSatisfied = metadata.Count >= requiredCount,
            SelectedPrimaryAsset = primary,
            Images = metadata.OrderByDescending(x => x.QualityScore).ThenBy(x => x.LocalPath, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public string ResolveObjectKey(string objectName, string objectType)
    {
        var value = NormalizeObjectKey($"{objectName} {objectType}");
        if (value.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "meteor-shower";
        if (value.Contains("solar-eclipse", StringComparison.OrdinalIgnoreCase)) return "solar-eclipse";
        if (value.Contains("lunar-eclipse", StringComparison.OrdinalIgnoreCase)) return "lunar-eclipse";
        if (value.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return value.Contains("moon", StringComparison.OrdinalIgnoreCase) || value.Contains("lunar", StringComparison.OrdinalIgnoreCase) ? "lunar-eclipse" : "solar-eclipse";
        foreach (var key in RequiredObjectKeys())
        {
            if (value.Contains(key, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        if (value.Contains("andromeda", StringComparison.OrdinalIgnoreCase)) return "andromeda-galaxy";
        if (value.Contains("ring", StringComparison.OrdinalIgnoreCase) && value.Contains("nebula", StringComparison.OrdinalIgnoreCase)) return "ring-nebula";
        if (value.Contains("nebula", StringComparison.OrdinalIgnoreCase)) return "orion-nebula";
        if (value.Contains("galaxy", StringComparison.OrdinalIgnoreCase)) return "andromeda-galaxy";
        return "milky-way";
    }

    private async Task<IReadOnlyCollection<NasaImageCandidate>> SearchNasaImagesAsync(string objectKey, CancellationToken cancellationToken)
    {
        var query = QueryMap.GetValueOrDefault(objectKey, objectKey.Replace('-', ' '));
        var endpoint = _nasaImagesOptions.SearchEndpoint.StartsWith('/') ? _nasaImagesOptions.SearchEndpoint : $"/{_nasaImagesOptions.SearchEndpoint}";
        var url = $"{_nasaImagesOptions.SearchBaseUrl.TrimEnd('/')}{endpoint}?q={Uri.EscapeDataString(query)}&media_type=image";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("NASA image search returned {StatusCode} for {ObjectKey}.", (int)response.StatusCode, objectKey);
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<NasaSearchResponse>(JsonOptions, cancellationToken);
        var candidates = new List<NasaImageCandidate>();
        foreach (var item in payload?.Collection?.Items ?? [])
        {
            var data = item.Data?.FirstOrDefault();
            var directLinks = item.Links?.Select(x => x.Href).Where(IsAllowedImageUrl).ToArray() ?? [];
            foreach (var link in directLinks)
            {
                candidates.Add(new NasaImageCandidate(link, data?.NasaId ?? string.Empty, data?.Title ?? objectKey, data?.Description ?? string.Empty, ScoreCandidate(data?.Title, data?.Description, link)));
            }

            if (directLinks.Length == 0 && !string.IsNullOrWhiteSpace(data?.NasaId))
            {
                var assetLinks = await GetAssetLinksAsync(data.NasaId, cancellationToken);
                candidates.AddRange(assetLinks.Select(link => new NasaImageCandidate(link, data.NasaId, data.Title ?? objectKey, data.Description ?? string.Empty, ScoreCandidate(data.Title, data.Description, link))));
            }
        }

        return candidates
            .Where(x => IsAllowedImageUrl(x.Url))
            .OrderByDescending(x => x.QualityScore)
            .ThenBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(_assetOptions.MaxImagesPerObject * 3, 10))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<string>> GetAssetLinksAsync(string nasaId, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = _nasaImagesOptions.AssetEndpoint.Replace("{nasaId}", Uri.EscapeDataString(nasaId));
            if (!endpoint.StartsWith('/'))
                endpoint = $"/{endpoint}";
            using var response = await _httpClient.GetAsync($"{_nasaImagesOptions.SearchBaseUrl.TrimEnd('/')}{endpoint}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];
            var payload = await response.Content.ReadFromJsonAsync<NasaAssetResponse>(JsonOptions, cancellationToken);
            return payload?.Collection?.Items?.Select(x => x.Href).Where(IsAllowedImageUrl).ToArray() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "NASA asset endpoint failed for {NasaId}.", nasaId);
            return [];
        }
    }

    private async Task<CelestialAssetImageMetadata?> TryDownloadCandidateAsync(string objectKey, string directory, NasaImageCandidate candidate, CancellationToken cancellationToken)
    {
        var extension = NormalizeExtension(Path.GetExtension(new Uri(candidate.Url).AbsolutePath));
        var fileName = $"{objectKey}-{Sanitize(candidate.NasaId.Length > 0 ? candidate.NasaId : candidate.Title)}{extension}";
        var localPath = Path.Combine(directory, fileName);
        if (File.Exists(localPath) && !_assetOptions.RefreshExistingAssets)
            return await BuildMetadataAsync(objectKey, localPath, candidate, source: "NASA", cancellationToken);

        using var response = await _httpClient.GetAsync(candidate.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            _logger.LogWarning("Skipping NASA asset {Url} because Content-Length is {Length} bytes.", candidate.Url, response.Content.Headers.ContentLength);
            return null;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(localPath);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > MaximumDownloadBytes)
            {
                output.Close();
                File.Delete(localPath);
                _logger.LogWarning("Skipping NASA asset {Url} because it exceeded {MaximumDownloadBytes} bytes.", candidate.Url, MaximumDownloadBytes);
                return null;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return await BuildMetadataAsync(objectKey, localPath, candidate, source: "NASA", cancellationToken);
    }

    private async Task<CelestialAssetImageMetadata?> TryDownloadApodSupplementAsync(string objectKey, string directory, IReadOnlyCollection<CelestialAssetImageMetadata> existing, CancellationToken cancellationToken)
    {
        var url = $"{_astronomyApiOptions.NasaBaseUrl.TrimEnd('/')}/planetary/apod?api_key={Uri.EscapeDataString(_astronomyApiOptions.NasaApiKey)}";
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == (HttpStatusCode)429 || !response.IsSuccessStatusCode)
            return null;

        var apod = await response.Content.ReadFromJsonAsync<NasaApodPayload>(JsonOptions, cancellationToken);
        var imageUrl = apod?.HdUrl ?? apod?.Url;
        if (!string.Equals(apod?.MediaType, "image", StringComparison.OrdinalIgnoreCase) || !IsAllowedImageUrl(imageUrl))
            return null;

        var query = QueryMap.GetValueOrDefault(objectKey, objectKey).Replace('-', ' ');
        var searchable = $"{apod.Title} {apod.Explanation}";
        if (!searchable.Contains(objectKey.Replace('-', ' '), StringComparison.OrdinalIgnoreCase) && !query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return null;

        if (existing.Any(x => x.OriginalUrl.Equals(imageUrl, StringComparison.OrdinalIgnoreCase)))
            return null;

        return await TryDownloadCandidateAsync(objectKey, directory, new NasaImageCandidate(imageUrl!, "APOD", apod.Title ?? objectKey, apod.Explanation ?? string.Empty, 0.75), cancellationToken);
    }

    private async Task<CelestialAssetImageMetadata> BuildMetadataAsync(string objectKey, string localPath, NasaImageCandidate candidate, string source, CancellationToken cancellationToken)
    {
        var imageInfo = await Image.IdentifyAsync(localPath, cancellationToken);
        return new CelestialAssetImageMetadata
        {
            ObjectKey = objectKey,
            Source = source,
            Title = candidate.Title,
            Description = candidate.Description,
            NasaId = candidate.NasaId,
            OriginalUrl = candidate.Url,
            LocalPath = localPath,
            DownloadedAtUtc = DateTimeOffset.UtcNow,
            LicenseNote = source == "NASA" ? "NASA imagery is generally not copyrighted; verify any credited third-party restrictions before publishing." : "Local curated asset; verify local rights metadata before publishing.",
            Width = imageInfo?.Width ?? 0,
            Height = imageInfo?.Height ?? 0,
            QualityScore = Math.Round(candidate.QualityScore + Math.Min((imageInfo?.Width ?? 0) * (imageInfo?.Height ?? 0) / 8_000_000d, 0.25), 3)
        };
    }

    private async Task<List<CelestialAssetImageMetadata>> ReadOrCreateLocalMetadataAsync(string objectKey, string directory, CancellationToken cancellationToken)
    {
        var metadata = await ReadObjectMetadataAsync(directory, cancellationToken);
        var knownPaths = metadata.Select(x => Path.GetFullPath(x.LocalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory).Where(IsAllowedLocalImage))
        {
            if (knownPaths.Contains(Path.GetFullPath(path)))
                continue;

            try
            {
                metadata.Add(await BuildMetadataAsync(objectKey, path, new NasaImageCandidate(path, string.Empty, Path.GetFileNameWithoutExtension(path), string.Empty, 0.65), source: "LocalCache", cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Unable to read local celestial asset metadata for {Path}.", path);
            }
        }

        await WriteObjectMetadataAsync(directory, metadata, cancellationToken);
        return metadata;
    }

    private async Task<List<CelestialAssetImageMetadata>> ReadObjectMetadataAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "asset-metadata.json");
        if (!File.Exists(path))
            return [];

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<CelestialAssetImageMetadata>>(stream, JsonOptions, cancellationToken) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unable to read celestial asset metadata {Path}.", path);
            return [];
        }
    }

    private static Task WriteObjectMetadataAsync(string directory, List<CelestialAssetImageMetadata> metadata, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(directory, "asset-metadata.json"), JsonSerializer.Serialize(metadata.OrderByDescending(x => x.QualityScore).ToArray(), JsonOptions), cancellationToken);

    private async Task WriteReportAsync(CelestialAssetIngestionReport report, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(Path.Combine(GetRootPath(), "asset-ingestion-report.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken);

    private async Task MergeReportObjectAsync(CelestialObjectIngestionResult result, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(GetRootPath(), "asset-ingestion-report.json");
        CelestialAssetIngestionReport? report = null;
        if (File.Exists(reportPath))
        {
            try
            {
                await using var stream = File.OpenRead(reportPath);
                report = await JsonSerializer.DeserializeAsync<CelestialAssetIngestionReport>(stream, JsonOptions, cancellationToken);
            }
            catch
            {
                report = null;
            }
        }

        var objects = report?.Objects.Where(x => !x.ObjectKey.Equals(result.ObjectKey, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        objects.Add(result);
        await WriteReportAsync(new CelestialAssetIngestionReport { GeneratedAtUtc = DateTimeOffset.UtcNow, Objects = objects.OrderBy(x => x.ObjectKey).ToArray() }, cancellationToken);
    }

    private CelestialObjectIngestionResult BuildResult(string objectKey, IReadOnlyCollection<CelestialAssetImageMetadata> metadata, int downloaded, bool skippedBecauseCached, IReadOnlyCollection<string> errors)
        => new()
        {
            ObjectKey = objectKey,
            ImagesFound = metadata.Count,
            ImagesDownloaded = downloaded,
            SkippedBecauseCached = skippedBecauseCached,
            Errors = errors,
            SelectedPrimaryAsset = SelectPrimary(metadata)?.LocalPath ?? string.Empty
        };

    private static CelestialAssetImageMetadata? SelectPrimary(IEnumerable<CelestialAssetImageMetadata> metadata)
        => metadata.OrderByDescending(x => x.QualityScore).ThenByDescending(x => x.Width * x.Height).FirstOrDefault();

    private void EnsureObjectDirectories()
    {
        Directory.CreateDirectory(GetRootPath());
        foreach (var objectKey in RequiredObjectKeys())
            Directory.CreateDirectory(GetObjectDirectory(objectKey));
    }

    private IEnumerable<string> RequiredObjectKeys() => _assetOptions.RequiredObjects.Select(NormalizeObjectKey).Distinct(StringComparer.OrdinalIgnoreCase);

    private string GetObjectDirectory(string objectKey) => Path.Combine(GetRootPath(), NormalizeObjectKey(objectKey));

    private string GetRootPath() => Path.IsPathRooted(_assetOptions.RootPath) ? _assetOptions.RootPath : _assetPathResolver.GetCelestialRoot();

    private bool IsAllowedLocalImage(string path)
        => _assetOptions.AllowedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private bool IsAllowedImageUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var extension = Path.GetExtension(uri.AbsolutePath);
        return _assetOptions.AllowedExtensions.Any(ext => extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    private string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || !_assetOptions.AllowedExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            return ".jpg";
        return extension.ToLowerInvariant();
    }

    private static string NormalizeObjectKey(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "milky-way" : normalized;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) || !char.IsLetterOrDigit(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }

    private static double ScoreCandidate(string? title, string? description, string url)
    {
        var score = 0.5;
        var text = $"{title} {description} {url}";
        if (text.Contains("hubble", StringComparison.OrdinalIgnoreCase)) score += 0.15;
        if (text.Contains("webb", StringComparison.OrdinalIgnoreCase)) score += 0.15;
        if (text.Contains("planet", StringComparison.OrdinalIgnoreCase)) score += 0.1;
        if (url.Contains("orig", StringComparison.OrdinalIgnoreCase)) score += 0.1;
        if (url.Contains("thumb", StringComparison.OrdinalIgnoreCase)) score -= 0.2;
        return score;
    }

    private sealed record NasaImageCandidate(string Url, string NasaId, string Title, string Description, double QualityScore);

    private sealed class NasaSearchResponse { public NasaCollection? Collection { get; init; } }
    private sealed class NasaCollection { public IReadOnlyCollection<NasaItem>? Items { get; init; } }
    private sealed class NasaItem { public IReadOnlyCollection<NasaData>? Data { get; init; } public IReadOnlyCollection<NasaLink>? Links { get; init; } }
    private sealed class NasaData
    {
        [JsonPropertyName("nasa_id")]
        public string? NasaId { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
    }
    private sealed class NasaLink { public string? Href { get; init; } }
    private sealed class NasaAssetResponse { public NasaAssetCollection? Collection { get; init; } }
    private sealed class NasaAssetCollection { public IReadOnlyCollection<NasaAssetItem>? Items { get; init; } }
    private sealed class NasaAssetItem { public string? Href { get; init; } }
    private sealed class NasaApodPayload
    {
        public string? Title { get; init; }
        public string? Explanation { get; init; }
        public string? Url { get; init; }
        [JsonPropertyName("hdurl")]
        public string? HdUrl { get; init; }
        [JsonPropertyName("media_type")]
        public string? MediaType { get; init; }
    }
}
