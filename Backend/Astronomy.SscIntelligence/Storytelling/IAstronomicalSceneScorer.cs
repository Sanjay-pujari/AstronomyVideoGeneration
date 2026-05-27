using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public interface IAstronomicalSceneScorer
{
    AstronomicalSceneScore Score(string sceneCode, string sceneTitle, string sceneIntent, IReadOnlyList<SkyObjectPosition> visibleObjects, NightWindowResult nightWindow);
}
