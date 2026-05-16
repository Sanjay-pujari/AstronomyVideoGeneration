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
        var items = new List<CelestialAssetPackExtractionItem>();
        var reportPath = Path.Combine(outputRoot, "asset-pack-extraction-report.json");

        if (!_options.Enabled)
            return await WriteReportAsync(false, sourcePath, outputRoot, items, extracted, skipped, ["Celestial asset pack extraction is disabled."], reportPath, cancellationToken);

        if (!File.Exists(sourcePath))
            return await WriteReportAsync(true, sourcePath, outputRoot, items, extracted, skipped, [$"Source sheet not found: {sourcePath}"], reportPath, cancellationToken);

        var mapPath = ResolvePath(string.IsNullOrWhiteSpace(_options.SheetMapPath) ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? "", "celestial-object-sheet-map.json") : _options.SheetMapPath);
        if (!File.Exists(mapPath))
            return await WriteReportAsync(true, sourcePath, outputRoot, items, extracted, skipped, [$"Manual crop map not found: {mapPath}"], reportPath, cancellationToken);

        var json = await File.ReadAllTextAsync(mapPath, cancellationToken);
        var map = JsonSerializer.Deserialize<Dictionary<string, CelestialAssetTileMapEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, CelestialAssetTileMapEntry>();
        if (map.Count == 0)
            warnings.Add("Crop map was empty; no objects extracted.");

        using var sheet = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
        foreach (var (rawKey, tile) in map)
        {
            var target = ResolveTarget(rawKey);
            var outputDirectory = Path.Combine(outputRoot, target.ObjectKey);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, target.FileName);

            if (tile.Width <= 0 || tile.Height <= 0 || tile.X < 0 || tile.Y < 0 || tile.X + tile.Width > sheet.Width || tile.Y + tile.Height > sheet.Height)
            {
                var warning = $"Invalid crop rectangle for {rawKey}.";
                warnings.Add(warning);
                items.Add(CreateItem(target.ObjectKey, outputPath, sourcePath, tile, false, warning));
                continue;
            }

            if (File.Exists(outputPath) && !_options.OverwriteExisting)
            {
                skipped.Add($"{target.ObjectKey}/{target.FileName}");
                items.Add(CreateItem(target.ObjectKey, outputPath, sourcePath, tile, true, "Skipped because output already exists."));
                continue;
            }

            using var crop = sheet.Clone(ctx => ctx.Crop(new Rectangle(tile.X, tile.Y, tile.Width, tile.Height)));
            TrimTransparentBounds(crop);
            await crop.SaveAsPngAsync(outputPath, cancellationToken);
            extracted.Add($"{target.ObjectKey}/{target.FileName}");
            items.Add(CreateItem(target.ObjectKey, outputPath, sourcePath, tile, true, string.Empty));
        }

        return await WriteReportAsync(true, sourcePath, outputRoot, items, extracted.Distinct().ToArray(), skipped.Distinct().ToArray(), warnings, reportPath, cancellationToken);
    }

    private static (string ObjectKey, string FileName) ResolveTarget(string rawKey)
    {
        var normalized = CelestialObjectKeyMapper.Map(rawKey);
        var lower = rawKey.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        var fileName = lower switch
        {
            "moon-full" or "full-moon" or "moon/full" or "full" => "full.png",
            "moon-gibbous" or "gibbous-moon" or "moon/gibbous" or "gibbous" => "gibbous.png",
            "moon-crescent" or "crescent-moon" or "moon/crescent" or "crescent" => "crescent.png",
            _ => "hero.png"
        };

        var objectKey = fileName == "hero.png" ? normalized : "moon";
        return (objectKey, fileName);
    }

    private static CelestialAssetPackExtractionItem CreateItem(string objectKey, string outputPath, string sourcePath, CelestialAssetTileMapEntry cropBox, bool success, string warning) => new()
    {
        ObjectKey = objectKey,
        OutputPath = outputPath,
        SourceSheetPath = sourcePath,
        CropBox = cropBox,
        Success = success,
        Warning = warning
    };

    private static void TrimTransparentBounds(Image<Rgba32> image)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A <= 8)
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        if (maxX < minX || maxY < minY)
            return;

        var padding = 4;
        var cropX = Math.Max(0, minX - padding);
        var cropY = Math.Max(0, minY - padding);
        var cropWidth = Math.Min(image.Width - cropX, maxX - minX + 1 + padding * 2);
        var cropHeight = Math.Min(image.Height - cropY, maxY - minY + 1 + padding * 2);
        if (cropWidth < image.Width || cropHeight < image.Height)
            image.Mutate(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight)));
    }

    private static async Task<CelestialAssetPackExtractionReport> WriteReportAsync(bool enabled, string sourcePath, string outputRoot, IReadOnlyCollection<CelestialAssetPackExtractionItem> items, IReadOnlyCollection<string> extracted, IReadOnlyCollection<string> skipped, IReadOnlyCollection<string> warnings, string reportPath, CancellationToken cancellationToken)
    {
        var report = new CelestialAssetPackExtractionReport
        {
            Enabled = enabled,
            SourceSheetPath = sourcePath,
            OutputRootPath = outputRoot,
            Objects = items,
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
