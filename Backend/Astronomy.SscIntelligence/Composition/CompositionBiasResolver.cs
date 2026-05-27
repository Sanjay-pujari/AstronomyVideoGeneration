using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed class CompositionBiasResolver : ICompositionBiasResolver
{
    public CompositionBiasResult Resolve(SceneIntentType sceneIntent, double rawAltitudeDeg, double rawAzimuthDeg, double angularSpreadDeg, (double Min, double Max) targetAltitudeRange)
    {
        var bias = sceneIntent switch
        {
            SceneIntentType.CloseUp => 3d,
            SceneIntentType.Educational => 5d,
            SceneIntentType.Grouping => 8d,
            SceneIntentType.HeroShot => 12d,
            SceneIntentType.WideNight => 16d,
            _ => 5d
        };
        var adjusted = Math.Clamp(rawAltitudeDeg + bias, 15d, 82d);
        return new CompositionBiasResult(adjusted, rawAzimuthDeg, $"{sceneIntent} altitude bias +{bias:0}° (spread={angularSpreadDeg:0.##}°, range={targetAltitudeRange.Min:0.##}°-{targetAltitudeRange.Max:0.##}°)");
    }
}
