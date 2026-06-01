using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public sealed class CinematicCameraPlanner : ICinematicCameraPlanner
{
    private static readonly object CameraMemoryLock = new();
    private static string? _previousSceneCode;
    private static double? _previousAzimuth;

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
            fov = Math.Min(fovRecommendationDeg, 34d);
            if (hero.Name.Contains("moon", StringComparison.OrdinalIgnoreCase))
            {
                verticalBias = 4d;
            }
            reason = $"hero target {hero.Name} center from resolved object Alt/Az";
        }
        else if (framingMode == "PlanetGrouping")
        {
            if (cameraObjects.Count < 2)
            {
                throw new InvalidOperationException("Planet grouping requires at least two resolved objects. Fallback camera generation is disabled.");
            }
            verticalBias = -3d;
            fov = spread + 16d;
            reason = $"grouping center from circular azimuth mean; fov from spread ({spread:0.##}°) + cinematic padding";
        }
        else if (framingMode == "SplitObjectFocus")
        {
            var target = cameraObjects.OrderBy(x => x.Magnitude).First();
            centerAltitude = target.AltitudeDeg;
            centerAzimuth = target.AzimuthDeg;
            fov = Math.Min(fovRecommendationDeg, 42d);
            verticalBias = target.AltitudeDeg < 15d ? 2d : 0.5d;
            reason = $"split-scene target {target.Name} center from resolved object Alt/Az; original grouping is too wide for one frame";
        }
        else if (framingMode == "OrientationWide")
        {
            fov = Math.Max(fovRecommendationDeg, spread + 22d);
            reason = "wide orientation for constellation/horizon context";
        }

        var paddingMultiplier = ResolvePaddingMultiplier(framingMode, sceneIntent);
        fov *= paddingMultiplier;
        fov = ClampFovByFramingMode(framingMode, fov);

        var plannedAltitude = Math.Clamp(centerAltitude + verticalBias - horizonBias, 2d, 85d);
        var plannedAzimuth = ApplyContinuityBlend(sceneCode, Normalize(centerAzimuth));
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

    private static double ApplyContinuityBlend(string? sceneCode, double currentAzimuth)
    {
        lock (CameraMemoryLock)
        {
            if (_previousAzimuth.HasValue)
            {
                var delta = CircularDistance(_previousAzimuth.Value, currentAzimuth);
                if (delta < 25d)
                {
                    currentAzimuth = Normalize((_previousAzimuth.Value * 0.45d) + (currentAzimuth * 0.55d));
                }
            }

            _previousAzimuth = currentAzimuth;
            _previousSceneCode = sceneCode;
            return currentAzimuth;
        }
    }

    private static double CircularDistance(double a, double b)
    {
        var delta = Math.Abs(Normalize(a) - Normalize(b));
        return delta > 180d ? 360d - delta : delta;
    }

    private static double ResolvePaddingMultiplier(string framingMode, SceneIntentType sceneIntent) => framingMode switch
    {
        "HeroObject" => 1.6d,
        "PlanetGrouping" => 1.8d,
        "SplitObjectFocus" => 1.45d,
        "OrientationWide" => 2.2d,
        _ => sceneIntent == SceneIntentType.WideNight ? 2.5d : 1.8d
    };

    private static double ClampFovByFramingMode(string framingMode, double fov) => framingMode switch
    {
        "HeroObject" => Math.Clamp(fov, 18d, 55d),
        "PlanetGrouping" => Math.Clamp(Math.Max(fov, 35d), 35d, 68d),
        "SplitObjectFocus" => Math.Clamp(fov, 18d, 55d),
        "OrientationWide" => Math.Clamp(fov, 45d, 95d),
        _ => Math.Clamp(fov, 25d, 75d)
    };

    private static string ResolveFramingMode(string? sceneCode, SceneIntentType sceneIntent, int objectCount)
    {
        if (!string.IsNullOrWhiteSpace(sceneCode))
        {
            if (sceneCode.Contains("moon_hero_scene", StringComparison.OrdinalIgnoreCase)) return "HeroObject";
            if (sceneCode.Contains("western_planet_grouping_scene", StringComparison.OrdinalIgnoreCase)) return objectCount < 2 ? "SplitObjectFocus" : "PlanetGrouping";
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
