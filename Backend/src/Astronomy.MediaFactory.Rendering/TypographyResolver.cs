using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;
using SixLabors.Fonts;

namespace Astronomy.MediaFactory.Rendering;

public enum TypographyTextRole
{
    Title,
    Subtitle,
    Body,
    Footer,
    CTA,
    Badge
}

public enum TypographyAssetKind
{
    Hero,
    Thumbnail,
    Gallery,
    ObservationGuide,
    FutureVisualAsset
}

public interface ITypographyResolver
{
    ResolvedTypography Resolve(TypographyRequest request);
}

public sealed record TypographyRequest(
    string? Language,
    TypographyTextRole Role,
    TypographyAssetKind AssetKind,
    float BaseFontSize,
    FontStyle Style,
    float WrapWidth,
    int CanvasWidth,
    int CanvasHeight);

public sealed record ResolvedTypography(
    Font Font,
    string FontFamilyName,
    float FontSizeScale,
    float LineHeight,
    float BaselinePadding,
    float WrapWidth,
    float SafeMarginX,
    float SafeMarginY);

public sealed class TypographyResolver : ITypographyResolver
{
    private readonly TypographyOptions _options;

    public TypographyResolver(IOptions<TypographyOptions>? options = null)
    {
        _options = options?.Value ?? new TypographyOptions();
    }

    public ResolvedTypography Resolve(TypographyRequest request)
    {
        var language = NormalizeLanguage(request.Language);
        var profile = ResolveProfile(language);
        var roleOptions = profile.Roles.TryGetValue(request.Role.ToString(), out var configuredRole)
            ? configuredRole
            : new TypographyRoleOptions();
        var fontFamilyName = ResolveFamilyName(roleOptions.FontFamilies.Count > 0 ? roleOptions.FontFamilies : profile.FontFamilies, request.Style);
        var scale = roleOptions.FontSizeScale ?? profile.FontSizeScale;
        var size = request.BaseFontSize * scale;
        var style = ResolveAvailableStyle(fontFamilyName, request.Style);
        var family = SystemFonts.Get(fontFamilyName);
        var font = family.CreateFont(size, style);
        var safeX = Math.Max(0f, request.CanvasWidth * profile.SafeMarginScaleX);
        var safeY = Math.Max(0f, request.CanvasHeight * profile.SafeMarginScaleY);
        return new ResolvedTypography(
            font,
            fontFamilyName,
            scale,
            roleOptions.LineHeight ?? profile.LineHeight,
            roleOptions.BaselinePadding ?? profile.BaselinePadding,
            request.WrapWidth * (roleOptions.WrapWidthScale ?? profile.WrapWidthScale),
            safeX,
            safeY);
    }

    private TypographyLanguageOptions ResolveProfile(string language)
        => _options.Languages.TryGetValue(language, out var profile)
            ? profile
            : _options.Languages.TryGetValue("en", out var english) ? english : TypographyOptions.CreateEnglishDefaults();

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        var normalized = language.Trim().ToLowerInvariant();
        var dash = normalized.IndexOfAny(['-', '_']);
        return dash > 0 ? normalized[..dash] : normalized;
    }

    private static string ResolveFamilyName(IReadOnlyList<string> preferredNames, FontStyle requestedStyle)
    {
        foreach (var name in preferredNames)
        {
            if (SystemFonts.TryGet(name, out _)) return name;
        }

        foreach (var name in TypographyOptions.CreateEnglishDefaults().FontFamilies)
        {
            if (SystemFonts.TryGet(name, out _)) return name;
        }

        var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fallbackFamily.Name))
            throw new InvalidOperationException("No system fonts available for astronomy visual composition.");
        return fallbackFamily.Name;
    }

    private static FontStyle ResolveAvailableStyle(string familyName, FontStyle requestedStyle)
    {
        var family = SystemFonts.Get(familyName);
        try
        {
            _ = family.CreateFont(12, requestedStyle);
            return requestedStyle;
        }
        catch (FontException)
        {
            return FontStyle.Regular;
        }
    }
}
