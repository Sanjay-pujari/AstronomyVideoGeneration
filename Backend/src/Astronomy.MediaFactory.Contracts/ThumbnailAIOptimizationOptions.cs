namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailAIOptimizationOptions
{
    public const string SectionName = "ThumbnailAIOptimization";

    public bool Enabled { get; set; } = true;
    public bool UseAzureOpenAI { get; set; } = true;
    public bool EnableHookOptimization { get; set; } = true;
    public bool EnableEmotionOptimization { get; set; } = true;
    public bool EnableCTRScoring { get; set; } = true;
    public int MaxHookWords { get; set; } = 5;
    public double MinimumConfidence { get; set; } = 0.70;
    public bool PreventScientificHallucinations { get; set; } = true;
    public IReadOnlyCollection<string> AllowedEmotions { get; set; } =
    [
        "wonder",
        "urgency",
        "curiosity",
        "discovery",
        "rarity"
    ];
    public IReadOnlyCollection<string> DisallowedPatterns { get; set; } =
    [
        "fake",
        "alien",
        "ufo",
        "apocalypse",
        "clickbait"
    ];
    public string OutputFileName { get; set; } = "thumbnail-ai-optimization.json";
}
