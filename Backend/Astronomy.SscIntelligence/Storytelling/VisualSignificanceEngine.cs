using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public sealed class VisualSignificanceEngine : IVisualSignificanceEngine
{
    public VisualSignificanceResult Score(CelestialEventType eventType, AngularRelationshipResult angular, IReadOnlyList<SkyObjectPosition> visibleObjects, NightWindowResult nightWindow)
    {
        var score = Base(eventType);
        if (angular.BrightestObject?.Magnitude is <= -1.0) score += 8;
        if (angular.AverageAltitudeDeg is >= 20 and <= 70) score += 8;
        if (visibleObjects.Any(x => x.AltitudeDeg < 10)) score -= 10;
        if (angular.MaxSpreadDeg > 80) score -= 8;
        if (visibleObjects.Count > 0 && visibleObjects.Count(x => x.AltitudeDeg < 15) >= (visibleObjects.Count + 1) / 2) score -= 6;
        if (nightWindow.IsNight || (nightWindow.SunAltitudeDeg ?? 0) <= -6) score += 5;
        score = Math.Clamp(score, 0, 100);
        return new(score, $"Event={eventType}; avgAlt={angular.AverageAltitudeDeg:0.0}; maxSpread={angular.MaxSpreadDeg:0.0}; night={nightWindow.IsNight}");
    }

    private static int Base(CelestialEventType t) => t switch
    {
        CelestialEventType.MoonPlanetPairing => 90,
        CelestialEventType.Conjunction => 85,
        CelestialEventType.PlanetaryGrouping => 80,
        CelestialEventType.PlanetaryParade => 78,
        CelestialEventType.BrightPlanetHero => 70,
        CelestialEventType.WideConstellationContext => 45,
        CelestialEventType.EducationalSkyMap => 50,
        _ => 20
    };
}
