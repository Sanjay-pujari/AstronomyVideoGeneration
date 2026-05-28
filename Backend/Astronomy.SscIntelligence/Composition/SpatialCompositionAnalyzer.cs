using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Composition;

public sealed class SpatialCompositionAnalyzer
{
    public SpatialCompositionAnalysis Analyze(IReadOnlyList<SkyObjectPosition> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.Count == 0) throw new ArgumentException("At least one object is required.", nameof(objects));

        var pairs = new List<SpatialPairDistance>();
        var maxAngular = 0.0;
        for (var i = 0; i < objects.Count; i++)
        {
            for (var j = i + 1; j < objects.Count; j++)
            {
                var azDelta = CircularAzimuthDelta(objects[i].AzimuthDeg, objects[j].AzimuthDeg);
                var altDelta = Math.Abs(objects[i].AltitudeDeg - objects[j].AltitudeDeg);
                var angular = AngularSeparationDeg(objects[i], objects[j]);
                pairs.Add(new SpatialPairDistance(objects[i].Name, objects[j].Name, azDelta, altDelta, angular));
                maxAngular = Math.Max(maxAngular, angular);
            }
        }

        var azimuthSpread = objects.Count == 1 ? 0 : CalculateAzimuthSpread(objects.Select(o => o.AzimuthDeg));
        var altitudeSpread = objects.Max(o => o.AltitudeDeg) - objects.Min(o => o.AltitudeDeg);
        var classification = Classify(maxAngular);
        var recommendedFov = RecommendFov(classification, maxAngular);
        var splitScene = classification == SpatialGroupingClassification.ImpossibleGrouping;
        var groups = splitScene ? BuildSuggestedGroups(objects) : [objects.Select(o => o.Name).ToArray()];

        return new SpatialCompositionAnalysis(pairs, azimuthSpread, altitudeSpread, maxAngular, classification, recommendedFov, splitScene, groups);
    }

    private static SpatialGroupingClassification Classify(double maxAngular) => maxAngular switch
    {
        < 15 => SpatialGroupingClassification.TightGrouping,
        <= 40 => SpatialGroupingClassification.MediumGrouping,
        <= 90 => SpatialGroupingClassification.WidePanorama,
        _ => SpatialGroupingClassification.ImpossibleGrouping
    };

    private static double RecommendFov(SpatialGroupingClassification c, double spread) => c switch
    {
        SpatialGroupingClassification.TightGrouping => Math.Clamp(Math.Max(15, spread * 1.35), 15, 35),
        SpatialGroupingClassification.MediumGrouping => Math.Clamp(Math.Max(30, spread * 1.25), 30, 60),
        SpatialGroupingClassification.WidePanorama => Math.Clamp(Math.Max(55, spread * 1.15), 55, 90),
        _ => 45
    };

    private static IReadOnlyList<IReadOnlyList<string>> BuildSuggestedGroups(IReadOnlyList<SkyObjectPosition> objects)
    {
        var sorted = objects.OrderBy(o => o.AzimuthDeg).ToList();
        var groups = new List<IReadOnlyList<string>>();
        var current = new List<string> { sorted[0].Name };
        for (var i = 1; i < sorted.Count; i++)
        {
            var previous = sorted[i - 1];
            var currentObj = sorted[i];
            if (CircularAzimuthDelta(previous.AzimuthDeg, currentObj.AzimuthDeg) > 45)
            {
                groups.Add(current.ToArray());
                current = [currentObj.Name];
            }
            else
            {
                current.Add(currentObj.Name);
            }
        }
        groups.Add(current.ToArray());
        return groups;
    }

    private static double CalculateAzimuthSpread(IEnumerable<double> azimuths)
    {
        var sorted = azimuths.Select(NormalizeDegrees).OrderBy(a => a).ToArray();
        if (sorted.Length <= 1) return 0;
        double largestGap = 0;
        for (var i = 1; i < sorted.Length; i++) largestGap = Math.Max(largestGap, sorted[i] - sorted[i - 1]);
        largestGap = Math.Max(largestGap, 360 - sorted[^1] + sorted[0]);
        return Math.Clamp(360 - largestGap, 0, 360);
    }

    private static double CircularAzimuthDelta(double a, double b)
    {
        var d = Math.Abs(NormalizeDegrees(a) - NormalizeDegrees(b)) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0) normalized += 360;
        return normalized;
    }

    private static double AngularSeparationDeg(SkyObjectPosition a, SkyObjectPosition b)
    {
        var alt1 = DegToRad(a.AltitudeDeg);
        var az1 = DegToRad(a.AzimuthDeg);
        var alt2 = DegToRad(b.AltitudeDeg);
        var az2 = DegToRad(b.AzimuthDeg);
        var c = Math.Sin(alt1) * Math.Sin(alt2) + Math.Cos(alt1) * Math.Cos(alt2) * Math.Cos(az1 - az2);
        c = Math.Clamp(c, -1, 1);
        return RadToDeg(Math.Acos(c));
    }

    private static double DegToRad(double d) => d * Math.PI / 180.0;
    private static double RadToDeg(double r) => r * 180.0 / Math.PI;
}
