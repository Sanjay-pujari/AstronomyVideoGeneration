using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public sealed class CelestialEventClassifier : ICelestialEventClassifier
{
    public CelestialEventClassification Classify(IReadOnlyList<SkyObjectPosition> visibleObjects, AngularRelationshipResult angular)
    {
        var planets = visibleObjects.Where(IsPlanet).ToList();
        var hasMoon = visibleObjects.Any(o => o.Name.Contains("moon", StringComparison.OrdinalIgnoreCase));
        var starsOrConstellations = visibleObjects.Count(o => o.ObjectType?.Contains("star", StringComparison.OrdinalIgnoreCase) == true || o.ObjectType?.Contains("constellation", StringComparison.OrdinalIgnoreCase) == true || o.Name.Contains("constellation", StringComparison.OrdinalIgnoreCase));

        if (hasMoon && planets.Any() && angular.PairwiseSeparations.Any(p => IsMoonPlanetPair(p, visibleObjects) && p.SeparationDeg <= 8d))
            return new(CelestialEventType.MoonPlanetPairing, "Moon and planet are within 8° separation.");

        if (angular.PairwiseSeparations.Any(p => AreBothPlanets(p, visibleObjects) && p.SeparationDeg <= 8d))
            return new(CelestialEventType.Conjunction, "Two visible planets are within 8° separation.");

        if (planets.Count >= 3 && angular.MaxSpreadDeg <= 45d)
            return new(CelestialEventType.PlanetaryGrouping, "Three or more planets are visible within 45° spread.");

        if (planets.Count >= 4)
            return new(CelestialEventType.PlanetaryParade, "Four or more planets are visible across the sky.");

        if (planets.Any(p => p.Magnitude <= -1d))
            return new(CelestialEventType.BrightPlanetHero, "A bright planet (magnitude <= -1) is visible.");

        if (visibleObjects.Count > 0 && starsOrConstellations >= Math.Max(1, visibleObjects.Count - 1))
            return new(CelestialEventType.WideConstellationContext, "Mostly stars/constellations are visible.");

        return new(CelestialEventType.LowSignificance, "No major visual astronomical relationship detected.");
    }

    private static bool IsPlanet(SkyObjectPosition o) =>
        o.ObjectType?.Contains("planet", StringComparison.OrdinalIgnoreCase) == true ||
        new[] { "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune" }.Any(p => o.Name.Equals(p, StringComparison.OrdinalIgnoreCase));

    private static bool AreBothPlanets(AngularPairSeparation pair, IReadOnlyList<SkyObjectPosition> objects)
        => IsPlanet(Get(pair.ObjectA, objects)) && IsPlanet(Get(pair.ObjectB, objects));

    private static bool IsMoonPlanetPair(AngularPairSeparation pair, IReadOnlyList<SkyObjectPosition> objects)
    {
        var a = Get(pair.ObjectA, objects);
        var b = Get(pair.ObjectB, objects);
        var aMoon = a.Name.Contains("moon", StringComparison.OrdinalIgnoreCase);
        var bMoon = b.Name.Contains("moon", StringComparison.OrdinalIgnoreCase);
        return (aMoon && IsPlanet(b)) || (bMoon && IsPlanet(a));
    }

    private static SkyObjectPosition Get(string name, IReadOnlyList<SkyObjectPosition> objects)
        => objects.First(x => x.Name == name);
}
