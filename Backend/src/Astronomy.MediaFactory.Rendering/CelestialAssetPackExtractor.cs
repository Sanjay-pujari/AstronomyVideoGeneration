using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Astronomy.MediaFactory.Rendering;

public sealed class CelestialAssetPackExtractor : ICelestialAssetPackExtractor
{
    private readonly CelestialAssetPackOptions _options;

    public CelestialAssetPackExtractor(IOptions<CelestialAssetPackOptions> options) => _options = options.Value;

    public async Task<CelestialAssetPackExtractionReport> ExtractAsync(CancellationToken cancellationToken)
    {
        var sourcePath = ResolvePath(_options.SourceSheetPath);
        var outputRoot = ResolvePath(_options.OutputRootPath);
        Directory.CreateDirectory(outputRoot);
        var warnings = new List<string>();
        var extracted = new List<string>();
        var skipped = new List<string>();
        var reportPath = Path.Combine(outputRoot, "asset-pack-extraction-report.json");

        if (!_options.Enabled)
            return await WriteReportAsync(false, sourcePath, outputRoot, extracted, skipped, new[] { "Celestial asset pack extraction is disabled." }, reportPath, cancellationToken);

        if (!File.Exists(sourcePath))
            return await WriteReportAsync(true, sourcePath, outputRoot, extracted, skipped, new[] { $"Source sheet not found: {sourcePath}" }, reportPath, cancellationToken);

        var mapPath = ResolvePath(string.IsNullOrWhiteSpace(_options.SheetMapPath) ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? "", "celestial-object-sheet-map.json") : _options.SheetMapPath);
        if (!File.Exists(mapPath))
            return await WriteReportAsync(true, sourcePath, outputRoot, extracted, skipped, new[] { $"Manual crop map not found: {mapPath}" }, reportPath, cancellationToken);

        var json = await File.ReadAllTextAsync(mapPath, cancellationToken);
        var map = JsonSerializer.Deserialize<Dictionary<string, CelestialAssetTileMapEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, CelestialAssetTileMapEntry>();
        if (map.Count == 0)
            warnings.Add("Crop map was empty; no objects extracted.");

        using var sheet = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        foreach (var (rawKey, tile) in map)
        {
            var key = CelestialObjectKeyMapper.Map(rawKey);
            var outputDirectory = Path.Combine(outputRoot, key);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "hero.png");
            if (File.Exists(outputPath) && !_options.OverwriteExisting)
            {
                skipped.Add(key);
                continue;
            }

            if (tile.Width <= 0 || tile.Height <= 0 || tile.X < 0 || tile.Y < 0 || tile.X + tile.Width > sheet.Width || tile.Y + tile.Height > sheet.Height)
            {
                warnings.Add($"Invalid crop rectangle for {rawKey}.");
                continue;
            }

            using var crop = sheet.Clone(ctx => ctx.Crop(new Rectangle(tile.X, tile.Y, tile.Width, tile.Height)));
            await crop.SaveAsPngAsync(outputPath, cancellationToken);
            extracted.Add(key);
        }

        return await WriteReportAsync(true, sourcePath, outputRoot, extracted.Distinct().ToArray(), skipped.Distinct().ToArray(), warnings, reportPath, cancellationToken);
    }

    private static async Task<CelestialAssetPackExtractionReport> WriteReportAsync(bool enabled, string sourcePath, string outputRoot, IReadOnlyCollection<string> extracted, IReadOnlyCollection<string> skipped, IReadOnlyCollection<string> warnings, string reportPath, CancellationToken cancellationToken)
    {
        var report = new CelestialAssetPackExtractionReport
        {
            Enabled = enabled,
            SourceSheetPath = sourcePath,
            OutputRootPath = outputRoot,
            ExtractedObjects = extracted,
            SkippedObjects = skipped,
            Warnings = warnings,
            ReportPath = reportPath
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return report;
    }

    private static string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
}
