using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class WeeklyAstronomicalCompositionEngine
{
    public WeeklyAstronomicalCompositionResult Compose(SceneObservationContext scene, IReadOnlyCollection<SceneObservationContext> allScenes)
    {
        var pool = allScenes
            .Where(s => s.IsVisible && s.AltitudeDegrees.HasValue && s.AzimuthDegrees.HasValue)
            .Where(s => !string.IsNullOrWhiteSpace(s.ObjectName) && !string.Equals(s.ObjectName, "Sky", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var targets = IsGroupingScene(scene)
            ? pool.Where(s => IsPriorityGroupingObject(s.ObjectName)).ToList()
            : pool.Where(s => string.Equals(s.ObjectName, scene.ObjectName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (targets.Count == 0)
            targets = pool;

        var includedObjects = targets.Select(s => s.ObjectName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var excludedObjects = pool.Select(s => s.ObjectName).Where(n => !includedObjects.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var centerAzimuth = CircularMeanDegrees(targets.Select(s => s.AzimuthDegrees!.Value));
        var centerAltitude = targets.Average(s => s.AltitudeDegrees!.Value);
        var spread = ComputeAngularSpread(targets.Select(s => (s.AzimuthDegrees!.Value, s.AltitudeDegrees!.Value)).ToList());

        var mode = ResolveRenderMode(targets.Count, spread);
        var fov = ComputeFov(mode, spread, targets.Count);
        var primary = targets.OrderByDescending(s => s.AltitudeDegrees).ThenBy(s => s.ObjectName, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.ObjectName ?? scene.ObjectName ?? "Sky";

        return new WeeklyAstronomicalCompositionResult(
            mode,
            Math.Round(centerAzimuth, 3),
            Math.Round(centerAltitude, 3),
            Math.Round(fov, 3),
            includedObjects,
            excludedObjects,
            Math.Round(spread, 3),
            primary,
            targets.Select(t => new WeeklyAstronomicalTargetSnapshot(t.ObjectName ?? "Unknown", t.AzimuthDegrees!.Value, t.AltitudeDegrees!.Value, t.IsVisible, t.LocalObservationTime, t.UtcObservationTime)).ToList());
    }

    private static bool IsGroupingScene(SceneObservationContext scene)
        => string.Equals(scene.SceneId, "s3_multi_object_grouping_01", StringComparison.OrdinalIgnoreCase)
           || scene.SceneType.Contains("Grouping", StringComparison.OrdinalIgnoreCase)
           || scene.SceneType.Contains("Conjunction", StringComparison.OrdinalIgnoreCase);

    private static bool IsPriorityGroupingObject(string? name)
        => name is not null && (name.Equals("Moon", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Venus", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Saturn", StringComparison.OrdinalIgnoreCase));

    private static string ResolveRenderMode(int objectCount, double spread)
    {
        if (objectCount <= 1) return "SingleObject";
        if (spread <= 6) return "Conjunction";
        if (spread <= 20) return "Grouping";
        return "Panorama";
    }

    private static double ComputeFov(string mode, double spread, int objectCount)
    {
        var baseFov = mode switch
        {
            "Conjunction" => Math.Max(25, spread * 3.0),
            "Grouping" => Math.Max(40, spread * 2.4),
            "Panorama" => Math.Max(65, spread * 1.8),
            _ => 30
        };

        return Math.Clamp(baseFov + Math.Max(0, objectCount - 2) * 2.5, 18, 100);
    }

    private static double ComputeAngularSpread(IReadOnlyList<(double Azimuth, double Altitude)> points)
    {
        if (points.Count <= 1) return 0;
        double max = 0;
        for (var i = 0; i < points.Count; i++)
        for (var j = i + 1; j < points.Count; j++)
        {
            var dx = CircularDeltaDegrees(points[i].Azimuth, points[j].Azimuth);
            var dy = points[i].Altitude - points[j].Altitude;
            var d = Math.Sqrt((dx * dx) + (dy * dy));
            if (d > max) max = d;
        }

        return max;
    }

    private static double CircularMeanDegrees(IEnumerable<double> degrees)
    {
        var rad = degrees.Select(d => d * Math.PI / 180d).ToList();
        var x = rad.Sum(Math.Cos);
        var y = rad.Sum(Math.Sin);
        var mean = Math.Atan2(y, x) * 180d / Math.PI;
        return (mean + 360d) % 360d;
    }

    private static double CircularDeltaDegrees(double a, double b)
    {
        var diff = Math.Abs(a - b) % 360d;
        return diff > 180d ? 360d - diff : diff;
    }
}

public sealed record WeeklyAstronomicalCompositionResult(
    string RenderMode,
    double CenterAzimuth,
    double CenterAltitude,
    double RecommendedFov,
    List<string> IncludedObjects,
    List<string> ExcludedObjects,
    double AngularSpread,
    string PrimaryObject,
    List<WeeklyAstronomicalTargetSnapshot> TargetObjects);

public sealed record WeeklyAstronomicalTargetSnapshot(
    string ObjectName,
    double Azimuth,
    double Altitude,
    bool Visibility,
    DateTime LocalDateTime,
    DateTimeOffset UtcDateTime);
