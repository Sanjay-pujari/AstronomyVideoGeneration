namespace Astronomy.SscIntelligence.SceneIntent;

public sealed class SceneIntentResolver : ISceneIntentResolver
{
    public SceneIntent Resolve(string sceneCode, string? sceneTitle = null)
    {
        var text = $"{sceneCode} {sceneTitle}".ToLowerInvariant();
        if (text.Contains("hero")) return SceneIntent.HeroShot;
        if (text.Contains("grouping")) return SceneIntent.Grouping;
        if (text.Contains("wide")) return SceneIntent.WideNight;
        if (text.Contains("education")) return SceneIntent.Educational;
        if (text.Contains("educational")) return SceneIntent.Educational;
        return SceneIntent.Grouping;
    }
}
