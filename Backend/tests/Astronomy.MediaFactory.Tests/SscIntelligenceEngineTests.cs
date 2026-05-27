using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Rendering;
using Astronomy.SscIntelligence.Visibility;
using FluentAssertions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SscIntelligenceEngineTests
{
    [Fact]
    public void CameraCenterCalculator_UsesCircularMean_ForAzimuthWraparound()
    {
        var calculator = new CameraCenterCalculator();
        var objects = new[]
        {
            new SkyObjectPosition("A", 40, 359, 1),
            new SkyObjectPosition("B", 42, 1, 1),
        };

        var (_, azimuth) = calculator.CalculateCenter(objects);

        azimuth.Should().BeLessThan(2);
    }

    [Fact]
    public void DynamicFovCalculator_ComputesSpreadBasedFov_ForMultipleObjects()
    {
        var calculator = new DynamicFovCalculator();
        var objects = new[]
        {
            new SkyObjectPosition("A", 20, 10, 1),
            new SkyObjectPosition("B", 20, 40, 1),
        };

        var result = calculator.Calculate(objects, 20, 25, new VisibilityRules());

        result.FovDeg.Should().BeApproximately(48, 1.0);
    }

    [Fact]
    public void VisibilityFilter_RemovesObjectsBelowMinimumAltitude()
    {
        var filter = new VisibilityFilter();
        var objects = new[]
        {
            new SkyObjectPosition("Low", 5, 30, 1),
            new SkyObjectPosition("High", 25, 30, 1),
        };

        var (visible, removed) = filter.Filter(objects, new VisibilityRules { MinimumObjectAltitudeDeg = 10 });

        visible.Should().ContainSingle(o => o.Name == "High");
        removed.Should().ContainSingle(r => r.Contains("Low"));
    }

    [Fact]
    public void Renderer_AlwaysIncludesConstellationLinesAndLabels()
    {
        var renderer = new StellariumSscRenderer();
        var script = renderer.Render(new SscRenderRequest(DateTime.UtcNow, 10, 20, 50, "Test", 30, 60, 40)).Script;

        script.Should().Contain("ConstellationMgr.setFlagLines(true);");
        script.Should().Contain("ConstellationMgr.setFlagLabels(true);");
    }

    [Fact]
    public void Renderer_AlwaysTerminatesWithQuitStellarium()
    {
        var renderer = new StellariumSscRenderer();
        var script = renderer.Render(new SscRenderRequest(DateTime.UtcNow, 10, 20, 50, "Test", 30, 60, 40)).Script;

        script.Should().Contain("core.quitStellarium();");
        script.TrimEnd().Should().EndWith("core.wait(10);\ncore.quitStellarium();");
    }

    [Fact]
    public void NightWindowResolver_FallbackForUdaipur_Is2045Local_1515Utc()
    {
        var resolver = new Astronomy.SscIntelligence.NightWindow.NightWindowResolver();
        var result = resolver.Resolve(new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc), "Asia/Kolkata", 24.5854, 73.7125, new VisibilityRules());
        result.BestObservationUtc.Hour.Should().Be(15);
        result.BestObservationUtc.Minute.Should().Be(15);
        result.BestObservationLocalTime.Hour.Should().Be(20);
        result.BestObservationLocalTime.Minute.Should().Be(45);
    }

    [Fact]
    public void SceneIntentResolver_ResolvesHeroAndWide()
    {
        var r = new Astronomy.SscIntelligence.SceneIntent.SceneIntentResolver();
        r.Resolve("moon_jupiter_hero_scene").Should().Be(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot);
        r.Resolve("best_night_wide_scene").Should().Be(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight);
    }

    [Fact]
    public void DynamicFovCalculator_DiffersBySceneIntent()
    {
        var calculator = new DynamicFovCalculator();
        var one = new[] { new SkyObjectPosition("Moon", 20, 30, 1) };
        calculator.Calculate(one, 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot).FovDeg.Should().Be(25);
        calculator.Calculate(one, 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight).FovDeg.Should().Be(55);
    }

}
