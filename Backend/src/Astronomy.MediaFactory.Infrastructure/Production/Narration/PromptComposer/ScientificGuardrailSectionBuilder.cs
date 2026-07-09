using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class ScientificGuardrailSectionBuilder
{
    public string Build(IReadOnlyList<NarrationFactV5> facts, IReadOnlyList<string> prohibited)
    {
        var allowed = facts.Count == 0
            ? "Use only broad, careful astronomy context. Do not add dates, times, directions, brightness, equipment, weather, or location details that are not already confirmed."
            : "Allowed knowledge to use naturally: " + string.Join("; ", facts.Select(f => $"{NarrationPromptComposer.NormalizeFactName(f.Name)} — {f.Value}")) + ".";
        var forbidden = prohibited.Count == 0
            ? "Forbidden assumptions: unconfirmed altitude, constellation, brightness, weather, equipment, exact visibility, or optical-aid claims."
            : "Forbidden assumptions and phrases: " + string.Join("; ", prohibited.Select(PromptLanguageCleaner.CleanText)) + ".";
        return string.Join("\n", [
            allowed,
            forbidden,
            "Scientific integrity matters more than drama. If a detail is not established, leave it out or phrase the moment generally.",
            "Do not turn uncertainty into certainty, and do not make the event sound more visible, rare, or dramatic than the confirmed information supports."
        ]);
    }
}
