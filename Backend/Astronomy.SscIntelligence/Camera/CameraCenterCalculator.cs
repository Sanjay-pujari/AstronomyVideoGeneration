using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Camera;

public sealed class CameraCenterCalculator : ICameraCenterCalculator
{
    public (double AltitudeDeg, double AzimuthDeg) CalculateCenter(IReadOnlyList<SkyObjectPosition> visibleObjects)
    {
        ArgumentNullException.ThrowIfNull(visibleObjects);
        if (visibleObjects.Count == 0)
        {
            throw new ArgumentException("At least one visible object is required.", nameof(visibleObjects));
        }

        var totalWeight = visibleObjects.Sum(o => o.Weight <= 0 ? 1.0 : o.Weight);
        var altitude = visibleObjects.Sum(o => o.AltitudeDeg * (o.Weight <= 0 ? 1.0 : o.Weight)) / totalWeight;

        var x = 0.0;
        var y = 0.0;
        foreach (var obj in visibleObjects)
        {
            var w = obj.Weight <= 0 ? 1.0 : obj.Weight;
            var radians = DegreesToRadians(obj.AzimuthDeg);
            x += Math.Cos(radians) * w;
            y += Math.Sin(radians) * w;
        }

        var azimuthDeg = RadiansToDegrees(Math.Atan2(y, x));
        if (azimuthDeg < 0)
        {
            azimuthDeg += 360;
        }

        return (altitude, azimuthDeg);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
