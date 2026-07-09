using System.Diagnostics;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Diagnostics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Libraries;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Models;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Directors;

/// <summary>Default Documentary Style Director implementation for Project Aurora.</summary>
public sealed class DocumentaryStyleDirector(DocumentaryVocabulary vocabulary, DocumentaryTransitionLibrary transitions, DocumentaryFactTransformer factTransformer, ILogger<DocumentaryStyleDirector> logger) : IDocumentaryStyleDirector
{
    /// <summary>The current style contract version.</summary>
    public const string Version = "AstroPulse-DocumentaryStyleContract-v1";

    /// <inheritdoc />
    public Task<DocumentaryStyleContract> BuildAsync(EditorialContract editorialContract, CreativeStoryboard creativeStoryboard, NarrationBriefsV5 narrationBriefs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sceneStyles = narrationBriefs.Briefs.OrderBy(b => b.SceneOrder).Select((brief, index) => BuildScene(brief, index, narrationBriefs.Briefs.OrderBy(b => b.SceneOrder).ToArray())).ToArray();
        var contract = new DocumentaryStyleContract(
            Version,
            string.IsNullOrWhiteSpace(editorialContract.VoiceProfile) ? "CalmDocumentary" : editorialContract.VoiceProfile,
            DocumentaryWritingRhythm.Default,
            ["Write natural spoken documentary prose.", "Convert planning intent into audience-facing narration decisions.", "Never expose production, prompt, or data-format labels."],
            sceneStyles,
            transitions.All,
            vocabulary.PreferredPhrases,
            ["Dates become conversational timing.", "Angles become approximate visible spacing.", "Viewing windows become practical opportunity language.", "Directions become horizon-oriented guidance.", "Visibility becomes region-aware observing guidance."],
            ["Do not invent unsupported facts.", "Do not mention raw contract field names.", "Preserve scene order and scene identifiers."]);
        logger.LogInformation("Documentary Style Director built {SceneCount} scene styles.", sceneStyles.Length);
        return Task.FromResult(contract);
    }

    /// <summary>Builds diagnostics for a completed style contract.</summary>
    public DocumentaryStyleDiagnostics BuildDiagnostics(DocumentaryStyleContract contract, TimeSpan executionTime, IReadOnlyList<string> warnings, IReadOnlyList<string> errors) => new(contract.SceneStyles.Count, contract.TransitionRules.Count, contract.SceneStyles.Sum(s => s.PreferredVocabulary.Count), contract.SceneStyles.Sum(s => s.FactTransformations.Count), warnings, errors, executionTime.ToString("c"), Version);

    private DocumentarySceneStyle BuildScene(NarrationBriefV5 brief, int index, IReadOnlyList<NarrationBriefV5> orderedBriefs)
    {
        var next = index + 1 < orderedBriefs.Count ? orderedBriefs[index + 1] : null;
        var transition = next is null ? "Close the documentary thought warmly." : transitions.Select(brief.ScenePurpose, next.ScenePurpose);
        return new DocumentarySceneStyle(
            brief.SceneId,
            brief.ScenePurpose,
            CleanAudiencePromise(brief.AudienceTakeaway),
            OpeningFor(brief.ScenePurpose),
            "Move from noticing to meaning using only contracted facts.",
            brief.MustIncludeEnding ? "Resolve the story and include the approved channel ending." : "Land the scene with a soft handoff.",
            transition,
            vocabulary.SelectPreferred(brief.ScenePurpose),
            vocabulary.ForbiddenExpressions,
            CleanObjective(brief.SceneGoal),
            DocumentaryWritingRhythm.Default,
            brief.FactsToMention.Select(factTransformer.Transform).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray());
    }

    private static string OpeningFor(string purpose) => purpose.ToLowerInvariant() switch { "hook" => "Begin with an observable moment, not an explanation.", "science" => "Begin from curiosity, then clarify the cause.", "observation" => "Begin with practical orientation to the sky.", "takeaway" or "closing" => "Begin by reflecting on what the audience has just learned.", _ => "Begin with a clear visual observation." };
    private static string CleanAudiencePromise(string value) => value.Replace("The viewer should", "The audience leaves able to", StringComparison.OrdinalIgnoreCase);
    private static string CleanObjective(string value) => value.Replace("Verified details", "confirmed details", StringComparison.OrdinalIgnoreCase).Replace("event identity", "sky event", StringComparison.OrdinalIgnoreCase);
}
