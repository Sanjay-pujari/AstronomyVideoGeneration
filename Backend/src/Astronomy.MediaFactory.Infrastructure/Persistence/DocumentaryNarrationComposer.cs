using System.Text.RegularExpressions;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class DocumentaryNarrationComposer
{
    private static readonly string[] AuthorInstructionPhrases =
    [
        "Open with", "Explain", "Focus on", "Describe", "Give safe", "Close with",
        "Call out", "Add a distinct", "Viewer-friendly terms", "Timing window",
        "Primary sky objects", "Event experience", "Sky geometry"
    ];

    public static DocumentaryNarrationSections Compose(DocumentaryNarrationSections input)
        => new(
            ConvertGuidanceToNarration(input.ColdOpen, "The sky story begins with a moment worth noticing."),
            ConvertGuidanceToNarration(input.Hook, "Stay with the sky for the timing, the view, and the reason this event matters."),
            ConvertGuidanceToNarration(input.Context, "This event is shaped by alignment, motion, and perspective across the visible sky."),
            ConvertGuidanceToNarration(input.MainStory, "As the moment unfolds, the scene changes slowly enough to follow and quickly enough to feel alive."),
            ConvertGuidanceToNarration(input.ViewingGuide, "Look during the local viewing window from a safe open location, and let the brightest landmarks guide your eyes."),
            ConvertGuidanceToNarration(input.EmotionalClosing, "When the moment passes, the sky keeps moving, but the memory of seeing it can stay with you."));

    public static string ConvertGuidanceToNarration(string? value, string fallback)
    {
        var source = value ?? string.Empty;
        var keptSentences = SplitSentences(source)
            .Select(RemoveRawTimestampText)
            .Select(sentence => sentence.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Where(sentence => !ContainsAuthorInstruction(sentence))
            .Select(CleanPromptLanguage)
            .Where(sentence => IsSpokenSentence(sentence) && !ContainsAuthorInstruction(sentence))
            .Select(EnsureTerminalPunctuation)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keptSentences.Length > 0)
            return string.Join(" ", keptSentences);

        var converted = CleanPromptLanguage(RemoveRawTimestampText(source));
        return IsSpokenSentence(converted) && !ContainsAuthorInstruction(converted)
            ? EnsureTerminalPunctuation(converted)
            : fallback;
    }

    private static IReadOnlyList<string> SplitSentences(string value)
        => SentenceSplitRegex().Split(value ?? string.Empty)
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

    private static string CleanPromptLanguage(string value)
    {
        var cleaned = value ?? string.Empty;
        foreach (var phrase in AuthorInstructionPhrases)
            cleaned = Regex.Replace(cleaned, @"\b" + Regex.Escape(phrase) + @"\b\s*(?:[:\-–—]|what|where|when|why|how|that|with)?", string.Empty, RegexOptions.IgnoreCase);

        return Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '–', '—');
    }

    private static string RemoveRawTimestampText(string value)
        => RawTimestampRegex().Replace(value ?? string.Empty, "the local viewing window");

    private static bool ContainsAuthorInstruction(string value)
        => AuthorInstructionPhrases.Any(phrase => !string.IsNullOrWhiteSpace(value) && value.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static bool IsSpokenSentence(string value)
        => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"[\p{L}\p{N}]", RegexOptions.CultureInvariant);

    private static string EnsureTerminalPunctuation(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.EndsWith(".", StringComparison.Ordinal) || trimmed.EndsWith("!", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ".";
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)?\b|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RawTimestampRegex();
}

internal sealed record DocumentaryNarrationSections(
    string ColdOpen,
    string Hook,
    string Context,
    string MainStory,
    string ViewingGuide,
    string EmotionalClosing);
