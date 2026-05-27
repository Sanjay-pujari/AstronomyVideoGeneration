using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Narrative;
using Astronomy.SscIntelligence.NightWindow;
using Astronomy.SscIntelligence.Spatial;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public class NarrativeSceneSplitterTests
{
    [Fact]
    public void ImpossibleGrouping_SplitsMoonAndWesternPlanets()
    {
        var splitter = new NarrativeSceneSplitter();
        var objects = new List<SkyObjectPosition> { new("Jupiter",40,260,-2), new("Venus",35,250,-4), new("Moon",60,120,-11) };
        var spatial = new AstronomicalSpatialCompositionEngine().Analyze(objects);
        var result = splitter.Split("hero_western_grouping_scene","Hero","en","us",DateTime.UtcNow,DateTime.UtcNow,null,objects,spatial,new NightWindowResult(DateTime.UtcNow,DateTime.UtcNow,true,-18,"x"));
        result.SplitApplied.Should().BeTrue();
        result.Scenes.Select(x=>x.SceneCode).Should().Contain(new[]{"western_planet_grouping_scene","moon_hero_scene"});
    }
}
