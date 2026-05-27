using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Camera;

public interface ICameraCenterCalculator
{
    (double AltitudeDeg, double AzimuthDeg) CalculateCenter(IReadOnlyList<SkyObjectPosition> weightedObjects);
}
