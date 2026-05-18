namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailOptions
{
    public const string SectionName = "ThumbnailGeneration";

    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "LocalAssetCollage";
    public string VisualPreset { get; init; } = "Premium Documentary";
    public string AssetRootPath { get; init; } = "assets/celestial";
    public string[] PreferredAssetFileNames { get; init; } = ["hero-transparent.png", "hero.png", "cinematic.png", "closeup.png"];
    public bool PreferPngAssets { get; init; } = true;
    public bool PreferAssetPackImages { get; init; } = true;
    public double DeepSpaceHeroPenalty { get; init; } = 0.45;
    public ThumbnailAtmosphereOptions Atmosphere { get; init; } = new();

    public int LongThumbnailWidth { get; init; } = 1280;
    public int LongThumbnailHeight { get; init; } = 720;
    public int ShortThumbnailWidth { get; init; } = 1080;
    public int ShortThumbnailHeight { get; init; } = 1920;

    public int MaxSupportObjectsLong { get; init; } = 2;
    public int MaxSupportObjectsShort { get; init; } = 1;

    public bool EnableStellariumBackground { get; init; } = true;
    public bool FallbackToStellariumFrame { get; init; } = true;
    public bool FallbackToExtractedFrame { get; init; } = true;
    public bool AllowLegacyFallbackOnFontFailure { get; init; }

    public bool EnableHookText { get; init; } = true;
    public int MaxHookWords { get; init; } = 4;

    public bool EnableBranding { get; init; } = true;
    public string BrandText { get; init; } = "AstroPulse";

    public int JpegQuality { get; init; } = 85;
    public long MaxFileSizeBytes { get; init; } = 2_097_152;

    public string LongThumbnailOutputName { get; init; } = "thumbnail-long.jpg";
    public string ShortThumbnailOutputName { get; init; } = "thumbnail-short.jpg";

    // Deprecated compatibility properties. They are intentionally not used by the
    // active LocalAssetCollage thumbnail flow, but remain bindable for older config.
    public string LongThumbnailStyle { get; init; } = "Deprecated";
    public string ShortThumbnailStyle { get; init; } = "Deprecated";
    public int LandscapeWidth { get; init; } = 1280;
    public int LandscapeHeight { get; init; } = 720;
    public int PortraitWidth { get; init; } = 1080;
    public int PortraitHeight { get; init; } = 1920;
    public bool RejectDarkFrames { get; init; } = true;
    public double MaxBlackPixelPercentage { get; init; } = 0.85;
    public double MinimumBrightnessScore { get; init; } = 0.08;
    public bool EnableAstronomySceneMode { get; init; } = true;
    public bool EnableContrastBoost { get; init; } = true;
    public bool EnableGlowEnhancement { get; init; } = true;
    public bool EnableGlowEffect { get; init; } = true;
    public bool EnableVignette { get; init; } = true;
    public bool EnableSharpnessBoost { get; init; } = true;
    public bool EnableGradientBackground { get; init; } = true;
    public bool AvoidFadeFrames { get; init; } = true;
    public double FadeAvoidanceSeconds { get; init; } = 1.5;
    public int CandidateFramesPerScene { get; init; } = 5;
    public string PrimaryFont { get; init; } = "";
    public string HindiFont { get; init; } = "";
    public string FallbackFont { get; init; } = "";
    public bool GenerateComparisonSheet { get; init; }

    public int Width => LongThumbnailWidth;
    public int Height => LongThumbnailHeight;
}

public sealed class ThumbnailAtmosphereOptions
{
    public double ProceduralShapeOpacity { get; init; } = 0.06;
    public double ProceduralShapeBlur { get; init; } = 120;
    public double ProceduralShapeContrast { get; init; } = 0.35;
}
