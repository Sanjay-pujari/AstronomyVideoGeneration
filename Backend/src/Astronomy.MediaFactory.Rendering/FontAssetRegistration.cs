using System.Reflection;
using System.Text;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;

namespace Astronomy.MediaFactory.Rendering;

public sealed record FontAssetDiagnostics(
    string RequestedFont,
    string ResolvedFont,
    string FontFile,
    bool GlyphSupport);

public static class FontAssetRegistration
{
    public const string DevanagariGlyphTest = "अआइईउऊएऐओऔकखग";

    public static FontFamily RegisterFont(
        IRuntimeAssetPathResolver assetPathResolver,
        string requestedFont,
        string glyphTest,
        ILogger? logger,
        out FontAssetDiagnostics diagnostics)
    {
        if (string.IsNullOrWhiteSpace(requestedFont))
            throw new InvalidOperationException("Configured font path/family is required.");

        var fontFile = assetPathResolver.ResolveFontPath(requestedFont);
        if (!File.Exists(fontFile))
            throw new FileNotFoundException($"Configured font must exist in the application fonts/assets directory: {fontFile}", fontFile);

        var collection = new FontCollection();
        var family = collection.Add(fontFile);
        var glyphSupport = SupportsGlyphs(family, glyphTest);
        diagnostics = new FontAssetDiagnostics(requestedFont, family.Name, fontFile, glyphSupport);

        logger?.LogInformation(
            "Font diagnostics: RequestedFont={RequestedFont}; ResolvedFont={ResolvedFont}; FontFile={FontFile}; GlyphSupport={GlyphSupport}",
            diagnostics.RequestedFont,
            diagnostics.ResolvedFont,
            diagnostics.FontFile,
            diagnostics.GlyphSupport);

        if (!glyphSupport)
            throw new InvalidOperationException($"Configured font does not contain all required glyphs. RequestedFont={diagnostics.RequestedFont}; ResolvedFont={diagnostics.ResolvedFont}; FontFile={diagnostics.FontFile}; GlyphSupport=false; GlyphTest={glyphTest}");

        return family;
    }

    public static bool SupportsGlyphs(FontFamily family, string glyphTest)
    {
        if (string.IsNullOrEmpty(glyphTest)) return true;

        foreach (var rune in glyphTest.EnumerateRunes())
        {
            if (!SupportsGlyph(family, rune.Value)) return false;
        }

        return true;
    }

    private static bool SupportsGlyph(FontFamily family, int codePoint)
    {
        var fontAssembly = typeof(FontFamily).Assembly;
        var codePointType = fontAssembly.GetType("SixLabors.Fonts.Unicode.CodePoint")
            ?? fontAssembly.GetType("SixLabors.Fonts.CodePoint");
        if (codePointType is null)
            throw new InvalidOperationException("Unable to verify font glyph support because SixLabors.Fonts CodePoint API was not found.");

        var codePoint = Activator.CreateInstance(codePointType, codePoint);
        var colorSupportType = fontAssembly.GetType("SixLabors.Fonts.ColorFontSupport");
        var colorSupport = colorSupportType is null ? null : Enum.ToObject(colorSupportType, 0);
        var methods = typeof(FontFamily).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var method in methods.Where(m => m.Name == "TryGetGlyphs"))
        {
            var parameters = method.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != codePointType) continue;

            object?[] args = parameters.Length == 3
                ? new object?[] { codePoint, colorSupport, null }
                : new object?[] { codePoint, null };
            if (method.Invoke(family, args) is bool supported)
                return supported;
        }

        foreach (var method in methods.Where(m => m.Name == "GetGlyphs"))
        {
            var parameters = method.GetParameters();
            if (parameters.Length < 1 || parameters[0].ParameterType != codePointType) continue;
            try
            {
                object?[] args = parameters.Length == 2 ? new object?[] { codePoint, colorSupport } : new object?[] { codePoint };
                return method.Invoke(family, args) is System.Collections.IEnumerable glyphs && glyphs.Cast<object>().Any();
            }
            catch (TargetInvocationException)
            {
                return false;
            }
        }

        throw new InvalidOperationException("Unable to verify font glyph support because SixLabors.Fonts glyph lookup API was not found.");
    }
}
