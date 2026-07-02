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
    public const string NegativePrompt = "text, typography, logos, UI, observation cards, icons, labels, buttons, CTA elements, panels, callouts, footers, watermarks, brand marks, captions, numerals, badges, data boxes, fake lettering, readable words, generic sky poster, placeholder panels, random planets, invented celestial objects, cropping";

    public const string PositiveArtworkOnlyInstruction = "AI artwork must be clean cinematic background artwork only. Do not render text, typography, logos, UI, observation cards, icons, labels, buttons, watermarks, brand marks, panels, callouts, or CTA elements; ThumbnailRenderer owns all final presentation.";
}
