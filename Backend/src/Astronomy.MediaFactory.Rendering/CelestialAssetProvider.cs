using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Rendering;

public sealed class CelestialAssetProvider : ICelestialAssetProvider
{
    private readonly ICelestialAssetIngestionService _ingestionService;
    private readonly CelestialAssetsOptions _options;
    private readonly ILogger<CelestialAssetProvider> _logger;
    private readonly IRuntimeAssetPathResolver _assetPathResolver;

    public CelestialAssetProvider(
        ICelestialAssetIngestionService ingestionService,
        IOptions<CelestialAssetsOptions> options,
        ILogger<CelestialAssetProvider> logger,
        IRuntimeAssetPathResolver? assetPathResolver = null)
    {
        _ingestionService = ingestionService;
        _options = options.Value;
        _logger = logger;
        _assetPathResolver = assetPathResolver ?? new RuntimeAssetPathResolver();
    }

    public async Task<CelestialAsset> GetAssetAsync(CelestialAssetRequest request, CancellationToken cancellationToken)
    {
        var objectKey = ResolveCategory(request.ObjectName, request.ObjectType);
        var status = await _ingestionService.GetObjectAsync(objectKey, cancellationToken);
        var primary = status?.Images.OrderByDescending(x => x.QualityScore).FirstOrDefault();
        if (primary is not null && File.Exists(primary.LocalPath) && !request.RefreshCache)
            return ToAsset(request, objectKey, primary, fallbackUsed: false);

        if (_options.Enabled && _options.DownloadIfMissing)
        {
            try
            {
                await _ingestionService.RefreshObjectAsync(objectKey, cancellationToken);
                status = await _ingestionService.GetObjectAsync(objectKey, cancellationToken);
                primary = status?.Images.OrderByDescending(x => x.QualityScore).FirstOrDefault(x => File.Exists(x.LocalPath));
                if (primary is not null)
                    return ToAsset(request, objectKey, primary, fallbackUsed: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "NASA celestial asset refresh failed for {ObjectName}. Falling back to Stellarium/procedural frame.", request.ObjectName);
            }
        }

        return await CreateLocalFallbackAssetAsync(request, objectKey, cancellationToken);
    }

    private CelestialAsset ToAsset(CelestialAssetRequest request, string objectKey, CelestialAssetImageMetadata metadata, bool fallbackUsed)
        => new()
        {
            ObjectName = request.ObjectName,
            ObjectType = request.ObjectType,
            Category = objectKey,
            LocalPath = metadata.LocalPath,
            Source = metadata.Source,
            Title = metadata.Title,
            Copyright = metadata.LicenseNote,
            OriginalUrl = metadata.OriginalUrl,
            FallbackUsed = fallbackUsed,
            BaseDirectory = _assetPathResolver.BaseDirectory
        };

    private async Task<CelestialAsset> CreateLocalFallbackAssetAsync(CelestialAssetRequest request, string objectKey, CancellationToken cancellationToken)
    {
        var root = Path.IsPathRooted(_options.RootPath) ? _options.RootPath : _assetPathResolver.GetCelestialRoot();
        var directory = Path.Combine(root, objectKey);
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, $"{Sanitize(request.ObjectName)}-fallback.jpg");
        if (!File.Exists(imagePath))
            await ProceduralCelestialFallback.CreateAsync(imagePath, request.ObjectName, objectKey, cancellationToken);

        return new CelestialAsset
        {
            ObjectName = request.ObjectName,
            ObjectType = request.ObjectType,
            Category = objectKey,
            LocalPath = imagePath,
            Source = "StellariumFrameFallback",
            Title = $"Fallback frame for {request.ObjectName}",
            OriginalUrl = string.Empty,
            FallbackUsed = true,
            BaseDirectory = _assetPathResolver.BaseDirectory
        };
    }

    private static string ResolveCategory(string objectName, string objectType)
    {
        var value = $"{objectName} {objectType}".ToLowerInvariant();
        if (value.Contains("meteor")) return "meteor-shower";
        if (value.Contains("solar") && value.Contains("eclipse")) return "solar-eclipse";
        if (value.Contains("lunar") && value.Contains("eclipse")) return "lunar-eclipse";
        if (value.Contains("eclipse")) return value.Contains("moon") || value.Contains("lunar") ? "lunar-eclipse" : "solar-eclipse";
        foreach (var planet in new[] { "jupiter", "saturn", "mars", "venus", "moon", "mercury", "uranus", "neptune" })
        {
            if (value.Contains(planet)) return planet;
        }
        if (value.Contains("orion") && value.Contains("nebula")) return "orion-nebula";
        if (value.Contains("ring") && value.Contains("nebula")) return "ring-nebula";
        if (value.Contains("nebula")) return "orion-nebula";
        if (value.Contains("andromeda") || value.Contains("galaxy")) return "andromeda-galaxy";
        return "milky-way";
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "celestial-object" : cleaned;
    }
}
