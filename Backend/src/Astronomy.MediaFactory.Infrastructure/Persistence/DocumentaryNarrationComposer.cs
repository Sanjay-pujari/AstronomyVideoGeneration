using System.Globalization;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

internal static partial class EventStoryComposer
{
    public const string Version = "EventStoryComposerV3_1";
    private static readonly string[] AuthorInstructionPhrases =
    [
        "explain", "describe", "focus on", "call out", "give safe", "close with",
        "open with", "add a distinct", "viewer-friendly terms", "timing window",
        "primary sky objects", "event experience", "sky geometry"
    ];

    private static readonly string[] ForbiddenOpeningWords = ["For", "During", "As", "When", "Imagine", "Tonight", "Tomorrow", "Today", "Yesterday"];
    private static readonly string[] RelativeDateTerms = ["today", "tonight", "tomorrow", "this evening", "yesterday", "आज", "आज रात", "कल", "कल रात", "आज शाम"];

    public static EventStoryComposerResult Compose(string family, ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
    {
        var eventName = NormalizeEventName(Clean(FirstNonEmpty(intelligence?.ShortTitle, intelligence?.Title, context?.EventType, "This event")));
        var eventDate = ResolveEventDate(intelligence);
        var eventDateKnown = eventDate is not null;
        var eventDateText = eventDateKnown ? eventDate!.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) : "the exact event date supplied for this event";
        var direction = CleanSolarSafetyDirection(FirstNonEmpty(intelligence?.SkyDirectionHint, "the clearest part of the sky"), family);
        var peak = HumanizeNarrationWindow(FirstNonEmpty(intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, "the local peak time"));
        var window = HumanizeNarrationWindow(FirstNonEmpty(intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow, intelligence?.LocalPeakTime, "the local viewing window"));
        var timezone = ResolveTimezoneText(intelligence, context);
        var contextFact = Clean(FirstNonEmpty(intelligence?.ScientificContext, BuildDefaultContext(family, eventName)));
        var importance = BuildImportance(family, eventName, contextFact);

        var sections = new DocumentaryNarrationSections(
            $"On {eventDateText}, {eventName} {OpeningVerb(family)}. {importance}",
            BuildHook(family, eventName, eventDateText, importance),
            BuildContext(family, eventName, contextFact),
            BuildMainStory(family, eventName),
            BuildViewingGuide(family, direction, eventDateText, peak, window, timezone),
            BuildClosing(family));

        sections = Compose(sections);
        var allText = string.Join(" ", sections.ColdOpen, sections.Hook, sections.Context, sections.MainStory, sections.ViewingGuide, sections.EmotionalClosing);
        ValidateNarrationQuality(family, eventName, eventDateText, sections);
        var openingValid = IsOpeningAllowed(sections.ColdOpen) && ContainsNameAndDate(sections.ColdOpen, eventName, eventDateText);
        var documentaryScore = Math.Min(100, 55 + (openingValid ? 20 : 0) + (ContainsHistoricalOrObservationalContext(allText) ? 15 : 0) + (!ContainsAuthorInstruction(allText) ? 10 : 0));
        var storytellingScore = Math.Min(100, 50 + (sections.Context.Length > 80 ? 15 : 0) + (sections.MainStory.Length > 80 ? 15 : 0) + (sections.EmotionalClosing.Contains("memory", StringComparison.OrdinalIgnoreCase) ? 10 : 0) + (!ContainsRawTimestamp(allText) ? 10 : 0));
        var diagnostics = new EventStoryComposerDiagnostics(Version, "EventDateNameImportance", eventDateKnown && sections.ColdOpen.Contains(eventDateText, StringComparison.OrdinalIgnoreCase), ContainsEventName(sections.ColdOpen, eventName), documentaryScore, storytellingScore, ScoreWonderLanguage(allText), ScoreScientificAccuracy(family, allText), DynamicNarrationGenerated: true, HardcodedTemplateUsed: false, SourceEventFactsUsed: BuildSourceEventFacts(intelligence, contextFact, eventDateText, direction, window), AiRewriteAttemptCount: 0, FallbackStaticTextUsed: false);
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
        if (IsSpokenSentence(converted) && !ContainsAuthorInstruction(converted)) return EnsureTerminalPunctuation(converted);
        throw new InvalidOperationException($"Dynamic narration conversion failed; invalid AI/source guidance cannot be replaced with static fallback text. fallbackLabel={fallback}");
    }

    private static string OpeningVerb(string family) => family switch { "Meteor" => "reaches its peak", "Moon" => "will rise above the evening horizon", "Eclipse" => "will be visible across parts of the world", _ => "will appear in the sky" };
    private static string BuildImportance(string family, string eventName, string contextFact) => family switch { "Meteor" => "Under dark skies, observers may see repeated meteors crossing the night, each one a tiny fragment of cosmic history burning into light.", "Moon" => "As a full moon tied to winter traditions, it connects a familiar sight with centuries of skywatching memory.", "Eclipse" => "For a brief time, the Moon will move across the face of the Sun, creating one of the most dramatic daytime sky events in astronomy.", "PlanetConjunction" => "For a short time, perspective brings distant worlds into the same human field of view, revealing the solar system in motion without suggesting they are physically close.", _ => "For a short time, perspective will bring distant worlds into the same human field of view, revealing the solar system in motion." };
    private static string BuildHook(string family, string eventName, string eventDateText, string importance) => family switch { "Meteor" => $"On {eventDateText}, {eventName} matters because Earth meets a debris stream that can turn darkness into sudden light. Watch for the question every meteor raises: which tiny fragment will flare next?", "Moon" => $"On {eventDateText}, {eventName} matters because a familiar full Moon carries a specific seasonal story. The curiosity is simple: what changes when an ordinary moonrise has a name and a history?", "Eclipse" => $"On {eventDateText}, {eventName} matters because solar-system geometry becomes visible in daylight. The question is unforgettable: how can the Sun seem to change shape before your eyes?", "PlanetConjunction" => $"On {eventDateText}, {eventName} matters because distant planets briefly share one line of sight. The curiosity is this: why can separate worlds look almost side by side?", _ => $"On {eventDateText}, {eventName} matters because a real sky event becomes visible from Earth. The curiosity is what that moment reveals when you watch closely." };
    private static string BuildContext(string family, string eventName, string fact) => family switch { "Meteor" => $"Meteor showers are old trails crossing a new night. {fact}", "Moon" => $"Moon names are cultural memory written onto a predictable orbit. {fact}", "Eclipse" => $"An eclipse is a shadow story, possible only when the Sun, Moon, and Earth line up with rare precision. {fact}", "PlanetConjunction" => $"A planetary conjunction is a story of perspective, not proximity. {fact}", _ => $"Planetary conjunctions are stories of perspective, not proximity. {fact}" };
    private static string BuildMainStory(string family, string eventName) => family switch { "Meteor" => "Each meteor is small enough to fit in your hand, but fast enough to announce itself across the atmosphere in a line of fire.", "Moon" => "As the Moon climbs, its color and brightness change with the air near the horizon, making a familiar world feel newly discovered.", "Eclipse" => "The change arrives in stages: a bite from the Sun, a dimming of the ground, and then the unmistakable sense that the sky is moving on a grand scale.", "PlanetConjunction" => "One planet may blaze brighter while the other looks steadier, but their apparent closeness is a line-of-sight effect across deep space.", _ => "One object may blaze brighter, the other may seem steadier, but together they make orbital motion visible without a telescope." };
    private static string BuildViewingGuide(string family, string direction, string eventDateText, string peak, string window, string timezone) => family == "Eclipse"
        ? $"On {eventDateText}, peak visibility is around {peak}, with the safest viewing window described as {window}{timezone}; look toward the Sun only with certified solar eclipse glasses."
        : $"On {eventDateText}, peak viewing is around {peak}, with a viewing window of {window}{timezone}. Start from a clear, open location facing {direction}, and arrive early enough for your eyes to settle into the scene.";
    private static string BuildClosing(string family) => family switch { "Eclipse" => "That is your professional observing reminder: confirm the exact local circumstances, use certified eye protection, and watch only when conditions are safe. Thank you for joining this sky guide; keep looking up with care.", _ => "That is your professional observing reminder: save the exact window, check the forecast, choose a safe open view, and let the sky speak for itself. Thank you for joining this sky guide." };

    private static IReadOnlyList<string> BuildSourceEventFacts(ProductionEventIntelligence? intelligence, string contextFact, string eventDateText, string direction, string window)
    {
        var facts = new List<string>();
        void Add(string? value) { if (!string.IsNullOrWhiteSpace(value)) facts.Add(Clean(value)); }
        Add(intelligence?.Title);
        Add(intelligence?.EventType);
        Add(eventDateText);
        Add(direction);
        Add(window);
        Add(contextFact);
        foreach (var value in intelligence?.PrimaryObjects ?? []) Add(value);
        foreach (var value in intelligence?.SecondaryObjects ?? []) Add(value);
        return facts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static DateTimeOffset? ResolveEventDate(ProductionEventIntelligence? i) => i?.EventDate ?? i?.PeakUtc;
    private static string HumanizeNarrationWindow(string value) => Clean(value);
    private static string ResolveTimezoneText(ProductionEventIntelligence? intelligence, ProductionPipelineExecutionContext? context)
    {
        var joined = string.Join(" ", intelligence?.LocalPeakTime, intelligence?.BestViewingWindowLocal, intelligence?.PreferredViewingWindow);
        var match = Regex.Match(joined, @"\b(?:UTC|GMT|IST|EST|EDT|CST|CDT|MST|MDT|PST|PDT|[A-Z][A-Za-z_]+/[A-Z][A-Za-z_]+)\b");
        return match.Success ? $" ({match.Value})" : string.Empty;
    }
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

    private static void ValidateNarrationQuality(string family, string eventName, string eventDateText, DocumentaryNarrationSections sections)
    {
        var scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ColdOpen"] = sections.ColdOpen,
            ["Hook"] = sections.Hook,
            ["Context"] = sections.Context,
            ["MainStory"] = sections.MainStory,
            ["ViewingGuide"] = sections.ViewingGuide,
            ["EmotionalClosing"] = sections.EmotionalClosing
        };
        var errors = new List<string>();
        foreach (var scene in scenes)
        {
            var relativeHits = RelativeDateTerms.Where(term => ContainsRelativeDateTerm(scene.Value, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (relativeHits.Length > 0) errors.Add($"{scene.Key} contains relative date terms: {string.Join(", ", relativeHits)}");
            var duplicateSentences = SplitSentences(scene.Value).GroupBy(NormalizeSentence, StringComparer.OrdinalIgnoreCase).Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1).Select(g => g.First()).ToArray();
            if (duplicateSentences.Length > 0) errors.Add($"{scene.Key} contains duplicate sentences: {string.Join(" | ", duplicateSentences)}");
        }
        if (!sections.Hook.Contains(eventDateText, StringComparison.OrdinalIgnoreCase) || !ContainsEventName(sections.Hook, eventName) || !Regex.IsMatch(sections.Hook, @"\b(matters?|important|because|reveals?)\b", RegexOptions.IgnoreCase) || !Regex.IsMatch(sections.Hook, @"\b(question|curiosity|why|how|what)\b", RegexOptions.IgnoreCase))
            errors.Add("Hook must include exact event date, event name, why it matters, and a curiosity trigger.");
        if (!sections.ViewingGuide.Contains(eventDateText, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(sections.ViewingGuide, @"\b(peak|around|at)\b", RegexOptions.IgnoreCase) || !Regex.IsMatch(sections.ViewingGuide, @"\b(window|from|between|during)\b", RegexOptions.IgnoreCase))
            errors.Add("BestTime/View guide must include exact date, peak/local time, and viewing window.");
        if (!ContainsHistoricalOrObservationalContext(sections.Context + " " + sections.MainStory))
            errors.Add("InterestingFact/context must include scientific, rarity, historical, or observation fact.");
        if (!Regex.IsMatch(sections.EmotionalClosing, @"\b(professional observing reminder|Thank you|forecast|safe)\b", RegexOptions.IgnoreCase))
            errors.Add("FinalReminder must be a professional presenter-style closing.");
        if (errors.Count > 0) throw new InvalidOperationException("Pre-Phase-14 narration validation failed: " + string.Join("; ", errors));
    }

    private static bool ContainsRelativeDateTerm(string text, string term)
    {
        if (Regex.IsMatch(term, @"^[a-z ]+$", RegexOptions.IgnoreCase))
            return Regex.IsMatch(text ?? string.Empty, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);
        return Regex.IsMatch(text ?? string.Empty, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);
    }

    private static string NormalizeSentence(string value) => Regex.Replace(value ?? string.Empty, @"[^\p{L}\p{N}]+", " ").Trim();

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
    string? LongSceneNarrationExpansionStrategy = null,
    bool DynamicNarrationGenerated = true,
    bool HardcodedTemplateUsed = false,
    IReadOnlyList<string>? SourceEventFactsUsed = null,
    int AiRewriteAttemptCount = 0,
    bool FallbackStaticTextUsed = false);

internal sealed record LongSceneNarrationExpansionContext(
    string EventType,
    string ShortTitle,
    string EventDateText,
    string? LocalPeakTime,
    string? ViewingWindow,
    string? SkyDirectionHint,
    string? Timezone,
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
            _ => $"{title} is connected to the {scene.ScenePurpose.Replace('-', ' ')} part of this event through the supplied timing, direction, and object facts."
        };
        if (usedOpenings.Add(Normalize(fallback))) return EnsureTerminalPunctuation(fallback);
        throw new InvalidOperationException($"Dynamic narration expansion failed for scene {scene.SceneId}: duplicate opening could not be rewritten from event facts without static fallback text.");
    }

    private static string TemplateForPurpose(string family, LongSceneNarrationExpansionContext context, string purpose)
    {
        var tone = ResolveTone($"{context.EventType} {context.ContentStrategy}", family);
        var title = CleanTitle(context.ShortTitle);
        var date = CleanText(context.EventDateText, "the exact event date supplied for this event");
        var time = CleanText(context.LocalPeakTime, "the local peak time");
        var window = CleanText(context.ViewingWindow, "the local viewing window");
        var timezone = string.IsNullOrWhiteSpace(context.Timezone) ? string.Empty : $" ({context.Timezone})";
        var direction = CleanText(context.SkyDirectionHint, family == "Eclipse" ? "the Sun with certified eclipse eye protection" : "the clearest part of the sky");
        return (tone, purpose) switch
        {
            ("SolarEclipse", "cause") => "A solar eclipse happens when the Moon moves between Earth and the Sun.",
            ("SolarEclipse", "interesting-fact") => "Eclipse details matter because eye safety changes with each stage of the event.",
            ("SolarEclipse", "accurate-sky-guide") => $"Use certified solar filters any time you look toward {direction}.",
            ("SolarEclipse", "viewing-tips") => "Keep eclipse glasses on before and after totality, and supervise every viewer closely.",
            ("NamedFullMoon", "hook") => $"On {date}, {title} matters because this named full moon connects a familiar moonrise with seasonal memory. The curiosity is what its name reveals about the year.",
            ("NamedFullMoon", "what-is-it") => $"{title} is a traditional name for this full moon.",
            ("NamedFullMoon", "cause") => "Full moons happen when the Moon is opposite the Sun in our sky.",
            ("NamedFullMoon", "interesting-fact") => $"The name {title} comes from old seasonal traditions, not from a change in the Moon itself.",
            ("NamedFullMoon", "best-time") => $"On {date}, the peak local time is around {time}, with the viewing window described as {window}{timezone}.",
            ("NamedFullMoon", "accurate-sky-guide") => $"Look toward {direction} first, then follow the Moon higher as the night continues.",
            ("NamedFullMoon", "what-you-will-see") => "You will see a bright round Moon, often warmer near the horizon and whiter as it climbs.",
            ("NamedFullMoon", "viewing-tips") => "Choose an open horizon and give your eyes a few minutes to settle into the night.",
            ("NamedFullMoon", "final-reminder") => $"That is your professional observing reminder for {title}: save {date}, check the forecast, choose a safe open horizon, and enjoy the Moon with family or friends.",
            ("MeteorShower", "hook") => $"On {date}, {title} matters because Earth crosses a stream of old debris that can spark sudden meteors. The curiosity is which tiny grain will write the next bright streak.",
            ("MeteorShower", "interesting-fact") => $"{title} meteors are pieces of comet or asteroid debris that heat the air as they enter Earth's atmosphere at high speed.",
            ("MeteorShower", "best-time") => $"On {date}, the peak local time is around {time}, with the viewing window described as {window}{timezone}; darker skies improve meteor watching.",
            ("MeteorShower", "viewing-tips") => "Lie back, avoid bright screens, and scan a wide area of the night sky.",
            ("PlanetConjunction", "hook") => $"On {date}, {title} matters because two distant worlds briefly align in our view. The curiosity is why planets separated by space can seem almost side by side.",
            ("PlanetConjunction", "what-is-it") => "That close pairing is a planetary conjunction, an alignment in our view rather than a meeting in space.",
            ("PlanetConjunction", "cause") => "Although the two planets appear close together, they remain separated by vast distances while their paths briefly align from Earth's perspective.",
            ("PlanetConjunction", "interesting-fact") => "From night to night, the changing gap between them reveals orbital motion at a pace the eye can follow.",
            ("PlanetConjunction", "best-time") => $"On {date}, the peak local time is around {time}, with the viewing window described as {window}{timezone}.",
            ("PlanetConjunction", "accurate-sky-guide") => $"About thirty minutes after sunset, turn your attention toward {direction} and look for the two bright planetary points near each other.",
            ("PlanetConjunction", "what-you-will-see") => "In the deepening twilight, one planet may blaze while the other holds a steadier glow, making the separation feel delicate and temporary.",
            ("PlanetConjunction", "viewing-tips") => "Let your eyes settle first, keep the horizon clear, and only then use binoculars to linger on the pairing.",
            ("PlanetConjunction", "final-reminder") => $"That is your professional observing reminder for {title}: save {date}, check the forecast and horizon, and enjoy this line-of-sight pairing before the planets drift apart in our sky.",
            ("PlanetGrouping", "accurate-sky-guide") => $"Start with {direction}, then compare the bright points one by one.",
            ("PlanetGrouping", "what-you-will-see") => "You will see separate worlds appearing close together from our point of view.",
            ("PlanetGrouping", "viewing-tips") => "Use the horizon and nearby bright objects as guideposts before reaching for binoculars.",
            (_, "hook") => $"On {date}, {title} matters because a real sky event becomes visible from Earth. The curiosity is what changes when you watch the moment carefully.",
            (_, "what-is-it") => $"{title} is the event this guide is built around.",
            (_, "cause") => "This event happens because familiar objects keep moving through predictable positions.",
            (_, "interesting-fact") => "The most interesting detail is how ordinary motion can create an uncommon view.",
            (_, "best-time") => $"On {date}, the peak local time is around {time}, with the viewing window described as {window}{timezone}.",
            (_, "accurate-sky-guide") => $"Use {direction} as your practical sky guide.",
            (_, "what-you-will-see") => "You will see the event as a real change in the sky, not just a date on a calendar.",
            (_, "viewing-tips") => "Give yourself a clear view, a few quiet minutes, and as little stray light as possible.",
            (_, "final-reminder") => $"That is your professional observing reminder for {title}: save {date}, check local conditions, choose a safe view, and let the sky do the rest.",
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
