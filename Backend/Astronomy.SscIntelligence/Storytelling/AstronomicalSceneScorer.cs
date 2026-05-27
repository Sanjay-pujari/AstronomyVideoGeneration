using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.Storytelling;

public sealed class AstronomicalSceneScorer(IAngularRelationshipAnalyzer angularAnalyzer, ICelestialEventClassifier classifier, IVisualSignificanceEngine significanceEngine) : IAstronomicalSceneScorer
{
    public AstronomicalSceneScore Score(string sceneCode, string sceneTitle, string sceneIntent, IReadOnlyList<SkyObjectPosition> visibleObjects, NightWindowResult nightWindow)
    {
        var angular = angularAnalyzer.Analyze(visibleObjects);
        var classification = classifier.Classify(visibleObjects, angular);
        var significance = significanceEngine.Score(classification.EventType, angular, visibleObjects, nightWindow);
        var primary = visibleObjects.OrderBy(x => x.Magnitude).Take(2).Select(x => x.Name).ToList();
        var secondary = visibleObjects.OrderByDescending(x => x.AltitudeDeg).Take(2).Select(x => x.Name).Except(primary).ToList();
        return new(classification.EventType, significance.Score, $"{classification.Reason} {significance.Reason}", sceneIntent, primary, secondary, angular);
    }
}
