namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record DocumentaryVisualLanguage
{
    public const string Version = "RC1-A.3";

    public required string LanguageVersion { get; init; }
    public required string StyleIdentity { get; init; }
    public required string RealismStandard { get; init; }
    public required string CompositionStandard { get; init; }
    public required string OverlayStandard { get; init; }
    public required string ProhibitedLanguage { get; init; }

    public string BuildPromptPolicyText() => string.Join(" ", new[]
    {
        $"Documentary Visual Language {LanguageVersion}.",
        StyleIdentity,
        RealismStandard,
        CompositionStandard,
        OverlayStandard,
        ProhibitedLanguage
    }).Trim();

    public static DocumentaryVisualLanguage Astronomy() => new()
    {
        LanguageVersion = Version,
        StyleIdentity = "Premium science documentary style: realistic, editorial, platform-native, and educational rather than fantasy or poster art.",
        RealismStandard = "Render celestial objects with scientifically respectful texture, circular geometry, natural color, and believable sky lighting.",
        CompositionStandard = "Use one clear primary visual subject, strong hierarchy, natural depth, atmosphere, and page or beat-specific editorial composition.",
        OverlayStandard = "Reserve clean negative space and deterministic safe overlay zones without generating any text in the image.",
        ProhibitedLanguage = "Avoid fantasy art, sci-fi poster language, cartoon styling, painterly planets, decorative clutter, and generated labels."
    };
}

public enum VisualPromptProduct
{
    Hero,
    Gallery,
    LongStoryFrame,
    ShortStoryFrame
}

public sealed record VisualPromptPolicy(
    string PositiveGuidance,
    string NegativeGuidance,
    string FrameworkVersion,
    string DocumentaryLanguageVersion,
    string DomainRuleVersion,
    IReadOnlyList<string> ProductsUpdated,
    bool PromptPolicyApplied,
    bool NegativePolicyApplied);

public sealed record VisualPromptPolicyReview(
    IReadOnlyList<string> ProductsUpdated,
    string FrameworkVersion,
    string DocumentaryLanguageVersion,
    string DomainRuleVersion,
    bool PromptPolicyApplied,
    bool NegativePolicyApplied,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);

public static class VisualPromptPolicyComposer
{
    public const string DomainRuleVersion = "Astronomy.RC1-A.3";

    public static VisualPromptPolicy Compose(VisualPromptProduct product)
    {
        var framework = VisualQualityFramework.Astronomy();
        var documentary = DocumentaryVisualLanguage.Astronomy();
        var productGuidance = product switch
        {
            VisualPromptProduct.Hero => "Hero guidance: one hero per frame, primary subject dominance, realistic celestial rendering, natural twilight or sky lighting, clean negative space for deterministic overlays, no embedded generated text, no fantasy, sci-fi, or poster-art language.",
            VisualPromptProduct.Gallery => "Gallery guidance: page-specific editorial composition, one clear primary visual subject per page, educational clarity, consistent documentary visual identity, safe area for overlays, no embedded generated text, and no decorative elements competing with the subject.",
            VisualPromptProduct.LongStoryFrame => "Long story frame guidance: native 16:9 landscape documentary composition, clear cinematic focal point, realistic celestial object rendering, beat-specific emotional intent, natural depth and atmosphere, safe overlay zones, and no portrait-derived framing.",
            VisualPromptProduct.ShortStoryFrame => "Short story frame guidance: native 9:16 portrait documentary composition, strong vertical hierarchy, fast visual comprehension, realistic celestial object rendering, beat-specific emotional intent, safe overlay zones, and no landscape-derived framing.",
            _ => string.Empty
        };

        var negative = string.Join(", ", new[]
        {
            framework.NegativePromptPolicy,
            "no fantasy art",
            "no sci-fi poster",
            "no cartoon",
            "no painterly planets",
            "no distorted planets",
            "no non-circular celestial bodies",
            "no fake labels or embedded text",
            "no oversaturated neon sky",
            "no random galaxies unless event-appropriate",
            "no Milky Way unless scientifically appropriate"
        });

        return new VisualPromptPolicy(
            string.Join(" ", framework.BuildPromptPolicyText(), documentary.BuildPromptPolicyText(), productGuidance).Trim(),
            negative,
            framework.FrameworkVersion,
            documentary.LanguageVersion,
            DomainRuleVersion,
            [product.ToString()],
            true,
            true);
    }

    public static VisualPromptPolicyReview CreateReview(params VisualPromptProduct[] products)
    {
        var framework = VisualQualityFramework.Astronomy();
        var documentary = DocumentaryVisualLanguage.Astronomy();
        var updated = products.Length == 0 ? Array.Empty<string>() : products.Select(p => p.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new VisualPromptPolicyReview(
            updated,
            framework.FrameworkVersion,
            documentary.LanguageVersion,
            DomainRuleVersion,
            updated.Length > 0,
            updated.Length > 0,
            [],
            ["Keep this shared composer as the single source for RC1-A visual-language prompt guidance; extend product-specific guidance here instead of duplicating policy text in builders."]);
    }
}
