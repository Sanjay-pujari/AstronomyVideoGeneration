using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public sealed class AngularRelationshipAnalyzer : IAngularRelationshipAnalyzer
{
    public AngularRelationshipResult Analyze(IReadOnlyList<SkyObjectPosition> visibleObjects)
    {
        if (visibleObjects.Count == 0)
            return new AngularRelationshipResult([], null, 0, 0, null, 0);

        var pairs = new List<AngularPairSeparation>();
        for (var i = 0; i < visibleObjects.Count; i++)
        {
            for (var j = i + 1; j < visibleObjects.Count; j++)
            {
                var s = CalculateSeparation(visibleObjects[i], visibleObjects[j]);
                pairs.Add(new AngularPairSeparation(visibleObjects[i].Name, visibleObjects[j].Name, s));
            }
        }

        var closest = pairs.Count > 0 ? pairs.MinBy(p => p.SeparationDeg) : null;
        var maxSpread = pairs.Count > 0 ? pairs.Max(p => p.SeparationDeg) : 0;
        var avgAlt = visibleObjects.Average(x => x.AltitudeDeg);
        var brightest = visibleObjects.MinBy(x => x.Magnitude);
        return new AngularRelationshipResult(pairs, closest, maxSpread, avgAlt, brightest, visibleObjects.Count);
    }

    private static double CalculateSeparation(SkyObjectPosition a, SkyObjectPosition b)
    {
        var alt1 = DegToRad(a.AltitudeDeg);
        var alt2 = DegToRad(b.AltitudeDeg);
        var az1 = DegToRad(a.AzimuthDeg);
        var az2 = DegToRad(b.AzimuthDeg);
        var cosSep = Math.Sin(alt1) * Math.Sin(alt2) + Math.Cos(alt1) * Math.Cos(alt2) * Math.Cos(az1 - az2);
        return RadToDeg(Math.Acos(Math.Clamp(cosSep, -1d, 1d)));
    }

    private static double DegToRad(double d) => d * Math.PI / 180d;
    private static double RadToDeg(double r) => r * 180d / Math.PI;
}
