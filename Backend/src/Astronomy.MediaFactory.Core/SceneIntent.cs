namespace Astronomy.MediaFactory.Core;

public sealed record SceneIntent(
    string SceneId,
    string ScenePurpose,
    string Language,
    string EventType,
    string EventName,
    SceneIntentRequiredFacts RequiredFacts,
    IReadOnlyDictionary<string, string> ObservationFacts,
    string NarrationIntent,
    string VisualIntent,
    IReadOnlyList<string> ScientificConstraints,
    string EditorialTone,
    IReadOnlyList<string> MissingFactWarnings);

public sealed record SceneIntentRequiredFacts(
    SceneIntentFact EventDate,
    SceneIntentFact BestViewingTime,
    SceneIntentFact ViewingWindow,
    SceneIntentFact Direction,
    SceneIntentFact Altitude,
    SceneIntentFact Constellation,
    SceneIntentFact Brightness,
    SceneIntentFact MoonInterference,
    SceneIntentFact Visibility,
    SceneIntentFact RelativePositions);

public sealed record SceneIntentFact(string Name, string? Value, string Priority, bool IsMissing);
