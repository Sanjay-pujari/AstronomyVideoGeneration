using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public interface IDynamicBiasLimiter
{
    DynamicBiasLimitResult Limit(SceneIntentType sceneIntent, double rawCameraAltitudeDeg, double proposedBiasDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> primaryTargets);
}
