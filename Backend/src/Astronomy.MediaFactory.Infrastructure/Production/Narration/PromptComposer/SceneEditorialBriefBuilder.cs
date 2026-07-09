using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class SceneEditorialBriefBuilder
{
    public string Build(IReadOnlyList<NarrationBriefV5> scenes, DocumentaryStyleContract? styleContract)
    {
        if (scenes.Count == 0) return "No narrative brief was supplied.";
        return string.Join("\n\n", scenes.OrderBy(s => s.SceneOrder).Select(s => BuildScene(s, styleContract)));
    }

    private static string BuildScene(NarrationBriefV5 scene, DocumentaryStyleContract? styleContract)
    {
        var style = styleContract?.SceneStyles.FirstOrDefault(s => string.Equals(s.SceneId, scene.SceneId, StringComparison.OrdinalIgnoreCase));
        var lines = new List<string>
        {
            $"Scene Story\n{Clean(scene.SceneGoal)}",
            $"Narrative Goal\n{Clean(scene.ScenePurpose)}",
            $"Audience Experience\n{CleanAudience(scene.AudienceTakeaway)}",
            $"Key Facts\n{FormatFacts(scene.FactsToMention)}",
            $"Observation Guidance\n{Clean(scene.GenerationInstructions)}",
            $"Emotional Tone\n{Clean(scene.Tone)}",
            $"Transition Context\n{Clean(scene.ConnectorToNext)}"
        };

        if (style is not null)
        {
            var atmosphere = string.Join(" ", new[] { style.OpeningStyle, style.DevelopmentStyle, style.ClosingStyle, style.TransitionStyle }.Select(Clean).Where(v => !string.IsNullOrWhiteSpace(v)));
            if (!string.IsNullOrWhiteSpace(atmosphere)) lines.Add($"Documentary Atmosphere\n{atmosphere}");
            if (style.FactTransformations.Count > 0) lines.Add($"Natural Fact Language\n{string.Join("; ", style.FactTransformations.Select(Clean))}");
        }

        return string.Join("\n\n", lines);
    }

    private static string FormatFacts(IReadOnlyList<NarrationFactV5> facts) => facts.Count == 0 ? "Only confirmed broad context." : string.Join("\n", facts.Select(f => $"- {NarrationPromptComposer.NormalizeFactName(f.Name)}: {Clean(f.Value)}"));
    private static string CleanAudience(string value) => Clean(value).Replace("The viewer should", "The viewer", StringComparison.OrdinalIgnoreCase).Replace("Viewer should", "Viewer", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string value) => PromptLanguageCleaner.CleanText(value)
        .Replace("Editorial writing objective", "Story movement", StringComparison.OrdinalIgnoreCase)
        .Replace("Natural sky details to weave into prose", "Sky details", StringComparison.OrdinalIgnoreCase)
        .Replace("Scientific boundaries for this scene", "Scientific boundary", StringComparison.OrdinalIgnoreCase)
        .Replace("Movement into the next scene", "Transition", StringComparison.OrdinalIgnoreCase)
        .Replace("Delivery feel", "Tone", StringComparison.OrdinalIgnoreCase)
        .Trim();
}
