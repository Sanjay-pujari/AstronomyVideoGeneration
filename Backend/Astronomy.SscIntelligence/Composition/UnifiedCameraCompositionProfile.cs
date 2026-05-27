using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed record UnifiedCameraCompositionProfile(double DesiredY, double DesiredX = 0.5d)
{
    public static UnifiedCameraCompositionProfile For(SceneIntentType sceneIntent) => sceneIntent switch
    {
        SceneIntentType.CloseUp => new(0.50d),
        SceneIntentType.Educational => new(0.55d),
        SceneIntentType.Grouping => new(0.60d),
        SceneIntentType.HeroShot => new(0.64d),
        SceneIntentType.WideNight => new(0.74d),
        _ => new(0.55d)
    };
}
