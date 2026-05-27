using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Camera;

public sealed class DynamicFovCalculator : IDynamicFovCalculator
{
    public CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules)
    {
        ArgumentNullException.ThrowIfNull(visibleObjects);
        if (visibleObjects.Count == 0)
        {
            throw new ArgumentException("At least one visible object is required.", nameof(visibleObjects));
        }

        var angularSpread = CalculateAngularSpread(visibleObjects);
        var fov = visibleObjects.Count == 1 ? 35.0 : Math.Clamp(angularSpread * 1.6, 20, 90);
        var requiresSplit = angularSpread > rules.MaximumGroupSpreadDeg;

        return new CameraSolution(centerAltitudeDeg, centerAzimuthDeg, fov, requiresSplit, angularSpread);
    }

    private static double CalculateAngularSpread(IReadOnlyList<SkyObjectPosition> objects)
    {
        if (objects.Count <= 1) return 0;

        var maxSeparation = 0.0;
        for (var i = 0; i < objects.Count; i++)
        {
            for (var j = i + 1; j < objects.Count; j++)
            {
                var separation = AngularSeparationDeg(objects[i], objects[j]);
                if (separation > maxSeparation) maxSeparation = separation;
            }
        }
        return maxSeparation;
    }

    private static double AngularSeparationDeg(SkyObjectPosition a, SkyObjectPosition b)
    {
        var alt1 = DegToRad(a.AltitudeDeg);
        var az1 = DegToRad(a.AzimuthDeg);
        var alt2 = DegToRad(b.AltitudeDeg);
        var az2 = DegToRad(b.AzimuthDeg);

        var cosSep = Math.Sin(alt1) * Math.Sin(alt2) + Math.Cos(alt1) * Math.Cos(alt2) * Math.Cos(az1 - az2);
        cosSep = Math.Clamp(cosSep, -1, 1);
        return RadToDeg(Math.Acos(cosSep));
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;
}
