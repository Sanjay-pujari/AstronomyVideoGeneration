using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static class VisualPromptLocalizationService
{
    private static readonly (string English, string Hindi)[] HindiReplacements =
    [
        ("Closest Approach", "सबसे नज़दीकी स्थिति"),
        ("Planet Conjunction", "ग्रह संयोग"),
        ("Conjunction", "ग्रह संयोग"),
        ("Mars + Jupiter", "मंगल + बृहस्पति"),
        ("Jupiter + Mars", "बृहस्पति + मंगल"),
        ("Mars", "मंगल"),
        ("Jupiter", "बृहस्पति"),
        ("Venus", "शुक्र"),
        ("Saturn", "शनि"),
        ("Mercury", "बुध"),
        ("Moon", "चंद्रमा"),
        ("Sun", "सूर्य"),
        ("Object Labels", "वस्तु नाम"),
        ("Sky Guide Cue", "आकाश मार्गदर्शन"),
        ("Callouts", "संकेत"),
        ("Best time", "सबसे अच्छा समय"),
        ("Best Time", "सबसे अच्छा समय"),
        ("Direction", "दिशा"),
        ("Equipment", "उपकरण"),
        ("Distance", "दूरी"),
        ("Separation", "दूरी"),
        ("Date", "तारीख"),
        ("Apart", "दूर"),
        ("APART", "दूर"),
        ("Look SE", "दक्षिण-पूर्व दिशा"),
        ("Look Se", "दक्षिण-पूर्व दिशा"),
        ("Southeast", "दक्षिण-पूर्व दिशा"),
        ("Northeast", "उत्तर-पूर्व दिशा"),
        ("Western sky", "पश्चिमी आकाश"),
        ("Look", "देखें"),
        ("East", "पूर्व दिशा"),
        ("SE", "दक्षिण-पूर्व दिशा")
    ];

    public static string LocalizeText(string text, string? language)
    {
        if (!IsHindi(language) || string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        var result = text;
        foreach (var (english, hindi) in HindiReplacements)
            result = Regex.Replace(result, $"(?<![A-Za-z]){Regex.Escape(english)}(?![A-Za-z])", hindi, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"(\d+(?:\.\d+)?°)\s*दूर", "$1 दूर", RegexOptions.CultureInvariant);
        return result;
    }

    public static IReadOnlyList<string> LocalizeList(IEnumerable<string> values, string? language)
        => values.Select(value => LocalizeText(value, language)).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

    public static void ValidateHindiPrompt(string prompt, string? language)
    {
        if (!IsHindi(language)) return;
        var forbidden = new[] { "Look Se", "APART", "Object Labels", "Sky Guide Cue", "Mars + Jupiter", "Closest Approach" };
        var hits = forbidden.Where(term => prompt.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (hits.Length > 0)
            throw new InvalidOperationException("Hindi visual prompt contains forbidden English terms: " + string.Join(", ", hits));
    }

    private static bool IsHindi(string? language) => !string.IsNullOrWhiteSpace(language) && language.StartsWith("hi", StringComparison.OrdinalIgnoreCase);
}
