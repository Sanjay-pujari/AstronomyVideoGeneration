using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class EventStoryComposer
{
    public const string Version = "EventStoryComposerV1";
    private static readonly string[] AuthorInstructionPhrases =
    [
        "explain", "describe", "focus on", "call out", "give safe", "close with",
        "open with", "add a distinct", "viewer-friendly terms", "timing window",
        "primary sky objects", "event experience", "sky geometry"
    ];

    private static readonly string[] ForbiddenOpeningWords = ["For", "During", "As", "When", "Imagine", "Tonight", "Tomorrow"];

    public static EventStoryComposerResult Compose(string family, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
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
        var diagnostics = new EventStoryComposerDiagnostics(Version, "EventDateNameImportance", eventDateKnown && sections.ColdOpen.Contains(eventDateText, StringComparison.OrdinalIgnoreCase), ContainsEventName(sections.ColdOpen, eventName), documentaryScore, storytellingScore, ScoreWonderLanguage(allText), ScoreScientificAccuracy(family, allText));
        return new EventStoryComposerResult(sections, diagnostics);
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
    private static string BuildImportance(string family, string eventName, string contextFact) => family switch { "Meteor" => "Under dark skies, observers may see repeated meteors crossing the night, each one a tiny fragment of cosmic history burning into light.", "Moon" => "As a full moon tied to winter traditions, it connects a familiar sight with centuries of skywatching memory.", "Eclipse" => "For a brief time, the Moon will move across the face of the Sun, creating one of the most dramatic daytime sky events in astronomy.", "PlanetConjunction" => "For a short time, perspective brings distant worlds into the same human field of view, revealing the solar system in motion without suggesting they are physically close.", _ => "For a short time, perspective will bring distant worlds into the same human field of view, revealing the solar system in motion." };
    private static string BuildHook(string family, string eventName) => family switch { "Meteor" => "The story begins quietly, then suddenly: a streak, a pause, and another flash where empty darkness seemed to be.", "Moon" => "Its light is familiar, but the first full moon of the year still changes the landscape, softening edges and pulling attention back to the horizon.", "Eclipse" => "Eclipses turn celestial mechanics into something physical, letting daylight itself become part of the drama.", _ => "To the eye, the planets may seem almost close enough to belong together, even though space keeps them separated by enormous distances." };
    private static string BuildContext(string family, string eventName, string fact) => family switch { "Meteor" => $"Meteor showers are old trails crossing a new night. {fact}", "Moon" => $"Moon names are cultural memory written onto a predictable orbit. {fact}", "Eclipse" => $"An eclipse is a shadow story, possible only when the Sun, Moon, and Earth line up with rare precision. {fact}", "PlanetConjunction" => $"A planetary conjunction is a story of perspective, not proximity. {fact}", _ => $"Planetary conjunctions are stories of perspective, not proximity. {fact}" };
    private static string BuildMainStory(string family, string eventName) => family switch { "Meteor" => "Each meteor is small enough to fit in your hand, but fast enough to announce itself across the atmosphere in a line of fire.", "Moon" => "As the Moon climbs, its color and brightness change with the air near the horizon, making a familiar world feel newly discovered.", "Eclipse" => "The change arrives in stages: a bite from the Sun, a dimming of the ground, and then the unmistakable sense that the sky is moving on a grand scale.", "PlanetConjunction" => "One planet may blaze brighter while the other looks steadier, but their apparent closeness is a line-of-sight effect across deep space.", _ => "One object may blaze brighter, the other may seem steadier, but together they make orbital motion visible without a telescope." };
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
    private static string BuildDefaultContext(string family, string eventName) => family switch { "Meteor" => "The shower comes from debris left along a comet or asteroid path.", "Moon" => "The full moon has long been used to mark seasons and passing months.", "Eclipse" => "Its path depends on the exact geometry of the Moon's shadow across Earth.", "PlanetConjunction" => "Although the planets appear close together, they remain separated by vast distances in space.", _ => "The alignment is created by changing orbital positions as seen from Earth." };
    private static int ScoreWonderLanguage(string text) => Math.Min(100, 75 + (ContainsAny(text, ["wonder", "memory", "light", "horizon", "distant", "worlds", "motion"]) ? 15 : 0) + (ContainsPerspectiveStatement(text) ? 10 : 0));
    private static int ScoreScientificAccuracy(string family, string text) => Math.Min(100, 85 + (family == "PlanetConjunction" && ContainsPerspectiveStatement(text) ? 15 : 5));
    private static bool ContainsPerspectiveStatement(string text)
        => Regex.IsMatch(text ?? string.Empty, @"\b(appear|apparent|seem)\s+(?:near|close|together|closeness)\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text ?? string.Empty, @"\b(separated|distances?|space|line.of.sight|perspective|proximity)\b", RegexOptions.IgnoreCase);
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

internal sealed record EventStoryComposerResult(DocumentaryNarrationSections Sections, EventStoryComposerDiagnostics Diagnostics);
internal sealed record EventStoryComposerDiagnostics(
    string ScriptComposerVersion,
    string OpeningStyle,
    bool EventDateMentioned,
    bool EventNameMentioned,
    int DocumentaryScore,
    int StorytellingScore,
    int WonderScore = 0,
    int ScientificAccuracyScore = 0,
    int LongSceneCount = 0,
    int ExtractedSectionCount = 0,
    bool ExpansionApplied = false,
    bool DuplicateFirstSentenceDetected = false,
    IReadOnlyList<string>? DuplicatePairs = null,
    IReadOnlyDictionary<string, string>? FirstSentenceByLongScene = null,
    string? LongSceneNarrationExpansionStrategy = null);

internal sealed record LongSceneNarrationExpansionContext(
    string EventType,
    string ShortTitle,
    string? LocalPeakTime,
    string? SkyDirectionHint,
    string? ContentStrategy);

internal sealed record LongSceneNarrationDraft(string SceneId, string ScenePurpose, string BodyText);

internal static class LongSceneNarrationExpander
{
    private const int ExtractedNarrationSectionCount = 6;

    public static IReadOnlyDictionary<string, string> Expand(
        string family,
        LongSceneNarrationExpansionContext context,
        IReadOnlyList<LongSceneNarrationDraft> scenes,
        out string strategy)
    {
        if (scenes.Count <= ExtractedNarrationSectionCount && !string.Equals(family, "PlanetConjunction", StringComparison.OrdinalIgnoreCase))
        {
            strategy = "NotApplied";
            return scenes.ToDictionary(scene => scene.SceneId, scene => scene.BodyText, StringComparer.OrdinalIgnoreCase);
        }

        strategy = $"LongSceneNarrationExpander:purpose-templates:{ResolveTone($"{context.EventType} {context.ContentStrategy}", family)}";
        var usedOpenings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return scenes.ToDictionary(
            scene => scene.SceneId,
            scene => BuildExpandedNarration(family, context, scene, usedOpenings),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildExpandedNarration(string family, LongSceneNarrationExpansionContext context, LongSceneNarrationDraft scene, ISet<string> usedOpenings)
    {
        var first = UniqueFirstSentence(family, context, scene, usedOpenings);
        var body = RemoveDuplicateOpening(scene.BodyText, first);
        return string.IsNullOrWhiteSpace(body) ? first : $"{first} {body}";
    }

    private static string UniqueFirstSentence(string family, LongSceneNarrationExpansionContext context, LongSceneNarrationDraft scene, ISet<string> usedOpenings)
    {
        var baseSentence = TemplateForPurpose(family, context, scene.ScenePurpose);
        var normalized = Normalize(baseSentence);
        if (usedOpenings.Add(normalized)) return baseSentence;

        var title = CleanTitle(context.ShortTitle);
        var fallback = scene.ScenePurpose switch
        {
            "hook" => $"{title} gives this sky story a clear opening moment.",
            "what-is-it" => $"{title} is the event at the center of this guide.",
            "cause" => "The reason behind this event comes from predictable motion in the sky.",
            "interesting-fact" => "One useful detail makes this event easier to understand before you watch.",
            "best-time" => $"The best viewing time is tied to {CleanText(context.LocalPeakTime, "the local viewing window")}.",
            "accurate-sky-guide" => $"Use {CleanText(context.SkyDirectionHint, "the approved sky direction")} as your starting guide.",
            "what-you-will-see" => "The view itself should be simple enough to recognize with patient eyes.",
            "viewing-tips" => "A little preparation will make the viewing experience calmer and clearer.",
            "final-reminder" => $"{title} is worth one last careful look before the moment passes.",
            _ => $"This scene adds a distinct part of the {title} story."
        };
        if (usedOpenings.Add(Normalize(fallback))) return EnsureTerminalPunctuation(fallback);
        return EnsureTerminalPunctuation($"Scene {scene.SceneId} adds a separate {scene.ScenePurpose.Replace('-', ' ')} note for {title}");
    }

    private static string TemplateForPurpose(string family, LongSceneNarrationExpansionContext context, string purpose)
    {
        var tone = ResolveTone($"{context.EventType} {context.ContentStrategy}", family);
        var title = CleanTitle(context.ShortTitle);
        var time = CleanText(context.LocalPeakTime, "the best local viewing window");
        var direction = CleanText(context.SkyDirectionHint, family == "Eclipse" ? "the Sun with certified eclipse eye protection" : "the clearest part of the sky");
        return (tone, purpose) switch
        {
            ("SolarEclipse", "cause") => "A solar eclipse happens when the Moon moves between Earth and the Sun.",
            ("SolarEclipse", "interesting-fact") => "Eclipse details matter because eye safety changes with each stage of the event.",
            ("SolarEclipse", "accurate-sky-guide") => $"Use certified solar filters any time you look toward {direction}.",
            ("SolarEclipse", "viewing-tips") => "Keep eclipse glasses on before and after totality, and supervise every viewer closely.",
            ("NamedFullMoon", "hook") => "The first full moon of the year rises with a name many people recognize.",
            ("NamedFullMoon", "what-is-it") => $"{title} is a traditional name for this full moon.",
            ("NamedFullMoon", "cause") => "Full moons happen when the Moon is opposite the Sun in our sky.",
            ("NamedFullMoon", "interesting-fact") => $"The name {title} comes from old seasonal traditions, not from a change in the Moon itself.",
            ("NamedFullMoon", "best-time") => $"The best time to enjoy this full moon is during {time}.",
            ("NamedFullMoon", "accurate-sky-guide") => $"Look toward {direction} first, then follow the Moon higher as the night continues.",
            ("NamedFullMoon", "what-you-will-see") => "You will see a bright round Moon, often warmer near the horizon and whiter as it climbs.",
            ("NamedFullMoon", "viewing-tips") => "Choose an open horizon and give your eyes a few minutes to settle into the night.",
            ("NamedFullMoon", "final-reminder") => $"{title} is familiar, but that is exactly what makes it worth noticing.",
            ("MeteorShower", "hook") => $"{title} can turn a quiet dark sky into sudden streaks of light.",
            ("MeteorShower", "best-time") => $"Meteor watching is strongest during {time}, especially under darker skies.",
            ("MeteorShower", "viewing-tips") => "Lie back, avoid bright screens, and scan a wide area of the night sky.",
            ("PlanetConjunction", "hook") => $"{title} opens with two distant worlds sharing one quiet patch of sky.",
            ("PlanetConjunction", "what-is-it") => $"{title} is a planetary conjunction: a close apparent pairing as seen from Earth.",
            ("PlanetConjunction", "cause") => "A planetary conjunction happens when two planets line up near the same direction in our sky.",
            ("PlanetConjunction", "interesting-fact") => "Although the planets appear close together, they remain separated by vast distances in space.",
            ("PlanetConjunction", "best-time") => $"The best time to watch the conjunction is during {time}.",
            ("PlanetConjunction", "accurate-sky-guide") => $"Start with {direction}, then look for the two bright planetary points near each other.",
            ("PlanetConjunction", "what-you-will-see") => "You will see an apparent pairing, not two worlds physically approaching each other.",
            ("PlanetConjunction", "viewing-tips") => "Give the sky a few minutes, keep the horizon clear, and use binoculars only after finding the pair with your eyes.",
            ("PlanetConjunction", "final-reminder") => $"{title} is a reminder that distance and perspective can meet in a single human view.",
            ("PlanetGrouping", "accurate-sky-guide") => $"Start with {direction}, then compare the bright points one by one.",
            ("PlanetGrouping", "what-you-will-see") => "You will see separate worlds appearing close together from our point of view.",
            ("PlanetGrouping", "viewing-tips") => "Use the horizon and nearby bright objects as guideposts before reaching for binoculars.",
            (_, "hook") => $"{title} opens with a sky moment worth noticing.",
            (_, "what-is-it") => $"{title} is the event this guide is built around.",
            (_, "cause") => "This event happens because familiar objects keep moving through predictable positions.",
            (_, "interesting-fact") => "The most interesting detail is how ordinary motion can create an uncommon view.",
            (_, "best-time") => $"The best time to watch is during {time}.",
            (_, "accurate-sky-guide") => $"Use {direction} as your practical sky guide.",
            (_, "what-you-will-see") => "You will see the event as a real change in the sky, not just a date on a calendar.",
            (_, "viewing-tips") => "Give yourself a clear view, a few quiet minutes, and as little stray light as possible.",
            (_, "final-reminder") => $"{title} is brief enough to miss, and memorable enough to plan for.",
            _ => $"{title} deserves a distinct note for this part of the story."
        };
    }

    private static string ResolveTone(string eventType, string family)
    {
        var text = $"{eventType} {family}";
        if (text.Contains("Eclipse", StringComparison.OrdinalIgnoreCase)) return "SolarEclipse";
        if (text.Contains("Meteor", StringComparison.OrdinalIgnoreCase)) return "MeteorShower";
        if (text.Contains("Moon", StringComparison.OrdinalIgnoreCase)) return "NamedFullMoon";
        if (text.Contains("Conjunction", StringComparison.OrdinalIgnoreCase)) return "PlanetConjunction";
        if (text.Contains("Planet", StringComparison.OrdinalIgnoreCase)) return "PlanetGrouping";
        return "Generic";
    }

    private static string RemoveDuplicateOpening(string body, string opening)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (Normalize(FirstSentence(trimmed)) == Normalize(opening))
            return trimmed[FirstSentence(trimmed).Length..].Trim();
        return trimmed;
    }

    private static string FirstSentence(string text)
    {
        var match = Regex.Match(text.Trim(), @"^.+?[.!?](?:\s|$)");
        return match.Success ? match.Value.Trim() : text.Trim();
    }

    private static string CleanTitle(string value) => string.IsNullOrWhiteSpace(value) ? "This sky event" : Regex.Replace(value, @"\s+", " ").Trim();
    private static string CleanText(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : Regex.Replace(value, @"\s+", " ").Trim();
    private static string Normalize(string value) => Regex.Replace(value ?? string.Empty, @"[^a-z0-9]+", " ", RegexOptions.IgnoreCase).Trim();
    private static string EnsureTerminalPunctuation(string value) => Regex.IsMatch(value.Trim(), @"[.!?]$") ? value.Trim() : value.Trim() + ".";
}

internal sealed record DocumentaryNarrationSections(string ColdOpen, string Hook, string Context, string MainStory, string ViewingGuide, string EmotionalClosing);
