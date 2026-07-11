namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.PromptComposer;

public sealed class OutputContractSectionBuilder
{
    public string Build() => string.Join("\n", [
        "Write scene-based narration only.",
        "Return one finished voiceover passage for each scene, in scene order.",
        "Use plain text only: no markdown, no diagnostics, no notes to the production team, and no commentary about how the narration was made.",
        "Every line should be ready to record as voiceover and use as captions.",
        "Use the channel ending supplied by the resolved language profile exactly once, only at the end of the final scene."
    ]);
}
