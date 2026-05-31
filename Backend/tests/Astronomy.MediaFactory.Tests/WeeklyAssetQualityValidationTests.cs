using System.Text.Json;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AssetRealization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Path = System.IO.Path;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklyAssetQualityValidationTests
{
    [Fact]
    public async Task ValidateAndPersistAsync_WritesReports_AndFailsExpandedAstrophotographyTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "weekly-asset-quality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assetsRoot = Path.Combine(root, "assets");
        Directory.CreateDirectory(assetsRoot);

        var moonPath = Path.Combine(assetsRoot, "moon_hero_scene.png");
        var planetPath = Path.Combine(assetsRoot, "western_planet_grouping_scene.png");
        var astroPath = Path.Combine(assetsRoot, "astrophotography_target_scene.png");
        var aiPath = Path.Combine(assetsRoot, "cinematic_weekly_sky_reveal.png");
        var nasaPath = Path.Combine(assetsRoot, "nasa_moon_context.png");
        var jwstPath = Path.Combine(assetsRoot, "jwst_deep_space_context.png");
        var motionPath = Path.Combine(assetsRoot, "motion_weekly_sky_map.png");
        var overlayPath = Path.Combine(assetsRoot, "educational_camera_settings_card.png");

        await WriteValidSkyAsync(moonPath, 1280, 720, true);
        await WriteValidSkyAsync(planetPath, 1280, 720, true);
        await WriteValidSkyAsync(astroPath, 1280, 720, false);
        await WriteValidSkyAsync(aiPath, 1024, 720, true);
        await WriteValidSkyAsync(nasaPath, 1024, 720, true);
        await WriteValidSkyAsync(jwstPath, 1024, 720, true);
        await WriteValidSkyAsync(motionPath, 1024, 720, true);
        await WriteValidSkyAsync(overlayPath, 1024, 720, true);

        var assets = new[]
        {
            Asset(moonPath, RealizedVisualAssetSourceType.StellariumBase),
            Asset(planetPath, RealizedVisualAssetSourceType.StellariumBase),
            Asset(astroPath, RealizedVisualAssetSourceType.StellariumExpanded),
            Asset(aiPath, RealizedVisualAssetSourceType.AICinematic),
            Asset(nasaPath, RealizedVisualAssetSourceType.NASA),
            Asset(jwstPath, RealizedVisualAssetSourceType.JWST),
            Asset(motionPath, RealizedVisualAssetSourceType.MotionGraphics),
            Asset(overlayPath, RealizedVisualAssetSourceType.EducationalOverlay)
        };
        var motionEntries = new[] { new MotionGraphicManifestEntry("seg-overview", "WeeklySkyOverview", "MotionGraphics", motionPath, 12, "weekly_sky_map", ["weekly-context"], ["Weekly sky map", "Moon and planets"]) };
        var overlayEntries = new[] { new EducationalOverlayManifestEntry("seg-astro", "AstrophotographyTip", "EducationalOverlay", overlayPath, 10, "camera_settings_card", ["weekly-context"], ["Use tripod", "Short exposure"]) };

        var result = await new WeeklyAssetQualityValidator(NullLogger.Instance).ValidateAndPersistAsync(root, assets, motionEntries, overlayEntries, CancellationToken.None);

        File.Exists(Path.Combine(root, "episode", "weekly-asset-quality-report.json")).Should().BeTrue();
        File.Exists(Path.Combine(root, "episode", "weekly-asset-quality-details.json")).Should().BeTrue();
        result.Report.TotalAssets.Should().Be(8);
        result.Report.ProductionReadyCount.Should().Be(7);
        result.Report.ProductionFailedCount.Should().Be(1);
        result.Report.StellariumPassed.Should().Be(2);
        result.Report.ExpandedFailed.Should().Be(1);
        result.Report.AiPassed.Should().Be(1);
        result.Report.NasaPassed.Should().Be(1);
        result.Report.JwstPassed.Should().Be(1);
        result.Report.MotionPassed.Should().Be(1);
        result.Report.OverlayPassed.Should().Be(1);
        result.Report.QualityGatePassed.Should().BeFalse();
        result.FailedAssetPaths.Should().Contain(astroPath);

        var detailsJson = await File.ReadAllTextAsync(result.DetailsPath);
        detailsJson.Should().Contain("Target not visible");
        var reportJson = await File.ReadAllTextAsync(result.ReportPath);
        using var reportDoc = JsonDocument.Parse(reportJson);
        reportDoc.RootElement.GetProperty("qualityGatePassed").GetBoolean().Should().BeFalse();
    }

    private static RealizedVisualAsset Asset(string path, RealizedVisualAssetSourceType sourceType)
    {
        var info = new FileInfo(path);
        var dimensions = ImageDimensionReader.Read(path);
        return new RealizedVisualAsset($"{sourceType}:{Path.GetFileNameWithoutExtension(path)}", sourceType, Path.GetFileNameWithoutExtension(path), path, true, info.Length, dimensions.Width, dimensions.Height, "test", true, true);
    }

    private static async Task WriteValidSkyAsync(string path, int width, int height, bool includeTargets)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(5, 8, 24));
        image.Mutate(ctx =>
        {
            ctx.BackgroundColor(new Rgba32(5, 8, 24));
            for (var i = 0; i < 80; i++)
            {
                var x = (i * 37) % width;
                var y = (i * 53) % height;
                var star = includeTargets ? new Rgba32((byte)(120 + i % 100), (byte)(130 + i % 80), 255) : new Rgba32(60, 70, 110);
                ctx.Fill(star, new RectangleF(x, y, 2 + i % 3, 2 + i % 3));
            }

            if (includeTargets)
            {
                ctx.Fill(new Rgba32(245, 230, 180), new EllipsePolygon(width * 0.55f, height * 0.45f, 24));
                ctx.Fill(new Rgba32(120, 180, 255), new EllipsePolygon(width * 0.42f, height * 0.55f, 10));
            }
        });
        await image.SaveAsPngAsync(path);
    }
}
