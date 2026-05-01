using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Rendering;

public enum SsmlNarrationProfile
{
    DailySkyGuide,
    TelescopeTargets,
    SpaceNews,
    AstrophotographyTips,
    Shorts
}

public interface ISsmlBuilder
{
    string BuildSsml(string text, string voiceName, SsmlNarrationProfile? profile = null, string? rateOverride = null, string? pitchOverride = null);
}

public sealed partial class SsmlBuilder : ISsmlBuilder
{
    private static readonly (string Term, string Level)[] EmphasisTargets =
    [
        ("Moon", "strong"),
        ("Jupiter", "moderate"),
        ("Saturn", "moderate"),
        ("Nebula", "moderate"),
        ("look up", "strong"),
        ("tonight", "moderate")
    ];

    private static readonly Regex SentencePauseRegex = SentencePauseRegexFactory();
    private static readonly Regex CommaPauseRegex = CommaPauseRegexFactory();

    public string BuildSsml(string text, string voiceName, SsmlNarrationProfile? profile = null, string? rateOverride = null, string? pitchOverride = null)
    {
        var tuned = ResolveProfile(profile, rateOverride, pitchOverride);
        var escapedVoice = SecurityElement.Escape(voiceName) ?? "en-US-AriaNeural";
        var body = BuildBody(text, tuned);

        return $"""
                <speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">
                  <voice name=\"{escapedVoice}\">
                    <prosody rate=\"{tuned.Rate}\" pitch=\"{tuned.Pitch}\">{body}</prosody>
                  </voice>
                </speak>
                """;
    }

    private static string BuildBody(string text, NarrationTuning tuning)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var renderedParagraphs = paragraphs.Select(p => ApplyInlineMarkup(p.Trim(), tuning)).Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join($" <break time=\"{tuning.ParagraphPauseMs}ms\"/> ", renderedParagraphs);
    }

    private static string ApplyInlineMarkup(string text, NarrationTuning tuning)
    {
        var escapedText = SecurityElement.Escape(Regex.Replace(text, "\\s+", " ")) ?? string.Empty;
        escapedText = ApplyAstronomyEmphasis(escapedText);
        escapedText = CommaPauseRegex.Replace(escapedText, $"$1<break time=\"{tuning.CommaPauseMs}ms\"/> ");
        escapedText = SentencePauseRegex.Replace(escapedText, $"$1<break time=\"{tuning.SentencePauseMs}ms\"/> ");
        return escapedText.Trim();
    }

    private static string ApplyAstronomyEmphasis(string escapedText)
    {
        var emphasized = escapedText;
        foreach (var (term, level) in EmphasisTargets)
        {
            var pattern = $@"(?<!<emphasis level=""(?:moderate|strong)"">)\b({Regex.Escape(term)})\b(?!</emphasis>)";
            emphasized = Regex.Replace(emphasized, pattern, $"<emphasis level=\"{level}\">$1</emphasis>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return emphasized;
    }

    private static NarrationTuning ResolveProfile(SsmlNarrationProfile? profile, string? rateOverride, string? pitchOverride)
    {
        var baseTuning = profile switch
        {
            SsmlNarrationProfile.TelescopeTargets => new NarrationTuning("92%", "+3%", 450, 300, 900),
            SsmlNarrationProfile.SpaceNews => new NarrationTuning("92%", "+3%", 450, 300, 900),
            SsmlNarrationProfile.AstrophotographyTips => new NarrationTuning("92%", "+3%", 450, 300, 900),
            SsmlNarrationProfile.Shorts => new NarrationTuning("92%", "+3%", 450, 300, 600),
            _ => new NarrationTuning("92%", "+3%", 450, 300, 900)
        };

        return baseTuning with
        {
            Rate = string.IsNullOrWhiteSpace(rateOverride) ? baseTuning.Rate : rateOverride.Trim(),
            Pitch = string.IsNullOrWhiteSpace(pitchOverride) ? baseTuning.Pitch : pitchOverride.Trim()
        };
    }

    private sealed record NarrationTuning(string Rate, string Pitch, int SentencePauseMs, int CommaPauseMs, int ParagraphPauseMs);

    [GeneratedRegex("([,;:])(?!\\s*<break)")]
    private static partial Regex CommaPauseRegexFactory();

    [GeneratedRegex("([.!?])(?!\\s*<break)")]
    private static partial Regex SentencePauseRegexFactory();
}
