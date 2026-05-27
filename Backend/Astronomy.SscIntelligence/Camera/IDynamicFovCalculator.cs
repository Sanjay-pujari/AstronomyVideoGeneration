using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Camera;

public interface IDynamicFovCalculator
{
    CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules);
}
