using Astronomy.SscIntelligence.Camera;
using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Rendering;
using Astronomy.SscIntelligence.Visibility;
using FluentAssertions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed partial class SscIntelligenceEngineTests
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

        var result = calculator.Calculate(objects, objects, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 20, 25, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.Grouping);

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
        var script = renderer.Render(new SscRenderRequest(DateTime.UtcNow, 10, 20, 50, "Test", 30, 60, 40, "shots", "test")).Script;

        script.Should().Contain("ConstellationMgr.setFlagLines(true);");
        script.Should().Contain("ConstellationMgr.setFlagLabels(true);");
    }

    [Fact]
    public void Renderer_AlwaysTerminatesWithQuitStellarium()
    {
        var renderer = new StellariumSscRenderer();
        var script = renderer.Render(new SscRenderRequest(DateTime.UtcNow, 10, 20, 50, "Test", 30, 60, 40, "shots", "test")).Script;

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
        calculator.Calculate(one, one, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot).FovDeg.Should().Be(25);
        calculator.Calculate(one, one, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight).FovDeg.Should().Be(55);
    }

}

public sealed partial class SscIntelligenceEngineTests
{
    [Fact]
    public void PrimaryTargetResolver_PrioritizesMoonAndVenus_AndLimitsToThree()
    {
        var resolver = new Astronomy.SscIntelligence.Composition.PrimaryTargetResolver();
        var result = resolver.Resolve([
            new SkyObjectPosition("Moon", 30, 20, -12),
            new SkyObjectPosition("Venus", 32, 30, -4),
            new SkyObjectPosition("Jupiter", 28, 40, -2),
            new SkyObjectPosition("Saturn", 25, 60, 1),
            new SkyObjectPosition("Orion Constellation", 27, 80, 3, "Constellation")
        ], "hero_moon_venus", "Moon and Venus", []);
        result.PrimaryTargets.Count.Should().Be(3);
        result.PrimaryTargets.Select(x => x.Name).Should().Contain(["Moon", "Venus", "Jupiter"]);
        result.ContextTargets.Select(x => x.Name).Should().Contain("Orion Constellation");
    }

    [Fact]
    public void CompositionBiasResolver_AppliesHeroAndWideAndClamp()
    {
        var resolver = new Astronomy.SscIntelligence.Composition.CompositionBiasResolver();
        resolver.Resolve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 40, 180, 20, (20, 50)).AltitudeDeg.Should().Be(52);
        resolver.Resolve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight, 70, 180, 20, (20, 50)).AltitudeDeg.Should().Be(82);
    }
}

public sealed partial class SscIntelligenceEngineTests
{
    [Fact]
    public void DynamicBiasLimiter_ReducesBias_WhenPrimaryNearTopEdge()
    {
        var limiter = new Astronomy.SscIntelligence.Composition.DynamicBiasLimiter();
        var result = limiter.Limit(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 45, 12, 25, [new SkyObjectPosition("Jupiter", 64, 100, -2)]);
        result.WasLimited.Should().BeTrue();
        result.LimitedBiasDeg.Should().BeLessThan(12);
    }

    [Fact]
    public void ScreenSpaceFramingSolver_CorrectsTopAndBottomSafeZone()
    {
        var solver = new Astronomy.SscIntelligence.Composition.ScreenSpaceFramingSolver();
        var top = solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 45, 180, 25, [new SkyObjectPosition("Moon", 64, 180, -10)], []);
        top.FinalCameraAltitudeDeg.Should().BeGreaterThan(45);

        var bottom = solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 45, 180, 25, [new SkyObjectPosition("Moon", 28, 180, -10)], []);
        bottom.FinalCameraAltitudeDeg.Should().BeLessThan(45);
    }

    [Fact]
    public void ScreenSpaceFramingSolver_ClampsFinalAltitude()
    {
        var solver = new Astronomy.SscIntelligence.Composition.ScreenSpaceFramingSolver();
        solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight, 90, 180, 55, [new SkyObjectPosition("Moon", 89, 180, -10)], []).FinalCameraAltitudeDeg.Should().BeLessThanOrEqualTo(82);
        solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight, 5, 180, 55, [new SkyObjectPosition("Moon", 2, 180, -10)], []).FinalCameraAltitudeDeg.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void DynamicFovCalculator_PreservesWideLargerThanHero()
    {
        var calculator = new DynamicFovCalculator();
        var one = new[] { new SkyObjectPosition("Moon", 20, 30, 1) };
        var hero = calculator.Calculate(one, one, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot);
        var wide = calculator.Calculate(one, one, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 20, 30, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight);
        wide.FovDeg.Should().BeGreaterThan(hero.FovDeg);
    }

    [Fact]
    public void DynamicFovCalculator_MarksImpossibleGrouping_ForVeryDistantObjects()
    {
        var calculator = new DynamicFovCalculator();
        var objects = new[]
        {
            new SkyObjectPosition("Moon", 62, 181, -12),
            new SkyObjectPosition("Venus", 22, 288, -4),
            new SkyObjectPosition("Jupiter", 35, 280, -2)
        };

        var result = calculator.Calculate(objects, objects, Array.Empty<SkyObjectPosition>(), Array.Empty<SkyObjectPosition>(), 40, 250, new VisibilityRules(), Astronomy.SscIntelligence.SceneIntent.SceneIntent.Grouping);
        result.RequiresSplit.Should().BeTrue();
        result.FovDeg.Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public void SpatialCompositionAnalyzer_Classifies_Impossible_AndSuggestsSplitGroups()
    {
        var analyzer = new Astronomy.SscIntelligence.Spatial.AstronomicalSpatialCompositionEngine();
        var analysis = analyzer.Analyze([
            new SkyObjectPosition("Moon", 62, 181, -12),
            new SkyObjectPosition("Venus", 22, 288, -4),
            new SkyObjectPosition("Jupiter", 35, 280, -2)]);

        analysis.CompositionClass.Should().Be(Astronomy.SscIntelligence.Spatial.SpatialCompositionClass.ImpossibleGrouping);
        analysis.SplitRecommended.Should().BeTrue();
        analysis.PairDistances.Should().NotBeEmpty();
        analysis.Clusters.Count.Should().Be(2);
        analysis.DominantCluster.ObjectNames.Should().Contain(["Jupiter", "Venus"]);
    }


    [Fact]
    public void SpatialCompositionEngine_HandlesAzimuthWraparoundAsTightGrouping()
    {
        var engine = new Astronomy.SscIntelligence.Spatial.AstronomicalSpatialCompositionEngine();
        var result = engine.Analyze([new SkyObjectPosition("A", 30, 359, 1), new SkyObjectPosition("B", 31, 1, 1)]);
        result.CompositionClass.Should().Be(Astronomy.SscIntelligence.Spatial.SpatialCompositionClass.TightGrouping);
    }

    [Fact]
    public void SpatialCompositionEngine_ClassifiesWidePanorama()
    {
        var engine = new Astronomy.SscIntelligence.Spatial.AstronomicalSpatialCompositionEngine();
        var result = engine.Analyze([new SkyObjectPosition("A", 30, 0, 1), new SkyObjectPosition("B", 30, 60, 1)]);
        result.CompositionClass.Should().Be(Astronomy.SscIntelligence.Spatial.SpatialCompositionClass.WidePanorama);
    }
    [Fact]
    public void SscIntelligenceService_UsesFinalFramedAltitude_ForRender()
    {
        var service = new Astronomy.SscIntelligence.SscIntelligenceService(
            new Astronomy.SscIntelligence.NightWindow.NightWindowResolver(),
            new VisibilityFilter(),
            new CameraCenterCalculator(),
            new DynamicFovCalculator(),
            new Astronomy.SscIntelligence.Composition.PrimaryTargetResolver(),
            new Astronomy.SscIntelligence.Composition.UnifiedCameraComposer(),
            new Astronomy.SscIntelligence.SceneIntent.SceneIntentResolver(),
            new StellariumSscRenderer(),
            new Astronomy.SscIntelligence.Spatial.AstronomicalSpatialCompositionEngine(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Astronomy.SscIntelligence.SscIntelligenceService>.Instance);

        var request = new Astronomy.SscIntelligence.SscIntelligenceRequest(
            DateTime.UtcNow,
            0,
            0,
            0,
            "Test",
            [new SkyObjectPosition("Moon", 70, 180, -12), new SkyObjectPosition("Venus", 68, 182, -4)],
            VisibilityRules: null,
            SunAltitudeDeg: null,
            Timezone: "UTC",
            AstronomicalNightStartUtc: null,
            AstronomicalNightEndUtc: null,
            SceneIntent: Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot,
            SceneCode: "grouping",
            SceneTitle: "grouping",
            ExplicitTargetObjectNames: null);

        var result = service.Generate(request);
        result.SscScript.Should().Contain($"core.moveToAltAzi({result.CameraAltitudeDeg:0.###}, {result.CameraAzimuthDeg:0.###}, 0);");
    }
}


public sealed partial class SscIntelligenceEngineTests
{
    [Fact]
    public void CinematicAnchorProfile_UsesExpectedHeroAndWideAnchors()
    {
        Astronomy.SscIntelligence.Composition.CinematicAnchorProfile.For(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot).DesiredY.Should().Be(0.64);
        Astronomy.SscIntelligence.Composition.CinematicAnchorProfile.For(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight).DesiredY.Should().Be(0.74);
    }

    [Fact]
    public void CinematicAnchorSolver_SolvesHeroAltitudeFromFormula()
    {
        var solver = new Astronomy.SscIntelligence.Composition.CinematicAnchorSolver();
        var result = solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 42, 180, 50,
            [new SkyObjectPosition("Moon", 40, 180, -12)],
            [new SkyObjectPosition("Moon", 40, 180, -12)],
            [],
            []);

        result.AnchoredCameraAltitudeDeg.Should().BeApproximately(47, 0.001);
    }

    [Fact]
    public void CinematicAnchorSolver_ClampsAltitude()
    {
        var solver = new Astronomy.SscIntelligence.Composition.CinematicAnchorSolver();
        solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight, 40, 180, 80, [new SkyObjectPosition("A", 80, 180, -2)], [new SkyObjectPosition("A", 80, 180, -2)], [], []).AnchoredCameraAltitudeDeg.Should().BeLessThanOrEqualTo(82);
        solver.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.CloseUp, 40, 180, 80, [new SkyObjectPosition("A", 0, 180, -2)], [new SkyObjectPosition("A", 0, 180, -2)], [], []).AnchoredCameraAltitudeDeg.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void FinalSafetyPass_PreventsClipping_AfterAnchor()
    {
        var framing = new Astronomy.SscIntelligence.Composition.ScreenSpaceFramingSolver();
        var result = framing.Solve(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 20, 180, 25, [new SkyObjectPosition("Moon", 70, 180, -12)], []);
        result.FinalCameraAltitudeDeg.Should().BeGreaterThan(20);
    }
}


public sealed partial class SscIntelligenceEngineTests
{
    [Fact]
    public void UnifiedCameraComposer_Profile_UsesExpectedHeroAndWideAnchors()
    {
        Astronomy.SscIntelligence.Composition.UnifiedCameraCompositionProfile.For(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot).DesiredY.Should().Be(0.64);
        Astronomy.SscIntelligence.Composition.UnifiedCameraCompositionProfile.For(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight).DesiredY.Should().Be(0.74);
    }

    [Fact]
    public void UnifiedCameraComposer_SolvesHeroAltitudeFromFormula()
    {
        var composer = new Astronomy.SscIntelligence.Composition.UnifiedCameraComposer();
        var result = composer.Compose(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 42, 180, 50,
            [new SkyObjectPosition("Moon", 40, 180, -12)],
            [new SkyObjectPosition("Moon", 40, 180, -12)],
            [],
            []);

        result.FinalCameraAltitudeDeg.Should().BeApproximately(47, 0.001);
    }

    [Fact]
    public void UnifiedCameraComposer_SafeZoneAdjustsTopAndBottomAndClamp()
    {
        var composer = new Astronomy.SscIntelligence.Composition.UnifiedCameraComposer();
        var top = composer.Compose(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 45, 180, 25,
            [new SkyObjectPosition("Moon", 80, 180, -12)],
            [new SkyObjectPosition("Moon", 80, 180, -12)], [], []);
        top.FinalCameraAltitudeDeg.Should().BeGreaterThan(45);

        var bottom = composer.Compose(Astronomy.SscIntelligence.SceneIntent.SceneIntent.HeroShot, 45, 180, 25,
            [new SkyObjectPosition("Moon", 10, 180, -12)],
            [new SkyObjectPosition("Moon", 10, 180, -12)], [], []);
        bottom.FinalCameraAltitudeDeg.Should().BeLessThan(45);

        composer.Compose(Astronomy.SscIntelligence.SceneIntent.SceneIntent.WideNight, 40, 180, 80,
            [new SkyObjectPosition("A", 80, 180, -2)],
            [new SkyObjectPosition("A", 80, 180, -2)], [], []).FinalCameraAltitudeDeg.Should().BeLessThanOrEqualTo(82);
        composer.Compose(Astronomy.SscIntelligence.SceneIntent.SceneIntent.CloseUp, 40, 180, 80,
            [new SkyObjectPosition("A", 0, 180, -2)],
            [new SkyObjectPosition("A", 0, 180, -2)], [], []).FinalCameraAltitudeDeg.Should().BeGreaterThanOrEqualTo(12);
    }
}
