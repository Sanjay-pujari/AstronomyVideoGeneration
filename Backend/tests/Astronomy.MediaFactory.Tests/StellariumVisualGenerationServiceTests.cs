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
        Assert.Contains("if (typeof core.setProjectionMode === \"function\")", script);
        Assert.Contains("core.setProjectionMode(\"ProjectionPerspective\")", script);
        Assert.Contains("core.selectObjectByName(\"Moon\"", script);
        Assert.Contains("if (typeof StelFileMgr !== \"undefined\"", script);
        Assert.Contains("core.screenshot(\"001-sky-overview\"", script);
    }


    [Fact]
    public void BuildSceneScript_UsesGenericObjectName_WhenTargetContainsVariantLabel()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions());
        var scene = new StellariumScene
        {
            SceneId = "002-moon",
            TargetObject = "Waxing Gibbous Moon",
            SceneTimeUtc = new DateTimeOffset(2026, 3, 17, 20, 0, 0, TimeSpan.Zero),
            OutputImagePath = Path.Combine("/tmp", "002-moon.png")
        };

        var script = builder.BuildSceneScript(scene);

        Assert.Contains("core.selectObjectByName(\"Moon\"", script);
        Assert.DoesNotContain("core.selectObjectByName(\"Waxing Gibbous Moon\"", script);
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

    [Fact]
    public async Task PrepareVisualsAsync_UsesDiscoveredStellariumCapture_WithVariableSuffix()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-discovery-" + Guid.NewGuid().ToString("N"));
        var captureDir = Path.Combine(outputDir, "visuals", "screenshots");
        Directory.CreateDirectory(captureDir);

        var discoveredPath = Path.Combine(captureDir, "001-sky-overview-0001.png");
        await File.WriteAllBytesAsync(discoveredPath, [1, 2, 3, 4]);

        var options = Options.Create(new StellariumOptions());
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(options, builder, NullLogger<StellariumVisualGenerationService>.Instance);

        var visuals = await sut.PrepareVisualsAsync(new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India"
        }, outputDir, CancellationToken.None);

        var firstScenePath = visuals.First();
        Assert.True(File.Exists(firstScenePath));
        Assert.Equal(4, new FileInfo(firstScenePath).Length);
        Assert.False(File.Exists(Path.ChangeExtension(firstScenePath, ".placeholder.txt")));
    }

    [Fact]
    public async Task PrepareVisualsAsync_NestsConfiguredDirectories_ByDateAndPipelineRun()
    {
        var runId = Guid.NewGuid().ToString("N");
        var outputDir = Path.Combine(Path.GetTempPath(), "media-output", "DailySkyGuide", "2026-03-17", runId);
        var scriptsRoot = Path.Combine(Path.GetTempPath(), "stellarium-scripts-root-" + Guid.NewGuid().ToString("N"));
        var capturesRoot = Path.Combine(Path.GetTempPath(), "stellarium-captures-root-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions
        {
            ScriptsDirectory = scriptsRoot,
            CaptureDirectory = capturesRoot
        });

        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(options, builder, NullLogger<StellariumVisualGenerationService>.Instance);
        var context = new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India"
        };

        var visuals = await sut.PrepareVisualsAsync(context, outputDir, CancellationToken.None);

        var expectedScope = Path.Combine("2026-03-17", runId);
        var expectedScriptPath = Path.Combine(scriptsRoot, expectedScope, "001-sky-overview.ssc");
        Assert.True(File.Exists(expectedScriptPath));
        Assert.All(visuals, path => Assert.StartsWith(Path.Combine(capturesRoot, expectedScope), path, StringComparison.OrdinalIgnoreCase));
    }
}
