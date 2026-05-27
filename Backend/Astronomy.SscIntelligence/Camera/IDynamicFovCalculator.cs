using Astronomy.SscIntelligence.Contracts;
using Astronomy.SscIntelligence.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public interface IDynamicFovCalculator
{
    CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules, SceneIntent intent);
}
