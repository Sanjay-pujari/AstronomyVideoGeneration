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
    private const byte TransparentAlphaThreshold = 8;
    private const int BackgroundBlackThreshold = 18;
    private const int ForegroundBlackThreshold = 30;
    private const int ObjectPaddingPixels = 6;

    private readonly CelestialAssetPackOptions _options;
    private readonly IRuntimeAssetPathResolver _assetPathResolver;

    public CelestialAssetPackExtractor(IOptions<CelestialAssetPackOptions> options, IRuntimeAssetPathResolver? assetPathResolver = null)
    {
        _options = options.Value;
        _assetPathResolver = assetPathResolver ?? new RuntimeAssetPathResolver();
    }

    public async Task<CelestialAssetPackExtractionReport> ExtractAsync(CancellationToken cancellationToken)
    {
        var sourcePath = ResolvePath(_options.SourceSheetPath);
        var outputRoot = string.IsNullOrWhiteSpace(_options.OutputRootPath) ? _assetPathResolver.GetCelestialRoot() : ResolvePath(_options.OutputRootPath);
        Directory.CreateDirectory(outputRoot);
        var warnings = new List<string>();
        var extracted = new List<string>();
        var skipped = new List<string>();
        var items = new List<CelestialAssetPackExtractionItem>();
        var reportPath = Path.Combine(outputRoot, "asset-pack-extraction-report.json");
        var mapPath = ResolvePath(string.IsNullOrWhiteSpace(_options.SheetMapPath) ? "assets/celestial/source/celestial-object-sheet-map.json" : _options.SheetMapPath);

        if (!_options.Enabled)
            return await WriteReportAsync(false, sourcePath, mapPath, outputRoot, items, extracted, skipped, ["Celestial asset pack extraction is disabled."], reportPath, cancellationToken);

        if (!File.Exists(sourcePath))
            return await WriteReportAsync(true, sourcePath, mapPath, outputRoot, items, extracted, skipped, [$"Source sheet not found: {sourcePath}"], reportPath, cancellationToken);

        if (!File.Exists(mapPath))
            return await WriteReportAsync(true, sourcePath, mapPath, outputRoot, items, extracted, skipped, [$"Manual crop map not found: {mapPath}"], reportPath, cancellationToken);

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
            var transparentPath = Path.Combine(outputDirectory, target.TransparentFileName);
            var fallbackPath = Path.Combine(outputDirectory, target.FallbackFileName);

            if (tile.Width <= 0 || tile.Height <= 0 || tile.X < 0 || tile.Y < 0 || tile.X + tile.Width > sheet.Width || tile.Y + tile.Height > sheet.Height)
            {
                var warning = $"Invalid crop rectangle for {rawKey}.";
                warnings.Add(warning);
                items.Add(CreateItem(target.ObjectKey, fallbackPath, transparentPath, sourcePath, tile, false, warning));
                continue;
            }

            var transparentExists = File.Exists(transparentPath);
            var fallbackExists = File.Exists(fallbackPath);
            if (transparentExists && fallbackExists && !_options.OverwriteExisting)
            {
                skipped.Add($"{target.ObjectKey}/{target.TransparentFileName}");
                skipped.Add($"{target.ObjectKey}/{target.FallbackFileName}");
                var warning = "Skipped because output already exists; alpha pixel count unavailable without re-extraction.";
                warnings.Add(warning);
                var dimensions = await ReadImageDimensionsAsync(transparentPath, cancellationToken);
                items.Add(CreateItem(
                    target.ObjectKey,
                    fallbackPath,
                    transparentPath,
                    sourcePath,
                    tile,
                    true,
                    warning,
                    transparencyApplied: true,
                    alphaPixelsRemoved: 0,
                    autoTrimApplied: false,
                    finalWidth: dimensions.Width,
                    finalHeight: dimensions.Height,
                    backgroundRemoved: true,
                    labelRemovalApplied: false,
                    borderRemovalApplied: false));
                continue;
            }

            using var mappedTile = sheet.Clone(ctx => ctx.Crop(new Rectangle(tile.X, tile.Y, tile.Width, tile.Height)));
            var objectBounds = DetectDominantObjectBounds(mappedTile) ?? DetectNonBlackBounds(mappedTile) ?? new Rectangle(0, 0, mappedTile.Width, mappedTile.Height);
            var autoTrimApplied = objectBounds.X > 0 || objectBounds.Y > 0 || objectBounds.Width < mappedTile.Width || objectBounds.Height < mappedTile.Height;
            var labelRemovalApplied = objectBounds.Bottom < mappedTile.Height;
            var borderRemovalApplied = autoTrimApplied;
            using var objectCrop = mappedTile.Clone(ctx => ctx.Crop(objectBounds));
            var extractionStats = ApplyTransparentBlackBackground(objectCrop);
            var finalTrimApplied = TrimTransparentBounds(objectCrop);
            autoTrimApplied = autoTrimApplied || finalTrimApplied;

            if (!transparentExists || _options.OverwriteExisting)
                await objectCrop.SaveAsPngAsync(transparentPath, cancellationToken);

            if (!fallbackExists || _options.OverwriteExisting)
            {
                using var fallback = objectCrop.Clone();
                FlattenTransparencyToBlack(fallback);
                await fallback.SaveAsPngAsync(fallbackPath, cancellationToken);
            }

            extracted.Add($"{target.ObjectKey}/{target.TransparentFileName}");
            extracted.Add($"{target.ObjectKey}/{target.FallbackFileName}");
            items.Add(CreateItem(
                target.ObjectKey,
                fallbackPath,
                transparentPath,
                sourcePath,
                tile,
                true,
                null,
                transparencyApplied: true,
                alphaPixelsRemoved: extractionStats.AlphaPixelsRemoved,
                autoTrimApplied: autoTrimApplied,
                finalWidth: objectCrop.Width,
                finalHeight: objectCrop.Height,
                backgroundRemoved: extractionStats.BackgroundRemoved,
                labelRemovalApplied: labelRemovalApplied,
                borderRemovalApplied: borderRemovalApplied));
        }

        return await WriteReportAsync(true, sourcePath, mapPath, outputRoot, items, extracted.Distinct().ToArray(), skipped.Distinct().ToArray(), warnings.Distinct().ToArray(), reportPath, cancellationToken);
    }

    private static (string ObjectKey, string TransparentFileName, string FallbackFileName) ResolveTarget(string rawKey)
    {
        var normalized = CelestialObjectKeyMapper.Map(rawKey);
        var lower = rawKey.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        var fileName = lower switch
        {
            "moon-full" or "full-moon" or "moon/full" or "full" => "full",
            "moon-gibbous" or "gibbous-moon" or "moon/gibbous" or "gibbous" => "gibbous",
            "moon-crescent" or "crescent-moon" or "moon/crescent" or "crescent" => "crescent",
            _ => "hero"
        };

        var objectKey = fileName == "hero" ? normalized : "moon";
        return (objectKey, $"{fileName}-transparent.png", $"{fileName}.png");
    }

    private static CelestialAssetPackExtractionItem CreateItem(
        string objectKey,
        string outputPath,
        string transparentOutputPath,
        string sourcePath,
        CelestialAssetTileMapEntry cropBox,
        bool success,
        string? warning,
        bool transparencyApplied = false,
        int alphaPixelsRemoved = 0,
        bool autoTrimApplied = false,
        int finalWidth = 0,
        int finalHeight = 0,
        bool backgroundRemoved = false,
        bool labelRemovalApplied = false,
        bool borderRemovalApplied = false) => new()
    {
        ObjectKey = objectKey,
        SourceSheetPath = sourcePath,
        CropBox = cropBox,
        OutputPath = outputPath,
        TransparentOutputPath = transparentOutputPath,
        Success = success,
        Warning = warning,
        TransparencyApplied = transparencyApplied,
        BackgroundRemoved = backgroundRemoved,
        AlphaPixelsRemoved = alphaPixelsRemoved,
        AutoTrimApplied = autoTrimApplied,
        FinalDimensions = new CelestialAssetPackImageDimensions { Width = finalWidth, Height = finalHeight },
        LabelRemovalApplied = labelRemovalApplied,
        BorderRemovalApplied = borderRemovalApplied
    };

    private static Rectangle? DetectDominantObjectBounds(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;
        var visited = new bool[width * height];
        Component? best = null;
        var queue = new Queue<Point>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (visited[index])
                    continue;

                visited[index] = true;
                if (!IsObjectCandidate(image[x, y]))
                    continue;

                var component = new Component(x, y);
                queue.Enqueue(new Point(x, y));
                while (queue.Count > 0)
                {
                    var point = queue.Dequeue();
                    component.Include(point.X, point.Y);
                    Enqueue(point.X + 1, point.Y);
                    Enqueue(point.X - 1, point.Y);
                    Enqueue(point.X, point.Y + 1);
                    Enqueue(point.X, point.Y - 1);
                }

                if (!component.IsUsable(width, height))
                    continue;

                if (best is null || component.Score(width, height) > best.Score(width, height))
                    best = component;

                void Enqueue(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        return;

                    var nextIndex = ny * width + nx;
                    if (visited[nextIndex])
                        return;

                    visited[nextIndex] = true;
                    if (IsObjectCandidate(image[nx, ny]))
                        queue.Enqueue(new Point(nx, ny));
                }
            }
        }

        return best?.ToPaddedRectangle(width, height, ObjectPaddingPixels);
    }

    private static Rectangle? DetectNonBlackBounds(Image<Rgba32> image)
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
                    if (!IsObjectCandidate(row[x]))
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        if (maxX < minX || maxY < minY)
            return null;

        var cropX = Math.Max(0, minX - ObjectPaddingPixels);
        var cropY = Math.Max(0, minY - ObjectPaddingPixels);
        var cropRight = Math.Min(image.Width - 1, maxX + ObjectPaddingPixels);
        var cropBottom = Math.Min(image.Height - 1, maxY + ObjectPaddingPixels);
        return new Rectangle(cropX, cropY, cropRight - cropX + 1, cropBottom - cropY + 1);
    }

    private static ExtractionStats ApplyTransparentBlackBackground(Image<Rgba32> image)
    {
        var removed = 0;
        var backgroundRemoved = false;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A <= TransparentAlphaThreshold)
                        continue;

                    var blackDistance = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                    if (blackDistance <= BackgroundBlackThreshold)
                    {
                        row[x] = new Rgba32(pixel.R, pixel.G, pixel.B, 0);
                        removed++;
                        backgroundRemoved = true;
                        continue;
                    }

                    if (blackDistance < ForegroundBlackThreshold)
                    {
                        var alphaScale = (blackDistance - BackgroundBlackThreshold) / (double)(ForegroundBlackThreshold - BackgroundBlackThreshold);
                        var alpha = (byte)Math.Clamp((int)Math.Round(pixel.A * alphaScale), 0, 255);
                        if (alpha < pixel.A)
                        {
                            row[x] = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                            removed++;
                            backgroundRemoved = true;
                        }
                    }
                }
            }
        });

        return new ExtractionStats(backgroundRemoved, removed);
    }

    private static void FlattenTransparencyToBlack(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A == 255)
                        continue;

                    var alpha = pixel.A / 255f;
                    row[x] = new Rgba32((byte)Math.Round(pixel.R * alpha), (byte)Math.Round(pixel.G * alpha), (byte)Math.Round(pixel.B * alpha), 255);
                }
            }
        });
    }

    private static bool TrimTransparentBounds(Image<Rgba32> image)
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
                    if (row[x].A <= TransparentAlphaThreshold)
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        if (maxX < minX || maxY < minY)
            return false;

        var padding = 2;
        var cropX = Math.Max(0, minX - padding);
        var cropY = Math.Max(0, minY - padding);
        var cropWidth = Math.Min(image.Width - cropX, maxX - minX + 1 + padding * 2);
        var cropHeight = Math.Min(image.Height - cropY, maxY - minY + 1 + padding * 2);
        if (cropWidth >= image.Width && cropHeight >= image.Height)
            return false;

        image.Mutate(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight)));
        return true;
    }

    private static bool IsObjectCandidate(Rgba32 pixel)
    {
        if (pixel.A <= TransparentAlphaThreshold)
            return false;

        var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
        var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
        return max > ForegroundBlackThreshold || max - min > 20;
    }

    private async Task<CelestialAssetPackExtractionReport> WriteReportAsync(bool enabled, string sourcePath, string sourceMapPath, string outputRoot, IReadOnlyCollection<CelestialAssetPackExtractionItem> items, IReadOnlyCollection<string> extracted, IReadOnlyCollection<string> skipped, IReadOnlyCollection<string> warnings, string reportPath, CancellationToken cancellationToken)
    {
        var successCount = items.Count(item => item.Success);
        var report = new CelestialAssetPackExtractionReport
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            SourceSheetPath = sourcePath,
            SourceMapPath = sourceMapPath,
            ObjectsProcessed = items.Count,
            SuccessCount = successCount,
            FailureCount = items.Count - successCount,
            TransparentAssetsGenerated = items.Count(item => item.Success && !string.IsNullOrWhiteSpace(item.TransparentOutputPath)),
            Items = items,
            Enabled = enabled,
            OutputRootPath = outputRoot,
            BaseDirectory = _assetPathResolver.BaseDirectory,
            ExtractedObjects = extracted,
            SkippedObjects = skipped,
            Warnings = warnings,
            ReportPath = reportPath
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken);
        return report;
    }

    private static async Task<CelestialAssetPackImageDimensions> ReadImageDimensionsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = await Image.IdentifyAsync(path, cancellationToken);
            return info is null
                ? new CelestialAssetPackImageDimensions()
                : new CelestialAssetPackImageDimensions { Width = info.Width, Height = info.Height };
        }
        catch
        {
            return new CelestialAssetPackImageDimensions();
        }
    }

    private string ResolvePath(string path) => Path.IsPathRooted(path) ? path : _assetPathResolver.ResolveAssetPath(path);

    private sealed class Component
    {
        public Component(int x, int y)
        {
            MinX = MaxX = x;
            MinY = MaxY = y;
        }

        public int MinX { get; private set; }
        public int MinY { get; private set; }
        public int MaxX { get; private set; }
        public int MaxY { get; private set; }
        public int Area { get; private set; }

        public void Include(int x, int y)
        {
            Area++;
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }

        public bool IsUsable(int imageWidth, int imageHeight)
        {
            var width = MaxX - MinX + 1;
            var height = MaxY - MinY + 1;
            if (Area < Math.Max(16, imageWidth * imageHeight / 2500) || width < 8 || height < 8)
                return false;

            var density = Area / (double)(width * height);
            var touchesMostEdges = MinX <= 2 && MinY <= 2 && MaxX >= imageWidth - 3 && MaxY >= imageHeight - 3;
            if (touchesMostEdges && density < 0.18)
                return false;

            var centerY = (MinY + MaxY) / 2d;
            var looksLikeHorizontalText = width > height * 2.8 && height < imageHeight * 0.22 && centerY > imageHeight * 0.62;
            return !looksLikeHorizontalText;
        }

        public double Score(int imageWidth, int imageHeight)
        {
            var centerX = (MinX + MaxX) / 2d;
            var centerY = (MinY + MaxY) / 2d;
            var normalizedDistance = Math.Sqrt(Math.Pow((centerX - imageWidth / 2d) / imageWidth, 2) + Math.Pow((centerY - imageHeight / 2d) / imageHeight, 2));
            return Area * (1.25 - Math.Min(0.75, normalizedDistance));
        }

        public Rectangle ToPaddedRectangle(int imageWidth, int imageHeight, int padding)
        {
            var x = Math.Max(0, MinX - padding);
            var y = Math.Max(0, MinY - padding);
            var right = Math.Min(imageWidth - 1, MaxX + padding);
            var bottom = Math.Min(imageHeight - 1, MaxY + padding);
            return new Rectangle(x, y, right - x + 1, bottom - y + 1);
        }
    }

    private sealed record ExtractionStats(bool BackgroundRemoved, int AlphaPixelsRemoved);
}
