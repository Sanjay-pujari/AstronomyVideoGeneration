using SceneIntentType = Astronomy.SscIntelligence.SceneIntent.SceneIntent;

namespace Astronomy.SscIntelligence.Composition;

public sealed record CinematicAnchorProfile(double DesiredY, double DesiredX)
{
    public static CinematicAnchorProfile For(SceneIntentType sceneIntent) => sceneIntent switch
    {
        SceneIntentType.CloseUp => new(0.50d, 0.50d),
        SceneIntentType.Educational => new(0.55d, 0.50d),
        SceneIntentType.Grouping => new(0.60d, 0.50d),
        SceneIntentType.HeroShot => new(0.64d, 0.50d),
        SceneIntentType.WideNight => new(0.74d, 0.50d),
        _ => new(0.60d, 0.50d)
    };
}
