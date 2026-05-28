using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public sealed class CinematicCameraPlanner : ICinematicCameraPlanner
{
    public CinematicCameraPlan Plan(string? sceneCode, SceneIntentType sceneIntent, IReadOnlyList<SkyObjectPosition> cameraObjects, double compositionCameraAltitudeDeg, double compositionCameraAzimuthDeg, double fovRecommendationDeg, string? regionId, DateTime observationUtc)
    {
        if (cameraObjects.Count == 0)
        {
            throw new InvalidOperationException("Cinematic camera planning requires resolved object geometry (Alt/Az). Fallback camera generation is disabled.");
        }

        var warnings = new List<string>();
        var framingMode = ResolveFramingMode(sceneCode, sceneIntent, cameraObjects.Count);
        var verticalBias = sceneIntent == SceneIntentType.HeroShot ? 2d : sceneIntent == SceneIntentType.WideNight ? -1d : 0.5d;
        var horizonBias = sceneIntent == SceneIntentType.WideNight ? 4d : 0d;

        var centerAltitude = cameraObjects.Average(x => x.AltitudeDeg);
        var centerAzimuth = CircularMean(cameraObjects.Select(x => x.AzimuthDeg));
        var spread = AngularSpread(cameraObjects.Select(x => x.AzimuthDeg).ToArray());

        var fov = fovRecommendationDeg;
        var reason = "composition recommended center and fov";

        if (framingMode == "HeroObject")
        {
            var hero = cameraObjects.OrderBy(x => x.Magnitude).First();
            centerAltitude = hero.AltitudeDeg;
            centerAzimuth = hero.AzimuthDeg;
            fov = Math.Clamp(Math.Min(fovRecommendationDeg, 34d), 18d, 40d);
            reason = $"hero target {hero.Name} center from resolved object Alt/Az";
        }
        else if (framingMode == "PlanetGrouping")
        {
            if (cameraObjects.Count < 2)
            {
                throw new InvalidOperationException("Planet grouping requires at least two resolved objects. Fallback camera generation is disabled.");
            }

            fov = Math.Clamp(spread + 16d, 28d, 68d);
            reason = $"grouping center from circular azimuth mean; fov from spread ({spread:0.##}°) + cinematic padding";
        }
        else if (framingMode == "OrientationWide")
        {
            fov = Math.Clamp(Math.Max(fovRecommendationDeg, spread + 22d), 45d, 85d);
            reason = "wide orientation for constellation/horizon context";
        }

        var plannedAltitude = Math.Clamp(centerAltitude + verticalBias - horizonBias, 2d, 85d);
        var plannedAzimuth = Normalize(centerAzimuth);
        if (Math.Abs(plannedAltitude - compositionCameraAltitudeDeg) > 20)
        {
            warnings.Add("composition-plan altitude delta > 20°; verify horizon safety");
        }

        return new CinematicCameraPlan(plannedAzimuth, plannedAltitude, fov, framingMode, verticalBias, horizonBias,
            framingMode == "HeroObject" ? "ObjectLocked" : "RegionPan",
            framingMode == "HeroObject" ? "SlowPushIn" : framingMode == "OrientationWide" ? "SlowPan" : "OrbitDrift",
            warnings,
            $"{reason}; region={regionId ?? "unknown"}; utc={observationUtc:O}; compositionAz={compositionCameraAzimuthDeg:0.##}");
    }

    private static string ResolveFramingMode(string? sceneCode, SceneIntentType sceneIntent, int objectCount)
    {
        if (!string.IsNullOrWhiteSpace(sceneCode))
        {
            if (sceneCode.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) return "HeroObject";
            if (sceneCode.Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
            if (sceneCode.Contains("best_night_wide_scene", StringComparison.OrdinalIgnoreCase)) return "OrientationWide";
        }

        if (sceneIntent == SceneIntentType.WideNight) return "OrientationWide";
        if (sceneIntent == SceneIntentType.Grouping || objectCount > 1) return "PlanetGrouping";
        return "HeroObject";
    }

    private static double CircularMean(IEnumerable<double> angles)
    {
        var radians = angles.Select(a => a * Math.PI / 180d).ToArray();
        var sin = radians.Sum(Math.Sin);
        var cos = radians.Sum(Math.Cos);
        return Normalize(Math.Atan2(sin, cos) * 180d / Math.PI);
    }

    private static double AngularSpread(IReadOnlyList<double> angles)
    {
        if (angles.Count < 2) return 0;
        var normalized = angles.Select(Normalize).OrderBy(x => x).ToArray();
        var maxGap = 0d;
        for (var i = 0; i < normalized.Length; i++)
        {
            var current = normalized[i];
            var next = i == normalized.Length - 1 ? normalized[0] + 360d : normalized[i + 1];
            maxGap = Math.Max(maxGap, next - current);
        }
        return 360d - maxGap;
    }

    private static double Normalize(double degrees)
    {
        var n = degrees % 360d;
        return n < 0 ? n + 360d : n;
    }
}
