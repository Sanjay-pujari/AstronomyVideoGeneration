using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CelestialAssetPackExtractorTests
{
    [Fact]
    public async Task ExtractAsync_CropsMappedTilesToHeroAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"asset-pack-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(root, "source");
        Directory.CreateDirectory(sourceDir);
        var sheetPath = Path.Combine(sourceDir, "celestial-object-sheet.png");
        var mapPath = Path.Combine(sourceDir, "celestial-object-sheet-map.json");
        using (var sheet = new Image<Rgba32>(300, 160, Color.Black))
        {
            await sheet.SaveAsPngAsync(sheetPath);
        }
        await File.WriteAllTextAsync(mapPath, """
        {
          "jupiter": { "x": 10, "y": 10, "width": 100, "height": 100 },
          "saturn": { "x": 120, "y": 10, "width": 100, "height": 100 }
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

        Assert.Contains("jupiter", report.ExtractedObjects);
        Assert.True(File.Exists(Path.Combine(root, "jupiter", "hero.png")));
        Assert.True(File.Exists(Path.Combine(root, "saturn", "hero.png")));
        Assert.True(File.Exists(Path.Combine(root, "asset-pack-extraction-report.json")));
    }
}
