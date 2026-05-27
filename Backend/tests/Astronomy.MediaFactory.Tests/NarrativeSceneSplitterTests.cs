using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Narrative;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Spatial;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public class NarrativeSceneSplitterTests
{
    [Fact]
    public void WidePanorama_WithMoonAndWesternPlanets_SplitsWhenRecommended()
    {
        var splitter = new NarrativeSceneSplitter();
        var objects = new List<SkyObjectPosition>
        {
            new("Jupiter", 22, 268, -2),
            new("Venus", 18, 246, -4),
            new("Moon", 58, 322, -11)
        };
        var spatial = new AstronomicalSpatialCompositionEngine().Analyze(objects);

        var result = splitter.Split("hero_western_grouping_scene", "Hero", "en", "us", DateTime.UtcNow, DateTime.UtcNow, null, objects, spatial, new NightWindowResult(DateTime.UtcNow, DateTime.UtcNow, true, -18, "x"), requiresSplit: false);

        result.SplitApplied.Should().BeTrue();
        result.Reason.Should().Be("spatial-split-recommended");
        result.Scenes.Select(x => x.SceneCode).Should().Contain(new[] { "western_planet_grouping_scene", "moon_hero_scene" });
    }

    [Fact]
    public void RequiresSplit_True_ForcesSplitEvenWhenCompositionNotImpossible()
    {
        var splitter = new NarrativeSceneSplitter();
        var objects = new List<SkyObjectPosition>
        {
            new("Jupiter", 30, 255, -2),
            new("Venus", 26, 250, -4),
            new("Moon", 50, 258, -11)
        };
        var spatial = new AstronomicalSpatialCompositionEngine().Analyze(objects);

        var result = splitter.Split("hero_western_grouping_scene", "Hero", "en", "us", DateTime.UtcNow, DateTime.UtcNow, null, objects, spatial, new NightWindowResult(DateTime.UtcNow, DateTime.UtcNow, true, -18, "x"), requiresSplit: true);

        result.SplitApplied.Should().BeTrue();
    }
}
