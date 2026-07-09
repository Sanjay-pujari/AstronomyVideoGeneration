using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Models;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

/// <summary>Describes documentary writing decisions for one scene.</summary>
public sealed record DocumentarySceneStyle(
    string SceneId,
    string ScenePurpose,
    string AudiencePromise,
    string OpeningStyle,
    string DevelopmentStyle,
    string ClosingStyle,
    string TransitionStyle,
    IReadOnlyList<string> PreferredVocabulary,
    IReadOnlyList<string> ForbiddenVocabulary,
    string EditorialObjective,
    DocumentaryWritingRhythm WritingRhythm,
    IReadOnlyList<string> FactTransformations);
