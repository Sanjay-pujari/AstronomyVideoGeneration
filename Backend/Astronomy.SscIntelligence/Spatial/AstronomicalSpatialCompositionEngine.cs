using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Spatial;

public sealed class AstronomicalSpatialCompositionEngine : IAstronomicalSpatialCompositionEngine
{
    private static readonly HashSet<string> PlanetNames = new(StringComparer.OrdinalIgnoreCase) { "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };

    public SpatialCompositionResult Analyze(IReadOnlyList<SkyObjectPosition> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.Count == 0) throw new ArgumentException("At least one object is required.", nameof(objects));

        var pairs = new List<SpatialPairAnalysis>();
        for (var i = 0; i < objects.Count; i++)
        for (var j = i + 1; j < objects.Count; j++)
        {
            var azDelta = CircularAzimuthDelta(objects[i].AzimuthDeg, objects[j].AzimuthDeg);
            var altDelta = Math.Abs(objects[i].AltitudeDeg - objects[j].AltitudeDeg);
            pairs.Add(new SpatialPairAnalysis(objects[i].Name, objects[j].Name, AngularSeparationDeg(objects[i], objects[j]), azDelta, altDelta));
        }

        var max = pairs.Count == 0 ? 0 : pairs.Max(x => x.AngularDistanceDeg);
        var compositionClass = max switch { < 15 => SpatialCompositionClass.TightGrouping, <= 40 => SpatialCompositionClass.MediumGrouping, <= 90 => SpatialCompositionClass.WidePanorama, _ => SpatialCompositionClass.ImpossibleGrouping };
        var range = compositionClass switch
        {
            SpatialCompositionClass.TightGrouping => (18d, 35d),
            SpatialCompositionClass.MediumGrouping => (35d, 65d),
            SpatialCompositionClass.WidePanorama => (65d, 95d),
            _ => null
        };
        var clusters = BuildClusters(objects);
        var dominant = clusters.OrderByDescending(c => c.Objects.Count >= 2)
            .ThenByDescending(c => c.Objects.Any(o => PlanetNames.Contains(o.Name) || o.Name.Equals("Moon", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(c => c.Objects.Min(o => o.Magnitude))
            .ThenByDescending(c => c.Objects.Max(o => o.AltitudeDeg)).First();
        var deferred = compositionClass == SpatialCompositionClass.ImpossibleGrouping ? objects.Where(o => !dominant.Objects.Contains(o)).ToArray() : Array.Empty<SkyObjectPosition>();

        return new SpatialCompositionResult(compositionClass, pairs, max,
            objects.Max(o => o.AltitudeDeg) - objects.Min(o => o.AltitudeDeg),
            CalculateAzimuthSpread(objects.Select(o => o.AzimuthDeg)),
            pairs.MinBy(x => x.AngularDistanceDeg),
            pairs.MaxBy(x => x.AngularDistanceDeg),
            range,
            compositionClass == SpatialCompositionClass.ImpossibleGrouping,
            clusters,
            dominant,
            deferred);
    }

    private static IReadOnlyList<SpatialObjectCluster> BuildClusters(IReadOnlyList<SkyObjectPosition> objects)
    {
        var remaining = new HashSet<SkyObjectPosition>(objects);
        var clusters = new List<SpatialObjectCluster>();
        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var queue = new Queue<SkyObjectPosition>([seed]);
            var group = new HashSet<SkyObjectPosition>();
            remaining.Remove(seed);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(current);
                var neighbors = remaining.Where(x => AngularSeparationDeg(current, x) <= 40).ToList();
                foreach (var n in neighbors) { remaining.Remove(n); queue.Enqueue(n); }
            }
            clusters.Add(new SpatialObjectCluster(group.OrderBy(x => x.Name).ToArray()));
        }
        return clusters.OrderByDescending(c => c.Objects.Count).ToArray();
    }

    private static double CalculateAzimuthSpread(IEnumerable<double> azimuths)
    {
        var sorted = azimuths.OrderBy(a => a).ToArray(); if (sorted.Length <= 1) return 0;
        double largestGap = 0; for (var i = 1; i < sorted.Length; i++) largestGap = Math.Max(largestGap, sorted[i] - sorted[i - 1]);
        largestGap = Math.Max(largestGap, 360 - sorted[^1] + sorted[0]); return 360 - largestGap;
    }
    private static double CircularAzimuthDelta(double a, double b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }
    private static double AngularSeparationDeg(SkyObjectPosition a, SkyObjectPosition b)
    {
        var alt1 = a.AltitudeDeg * Math.PI / 180; var az1 = a.AzimuthDeg * Math.PI / 180; var alt2 = b.AltitudeDeg * Math.PI / 180; var az2 = b.AzimuthDeg * Math.PI / 180;
        var c = Math.Sin(alt1) * Math.Sin(alt2) + Math.Cos(alt1) * Math.Cos(alt2) * Math.Cos(az1 - az2);
        return Math.Acos(Math.Clamp(c, -1, 1)) * 180 / Math.PI;
    }
}
