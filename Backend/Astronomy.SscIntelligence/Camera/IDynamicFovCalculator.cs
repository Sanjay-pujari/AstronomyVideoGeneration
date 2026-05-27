using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public interface IDynamicFovCalculator
{
    CameraSolution Calculate(IReadOnlyList<SkyObjectPosition> visibleObjects, double centerAltitudeDeg, double centerAzimuthDeg, VisibilityRules rules, SceneIntentType intent);
}
