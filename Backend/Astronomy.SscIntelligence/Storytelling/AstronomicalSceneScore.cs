namespace Astronomy.SscIntelligence.Storytelling;

public sealed record AstronomicalSceneScore(
    CelestialEventType EventType,
    int Score,
    string Reason,
    string RecommendedSceneIntent,
    IReadOnlyList<string> RecommendedPrimaryTargets,
    IReadOnlyList<string> RecommendedSecondaryTargets,
    AngularRelationshipResult AngularRelationships);
