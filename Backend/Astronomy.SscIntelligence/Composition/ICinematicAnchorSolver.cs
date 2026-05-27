using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public interface ICinematicAnchorSolver
{
    CinematicAnchorResult Solve(SceneIntentType sceneIntent, double currentCameraAltitudeDeg, double cameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> visibleObjects, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets, IReadOnlyList<SkyObjectPosition> contextTargets);
}
