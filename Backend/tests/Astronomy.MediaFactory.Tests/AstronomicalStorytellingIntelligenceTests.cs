using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.Storytelling;
using FluentAssertions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomicalStorytellingIntelligenceTests
{
    private readonly IAngularRelationshipAnalyzer _angular = new AngularRelationshipAnalyzer();
    private readonly ICelestialEventClassifier _classifier = new CelestialEventClassifier();
    private readonly IVisualSignificanceEngine _significance = new VisualSignificanceEngine();

    [Fact]
    public void MoonAndVenus_Within8Deg_ClassifiesMoonPlanetPairing()
    {
        var objs = new[] { new SkyObjectPosition("Moon", 35, 100, -12, "Moon"), new SkyObjectPosition("Venus", 35, 107, -4, "Planet") };
        _classifier.Classify(objs, _angular.Analyze(objs)).EventType.Should().Be(CelestialEventType.MoonPlanetPairing);
    }

    [Fact]
    public void VenusAndJupiter_Within8Deg_ClassifiesConjunction()
    {
        var objs = new[] { new SkyObjectPosition("Venus", 35, 100, -4, "Planet"), new SkyObjectPosition("Jupiter", 35, 106, -2, "Planet") };
        _classifier.Classify(objs, _angular.Analyze(objs)).EventType.Should().Be(CelestialEventType.Conjunction);
    }

    [Fact]
    public void ThreePlanets_Within45Deg_ClassifiesGrouping()
    {
        var objs = new[] { new SkyObjectPosition("Venus", 25, 100, -4, "Planet"), new SkyObjectPosition("Jupiter", 28, 120, -2, "Planet"), new SkyObjectPosition("Saturn", 30, 140, 1, "Planet") };
        _classifier.Classify(objs, _angular.Analyze(objs)).EventType.Should().Be(CelestialEventType.PlanetaryGrouping);
    }

    [Fact]
    public void BrightVenusAlone_ClassifiesBrightPlanetHero()
    {
        var objs = new[] { new SkyObjectPosition("Venus", 40, 150, -4, "Planet") };
        _classifier.Classify(objs, _angular.Analyze(objs)).EventType.Should().Be(CelestialEventType.BrightPlanetHero);
    }

    [Fact]
    public void RandomStars_ClassifyWideOrLow()
    {
        var objs = new[] { new SkyObjectPosition("Sirius", 25, 110, -1.4, "Star"), new SkyObjectPosition("Orion Constellation", 32, 145, 3, "Constellation") };
        _classifier.Classify(objs, _angular.Analyze(objs)).EventType.Should().BeOneOf(CelestialEventType.WideConstellationContext, CelestialEventType.LowSignificance);
    }

    [Fact]
    public void LowAltitude_ReducesScore()
    {
        var n = new NightWindowResult(DateTime.UtcNow, DateTime.UtcNow, true, -12);
        var hi = new[] { new SkyObjectPosition("Venus", 45, 120, -4, "Planet") };
        var lo = new[] { new SkyObjectPosition("Venus", 5, 120, -4, "Planet") };
        var s1 = _significance.Score(CelestialEventType.BrightPlanetHero, _angular.Analyze(hi), hi, n).Score;
        var s2 = _significance.Score(CelestialEventType.BrightPlanetHero, _angular.Analyze(lo), lo, n).Score;
        s2.Should().BeLessThan(s1);
    }

    [Fact]
    public void Scoring_AlwaysInRange()
    {
        var objs = new[] { new SkyObjectPosition("Venus", 5, 10, -4, "Planet"), new SkyObjectPosition("Jupiter", 7, 300, -2, "Planet") };
        var score = _significance.Score(CelestialEventType.Conjunction, _angular.Analyze(objs), objs, new NightWindowResult(DateTime.UtcNow, DateTime.UtcNow, false, 0)).Score;
        score.Should().BeInRange(0, 100);
    }
}
