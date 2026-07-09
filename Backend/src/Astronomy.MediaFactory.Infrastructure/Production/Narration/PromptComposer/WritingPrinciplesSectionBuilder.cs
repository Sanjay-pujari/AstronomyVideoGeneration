using Astronomy.MediaFactory.Infrastructure.Production.Narration.Style.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class WritingPrinciplesSectionBuilder
{
    public string Build(IReadOnlyList<string> preferred, IReadOnlyList<string> forbidden, DocumentaryStyleContract? styleContract)
    {
        var vocabulary = styleContract?.VocabularyRules.Count > 0 ? styleContract.VocabularyRules : preferred;
        var rhythm = "Observe → curiosity → explanation → observation guidance → natural transition.";
        return string.Join("\n", [
            "Sentence rhythm: vary short orientation lines with longer, graceful explanatory lines that sound natural when spoken aloud.",
            "Paragraph rhythm: each scene should feel like one clean documentary beat, not a list of information.",
            "Transition rhythm: let the final sentence of each scene quietly open the door to the next scene.",
            $"Documentary pacing: {rhythm}",
            "Every documentary should naturally answer what is happening, why it is happening, when to observe it, where to look, what will be visible, what equipment helps, and why the event is memorable—never as a list.",
            "Observation guidance is part of the Astro Pulse identity: translate technical direction, altitude, visibility, and equipment details into spoken skywatching language.",
            vocabulary.Count == 0 ? "Preferred vocabulary: clear sky language, practical observing verbs, and emotionally restrained wonder." : $"Preferred vocabulary: {string.Join(", ", vocabulary.Select(PromptLanguageCleaner.CleanText))}.",
            forbidden.Count == 0 ? "Forbidden vocabulary: hype, overpromising, technical production language, and unsupported certainty." : $"Forbidden vocabulary: {string.Join(", ", forbidden.Select(PromptLanguageCleaner.CleanText))}."
        ]);
    }
}
