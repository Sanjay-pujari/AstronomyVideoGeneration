using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CelestialAssetPackExtractorTests
{
    [Fact]
    public async Task ExtractAsync_GeneratesTransparentObjectCutoutsAndFallbackAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"asset-pack-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceDir);
        var sheetPath = Path.Combine(sourceDir, "celestial-object-sheet.png");
        var mapPath = Path.Combine(sourceDir, "celestial-object-sheet-map.json");
        using (var sheet = new Image<Rgba32>(300, 160, Color.Black))
        {
            DrawSyntheticInfographicTile(sheet, new Rectangle(10, 10, 120, 120), Color.Orange);
            DrawSyntheticInfographicTile(sheet, new Rectangle(150, 10, 90, 90), Color.White);
            await sheet.SaveAsPngAsync(sheetPath);
        }

        await File.WriteAllTextAsync(mapPath, """
        {
          "jupiter": { "x": 10, "y": 10, "width": 120, "height": 120 },
          "moon-full": { "x": 150, "y": 10, "width": 90, "height": 90 }
        }
        """);

        var extractor = new CelestialAssetPackExtractor(Options.Create(new CelestialAssetPackOptions
        {
            SourceSheetPath = sheetPath,
            OutputRootPath = root,
            SheetMapPath = mapPath,
            OverwriteExisting = true
        }));

        var report = await extractor.ExtractAsync(CancellationToken.None);

        var transparentPath = Path.Combine(root, "jupiter", "hero-transparent.png");
        var fallbackPath = Path.Combine(root, "jupiter", "hero.png");
        Assert.Contains("jupiter/hero-transparent.png", report.ExtractedObjects);
        Assert.Contains("jupiter/hero.png", report.ExtractedObjects);
        Assert.Contains("moon/full-transparent.png", report.ExtractedObjects);
        Assert.Contains("moon/full.png", report.ExtractedObjects);
        Assert.True(File.Exists(transparentPath));
        Assert.True(File.Exists(fallbackPath));
        Assert.True(File.Exists(Path.Combine(root, "moon", "full-transparent.png")));

        using var transparent = await Image.LoadAsync<Rgba32>(transparentPath);
        Assert.True(transparent.Width < 120);
        Assert.True(transparent.Height < 120);
        Assert.Equal(0, transparent[0, 0].A);
        Assert.True(transparent[transparent.Width / 2, transparent.Height / 2].A > 200);
        Assert.True(transparent.Height < 80);

        var item = Assert.Single(report.Objects.Where(o => o.ObjectKey == "jupiter"));
        Assert.True(item.TransparencyApplied);
        Assert.True(item.BackgroundRemoved);
        Assert.True(item.AlphaPixelsRemoved > 0);
        Assert.True(item.AutoTrimApplied);
        Assert.Equal(transparent.Width, item.FinalDimensions.Width);
        Assert.Equal(transparent.Height, item.FinalDimensions.Height);

        var reportJson = await File.ReadAllTextAsync(Path.Combine(root, "asset-pack-extraction-report.json"));
        Assert.Contains("transparencyApplied", reportJson);
        Assert.Contains("alphaPixelsRemoved", reportJson);
        Assert.Contains("autoTrimApplied", reportJson);
        Assert.Contains("finalDimensions", reportJson);
        Assert.Contains("backgroundRemoved", reportJson);
    }

    private static void DrawSyntheticInfographicTile(Image<Rgba32> sheet, Rectangle tile, Color objectColor)
    {
        sheet.Mutate(ctx =>
        {
            ctx.Fill(Color.Black, tile);
            ctx.Fill(objectColor, new SixLabors.ImageSharp.Drawing.EllipsePolygon(tile.X + tile.Width / 2f, tile.Y + tile.Height * 0.43f, tile.Width * 0.24f));
        });

        for (var x = tile.X; x < tile.Right; x++)
        {
            sheet[x, tile.Y] = new Rgba32(47, 79, 79, 255);
            sheet[x, tile.Bottom - 1] = new Rgba32(47, 79, 79, 255);
        }

        for (var y = tile.Y; y < tile.Bottom; y++)
        {
            sheet[tile.X, y] = new Rgba32(47, 79, 79, 255);
            sheet[tile.Right - 1, y] = new Rgba32(47, 79, 79, 255);
        }

        // Simulate label and diameter text that should not survive object extraction.
        for (var y = tile.Y + (int)(tile.Height * 0.76f); y < tile.Y + (int)(tile.Height * 0.82f); y++)
        {
            for (var x = tile.X + 15; x < tile.Right - 15; x++)
                sheet[x, y] = new Rgba32(255, 255, 255, 255);
        }

        for (var y = tile.Y + (int)(tile.Height * 0.87f); y < tile.Y + (int)(tile.Height * 0.91f); y++)
        {
            for (var x = tile.X + 26; x < tile.Right - 26; x++)
                sheet[x, y] = new Rgba32(211, 211, 211, 255);
        }
    }

}
