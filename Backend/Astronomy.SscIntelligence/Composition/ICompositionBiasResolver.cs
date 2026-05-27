using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public interface ICompositionBiasResolver
{
    CompositionBiasResult Resolve(SceneIntentType sceneIntent, double rawAltitudeDeg, double rawAzimuthDeg, double angularSpreadDeg, (double Min, double Max) targetAltitudeRange);
}
