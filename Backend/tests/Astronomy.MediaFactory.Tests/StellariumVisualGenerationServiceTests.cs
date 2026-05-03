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
    public void BuildSceneScript_UsesSceneObjectName_WhenSelectingTargets()
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

        Assert.Contains("core.selectObjectByName(\"Waxing Gibbous Moon\"", script);
        Assert.Contains("LabelMgr.labelObject(\"Waxing Gibbous Moon\", \"Waxing Gibbous Moon\", true, 24)", script);
    }


    [Fact]
    public void BuildSceneScript_EnablesObjectPointer_ForPlanetAndMoonScenes()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions());
        var scene = new StellariumScene
        {
            SceneId = "003-planet",
            TargetObject = "Jupiter",
            SceneTimeUtc = new DateTimeOffset(2026, 3, 17, 20, 0, 0, TimeSpan.Zero),
            OutputImagePath = Path.Combine("/tmp", "003-planet.png")
        };

        var script = builder.BuildSceneScript(scene);

        Assert.Contains("safeCall(StelObjectMgr, \"setFlagSelectedObjectPointer\", [true]);", script);
        Assert.Contains("ConstellationMgr.setFlagLines(true);", script);
        Assert.Contains("ConstellationMgr.setFlagLabels(true);", script);
        Assert.Contains("ConstellationMgr.setFlagBoundaries(false);", script);
    }

    [Fact]
    public void BuildSceneScript_UsesMinimalLabels_ForDeepSkyScenes()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions());
        var scene = new StellariumScene
        {
            SceneId = "004-deep-sky",
            TargetObject = "Orion Nebula",
            SceneTimeUtc = new DateTimeOffset(2026, 3, 17, 20, 0, 0, TimeSpan.Zero),
            OutputImagePath = Path.Combine("/tmp", "004-deep-sky.png")
        };

        var script = builder.BuildSceneScript(scene);

        Assert.Contains("safeCall(StelSkyDrawer, \"setFlagStarName\", [false]);", script);
        Assert.Contains("ConstellationMgr.setFlagLabels(true);", script);
    }


    [Fact]
    public void BuildSceneScript_DoesNotUseMoveToAltAzi()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions());
        var scene = new StellariumScene
        {
            SceneId = "005-object",
            TargetObject = "Mars",
            SceneTimeUtc = new DateTimeOffset(2026, 3, 17, 20, 0, 0, TimeSpan.Zero),
            OutputImagePath = Path.Combine("/tmp", "005-object.png")
        };

        var script = builder.BuildSceneScript(scene);

        Assert.DoesNotContain("moveToAltAzi", script);
    }

    [Fact]
    public async Task PrepareVisualsAsync_GeneratesDailySkyGuideScenesAndManifest()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-scenes-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions());
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(
            options,
            builder,
            Options.Create(new ObservationOptions()),
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

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
        Assert.True(Directory.EnumerateFiles(Path.Combine(outputDir, "visuals", "scripts"), "002-*.json").Any());
    }

    [Fact]
    public async Task PrepareVisualsAsync_CreatesPlaceholders_WhenStellariumUnavailable()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-fallback-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions { ExecutablePath = Path.Combine(outputDir, "missing-stellarium") });
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(
            options,
            builder,
            Options.Create(new ObservationOptions()),
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

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
        var sut = new StellariumVisualGenerationService(
            options,
            builder,
            Options.Create(new ObservationOptions()),
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

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
        var sut = new StellariumVisualGenerationService(
            options,
            builder,
            Options.Create(new ObservationOptions()),
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);
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

    [Fact]
    public async Task PrepareVisualsAsync_UsesNightObservationTimes_AndUtcInScripts()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-night-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions());
        var obs = Options.Create(new ObservationOptions { SkyOverviewMinutesAfterSunset = 90, DefaultObservationHour = 22, Timezone = "Asia/Kolkata" });
        var builder = new StellariumScriptBuilder(options.Value);
        var sut = new StellariumVisualGenerationService(
            options,
            builder,
            obs,
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

        var context = new AstronomyContext { Date = new DateOnly(2026, 6, 21), LocationName = "Udaipur, India", TimeZone = "Asia/Kolkata", Latitude = 24.5854, Longitude = 73.7125 };
        await sut.PrepareVisualsAsync(context, outputDir, CancellationToken.None);

        var moonMetaPath = Path.Combine(outputDir, "visuals", "scripts", "002-moon.json");
        var deepSkyMetaPath = Path.Combine(outputDir, "visuals", "scripts", "004-orion.json");
        var scriptPath = Path.Combine(outputDir, "visuals", "scripts", "001-sky-overview.ssc");

        var moonMeta = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(moonMetaPath));
        var moonUtc = moonMeta.RootElement.GetProperty("SceneTimeUtc").GetDateTimeOffset();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = TimeZoneInfo.ConvertTime(moonUtc, tz);
        Assert.True(local.Hour >= 18 || local.Hour <= 6);

        var overviewMeta = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "visuals", "scripts", "001-sky-overview.json")));
        var overviewUtc = overviewMeta.RootElement.GetProperty("SceneTimeUtc").GetDateTimeOffset();
        var overviewLocal = TimeZoneInfo.ConvertTime(overviewUtc, tz);
        Assert.True(overviewLocal.Hour >= 19);

        var deepMeta = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(deepSkyMetaPath));
        var deepUtc = deepMeta.RootElement.GetProperty("SceneTimeUtc").GetDateTimeOffset();
        var deepLocal = TimeZoneInfo.ConvertTime(deepUtc, tz);
        Assert.True(deepLocal.Hour >= 21 || deepLocal.Hour <= 5);

        var script = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("core.setDate(\"", script);
        Assert.Contains("\", \"utc\");", script);
    }

    [Fact]
    public void BuildSceneScript_UsesExactObservationContextUtcTime()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions());
        var scene = new StellariumScene
        {
            SceneId = "002-moon",
            TargetObject = "Moon",
            OutputImagePath = Path.Combine("/tmp", "002-moon.png"),
            ObservationContext = new SceneObservationContext
            {
                ObjectName = "Moon",
                UtcObservationTime = new DateTimeOffset(2026, 5, 2, 19, 50, 0, TimeSpan.Zero)
            }
        };

        var script = builder.BuildSceneScript(scene);
        Assert.Contains("core.setDate(\"2026-05-02T19:50:00Z\", \"utc\");", script);
    }

    [Fact]
    public async Task PrepareVisualsAsync_SkipsObjectScenes_BelowAltitudeThreshold()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-threshold-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions());
        var observationOptions = Options.Create(new ObservationOptions { MinimumObjectAltitudeDegrees = 89 });
        var sut = new StellariumVisualGenerationService(
            options,
            new StellariumScriptBuilder(options.Value),
            observationOptions,
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

        var visuals = await sut.PrepareVisualsAsync(new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India"
        }, outputDir, CancellationToken.None);

        Assert.Equal(2, visuals.Count);
        Assert.DoesNotContain(visuals, v => Path.GetFileName(v).Contains("moon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareVisualsAsync_WritesSceneGeneratedSscContext()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "stellarium-context-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StellariumOptions());
        var sut = new StellariumVisualGenerationService(
            options,
            new StellariumScriptBuilder(options.Value),
            Options.Create(new ObservationOptions()),
            new ObservationTimeService(),
            NullLogger<StellariumVisualGenerationService>.Instance);

        await sut.PrepareVisualsAsync(new AstronomyContext
        {
            Date = new DateOnly(2026, 3, 17),
            LocationName = "Udaipur, India"
        }, outputDir, CancellationToken.None);

        var generatedContextFiles = Directory.EnumerateFiles(Path.Combine(outputDir, "visuals", "scripts"), "*.generated-ssc-context.json").ToList();
        Assert.NotEmpty(generatedContextFiles);
    }

    [Fact]
    public void BuildSceneScript_WithCinematicMotionDisabled_UsesStableZoomBehavior()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions { EnableCinematicMotion = false });
        var scene = new StellariumScene
        {
            SceneId = "002-moon",
            TargetObject = "Moon",
            OutputImagePath = Path.Combine("/tmp", "002-moon.png")
        };

        var script = builder.BuildSceneScript(scene);
        Assert.Contains("StelMovementMgr.zoomTo(30, 2.0);", script);
        Assert.Contains("core.wait(6.0);", script);
        Assert.DoesNotContain("// Cinematic zoom-in enabled.", script);
    }

    [Fact]
    public void BuildSceneScript_WithCinematicMotionEnabled_UsesTwoZoomCallsAndWaitsBeforeScreenshot()
    {
        var builder = new StellariumScriptBuilder(new StellariumOptions
        {
            EnableCinematicMotion = true,
            CinematicWaitBeforeScreenshotSeconds = 8
        });

        var scene = new StellariumScene
        {
            SceneId = "003-planet",
            TargetObject = "Jupiter",
            OutputImagePath = Path.Combine("/tmp", "003-planet.png")
        };

        var script = builder.BuildSceneScript(scene);
        Assert.Contains("// Cinematic zoom-in enabled.", script);
        Assert.Contains("StelMovementMgr.zoomTo(60, 0.0);", script);
        Assert.Contains("StelMovementMgr.zoomTo(35, 6.0);", script);
        Assert.Contains("core.wait(8.0);", script);
        Assert.Contains("LabelMgr.labelObject(\"Jupiter\", \"Jupiter\", true, 24)", script);
        Assert.Contains("core.moveToSelectedObject(2.0);", script);
        Assert.Contains("StelMovementMgr.setFlagTracking(true);", script);
        Assert.Matches("core\\.wait\\(8\\.0\\);\\s*// Screenshot\\s*core\\.screenshot", script);
    }
}
