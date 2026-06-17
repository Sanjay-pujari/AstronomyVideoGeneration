using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class DocumentaryScriptComposer
{
    public const string Version = "DocumentaryScriptComposerV1";
    private static readonly string[] AuthorInstructionPhrases =
    [
        "explain", "describe", "focus on", "call out", "give safe", "close with",
        "open with", "add a distinct", "viewer-friendly terms", "timing window",
        "primary sky objects", "event experience", "sky geometry"
    ];

    private static readonly string[] ForbiddenOpeningWords = ["For", "During", "As", "When", "Imagine", "Look", "Tonight", "Tomorrow"];

    public static DocumentaryScriptComposerResult Compose(string family, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
    {
        var eventName = NormalizeEventName(Clean(FirstNonEmpty(intelligence?.ShortTitle, intelligence?.Title, context?.EventType, "This event")));
        var eventDate = ResolveEventDate(intelligence);
        var eventDateKnown = eventDate is not null;
        var eventDateText = eventDateKnown ? eventDate!.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) : "the event date";
        var direction = CleanSolarSafetyDirection(FirstNonEmpty(intelligence?.SkyDirectionHint, "the clearest part of the sky"), family);
        var window = HumanizeNarrationWindow(FirstNonEmpty(intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, intelligence?.LocalPeakTime, "the local viewing window"));
        var contextFact = Clean(FirstNonEmpty(intelligence?.ScientificContext, BuildDefaultContext(family, eventName)));
        var importance = BuildImportance(family, eventName, contextFact);

        var sections = new DocumentaryNarrationSections(
            $"On {eventDateText}, {eventName} {OpeningVerb(family)}. {importance}",
            BuildHook(family, eventName),
            BuildContext(family, eventName, contextFact),
            BuildMainStory(family, eventName),
            BuildViewingGuide(family, direction, window),
            BuildClosing(family));

        sections = Compose(sections);
        var allText = string.Join(" ", sections.ColdOpen, sections.Hook, sections.Context, sections.MainStory, sections.ViewingGuide, sections.EmotionalClosing);
        var openingValid = IsOpeningAllowed(sections.ColdOpen) && ContainsNameAndDate(sections.ColdOpen, eventName, eventDateText);
        var documentaryScore = Math.Min(100, 55 + (openingValid ? 20 : 0) + (ContainsHistoricalOrObservationalContext(allText) ? 15 : 0) + (!ContainsAuthorInstruction(allText) ? 10 : 0));
        var storytellingScore = Math.Min(100, 50 + (sections.Context.Length > 80 ? 15 : 0) + (sections.MainStory.Length > 80 ? 15 : 0) + (sections.EmotionalClosing.Contains("memory", StringComparison.OrdinalIgnoreCase) ? 10 : 0) + (!ContainsRawTimestamp(allText) ? 10 : 0));
        var diagnostics = new DocumentaryScriptComposerDiagnostics(Version, "EventDateNameImportance", eventDateKnown && sections.ColdOpen.Contains(eventDateText, StringComparison.OrdinalIgnoreCase), ContainsEventName(sections.ColdOpen, eventName), documentaryScore, storytellingScore);
        return new DocumentaryScriptComposerResult(sections, diagnostics);
    }

    public static DocumentaryNarrationSections Compose(DocumentaryNarrationSections input)
        => new(ConvertGuidanceToNarration(input.ColdOpen, "This event begins with a sky moment worth remembering."), ConvertGuidanceToNarration(input.Hook, "The view is brief, beautiful, and shaped by motion across the sky."), ConvertGuidanceToNarration(input.Context, "For centuries, people have used the sky as a calendar, a compass, and a source of wonder."), ConvertGuidanceToNarration(input.MainStory, "Above us, familiar worlds keep moving, turning a simple night outside into a live astronomy story."), ConvertGuidanceToNarration(input.ViewingGuide, "The strongest view comes during the local viewing window from a clear, open place."), ConvertGuidanceToNarration(input.EmotionalClosing, "The moment passes quickly, but the memory can stay with you for years."));

    public static string ConvertGuidanceToNarration(string? value, string fallback)
    {
        var source = value ?? string.Empty;
        var keptSentences = SplitSentences(source).Select(RemoveRawTimestampText).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Where(s => !ContainsAuthorInstruction(s)).Select(CleanPromptLanguage).Where(s => IsSpokenSentence(s) && !ContainsAuthorInstruction(s)).Select(EnsureTerminalPunctuation).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keptSentences.Length > 0) return string.Join(" ", keptSentences);
        var converted = CleanPromptLanguage(RemoveRawTimestampText(source));
        return IsSpokenSentence(converted) && !ContainsAuthorInstruction(converted) ? EnsureTerminalPunctuation(converted) : fallback;
    }

    private static string OpeningVerb(string family) => family switch { "Meteor" => "reaches its peak", "Moon" => "will rise above the evening horizon", "Eclipse" => "will be visible across parts of the world", _ => "will appear in the sky" };
    private static string BuildImportance(string family, string eventName, string contextFact) => family switch { "Meteor" => "Under dark skies, observers may see repeated meteors crossing the night, each one a tiny fragment of cosmic history burning into light.", "Moon" => "As a full moon tied to winter traditions, it connects a familiar sight with centuries of skywatching memory.", "Eclipse" => "For a brief time, the Moon will move across the face of the Sun, creating one of the most dramatic daytime sky events in astronomy.", _ => "For a short time, perspective will bring distant worlds into the same human field of view, revealing the solar system in motion." };
    private static string BuildHook(string family, string eventName) => family switch { "Meteor" => "The story begins quietly, then suddenly: a streak, a pause, and another flash where empty darkness seemed to be.", "Moon" => "Its light is familiar, but the first full moon of the year still changes the landscape, softening edges and pulling attention back to the horizon.", "Eclipse" => "Eclipses turn celestial mechanics into something physical, letting daylight itself become part of the drama.", _ => "To the eye, the planets may seem almost close enough to belong together, even though space keeps them separated by enormous distances." };
    private static string BuildContext(string family, string eventName, string fact) => family switch { "Meteor" => $"Meteor showers are old trails crossing a new night. {fact}", "Moon" => $"Moon names are cultural memory written onto a predictable orbit. {fact}", "Eclipse" => $"An eclipse is a shadow story, possible only when the Sun, Moon, and Earth line up with rare precision. {fact}", _ => $"Planetary conjunctions are stories of perspective, not proximity. {fact}" };
    private static string BuildMainStory(string family, string eventName) => family switch { "Meteor" => "Each meteor is small enough to fit in your hand, but fast enough to announce itself across the atmosphere in a line of fire.", "Moon" => "As the Moon climbs, its color and brightness change with the air near the horizon, making a familiar world feel newly discovered.", "Eclipse" => "The change arrives in stages: a bite from the Sun, a dimming of the ground, and then the unmistakable sense that the sky is moving on a grand scale.", _ => "One object may blaze brighter, the other may seem steadier, but together they make orbital motion visible without a telescope." };
    private static string BuildViewingGuide(string family, string direction, string window) => family == "Eclipse"
        ? $"The event reaches its strongest visibility during {window}; look toward the Sun only with certified solar eclipse glasses."
        : $"The best view comes from a clear, open location facing {direction}. The event reaches its strongest visibility during {window}, so arrive early enough for your eyes to settle into the scene.";
    private static string BuildClosing(string family) => family switch { "Eclipse" => "Moments like this remind us how dynamic our sky really is. The shadow passes quickly. But the memory can stay with you for years.", _ => "Moments like this reward patience and attention. The sky moves on. But the memory of seeing it can stay with you for years." };

    private static DateTimeOffset? ResolveEventDate(ProductionEventIntelligence? i) => i?.EventDate ?? i?.PeakUtc;
    private static string HumanizeNarrationWindow(string value) => ContainsRawTimestamp(value) ? "the local viewing window" : Clean(value);
    private static bool ContainsRawTimestamp(string value) => RawTimestampRegex().IsMatch(value ?? string.Empty);
    private static bool IsOpeningAllowed(string value) { var first = Clean(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty; return !ForbiddenOpeningWords.Contains(first, StringComparer.OrdinalIgnoreCase); }
    private static bool ContainsNameAndDate(string opening, string name, string date) => opening.Contains(date, StringComparison.OrdinalIgnoreCase) && ContainsEventName(opening, name);
    private static bool ContainsEventName(string opening, string name) => SignificantWords(name).Any(w => opening.Contains(w, StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<string> SignificantWords(string value) => Regex.Matches(value ?? string.Empty, @"[\p{L}\p{N}]{4,}").Select(m => m.Value).Where(w => !string.Equals(w, "event", StringComparison.OrdinalIgnoreCase));
    private static bool ContainsHistoricalOrObservationalContext(string text) => ContainsAny(text, ["centuries", "traditions", "observers", "horizon", "atmosphere", "telescope", "shadow", "perspective"]);
    private static bool ContainsAny(string text, IEnumerable<string> terms) => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static string BuildDefaultContext(string family, string eventName) => family switch { "Meteor" => "The shower comes from debris left along a comet or asteroid path.", "Moon" => "The full moon has long been used to mark seasons and passing months.", "Eclipse" => "Its path depends on the exact geometry of the Moon's shadow across Earth.", _ => "The alignment is created by changing orbital positions as seen from Earth." };
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
    private static IReadOnlyList<string> SplitSentences(string value) => SentenceSplitRegex().Split(value ?? string.Empty).Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
    private static string CleanPromptLanguage(string value) { var cleaned = value ?? string.Empty; foreach (var phrase in AuthorInstructionPhrases) cleaned = Regex.Replace(cleaned, @"\b" + Regex.Escape(phrase) + @"\b\s*(?:[:\-–—]|what|where|when|why|how|that|with)?", string.Empty, RegexOptions.IgnoreCase); return RemoveDoublePeriods(Regex.Replace(cleaned, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '–', '—')); }
    private static string RemoveRawTimestampText(string value) => RawTimestampRegex().Replace(value ?? string.Empty, "the local viewing window");
    private static bool ContainsAuthorInstruction(string value) => AuthorInstructionPhrases.Any(p => !string.IsNullOrWhiteSpace(value) && value.Contains(p, StringComparison.OrdinalIgnoreCase));
    private static bool IsSpokenSentence(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"[\p{L}\p{N}]", RegexOptions.CultureInvariant);
    private static string EnsureTerminalPunctuation(string value) { var t = RemoveDoublePeriods((value ?? string.Empty).Trim()); return t.EndsWith('.') || t.EndsWith('!') || t.EndsWith('?') ? t : t + "."; }
    private static string RemoveDoublePeriods(string value) => Regex.Replace(value ?? string.Empty, @"\.{2,}", ".");
    private static string NormalizeEventName(string value) => Regex.Replace(value ?? string.Empty, @"^Total Solar Eclipse\b", "a total solar eclipse", RegexOptions.IgnoreCase);
    private static string CleanSolarSafetyDirection(string value, string family)
    {
        var cleaned = Clean(value);
        if (family == "Eclipse" && cleaned.Equals("Sun direction during local daytime only; path-specific visibility required.", StringComparison.OrdinalIgnoreCase))
            return "the Sun only with certified solar eclipse glasses";
        return cleaned;
    }
    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex SentenceSplitRegex();
    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)?\b|\b\d{1,2}:\d{2}\s*(?:[+-]\d{2}:?\d{2}|UTC|GMT)\b", RegexOptions.IgnoreCase)] private static partial Regex RawTimestampRegex();
}

internal sealed record DocumentaryScriptComposerResult(DocumentaryNarrationSections Sections, DocumentaryScriptComposerDiagnostics Diagnostics);
internal sealed record DocumentaryScriptComposerDiagnostics(string ScriptComposerVersion, string OpeningStyle, bool EventDateMentioned, bool EventNameMentioned, int DocumentaryScore, int StorytellingScore);

internal sealed record DocumentaryNarrationSections(string ColdOpen, string Hook, string Context, string MainStory, string ViewingGuide, string EmotionalClosing);
