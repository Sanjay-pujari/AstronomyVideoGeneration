using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class CelestialAssetProvider : ICelestialAssetProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] RequiredCategories =
    [
        "jupiter", "saturn", "mars", "venus", "moon", "mercury", "uranus", "neptune", "meteor-showers", "solar-eclipse", "lunar-eclipse", "nebula", "galaxy", "milky-way", "constellations"
    ];

    private readonly HttpClient _httpClient;
    private readonly AstronomyApiOptions _apiOptions;
    private readonly ILogger<CelestialAssetProvider> _logger;

    public CelestialAssetProvider(HttpClient httpClient, IOptions<AstronomyApiOptions> apiOptions, ILogger<CelestialAssetProvider> logger)
    {
        _httpClient = httpClient;
        _apiOptions = apiOptions.Value;
        _logger = logger;
    }

    public async Task<CelestialAsset> GetAssetAsync(CelestialAssetRequest request, CancellationToken cancellationToken)
    {
        EnsureAssetDirectories();
        var category = ResolveCategory(request.ObjectName, request.ObjectType);
        var directory = GetCategoryDirectory(category);
        var cached = FindCachedAsset(directory, request.ObjectName, request.ObjectType, category);
        if (cached is not null && !request.RefreshCache)
            return cached;

        try
        {
            var downloaded = await TryDownloadNasaAssetAsync(request, category, directory, cancellationToken);
            if (downloaded is not null)
                return downloaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NASA asset ingestion failed for {ObjectName}. Using cache/local fallback.", request.ObjectName);
        }

        cached = FindCachedAsset(directory, request.ObjectName, request.ObjectType, category);
        if (cached is not null)
            return new CelestialAsset
            {
                ObjectName = cached.ObjectName,
                ObjectType = cached.ObjectType,
                Category = cached.Category,
                LocalPath = cached.LocalPath,
                Source = cached.Source,
                Title = cached.Title,
                Copyright = cached.Copyright,
                OriginalUrl = cached.OriginalUrl,
                FallbackUsed = true
            };

        return await CreateLocalFallbackAssetAsync(request, category, directory, cancellationToken);
    }

    private async Task<CelestialAsset?> TryDownloadNasaAssetAsync(CelestialAssetRequest request, string category, string directory, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(BuildNasaQuery(request.ObjectName, request.ObjectType, category));
        var imageSearchBaseUrl = ResolveNasaImageSearchBaseUrl(_apiOptions.NasaBaseUrl);
        using var search = await _httpClient.GetAsync($"{imageSearchBaseUrl}/search?q={query}&media_type=image", cancellationToken);
        if (!search.IsSuccessStatusCode)
        {
            _logger.LogWarning("NASA image search returned {StatusCode} for {ObjectName}.", (int)search.StatusCode, request.ObjectName);
            return null;
        }

        var payload = await search.Content.ReadFromJsonAsync<NasaImageSearchResponse>(cancellationToken: cancellationToken);
        var item = payload?.Collection?.Items?.FirstOrDefault(x => x.Links?.Any(l => IsImageHref(l.Href)) == true);
        var link = item?.Links?.FirstOrDefault(l => IsImageHref(l.Href));
        if (item is null || link?.Href is null)
            return null;

        var originalUrl = link.Href;
        var extension = Path.GetExtension(new Uri(originalUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
            extension = ".jpg";

        var fileName = $"{Sanitize(request.ObjectName)}-nasa{extension.ToLowerInvariant()}";
        var imagePath = Path.Combine(directory, fileName);
        if (request.RefreshCache || !File.Exists(imagePath))
        {
            using var image = await _httpClient.GetStreamAsync(originalUrl, cancellationToken);
            await using var output = File.Create(imagePath);
            await image.CopyToAsync(output, cancellationToken);
        }

        var data = item.Data?.FirstOrDefault();
        var metadata = new CelestialAssetMetadata
        {
            Source = "NASA",
            Copyright = data?.SecondaryCreator ?? data?.Center ?? string.Empty,
            Title = data?.Title ?? request.ObjectName,
            ObjectType = request.ObjectType,
            DownloadedAtUtc = DateTimeOffset.UtcNow,
            OriginalUrl = originalUrl
        };
        await File.WriteAllTextAsync(Path.ChangeExtension(imagePath, ".metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);

        return new CelestialAsset
        {
            ObjectName = request.ObjectName,
            ObjectType = request.ObjectType,
            Category = category,
            LocalPath = imagePath,
            Source = metadata.Source,
            Title = metadata.Title,
            Copyright = metadata.Copyright,
            OriginalUrl = metadata.OriginalUrl
        };
    }

    private static CelestialAsset? FindCachedAsset(string directory, string objectName, string objectType, string category)
    {
        var image = Directory.EnumerateFiles(directory)
            .Where(IsSupportedImage)
            .OrderByDescending(x => Path.GetFileName(x).Contains("portrait", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (image is null)
            return null;

        var metadataPath = Path.ChangeExtension(image, ".metadata.json");
        CelestialAssetMetadata? metadata = null;
        if (File.Exists(metadataPath))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<CelestialAssetMetadata>(File.ReadAllText(metadataPath));
            }
            catch
            {
                metadata = null;
            }
        }

        return new CelestialAsset
        {
            ObjectName = objectName,
            ObjectType = objectType,
            Category = category,
            LocalPath = image,
            Source = metadata?.Source ?? "LocalCache",
            Title = metadata?.Title ?? Path.GetFileNameWithoutExtension(image),
            Copyright = metadata?.Copyright ?? string.Empty,
            OriginalUrl = metadata?.OriginalUrl ?? string.Empty
        };
    }

    private static async Task<CelestialAsset> CreateLocalFallbackAssetAsync(CelestialAssetRequest request, string category, string directory, CancellationToken cancellationToken)
    {
        var imagePath = Path.Combine(directory, $"{Sanitize(request.ObjectName)}-fallback.jpg");
        if (!File.Exists(imagePath))
            await ProceduralCelestialFallback.CreateAsync(imagePath, request.ObjectName, category, cancellationToken);

        var metadata = new CelestialAssetMetadata
        {
            Source = "LocalFallback",
            Title = $"Deterministic fallback for {request.ObjectName}",
            ObjectType = request.ObjectType,
            DownloadedAtUtc = DateTimeOffset.UtcNow,
            OriginalUrl = string.Empty
        };
        await File.WriteAllTextAsync(Path.ChangeExtension(imagePath, ".metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);

        return new CelestialAsset
        {
            ObjectName = request.ObjectName,
            ObjectType = request.ObjectType,
            Category = category,
            LocalPath = imagePath,
            Source = metadata.Source,
            Title = metadata.Title,
            OriginalUrl = metadata.OriginalUrl,
            FallbackUsed = true
        };
    }

    private static void EnsureAssetDirectories()
    {
        foreach (var category in RequiredCategories)
            Directory.CreateDirectory(GetCategoryDirectory(category));
    }

    private static string GetCategoryDirectory(string category)
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "assets", "celestial");
        return Path.Combine(root, category);
    }

    private static string ResolveNasaImageSearchBaseUrl(string configuredBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl) && configuredBaseUrl.Contains("images-api.nasa.gov", StringComparison.OrdinalIgnoreCase))
            return configuredBaseUrl.TrimEnd('/');
        return "https://images-api.nasa.gov";
    }

    private static string ResolveCategory(string objectName, string objectType)
    {
        var value = $"{objectName} {objectType}".ToLowerInvariant();
        if (value.Contains("meteor")) return "meteor-showers";
        if (value.Contains("solar") && value.Contains("eclipse")) return "solar-eclipse";
        if (value.Contains("lunar") && value.Contains("eclipse")) return "lunar-eclipse";
        if (value.Contains("eclipse")) return value.Contains("moon") || value.Contains("lunar") ? "lunar-eclipse" : "solar-eclipse";
        foreach (var planet in new[] { "jupiter", "saturn", "mars", "venus", "moon", "mercury", "uranus", "neptune" })
        {
            if (value.Contains(planet)) return planet;
        }
        if (value.Contains("nebula")) return "nebula";
        if (value.Contains("galaxy") || value.Contains("andromeda")) return "galaxy";
        if (value.Contains("constellation") || value.Contains("orion")) return "constellations";
        return "milky-way";
    }

    private static string BuildNasaQuery(string objectName, string objectType, string category)
    {
        if (category == "meteor-showers") return "meteor shower night sky";
        if (category.EndsWith("eclipse", StringComparison.OrdinalIgnoreCase)) return category.Replace('-', ' ');
        if (category == "milky-way") return "Milky Way starfield";
        if (category == "constellations") return $"{objectName} constellation";
        return $"{objectName} {objectType} NASA";
    }

    private static bool IsImageHref(string? href)
        => !string.IsNullOrWhiteSpace(href) && (href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || href.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || href.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedImage(string path)
        => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "celestial-object" : cleaned;
    }

    private sealed class CelestialAssetMetadata
    {
        [JsonPropertyName("source")]
        public string Source { get; init; } = "NASA";
        [JsonPropertyName("copyright")]
        public string Copyright { get; init; } = "";
        [JsonPropertyName("title")]
        public string Title { get; init; } = "";
        [JsonPropertyName("objectType")]
        public string ObjectType { get; init; } = "";
        [JsonPropertyName("downloadedAtUtc")]
        public DateTimeOffset DownloadedAtUtc { get; init; }
        [JsonPropertyName("originalUrl")]
        public string OriginalUrl { get; init; } = "";
    }

    private sealed class NasaImageSearchResponse { public NasaImageCollection? Collection { get; init; } }
    private sealed class NasaImageCollection { public IReadOnlyCollection<NasaImageItem>? Items { get; init; } }
    private sealed class NasaImageItem { public IReadOnlyCollection<NasaImageData>? Data { get; init; } public IReadOnlyCollection<NasaImageLink>? Links { get; init; } }
    private sealed class NasaImageData { public string? Title { get; init; } public string? Center { get; init; } public string? SecondaryCreator { get; init; } }
    private sealed class NasaImageLink { public string? Href { get; init; } }
}
