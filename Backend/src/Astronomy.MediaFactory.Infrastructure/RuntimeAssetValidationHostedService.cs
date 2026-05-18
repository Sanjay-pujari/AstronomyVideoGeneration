using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure;

public sealed class RuntimeAssetValidationHostedService : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IRuntimeAssetPathResolver _assetPathResolver;
    private readonly ThumbnailFontOptions _fontOptions;
    private readonly ThumbnailOptions _thumbnailOptions;
    private readonly ILogger<RuntimeAssetValidationHostedService> _logger;

    public RuntimeAssetValidationHostedService(
        IRuntimeAssetPathResolver assetPathResolver,
        IOptions<ThumbnailFontOptions> fontOptions,
        IOptions<ThumbnailOptions> thumbnailOptions,
        ILogger<RuntimeAssetValidationHostedService> logger)
    {
        _assetPathResolver = assetPathResolver;
        _fontOptions = fontOptions.Value;
        _thumbnailOptions = thumbnailOptions.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var checkedPaths = new List<string>();
        var missingAssets = new List<string>();

        CheckFile(_fontOptions.DefaultEnglishFont);
        CheckFile(_fontOptions.HindiFont);
        CheckDirectory("assets/celestial");
        CheckDirectory("assets/celestial/jupiter");
        CheckDirectory("assets/celestial/venus");
        CheckDirectory("assets/celestial/moon");

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            appBaseDirectory = _assetPathResolver.BaseDirectory,
            assetsRoot = _assetPathResolver.GetAssetsRoot(),
            fontsRoot = _assetPathResolver.GetFontsRoot(),
            celestialRoot = _assetPathResolver.GetCelestialRoot(),
            missingAssets,
            checkedPaths
        };

        var reportPath = Path.Combine(_assetPathResolver.GetAssetsRoot(), "runtime-assets-validation-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? _assetPathResolver.GetAssetsRoot());
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);

        if (missingAssets.Count > 0)
            _logger.LogWarning("Runtime asset validation found {Count} missing assets. Report: {ReportPath}", missingAssets.Count, reportPath);

        if (_thumbnailOptions.Enabled)
        {
            var missingFonts = new[] { _fontOptions.DefaultEnglishFont, _fontOptions.HindiFont }
                .Select(_assetPathResolver.ResolveFontPath)
                .Where(path => !CanReadFile(path))
                .ToArray();
            if (missingFonts.Length > 0)
                throw new FileNotFoundException("Thumbnail font missing from executable assets folder: " + string.Join(", ", missingFonts), missingFonts[0]);
        }

        void CheckFile(string relativePath)
        {
            var resolved = _assetPathResolver.ResolveFontPath(relativePath);
            checkedPaths.Add(resolved);
            if (!CanReadFile(resolved))
                missingAssets.Add(resolved);
        }

        void CheckDirectory(string relativePath)
        {
            var resolved = _assetPathResolver.ResolveAssetPath(relativePath);
            checkedPaths.Add(resolved);
            if (!Directory.Exists(resolved))
                missingAssets.Add(resolved);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool CanReadFile(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream.CanRead;
        }
        catch
        {
            return false;
        }
    }
}
