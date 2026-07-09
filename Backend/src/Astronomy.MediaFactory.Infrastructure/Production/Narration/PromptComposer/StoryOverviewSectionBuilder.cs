namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class StoryOverviewSectionBuilder
{
    public string Build(string language, string storyArc) => string.Join("\n", [
        $"Write in {language}.",
        $"Shape the narration as a single documentary arc: {storyArc}.",
        "The emotional progression should move from curiosity, to orientation, to understanding, to practical confidence, and finally to a quiet invitation to keep watching the sky.",
        "The documentary objective is to help the audience understand what is happening, why it is worth noticing, and how to observe it without exaggeration."
    ]);
}
