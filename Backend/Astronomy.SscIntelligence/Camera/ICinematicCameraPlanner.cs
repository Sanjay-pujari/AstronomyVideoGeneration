using Astronomy.SscIntelligence.Contracts;
using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Camera;

public interface ICinematicCameraPlanner
{
    CinematicCameraPlan Plan(
        string? sceneCode,
        SceneIntentType sceneIntent,
        IReadOnlyList<SkyObjectPosition> cameraObjects,
        double compositionCameraAltitudeDeg,
        double compositionCameraAzimuthDeg,
        double fovRecommendationDeg,
        string? regionId,
        DateTime observationUtc);
}
