namespace Astronomy.MediaFactory.Core;

public sealed record ThumbnailTheme(
    string Name,
    string BackgroundOverlayColor,
    float BackgroundOverlayOpacity,
    string PrimaryTextColor,
    string SecondaryTextColor,
    string AccentColor,
    string CardFillColor,
    float CardFillOpacity,
    string CardStrokeColor,
    string BrandText,
    string FontFamily)
{
    public static ThumbnailTheme DiscoveryDarkGold { get; } = new(
        "Discovery Dark Gold",
        "#050914",
        0.42f,
        "#FFF8E6",
        "#D8E7FF",
        "#F4B83F",
        "#081224",
        0.76f,
        "#F4B83F",
        "Astronomy",
        "Arial");
}

public sealed record ThumbnailRendererInput(
    string ArtworkImagePath,
    string OutputImagePath,
    string OutputLayoutJsonPath,
    ThumbnailPromptContract PromptContract,
    PlatformStorytellingStrategy PlatformStorytellingStrategy,
    CompositionProfile CompositionProfile,
    ThumbnailTheme Theme,
    string? Subtitle = null,
    string? Cta = null);

public sealed record ThumbnailRenderResult(string ImagePath, string LayoutJsonPath, IReadOnlyList<ThumbnailRenderComponentLayout> Components);

public sealed record ThumbnailRenderComponentLayout(
    string Component,
    string Renderer,
    string Text,
    float X,
    float Y,
    float Width,
    float Height,
    int ZIndex,
    string SafeArea);

public static class ThumbnailArtworkPromptRules
{
    public const string NegativePrompt = "watermarks, brand marks, external logos, fake lettering, unreadable gibberish text, generic sky poster, placeholder empty panels, random planets, invented celestial objects, cropping, stretched objects, squeezed layout";

    public const string PositiveArtworkOnlyInstruction = "Generate final finished thumbnail image. Include all text, icons, panels, callouts, labels, and footer inside the image. The AI image must be the complete final thumbnail. No post-processing overlay will be added. All visible text must be crisp, localized, and mobile-readable.";
}
