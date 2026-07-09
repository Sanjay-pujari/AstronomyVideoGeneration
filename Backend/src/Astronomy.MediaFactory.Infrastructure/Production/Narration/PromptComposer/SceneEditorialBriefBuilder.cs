using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class SceneEditorialBriefBuilder
{
    public string Build(IReadOnlyList<NarrationBriefV5> scenes, DocumentaryStyleContract? styleContract)
    {
        if (scenes.Count == 0) return "No scene briefs were supplied.";
        return string.Join("\n\n", scenes.OrderBy(s => s.SceneOrder).Select(s => BuildScene(s, styleContract)));
    }

    private static string BuildScene(NarrationBriefV5 scene, DocumentaryStyleContract? styleContract)
    {
        var style = styleContract?.SceneStyles.FirstOrDefault(s => string.Equals(s.SceneId, scene.SceneId, StringComparison.OrdinalIgnoreCase));
        var lines = new List<string>
        {
            $"Scene {scene.SceneOrder}: {scene.SceneId} — {scene.ScenePurpose}",
            $"Editorial writing objective: {Clean(scene.SceneGoal)}",
            $"Desired audience understanding: {CleanAudience(scene.AudienceTakeaway)}",
            $"Natural sky details to weave into prose: {FormatFacts(scene.FactsToMention)}",
            $"Scientific boundaries for this scene: {FormatAvoidance(scene.FactsToAvoid)}",
            $"Movement into the next scene: {Clean(scene.ConnectorToNext)}",
            $"Delivery feel: {scene.Tone}; {scene.Pacing}; {scene.TargetLength}. {Clean(scene.GenerationInstructions)}"
        };

        if (style is not null)
        {
            lines.Add($"Opening movement: {style.OpeningStyle}");
            lines.Add($"Development movement: {style.DevelopmentStyle}");
            lines.Add($"Closing movement: {style.ClosingStyle}");
            lines.Add($"Transition feel: {style.TransitionStyle}");
            if (style.FactTransformations.Count > 0) lines.Add($"Documentary phrasing opportunities: {string.Join("; ", style.FactTransformations.Select(Clean))}");
        }

        return string.Join("\n", lines);
    }

    private static string FormatFacts(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "only confirmed broad context" : string.Join("; ", facts.Select(f => $"{NarrationPromptComposer.NormalizeFactName(f.Name)} — {f.Value}"));
    private static string FormatAvoidance(IReadOnlyList<string> values) => values.Count == 0 ? "do not invent unconfirmed specifics" : string.Join("; ", values.Select(Clean));
    private static string CleanAudience(string value) => Clean(value).Replace("The viewer should", "The audience should come away able to", StringComparison.OrdinalIgnoreCase).Replace("Viewer should", "The audience should come away able to", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string value) => PromptLanguageCleaner.CleanText(value);
}
