using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumVisualGenerationServiceTests
{
    [Fact]
    public void BuildSceneScript_ContainsProjectionLandscapeAndScreenshotTarget()
    {
        var options = new StellariumOptions
        {
            DefaultLandscape = "guereins",
            DefaultProjection = "ProjectionPerspective"
        };

        var builder = new StellariumScriptBuilder(options);
        var scene = new StellariumScene
        {
            SceneId = "001-sky-overview",
            TargetObject = "Moon",
            SceneTimeUtc = new DateTimeOffset(2026, 3, 17, 20, 0, 0, TimeSpan.Zero),
            OutputImagePath = Path.Combine("/tmp", "001-sky-overview.png")
        };

        var script = builder.BuildSceneScript(scene);

        Assert.Contains("LandscapeMgr.setCurrentLandscapeName(\"guereins\")", script);
        Assert.Contains("StelMovementMgr.setCurrentProjectionTypeKey(\"ProjectionPerspective\")", script);
        Assert.Contains("core.selectObjectByName(\"Moon\"", script);
        Assert.Contains("core.screenshot(\"001-sky-overview.png\"", script);
    }

    [Fact]
    public async Task PrepareVisualsAsync_GeneratesDailySkyGuideScenesAndManifest()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-scenes-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions());
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(options, builder, NullLogger<StellariumVisualGenerationService>.Instance);

        var context = new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India",
            Events =
            [
                new AstronomyEventModel { Category = "Planet", ObjectName = "Jupiter", Details = "Bright in southwest" },
                new AstronomyEventModel { Category = "Deep Sky", ObjectName = "Orion Nebula", Details = "Visible after dusk" }
            ]
        };

        var visuals = await sut.PrepareVisualsAsync(context, outputDir, CancellationToken.None);

        Assert.Equal(5, visuals.Count);
        Assert.All(visuals, v => Assert.True(File.Exists(v)));
        Assert.True(File.Exists(Path.Combine(outputDir, "visuals", "capture-manifest.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "visuals", "scripts", "001-sky-overview.ssc")));
        Assert.True(File.Exists(Path.Combine(outputDir, "visuals", "scripts", "002-moon.json")));
    }

    [Fact]
    public async Task PrepareVisualsAsync_CreatesPlaceholders_WhenStellariumUnavailable()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-fallback-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions { ExecutablePath = Path.Combine(outputDir, "missing-stellarium") });
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(options, builder, NullLogger<StellariumVisualGenerationService>.Instance);

        var visuals = await sut.PrepareVisualsAsync(new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India"
        }, outputDir, CancellationToken.None);

        Assert.NotEmpty(visuals);
        Assert.All(visuals, path =>
        {
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 1000);
            var placeholderInfoPath = Path.ChangeExtension(path, ".placeholder.txt");
            Assert.True(File.Exists(placeholderInfoPath));
            Assert.Contains("Placeholder generated for scene", File.ReadAllText(placeholderInfoPath));
        });
    }
}
