namespace Astronomy.SscIntelligence.SceneIntent;

public interface ISceneIntentResolver
{
    SceneIntent Resolve(string sceneCode, string? sceneTitle = null);
}
