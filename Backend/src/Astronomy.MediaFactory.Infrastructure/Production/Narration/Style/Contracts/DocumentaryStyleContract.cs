using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Models;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

/// <summary>First-class production artifact carrying documentary style decisions into prompt composition.</summary>
public sealed record DocumentaryStyleContract(
    string Version,
    string VoiceProfile,
    DocumentaryWritingRhythm DocumentaryRhythm,
    IReadOnlyList<string> GlobalWritingRules,
    IReadOnlyList<DocumentarySceneStyle> SceneStyles,
    IReadOnlyDictionary<string, string> TransitionRules,
    IReadOnlyList<string> VocabularyRules,
    IReadOnlyList<string> FactTransformationRules,
    IReadOnlyList<string> WritingConstraints);
