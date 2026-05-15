namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailOptions
{
    public const string SectionName = "ThumbnailGeneration";

    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "CinematicComposed";
    public bool FallbackToExtractedFrame { get; init; } = true;

    public string LongThumbnailStyle { get; init; } = "CinematicPoster";
    public string ShortThumbnailStyle { get; init; } = "HighEnergyFrame";

    public int LandscapeWidth { get; init; } = 1280;
    public int LandscapeHeight { get; init; } = 720;

    public int PortraitWidth { get; init; } = 1080;
    public int PortraitHeight { get; init; } = 1920;

    public bool RejectDarkFrames { get; init; } = true;
    public double MaxBlackPixelPercentage { get; init; } = 0.40;
    public double MinimumBrightnessScore { get; init; } = 0.35;

    public bool EnableContrastBoost { get; init; } = true;
    public bool EnableGlowEnhancement { get; init; } = true;
    public bool EnableGlowEffect { get; init; } = true;
    public bool EnableVignette { get; init; } = true;
    public bool EnableSharpnessBoost { get; init; } = true;
    public bool EnableGradientBackground { get; init; } = true;

    public bool EnableHookText { get; init; } = true;
    public int MaxHookWords { get; init; } = 5;

    public bool EnableBranding { get; init; } = true;
    public string BrandText { get; init; } = "AstroPulse";

    public bool AvoidFadeFrames { get; init; } = true;
    public double FadeAvoidanceSeconds { get; init; } = 1.0;

    public int CandidateFramesPerScene { get; init; } = 3;

    public string LongThumbnailOutputName { get; init; } = "thumbnail-long.jpg";
    public string ShortThumbnailOutputName { get; init; } = "thumbnail-short.jpg";

    // Legacy aliases retained for older callers/tests that still rely on Width/Height.
    public int Width => LandscapeWidth;
    public int Height => LandscapeHeight;
}
