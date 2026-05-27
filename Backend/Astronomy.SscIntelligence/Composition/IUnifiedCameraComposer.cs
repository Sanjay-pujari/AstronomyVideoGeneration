using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public interface IUnifiedCameraComposer
{
    UnifiedCameraCompositionResult Compose(SceneIntentType sceneIntent, double rawCameraAltitudeDeg, double rawCameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets);
}
