using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class SceneEditorialBriefBuilder
{
    public string Build(IReadOnlyList<NarrationBriefV5> scenes, DocumentaryStyleContract? styleContract)
    {
        if (scenes.Count == 0) return "No private producer notes were supplied.";
        return string.Join("\n\n", scenes.OrderBy(s => s.SceneOrder).Select(s => BuildScene(s, styleContract)));
    }

    private static string BuildScene(NarrationBriefV5 scene, DocumentaryStyleContract? styleContract)
    {
        var style = styleContract?.SceneStyles.FirstOrDefault(s => string.Equals(s.SceneId, scene.SceneId, StringComparison.OrdinalIgnoreCase));
        var facts = FormatFacts(scene.FactsToMention);
        var atmosphere = style is null ? string.Empty : string.Join(" ", new[] { style.OpeningStyle, style.DevelopmentStyle, style.ClosingStyle, style.TransitionStyle }.Select(Clean).Where(v => !string.IsNullOrWhiteSpace(v)));
        var factLanguage = style is null || style.FactTransformations.Count == 0 ? string.Empty : " " + string.Join(" ", style.FactTransformations.Select(Clean));
        var formatCue = scene.TargetLength.Equals("short", StringComparison.OrdinalIgnoreCase)
            ? "This is the short cut, so favor a faster hook, tighter pacing, and direct observing action without reusing the long narration."
            : "This is the long cut, so give the writer room for richer explanation, slower spoken rhythm, and stronger science context.";

        return Clean($"These are confidential producer notes for a professional documentary writer, not lines for the script. {scene.SceneGoal} {scene.AudienceTakeaway} Work from these confirmed sky details: {facts} {scene.GenerationInstructions} The delivery can stay {scene.Tone}, with continuity shaped by {scene.ConnectorToNext}. {formatCue} {atmosphere}{factLanguage} The writer must create fresh spoken narration and must not quote or paraphrase these notes.");
    }

    private static string FormatFacts(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "only broad confirmed context" : string.Join("; ", facts.Select(f => $"{NarrationPromptComposer.NormalizeFactName(f.Name)} is {Clean(f.Value)}"));
    private static string Clean(string value) => PromptLanguageCleaner.CleanText(value)
        .Replace("The viewer should", "The audience can", StringComparison.OrdinalIgnoreCase)
        .Replace("Viewer should", "The audience can", StringComparison.OrdinalIgnoreCase)
        .Replace("guide the viewer", "orient the audience", StringComparison.OrdinalIgnoreCase)
        .Replace("open by", "begin with", StringComparison.OrdinalIgnoreCase)
        .Replace("end with", "close on", StringComparison.OrdinalIgnoreCase)
        .Trim();
}
