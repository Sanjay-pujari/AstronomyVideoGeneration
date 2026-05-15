namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailCinematicAIOptions
{
    public const string SectionName = "ThumbnailCinematicAI";

    public bool Enabled { get; set; } = true;
    public bool EnableSmartCropping { get; set; } = true;
    public bool EnableObjectFocusEnhancement { get; set; } = true;
    public bool EnableColorMoodGrading { get; set; } = true;
    public bool EnableVisualHierarchyOptimization { get; set; } = true;
    public bool EnablePlanetMoonEnhancement { get; set; } = true;
    public bool EnableConjunctionFraming { get; set; } = true;
    public bool EnablePortraitSafeCropping { get; set; } = true;
    public IReadOnlyCollection<string> AllowedMoodProfiles { get; set; } =
    [
        "dramatic",
        "moonlight",
        "deepSpace",
        "sunset",
        "cinematicBlue",
        "warmGlow"
    ];
    public bool PreventFakeAstronomy { get; set; } = true;
    public double MaximumObjectScaleBoost { get; set; } = 1.30;
    public string OutputFileName { get; set; } = "thumbnail-cinematic-ai-report.json";
}
