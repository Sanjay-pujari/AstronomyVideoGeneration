using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public interface IScreenSpaceFramingSolver
{
    ScreenSpaceFramingResult Solve(SceneIntentType sceneIntent, double cameraAltitudeDeg, double cameraAzimuthDeg, double fovDeg, IReadOnlyList<SkyObjectPosition> primaryTargets, IReadOnlyList<SkyObjectPosition> secondaryTargets);
}
