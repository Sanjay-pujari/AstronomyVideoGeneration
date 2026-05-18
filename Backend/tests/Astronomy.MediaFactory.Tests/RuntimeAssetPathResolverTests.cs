using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class RuntimeAssetPathResolverTests
{
    [Fact]
    public async Task ResolveFontPath_UsesAppContextBaseDirectoryAssets()
    {
        var resolver = new RuntimeAssetPathResolver();
        var english = resolver.ResolveFontPath("assets/fonts/Montserrat-ExtraBold.ttf");
        var hindi = resolver.ResolveFontPath("assets/fonts/NotoSansDevanagari-Bold.ttf");

        Directory.CreateDirectory(Path.GetDirectoryName(english)!);
        await File.WriteAllTextAsync(english, "english-font");
        await File.WriteAllTextAsync(hindi, "hindi-font");

        Assert.StartsWith(resolver.BaseDirectory, english, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(resolver.BaseDirectory, hindi, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(english));
        Assert.True(File.Exists(hindi));
    }

    [Fact]
    public async Task ResolveCelestialAssetPath_UsesAppContextBaseDirectoryAssets()
    {
        var resolver = new RuntimeAssetPathResolver();
        var heroPath = resolver.ResolveCelestialAssetPath("jupiter", "hero-transparent.png");
        Directory.CreateDirectory(Path.GetDirectoryName(heroPath)!);
        using (var image = new Image<Rgba32>(4, 4, Color.Orange))
            await image.SaveAsPngAsync(heroPath);

        Assert.Equal(Path.Combine(resolver.BaseDirectory, "assets", "celestial", "jupiter", "hero-transparent.png"), heroPath);
        Assert.True(File.Exists(heroPath));
        Assert.DoesNotContain("Backend/src", heroPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractPack_RelativeDefaultsWriteToRuntimeAssetsFolder()
    {
        var resolver = new RuntimeAssetPathResolver();
        var sourceDir = Path.Combine(resolver.GetCelestialRoot(), "source");
        Directory.CreateDirectory(sourceDir);
        var sheetPath = Path.Combine(sourceDir, "celestial-object-sheet.png");
        var mapPath = Path.Combine(sourceDir, "celestial-object-sheet-map.json");

        using (var sheet = new Image<Rgba32>(40, 40, Color.Black))
        {
            for (var y = 8; y < 32; y++)
            for (var x = 8; x < 32; x++)
                sheet[x, y] = new Rgba32(255, 165, 0, 255);
            await sheet.SaveAsPngAsync(sheetPath);
        }

        await File.WriteAllTextAsync(mapPath, """
        { "jupiter": { "x": 0, "y": 0, "width": 40, "height": 40 } }
        """);

        var extractor = new CelestialAssetPackExtractor(Options.Create(new CelestialAssetPackOptions
        {
            SourceSheetPath = "assets/celestial/source/celestial-object-sheet.png",
            SheetMapPath = "assets/celestial/source/celestial-object-sheet-map.json",
            OutputRootPath = "assets/celestial",
            OverwriteExisting = true
        }), resolver);

        var report = await extractor.ExtractAsync(CancellationToken.None);
        var outputPath = resolver.ResolveCelestialAssetPath("jupiter", "hero-transparent.png");

        Assert.True(File.Exists(outputPath));
        Assert.StartsWith(resolver.GetCelestialRoot(), outputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(resolver.GetCelestialRoot(), report.OutputRootPath);
    }

    [Fact]
    public void MissingRuntimeFont_ProducesClearDiagnosticMessage()
    {
        var missing = $"assets/fonts/missing-{Guid.NewGuid():N}.ttf";
        var resolved = new RuntimeAssetPathResolver().ResolveFontPath(missing);

        var exception = Assert.Throws<FileNotFoundException>(() => ThrowIfMissing(resolved));
        Assert.Contains("Thumbnail font missing from executable assets folder:", exception.Message);
        Assert.Contains(resolved, exception.Message);
    }

    private static void ThrowIfMissing(string resolvedPath)
    {
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Thumbnail font missing from executable assets folder: {resolvedPath}", resolvedPath);
    }
}
