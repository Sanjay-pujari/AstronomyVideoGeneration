using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventProductionIntelligenceAdapter(IMediaEventStrategyResolver strategyResolver) : IEventProductionIntelligenceAdapter
{
    public ProductionEventIntelligence Normalize(ProductionPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Request;
        var seed = new ProductionEventIntelligence(
            Domain: "Astronomy",
            EventType: Clean(source.EventType, "AstronomyEvent"),
            Title: Clean(source.Title, "Astronomy event"),
            ShortTitle: Clean(source.ShortTitle, Shorten(source.Title)),
            EventDate: source.PeakUtc ?? source.StartUtc ?? source.ScheduledUtc,
            PeakUtc: source.PeakUtc,
            LocalPeakTime: BlankToNull(source.LocalPeakTime),
            BestViewingWindowLocal: BlankToNull(source.BestViewingWindowLocal),
            SkyDirectionHint: BlankToNull(source.SkyDirectionHint),
            VisibilityRegion: BlankToNull(source.VisibilityRegion) ?? source.RegionId,
            PrimaryObjects: CleanList(source.PrimaryObjects),
            SecondaryObjects: CleanList(source.SecondaryObjects),
            ViewingQuality: ResolveViewingQuality(source),
            MoonInterference: BlankToNull(source.MoonInterference),
            MoonIlluminationPercent: source.MoonIlluminationPercent,
            ScientificContext: ResolveScientificContext(source),
            ViewerInstructions: ResolveViewerInstructions(source),
            VisualMotifs: [],
            SceneStrategy: [],
            QualityWarnings: source.Warnings ?? [],
            ForbiddenTerms: []);

        var strategy = strategyResolver.Resolve(seed.EventType, seed.Title);
        var definition = strategy.BuildDefinition(seed);
        return seed with
        {
            VisualMotifs = definition.VisualMotifs,
            SceneStrategy = definition.SceneStoryArcShort.Concat(definition.SceneStoryArcLong).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ForbiddenTerms = definition.ForbiddenUnrelatedObjects,
            StrategyId = strategy.EventType,
            ResolvedObjectNames = seed.PrimaryObjects.Concat(seed.SecondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ForbiddenObjectNames = definition.ForbiddenUnrelatedObjects,
            RequiredVisualObjects = definition.RequiredVisualObjects ?? seed.PrimaryObjects,
            RequiredNarrationFacts = definition.RequiredNarrationFacts ?? definition.RequiredFactualFields,
            PreferredViewingWindow = seed.BestViewingWindowLocal ?? seed.LocalPeakTime,
            ViewingSafetyRules = definition.ViewingSafetyRules ?? [],
            ThumbnailCopyCandidates = definition.ThumbnailHooks,
            HeroCopyCandidates = definition.HeroCopyCandidates ?? definition.ThumbnailHooks,
            ShortSceneArc = definition.SceneStoryArcShort,
            LongSceneArc = definition.SceneStoryArcLong,
            ValidationRules = definition.ValidationRules,
            QualityWarnings = seed.QualityWarnings.Concat(BuildQualityWarnings(seed, definition)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IReadOnlyList<string> BuildQualityWarnings(ProductionEventIntelligence intelligence, MediaEventStrategyDefinition strategy)
    {
        var warnings = new List<string>();
        foreach (var field in strategy.RequiredFactualFields)
        {
            var missing = field switch
            {
                nameof(ProductionEventIntelligence.BestViewingWindowLocal) => string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal),
                nameof(ProductionEventIntelligence.SkyDirectionHint) => string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint),
                nameof(ProductionEventIntelligence.MoonInterference) => string.IsNullOrWhiteSpace(intelligence.MoonInterference),
                nameof(ProductionEventIntelligence.MoonIlluminationPercent) => !intelligence.MoonIlluminationPercent.HasValue,
                nameof(ProductionEventIntelligence.LocalPeakTime) => string.IsNullOrWhiteSpace(intelligence.LocalPeakTime),
                nameof(ProductionEventIntelligence.PeakUtc) => !intelligence.PeakUtc.HasValue,
                _ => false
            };
            if (missing) warnings.Add($"Production intelligence is missing recommended field '{field}' for {strategy.EventType}.");
        }
        return warnings;
    }

    private static string? ResolveViewingQuality(ContentPlanProductionPipelineRequest source)
        => source.VisibilityScore.HasValue ? $"Visibility score {source.VisibilityScore:0.##}/10" : null;

    private static string? ResolveScientificContext(ContentPlanProductionPipelineRequest source)
    {
        if (source.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase))
            return $"{source.Title} occurs when Earth crosses comet or asteroid debris, creating meteor streaks from the shower radiant.";
        if (source.EventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase))
            return $"{source.Title} is an eclipse event governed by Sun, Earth, and Moon geometry.";
        if (source.PrimaryObjects.Count > 0)
            return $"{source.Title} centers on {string.Join(", ", source.PrimaryObjects)}.";
        return source.Title;
    }

    private static IReadOnlyList<string> ResolveViewerInstructions(ContentPlanProductionPipelineRequest source)
    {
        var instructions = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.BestViewingWindowLocal)) instructions.Add($"Watch during {source.BestViewingWindowLocal}.");
        if (!string.IsNullOrWhiteSpace(source.SkyDirectionHint)) instructions.Add($"Look {source.SkyDirectionHint}.");
        if (source.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) instructions.Add("Use naked-eye viewing from a dark open location; no telescope is needed.");
        return instructions;
    }

    private static IReadOnlyList<string> CleanList(IEnumerable<string>? values)
        => values?.Select(v => Clean(v, string.Empty)).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Shorten(string? value)
    {
        var clean = Clean(value, "Astronomy event");
        return clean.Length <= 48 ? clean : clean[..48].Trim();
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Clean(value, string.Empty);
}

public sealed class MediaEventStrategyResolver(IEnumerable<IMediaEventStrategy> strategies) : IMediaEventStrategyResolver
{
    public IMediaEventStrategy Resolve(string eventType, string title)
        => strategies.FirstOrDefault(s => s.CanHandle(eventType, title)) ?? strategies.First(s => s is GenericAstronomyEventStrategy);
}

public abstract class MediaEventStrategyBase : IMediaEventStrategy
{
    public abstract string EventType { get; }

    public virtual QuestionQualityContract QuestionQualityContract => DefaultQuestionQualityContract;

    protected static QuestionQualityIntentGroup Intent(string intent, params string[] acceptedPhrases) => new(intent, acceptedPhrases);

    protected static readonly QuestionQualityContract DefaultQuestionQualityContract = new(
        WhatRequiredIntents:
        [
            Intent("opening overview", "will", "appears", "appear", "happening", "highlight", "sky", "event", "means"),
            Intent("viewer-visible outcome", "see", "watch", "view", "visible", "highlight", "sky", "appears", "streaks", "alignment")
        ],
        WhereRequiredIntents:
        [
            Intent("sky orientation", "north", "south", "east", "west", "horizon", "above", "sky", "visible", "open")
        ],
        WhenRequiredIntents:
        [
            Intent("viewer timing", "best viewing", "watch during", "stargazing is", "time", "window", "peak")
        ],
        HowRequiredIntents:
        [
            Intent("practical observing instruction", "find", "look", "use", "start", "scan", "locate", "face", "follow", "avoid", "eyes adjust", "binoculars", "certified eclipse glasses", "solar filters")
        ],
        WhyRequiredIntents:
        [
            Intent("event significance", "°", "angular separation", "rarity", "rare", "uncommon", "close pairing", "brightness", "bright", "alignment", "meteor", "full moon", "lunar", "eclipse", "culture", "scientific", "Milky Way", "dark sky")
        ],
        ActionRequiredIntents:
        [
            Intent("closing call to action", "step outside", "watch", "enjoy", "look", "view", "try", "mark", "clear skies", "reminder", "save", "check", "prepare", "choose", "plan", "pick")
        ]);
    public abstract bool CanHandle(string eventType, string title);
    public abstract MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence);
    public virtual QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => BuildGenericQuestionAnswerSet(intelligence, context);

    protected static QuestionAnswerSetDto CreateSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context, IReadOnlyList<QuestionAnswerDto> answers)
        => new(null, context.AstronomyEventIntelligenceId, context.EventCode, intelligence.Title, intelligence.EventType, context.RegionId, context.Language, context.Version, AstronomyQuestionSetStatus.Generated, context.GeneratedUtc, answers);

    protected static QuestionAnswerDto Answer(string type, string question, string title, string answer, int order)
        => new(null, type, question, title, Clean(answer), order);

    protected static string ObjectPhrase(ProductionEventIntelligence intelligence, string fallback = "the main sky target")
        => JoinNatural(Objects(intelligence, fallback).Take(3).ToArray());

    protected static string AllObjectsPhrase(ProductionEventIntelligence intelligence, string fallback = "the main sky target")
        => JoinNatural(Objects(intelligence, fallback));

    protected static string ViewingTime(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => !string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal)
            ? intelligence.BestViewingWindowLocal!
            : !string.IsNullOrWhiteSpace(intelligence.LocalPeakTime)
                ? intelligence.LocalPeakTime!
                : $"around {context.LocalPeakTime:h:mm tt} {context.TimeZoneAbbreviation}";

    protected static string Direction(ProductionEventIntelligence intelligence)
        => !string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint) ? intelligence.SkyDirectionHint! : "the clearest open sky";

    protected static string FormattedDirection(ProductionEventIntelligence intelligence)
        => FormatSkyDirection(Direction(intelligence));

    protected static QuestionAnswerSetDto BuildGenericQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
    {
        var objects = ObjectPhrase(intelligence);
        return CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{objects} will be the highlight in {context.LocationName}’s sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look toward {FormattedDirection(intelligence)} with a clear horizon.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best viewing is {ViewingTime(intelligence, context)}, near the peak of the event.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", $"Find {objects} first, then use {(!string.IsNullOrWhiteSpace(intelligence.ReferenceObject) ? intelligence.ReferenceObject : "a clear open horizon")} as your guide.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", $"This {Humanize(intelligence.EventType)} matters because it highlights a notable alignment or change in the sky.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Step outside", "If skies are clear, set a reminder and step outside at the best time.", 6)
        ]);
    }

    protected static string FormatSkyDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return "the clearest open sky";
        var normalized = direction.Trim().ToLowerInvariant()
            .Replace(" direction", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" sky", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized switch
        {
            "north" => "the northern sky",
            "northeast" or "north-east" => "the northeastern sky",
            "east" => "the eastern sky",
            "southeast" or "south-east" => "the southeastern sky",
            "south" => "the southern sky",
            "southwest" or "south-west" => "the southwestern sky",
            "west" => "the western sky",
            "northwest" or "north-west" => "the northwestern sky",
            _ => normalized
        };
    }

    protected static string FormatAltitude(decimal? altitude)
    {
        if (!altitude.HasValue) return "comfortably";
        var rounded = Math.Round(altitude.Value);
        return rounded switch
        {
            >= 25 and <= 35 => "about one-third",
            >= 15 and < 25 => "not far",
            > 35 and <= 55 => "about halfway",
            > 55 => "high",
            _ => "low"
        };
    }

    protected static string JoinNatural(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "the main sky target",
        1 => values[0],
        2 => $"{values[0]} and {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}"
    };

    protected static string Clean(string text) => Regex.Replace(text, "\\s+", " ").Trim();
    protected static string Humanize(string value) => string.IsNullOrWhiteSpace(value) ? "astronomy event" : value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
    protected static bool IsEvening(DateTimeOffset localPeak) => localPeak.Hour is >= 17 and <= 21;

    protected static string[] StandardQuestions =>
    [
        "What is happening?",
        "When is the best time to watch?",
        "Where should I look?",
        "How do I watch it?",
        "Why is this event special?",
        "What should I do now?"
    ];

    protected static string[] Objects(ProductionEventIntelligence intelligence, params string[] fallback)
        => intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects).Where(o => !string.IsNullOrWhiteSpace(o)).DefaultIfEmpty(fallback.FirstOrDefault() ?? "sky target").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}


public sealed class MeteorShowerStrategy : MediaEventStrategyBase
{
    public override string EventType => "MeteorShower";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("meteor overview", "meteor shower", "meteor streaks", "space debris", "shooting stars"), Intent("visible outcome", "producing", "will see", "bright meteor", "streaks")],
        WhereRequiredIntents: [Intent("radiant or open-sky direction", "north", "south", "east", "west", "overhead", "radiant", "anywhere", "dark sky", "open sky")],
        WhenRequiredIntents: [Intent("dark viewing window", "best viewing", "midnight", "pre-dawn", "darkest", "night", "00:", "01:", "02:", "03:", "04:", "05:")],
        HowRequiredIntents: [Intent("naked-eye dark-sky guidance", "no telescope", "naked eye", "dark location", "avoid city lights", "avoid bright lights", "eyes 20 minutes", "eyes adjust", "lie back", "watch patiently")],
        WhyRequiredIntents: [Intent("meteor-shower significance", "strongest annual meteor showers", "annual meteor shower", "meteor", "moon interference", "dark sky", "illumination", "viewing quality")],
        ActionRequiredIntents: [Intent("meteor viewing CTA", "set a reminder", "save", "check weather", "pick a dark", "choose a dark", "plan viewing", "dark open location")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase) || title.Contains("meteor", StringComparison.OrdinalIgnoreCase);

    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(
        EventType,
        StandardQuestions,
        ["Hook — peak alert", "What is happening — meteor shower explanation", "Best time — midnight to pre-dawn", "Where to look — radiant direction", "Viewing tips — dark sky, naked eye, moon impact", "Reminder CTA"],
        ["Intro hook", "What is the meteor shower?", "Why viewing quality is good locally", "Best local viewing window", "Where to look in the sky", "Viewing tips and expectations", "Safety/weather reminder", "CTA"],
        ["meteor streaks", "dark sky", "radiant hint", "open landscape", "local viewing context", "clean cinematic astronomy style"],
        [nameof(ProductionEventIntelligence.BestViewingWindowLocal), nameof(ProductionEventIntelligence.SkyDirectionHint), nameof(ProductionEventIntelligence.MoonInterference), nameof(ProductionEventIntelligence.MoonIlluminationPercent)],
        "urgent, wonder-led, practical, concise",
        ["Peak Night", "Midnight–Pre-dawn", "Low Moon", "Look East to Overhead"],
        ["Venus", "Jupiter", "conjunction", "planet pairing", "look west", "after sunset"],
        ["Use bestViewingWindowLocal instead of a daytime localPeakTime.", "Mention no telescope needed.", "Mention dark sky and moon interference."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
    {
        var showerName = intelligence.Title.Contains("meteor", StringComparison.OrdinalIgnoreCase) ? intelligence.Title : $"{intelligence.Title} meteor shower";
        var bestWindow = ViewingTime(intelligence, context);
        var direction = Direction(intelligence);
        var moonInterference = string.IsNullOrWhiteSpace(intelligence.MoonInterference) ? "low" : intelligence.MoonInterference!;
        var moonPhrase = intelligence.MoonIlluminationPercent.HasValue
            ? $"{moonInterference.ToLowerInvariant()} moon interference at about {Math.Round(intelligence.MoonIlluminationPercent.Value):0}% illumination"
            : $"{moonInterference.ToLowerInvariant()} moon interference";
        var reminder = FormatMeteorReminderNight(bestWindow, context.LocalPeakTime);
        return CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{showerName} peaks as Earth crosses space debris, producing bright meteor streaks.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look {direction}; {(!string.IsNullOrWhiteSpace(intelligence.ReferenceObject) ? intelligence.ReferenceObject : "meteors can appear anywhere across the dark sky")}.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time to watch?", "Best viewing time", $"Best viewing is {bestWindow}, when the sky is darkest.", 3),
            Answer(AstronomyQuestionTypes.How, "How do I watch it?", "How to observe", "No telescope is needed; avoid city lights, lie back, and give your eyes 20 minutes to adjust.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is this event special?", "Why it matters", $"{showerName} is one of the strongest annual meteor showers, with {moonPhrase} improving viewing quality.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Set a reminder", $"Set a reminder for {reminder}, check weather, and pick a dark open location.", 6)
        ]);
    }

    private static string FormatMeteorReminderNight(string bestWindow, DateTimeOffset localPeak)
    {
        var date = localPeak.Date;
        var match = Regex.Match(bestWindow ?? string.Empty, @"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})");
        if (match.Success && int.TryParse(match.Groups["year"].Value, out var year) && int.TryParse(match.Groups["month"].Value, out var month) && int.TryParse(match.Groups["day"].Value, out var day))
            date = new DateTime(year, month, day);
        return $"the night of {date.AddDays(-1):MMM d}/{date:dd}";
    }
}

public sealed class PlanetPairingStrategy : MediaEventStrategyBase
{
    public override string EventType => "PlanetPairing";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("pairing overview", "will appear close", "pairing", "close together", "sky")],
        WhereRequiredIntents: [Intent("direction and altitude", "north", "south", "east", "west", "horizon", "above")],
        WhenRequiredIntents: [Intent("viewer-friendly time", "best viewing", "AM", "PM", "IST", "shortly after sunset", "peak")],
        HowRequiredIntents: [Intent("object-finding instruction", "find", "look", "nearby", "binoculars", "horizon", "scan")],
        WhyRequiredIntents: [Intent("pairing significance", "°", "close pairing", "bright", "close together", "easy to notice")],
        ActionRequiredIntents: [Intent("pairing CTA", "set a reminder", "save", "check", "clear", "enjoy", "watch")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase) || title.Contains("planet pairing", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Objects", "Time", "Direction", "Separation", "CTA"], ["Intro", "Objects", "Geometry", "Timing", "Finding guide", "Viewing tips", "Photo tip", "CTA"], ["two bright planets", "twilight gradient", "horizon guide", "clean labels"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "clear, elegant, orientation-first", ["Close Pairing", "Look West", "Tonight"], ["meteor shower", "radiant", "eclipse shadow"], ["Name both planets and angular context."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
    {
        var objects = AllObjectsPhrase(intelligence, "the two sky objects");
        var names = Objects(intelligence, "the first object", "the second object");
        var how = names.Length >= 2 ? $"Find bright {names[0]} first, then look slightly nearby for {names[1]}; binoculars are optional." : $"Find {objects} near {FormattedDirection(intelligence)}; binoculars are optional.";
        var why = intelligence.AngularSeparationDegrees.HasValue ? $"{objects} appear only {intelligence.AngularSeparationDegrees.Value:0.##}° apart, creating a striking close pairing." : $"{objects} are bright objects appearing close together, making the pairing easy to notice.";
        return CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{objects} will appear close together in {context.LocationName}’s sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look toward {FormattedDirection(intelligence)}, {FormatAltitude(intelligence.AltitudeDegrees)} above the horizon.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best viewing is {ViewingTime(intelligence, context)}, {DescribeViewingTime(context.LocalPeakTime)}.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", how, 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", why, 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Step outside", "If skies are clear, set a reminder and enjoy the close pairing.", 6)
        ]);
    }

    private static string DescribeViewingTime(DateTimeOffset localPeak) => IsEvening(localPeak) ? "shortly after sunset" : "near the peak of the event";
}

public sealed class ConjunctionStrategy : MediaEventStrategyBase
{
    public override string EventType => "Conjunction";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("conjunction overview", "conjunction", "alignment", "form", "sky")],
        WhereRequiredIntents: [Intent("direction and altitude", "north", "south", "east", "west", "horizon", "above")],
        WhenRequiredIntents: [Intent("viewer-friendly time", "best viewing", "AM", "PM", "IST", "shortly after sunset", "peak")],
        HowRequiredIntents: [Intent("alignment finding instruction", "find", "scan", "look", "same part of the sky", "clear horizon")],
        WhyRequiredIntents: [Intent("alignment significance", "°", "alignment", "conjunction", "visually striking", "easy to compare")],
        ActionRequiredIntents: [Intent("conjunction CTA", "save", "watch", "clear", "set a reminder", "check")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || title.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "What aligns", "Best time", "Where", "How close", "CTA"], ["Intro", "Conjunction geometry", "Local timing", "Sky direction", "Finding guide", "Why it matters", "Viewing reminder", "CTA"], ["aligned objects", "subtle orbit lines", "horizon compass", "cinematic sky"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "precise, calm, factual", ["Close Conjunction", "Tonight", "Look Up"], ["meteor shower", "radiant"], ["Do not describe unrelated planets."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
    {
        var objects = AllObjectsPhrase(intelligence, "the conjunction objects");
        var names = Objects(intelligence, "the first object", "the second object");
        var how = names.Length >= 2 ? $"Find {names[0]} first, then scan nearby for {names[1]} in the same part of the sky." : $"Use a clear horizon and scan {FormattedDirection(intelligence)} for {objects}.";
        var why = intelligence.AngularSeparationDegrees.HasValue ? $"{objects} appear only {intelligence.AngularSeparationDegrees.Value:0.##}° apart, making the alignment visually striking." : $"{objects} appear close in the same part of our sky, making the bright conjunction easy to compare.";
        return CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{objects} form a conjunction, an apparent alignment in {context.LocationName}’s sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look toward {FormattedDirection(intelligence)}, {FormatAltitude(intelligence.AltitudeDegrees)} above the horizon.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best viewing is {ViewingTime(intelligence, context)}, {DescribeViewingTime(context.LocalPeakTime)}.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", how, 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", why, 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Step outside", "If skies are clear, save the time and watch the alignment.", 6)
        ]);
    }

    private static string DescribeViewingTime(DateTimeOffset localPeak) => IsEvening(localPeak) ? "shortly after sunset" : "near the peak of the event";
}

public sealed class NamedFullMoonStrategy : MediaEventStrategyBase
{
    public override string EventType => "NamedFullMoon";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("full-moon overview", "named full moon", "full moon", "fully illuminated", "Moon appears")],
        WhereRequiredIntents: [Intent("moonrise direction", "north", "south", "east", "west", "horizon", "moonrise")],
        WhenRequiredIntents: [Intent("moonrise time", "best viewing", "moonrise", "AM", "PM", "IST")],
        HowRequiredIntents: [Intent("moon finding instruction", "use the open horizon", "follow the bright Moon", "find", "look", "rises higher")],
        WhyRequiredIntents: [Intent("full-moon meaning", "full moon", "lunar", "culture", "seasonal", "public skywatching")],
        ActionRequiredIntents: [Intent("moon CTA", "save", "check clouds", "check weather", "prepare", "clear eastern view", "watch")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("FullMoon", StringComparison.OrdinalIgnoreCase) || title.Contains("full moon", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Moon name", "Rise time", "Where", "Viewing tip", "CTA"], ["Intro", "Name and meaning", "Local moonrise", "Direction", "Visual expectations", "Photo tips", "Weather note", "CTA"], ["large moon", "warm horizon", "landscape silhouette", "clean lunar labels"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "warm, cultural, observational", ["Full Moon Tonight", "Moonrise", "Look East"], ["meteor shower", "planet conjunction"], ["Use local moonrise or best viewing window."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{intelligence.Title} is a named full moon, when the Moon appears fully illuminated.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look toward {FormattedDirection(intelligence)} with an open horizon for moonrise.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best viewing is {ViewingTime(intelligence, context)}, when the full moon is easy to see.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", "Use the open horizon first, then follow the bright Moon as it rises higher.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", "A named full moon connects a bright lunar view with seasonal culture and public skywatching interest.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Watch moonrise", "Save the moonrise time, check clouds, and prepare a clear eastern view.", 6)
        ]);
}

public sealed class NewMoonStrategy : MediaEventStrategyBase
{
    public override string EventType => "NewMoon";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("new-moon overview", "New Moon", "Moon is hidden", "darker night sky", "dark sky")],
        WhereRequiredIntents: [Intent("dark-site direction", "dark open sky", "away from city lights", "north", "south", "east", "west", "sky")],
        WhenRequiredIntents: [Intent("dark-sky window", "best stargazing", "moonlight is absent", "dark-sky window", "21:", "22:", "23:", "00:", "01:", "02:", "03:", "04:")],
        HowRequiredIntents: [Intent("stargazing guidance", "eyes adjust", "scan", "darkest sky", "star map", "constellations")],
        WhyRequiredIntents: [Intent("dark-sky significance", "dark sky", "Milky Way", "faint stars", "clusters", "moonlight")],
        ActionRequiredIntents: [Intent("stargazing CTA", "save", "check weather", "prepare", "plan", "dark-sky window", "low-light observing spot")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("NewMoon", StringComparison.OrdinalIgnoreCase) || title.Contains("new moon", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Dark sky", "Best night", "Where", "What to see", "CTA"], ["Intro", "Why new moon matters", "Local dark window", "Best targets", "Viewing tips", "Safety/weather", "Planning reminder", "CTA"], ["dark sky", "Milky Way hint", "star field", "open landscape"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal)], "quiet, inviting, dark-sky focused", ["Darkest Night", "New Moon", "Stargazing"], ["full moon glare", "conjunction-only visuals"], ["Emphasize dark-sky opportunity."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", "New Moon means the Moon is hidden in glare, giving a darker night sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Choose a dark open sky away from city lights, especially toward {FormattedDirection(intelligence)}.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Best viewing time", $"Best stargazing is {ViewingTime(intelligence, context)}, when moonlight is absent.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I find it?", "How to observe", "Let your eyes adjust, scan the darkest sky, and use a star map for constellations.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", "New Moon matters because dark sky improves faint stars, clusters, and Milky Way viewing.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Plan stargazing", "Save the dark-sky window, check weather, and prepare a low-light observing spot.", 6)
        ]);
}

public sealed class LunarEclipseStrategy : MediaEventStrategyBase
{
    public override string EventType => "LunarEclipse";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("lunar-eclipse overview", "lunar eclipse", "Earth’s shadow", "Earth shadow", "Moon", "eclipse")],
        WhereRequiredIntents: [Intent("moon direction", "north", "south", "east", "west", "horizon", "Moon is visible")],
        WhenRequiredIntents: [Intent("phase timing", "watch during", "phase", "phases", "AM", "PM", "IST")],
        HowRequiredIntents: [Intent("phase watching guidance", "find the Moon", "watch each shadow phase", "binoculars", "closer view")],
        WhyRequiredIntents: [Intent("eclipse significance", "lunar eclipse", "Earth’s shadow", "copper red", "Moon", "eclipse")],
        ActionRequiredIntents: [Intent("eclipse CTA", "save", "check weather", "choose", "clear Moon-facing view", "phase times")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("LunarEclipse", StringComparison.OrdinalIgnoreCase) || title.Contains("lunar eclipse", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Eclipse type", "Timing", "Where", "Viewing safety", "CTA"], ["Intro", "Eclipse geometry", "Local phases", "Sky direction", "Color/brightness expectations", "Viewing tips", "Weather reminder", "CTA"], ["Moon in shadow", "Earth shadow arc", "red lunar tint", "phase timeline"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal), nameof(ProductionEventIntelligence.SkyDirectionHint)], "dramatic, precise, reassuring", ["Lunar Eclipse", "Watch Time", "Moon Turns Red"], ["solar filter instructions", "meteor radiant"], ["Use phase times if available."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{intelligence.Title} will show Earth’s shadow crossing the Moon in the sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where should I look?", "Where to look", $"Look toward {FormattedDirection(intelligence)} where the Moon is visible above the horizon.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Eclipse timing", $"Watch during {ViewingTime(intelligence, context)} to follow the eclipse phases.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I watch it?", "How to observe", "Find the Moon, watch each shadow phase, and use binoculars only for a closer view.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", "A lunar eclipse is special because Earth’s shadow can turn the Moon copper red.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Set a reminder", "Save the phase times, check weather, and choose a clear Moon-facing view.", 6)
        ]);
}

public sealed class SolarEclipseStrategy : MediaEventStrategyBase
{
    public override string EventType => "SolarEclipse";
    public override QuestionQualityContract QuestionQualityContract => new(
        WhatRequiredIntents: [Intent("solar-eclipse overview", "solar eclipse", "Moon covers", "Sun", "sky")],
        WhereRequiredIntents: [Intent("safe visibility guidance", "visible", "visibility", "Sun safely filtered", "local sky", "sky")],
        WhenRequiredIntents: [Intent("safe eclipse timing", "watch during", "certified eye protection", "AM", "PM", "IST")],
        HowRequiredIntents: [Intent("eye-safety instruction", "certified eclipse glasses", "solar filters", "eye protection", "view the Sun")],
        WhyRequiredIntents: [Intent("solar-eclipse significance", "solar eclipse", "rare", "dramatic", "Moon and Sun align", "align")],
        ActionRequiredIntents: [Intent("safe eclipse CTA", "check weather", "save", "prepare", "certified eclipse glasses", "before viewing")]);
    public override bool CanHandle(string eventType, string title) => eventType.Contains("SolarEclipse", StringComparison.OrdinalIgnoreCase) || title.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Eclipse type", "Timing", "Where visible", "Eye safety", "CTA"], ["Intro", "Eclipse geometry", "Local circumstances", "Visibility map", "Eye safety", "What to expect", "Weather reminder", "CTA"], ["Sun and Moon silhouette", "eclipse path", "certified eclipse glasses", "clean safety labels"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal), nameof(ProductionEventIntelligence.VisibilityRegion)], "urgent, safety-first, precise", ["Solar Eclipse", "Eye Safety", "Visible From"], ["meteor shower", "naked-eye Sun viewing"], ["Never imply direct Sun viewing without certified protection."]);

    public override QuestionAnswerSetDto BuildQuestionAnswerSet(ProductionEventIntelligence intelligence, QuestionAnswerSetBuildContext context)
        => CreateSet(intelligence, context,
        [
            Answer(AstronomyQuestionTypes.What, "What is happening?", "What you’ll see", $"{intelligence.Title} will happen when the Moon covers part of the Sun in the sky.", 1),
            Answer(AstronomyQuestionTypes.Where, "Where is it visible?", "Where visible", $"Use local sky visibility for {intelligence.VisibilityRegion ?? context.LocationName} and keep the Sun safely filtered.", 2),
            Answer(AstronomyQuestionTypes.When, "When is the best time?", "Eclipse timing", $"Watch during {ViewingTime(intelligence, context)} using certified eye protection throughout.", 3),
            Answer(AstronomyQuestionTypes.How, "How can I watch safely?", "Eye safety", "Use certified eclipse glasses or solar filters every time you view the Sun.", 4),
            Answer(AstronomyQuestionTypes.Why, "Why is it special?", "Why it matters", "A solar eclipse is rare and dramatic because Moon and Sun align from our viewpoint.", 5),
            Answer(AstronomyQuestionTypes.Action, "What should I do now?", "Prepare safely", "Check weather, save the time, and prepare certified eclipse glasses before viewing.", 6)
        ]);
}

public sealed class GenericAstronomyEventStrategy : MediaEventStrategyBase
{
    public override string EventType => "AstronomyEvent";
    public override bool CanHandle(string eventType, string title) => true;
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "What", "When", "Where", "How", "CTA"], ["Intro", "Event context", "Best time", "Sky direction", "Viewing guide", "Scientific context", "Reminder", "CTA"], ["cinematic night sky", "local horizon", "clean astronomy labels"], [nameof(ProductionEventIntelligence.LocalPeakTime)], "clear, factual, viewer-first", ["Sky Event", "Watch Time", "Look Up"], [], ["Avoid generic-only output; use event title, objects, timing, and direction."]);
}

public sealed class ProductionPipelineQualityValidator(IEventSceneValidationStrategyResolver sceneValidationStrategyResolver, IMediaEventStrategyResolver? mediaEventStrategyResolver = null) : IProductionPipelineQualityValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ProductionValidationResult> ValidateBeforeVideoAssemblyAsync(ProductionEventIntelligence intelligence, string eventWorkingRoot, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        ValidateTextQuality(intelligence, eventWorkingRoot, warnings, errors);
        ValidateScenePlan(intelligence, eventWorkingRoot, warnings, errors);
        ValidateSceneAssetStrategy(intelligence, eventWorkingRoot, warnings, errors);
        var titleValidationDiagnostics = BuildTitleValidationDiagnostics(intelligence, eventWorkingRoot, ReadAllText(eventWorkingRoot));
        await WriteValidationAsync(Path.Combine(eventWorkingRoot, "production-quality-validation-before-assembly.json"), intelligence, warnings, errors, cancellationToken, titleValidationDiagnostics: titleValidationDiagnostics);
        return new(errors.Count == 0, warnings, errors);
    }

    public async Task<ProductionValidationResult> ValidateFinalOutputAsync(ProductionEventIntelligence intelligence, string outputRoot, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var currentRunIntelligence = await LoadCurrentRunProductionEventIntelligenceAsync(outputRoot, intelligence, cancellationToken);
        var sourceNotes = await LoadCurrentRunSourceNotesAsync(outputRoot, cancellationToken);
        var strategy = (mediaEventStrategyResolver ?? CreateDefaultMediaEventStrategyResolver()).Resolve(currentRunIntelligence.EventType, currentRunIntelligence.Title);
        var strategyDefinition = strategy.BuildDefinition(currentRunIntelligence);

        ValidateTextQuality(currentRunIntelligence, outputRoot, warnings, errors, strategyDefinition, sourceNotes);
        var leakageHits = FindForbiddenLeakageHits(currentRunIntelligence, outputRoot, strategyDefinition, sourceNotes);
        foreach (var hit in leakageHits)
            errors.Add("Forbidden leakage detected: " + JsonSerializer.Serialize(hit, JsonOptions));

        foreach (var profile in new[] { "short", "long" })
        {
            var sceneRoot = Path.Combine(outputRoot, "scene-approval-v3", profile);
            if (!Directory.Exists(sceneRoot) || !Directory.EnumerateFiles(sceneRoot, "scene-*.png").Any()) errors.Add($"{profile} scenes were not materialized in the production plan folder.");
            var video = Path.Combine(outputRoot, "video-assembly", profile, profile == "short" ? "final-video-short.mp4" : "final-video-long.mp4");
            if (!File.Exists(video)) errors.Add($"{profile} final video is missing from the production plan folder.");
        }
        if (!File.Exists(Path.Combine(outputRoot, "hero", "hero.png"))) errors.Add("Hero image is missing from the production plan folder.");
        foreach (var file in new[] { "landscape.png", "square.png", "portrait.png" })
            if (!File.Exists(Path.Combine(outputRoot, "thumbnails", file))) errors.Add($"Thumbnail {file} is missing from the production plan folder.");
        var titleValidationDiagnostics = BuildTitleValidationDiagnostics(currentRunIntelligence, outputRoot, ReadFinalValidationText(outputRoot));
        await WriteValidationAsync(Path.Combine(outputRoot, "production-quality-validation-final.json"), currentRunIntelligence, warnings, errors, cancellationToken, strategy.EventType, leakageHits, titleValidationDiagnostics);
        return new(errors.Count == 0, warnings, errors);
    }

    private static IMediaEventStrategyResolver CreateDefaultMediaEventStrategyResolver()
        => new MediaEventStrategyResolver([
            new MeteorShowerStrategy(),
            new PlanetPairingStrategy(),
            new ConjunctionStrategy(),
            new NamedFullMoonStrategy(),
            new NewMoonStrategy(),
            new LunarEclipseStrategy(),
            new SolarEclipseStrategy(),
            new GenericAstronomyEventStrategy()
        ]);

    private static void ValidateTextQuality(ProductionEventIntelligence intelligence, string root, List<string> warnings, List<string> errors, MediaEventStrategyDefinition? strategyDefinition = null, IReadOnlyList<string>? sourceNotes = null)
    {
        var text = strategyDefinition is null ? ReadAllText(root) : ReadFinalValidationText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            warnings.Add($"No readable production text was found under {root} yet.");
            return;
        }
        var titleValidationDiagnostics = BuildTitleValidationDiagnostics(intelligence, root, text);
        if (!titleValidationDiagnostics.TitleFound) errors.Add("Output does not mention the event title or short title.");
        if (!HasBestViewingWindowEvidence(intelligence, text)) errors.Add("Output does not use bestViewingWindowLocal.");
        foreach (var stale in new[] { "Golden Pilot", "golden pilot" })
            if (ContainsToken(text, stale)) errors.Add($"Output contains stale Golden Pilot term '{stale}'.");
        if (strategyDefinition is null)
        {
            foreach (var forbidden in intelligence.ForbiddenTerms.Where(f => !string.IsNullOrWhiteSpace(f)))
                if (ContainsToken(text, forbidden)) errors.Add($"Output contains forbidden unrelated term '{forbidden}'.");
        }
    }

    private static async Task<ProductionEventIntelligence> LoadCurrentRunProductionEventIntelligenceAsync(string outputRoot, ProductionEventIntelligence fallback, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        if (!File.Exists(path)) return fallback;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProductionEventIntelligence>(stream, JsonOptions, cancellationToken) ?? fallback;
    }

    private static async Task<IReadOnlyList<string>> LoadCurrentRunSourceNotesAsync(string outputRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "plan-input", "content-plan-production-request.json");
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var request = await JsonSerializer.DeserializeAsync<ContentPlanProductionPipelineRequest>(stream, JsonOptions, cancellationToken);
        return request?.SourceNotes ?? [];
    }

    private static IReadOnlyList<string> ResolveForbiddenTermsForFinalValidation(ProductionEventIntelligence intelligence, MediaEventStrategyDefinition strategyDefinition, IReadOnlyList<string> sourceNotes)
    {
        var candidateTerms = strategyDefinition.ForbiddenUnrelatedObjects
            .Concat(["Golden Pilot", "golden pilot", "7:23 PM IST"])
            .Concat(intelligence.ForbiddenTerms)
            .Concat(intelligence.ForbiddenObjectNames ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var allowedEvidence = intelligence.PrimaryObjects
            .Concat(intelligence.SecondaryObjects)
            .Concat(sourceNotes)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return candidateTerms
            .Where(term => !allowedEvidence.Any(evidence => ContainsToken(evidence, term)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ForbiddenLeakageHit> FindForbiddenLeakageHits(ProductionEventIntelligence intelligence, string outputRoot, MediaEventStrategyDefinition strategyDefinition, IReadOnlyList<string> sourceNotes)
    {
        var forbiddenTerms = ResolveForbiddenTermsForFinalValidation(intelligence, strategyDefinition, sourceNotes);
        if (forbiddenTerms.Count == 0) return [];

        var hits = new List<ForbiddenLeakageHit>();
        foreach (var file in EnumerateFinalValidationScanFiles(outputRoot))
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbiddenTerms)
            {
                foreach (var hit in FindTermHits(file, text, term))
                    hits.Add(hit);
            }
        }

        return hits
            .GroupBy(hit => $"{hit.Term.ToLowerInvariant()}|{Path.GetFullPath(hit.File).ToLowerInvariant()}|{hit.Field}|{hit.Snippet}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(hit => hit.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.Term, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateFinalValidationScanFiles(string outputRoot)
    {
        if (!Directory.Exists(outputRoot)) return [];
        var includedRoots = new[]
        {
            Path.Combine(outputRoot, "plan-input"),
            Path.Combine(outputRoot, "question-engine"),
            Path.Combine(outputRoot, "scene-approval-v3"),
            Path.Combine(outputRoot, "hero"),
            Path.Combine(outputRoot, "thumbnails"),
            Path.Combine(outputRoot, "narration"),
            Path.Combine(outputRoot, "tts"),
            Path.Combine(outputRoot, "video-assembly"),
            Path.Combine(outputRoot, "validation")
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".md", ".srt", ".vtt", ".csv" };
        return includedRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(IsFinalValidationScanFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsFinalValidationScanFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("production-quality-validation-final.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals("phase-manifest.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(fileName, @"^phase-\d{2}-validation\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        if (fileName.Contains("fallback-template", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Contains("cached", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Contains($"{Path.DirectorySeparatorChar}fallback-templates{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static IEnumerable<ForbiddenLeakageHit> FindTermHits(string file, string text, string term)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        using var doc = TryParseJson(text);
        if (doc is not null)
        {
            foreach (var hit in FindJsonTermHits(file, doc.RootElement, "$", term))
                yield return hit;
            yield break;
        }

        foreach (Match match in TokenMatches(text, term))
            yield return new ForbiddenLeakageHit(term, NormalizePath(file), "text", BuildSnippet(text, match.Index, match.Length));
    }

    private static JsonDocument? TryParseJson(string text)
    {
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<ForbiddenLeakageHit> FindJsonTermHits(string file, JsonElement element, string field, string term, bool parentIsGeneratedContent = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsForbiddenLeakageMetadataField(property.Name)) continue;
                    var childField = field == "$" ? property.Name : $"{field}.{property.Name}";
                    var childIsGeneratedContent = parentIsGeneratedContent || IsGeneratedContentField(property.Name);
                    foreach (var hit in FindJsonTermHits(file, property.Value, childField, term, childIsGeneratedContent))
                        yield return hit;
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var hit in FindJsonTermHits(file, item, $"{field}[{index}]", term, parentIsGeneratedContent))
                        yield return hit;
                    index++;
                }
                break;
            case JsonValueKind.String:
                if (!parentIsGeneratedContent && !IsGeneratedContentField(LastJsonPathSegment(field))) yield break;
                var value = element.GetString() ?? string.Empty;
                foreach (Match match in TokenMatches(value, term))
                    yield return new ForbiddenLeakageHit(term, NormalizePath(file), field, BuildSnippet(value, match.Index, match.Length));
                break;
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string LastJsonPathSegment(string field)
    {
        var lastDot = field.LastIndexOf('.');
        var segment = lastDot >= 0 ? field[(lastDot + 1)..] : field;
        var bracket = segment.IndexOf('[');
        return bracket >= 0 ? segment[..bracket] : segment;
    }

    private static bool IsGeneratedContentField(string propertyName)
        => propertyName.Equals("title", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("subtitle", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("narration", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("narrationText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("text", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("overlayText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("prompt", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("purpose", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("description", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("scenePurpose", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("sceneText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("hook", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("cta", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("script", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("content", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("manifestContent", StringComparison.OrdinalIgnoreCase);

    private static bool IsForbiddenLeakageMetadataField(string propertyName)
        => propertyName.Equals("forbiddenTermsChecked", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("allowedTerms", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenTerms", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenObjectNames", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenUnrelatedObjects", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("validationRules", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("checks", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("ruleDescriptions", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenLeakageHits", StringComparison.OrdinalIgnoreCase);

    private static MatchCollection TokenMatches(string haystack, string needle)
    {
        var trimmed = needle.Trim();
        var escaped = Regex.Escape(trimmed);
        escaped = Regex.Replace(escaped, @"\s+", @"\s+");
        var startsWithToken = char.IsLetterOrDigit(trimmed[0]) || trimmed[0] == '_';
        var endsWithToken = char.IsLetterOrDigit(trimmed[^1]) || trimmed[^1] == '_';
        var pattern = $"{(startsWithToken ? @"(?<![\p{L}\p{N}_])" : string.Empty)}{escaped}{(endsWithToken ? @"(?![\p{L}\p{N}_])" : string.Empty)}";
        return Regex.Matches(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildSnippet(string text, int index, int length)
    {
        const int context = 60;
        var start = Math.Max(0, index - context);
        var end = Math.Min(text.Length, index + length + context);
        var snippet = text[start..end].Replace("\r", " ").Replace("\n", " ");
        snippet = Regex.Replace(snippet, @"\s+", " ").Trim();
        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }

    private sealed record ForbiddenLeakageHit(string Term, string File, string Field, string Snippet);

    private static void ValidateScenePlan(ProductionEventIntelligence intelligence, string eventWorkingRoot, List<string> warnings, List<string> errors)
    {
        var scenePlanPath = ResolveCurrentRunFile(eventWorkingRoot, "question-driven-scene-plan.json");
        if (!File.Exists(scenePlanPath))
        {
            errors.Add("Question-driven scene plan is missing before video assembly.");
            return;
        }
        var text = File.ReadAllText(scenePlanPath);
        if (!ContainsToken(text, intelligence.EventType) && !ContainsToken(text, intelligence.ShortTitle)) warnings.Add("Scene plan should include event type or short title.");
        foreach (var motif in intelligence.VisualMotifs.Take(3))
            if (!ContainsToken(text, motif.Split(' ')[0])) warnings.Add($"Scene plan may be missing visual motif '{motif}'.");
    }


    private void ValidateSceneAssetStrategy(ProductionEventIntelligence intelligence, string currentRunRoot, List<string> warnings, List<string> errors)
    {
        var sceneRoot = ResolveCurrentRunDirectory(currentRunRoot, "scene-approval-v3");
        if (!Directory.Exists(sceneRoot))
        {
            errors.Add("Current-run scene approval directory is missing before scene asset validation.");
            return;
        }

        foreach (var profile in new[] { "short", "long" })
        {
            var profileRoot = Path.Combine(sceneRoot, profile);
            if (!Directory.Exists(profileRoot))
            {
                errors.Add($"Current-run {profile} scene approval directory is missing.");
                continue;
            }

            if (!Directory.EnumerateFiles(profileRoot, "scene-*-final.png").Any()) errors.Add($"Current-run {profile} scene images are missing.");
        }

        var context = ReadCurrentRunSceneValidationContext(intelligence, currentRunRoot, sceneRoot);
        if (context.InfographicSpecs.Count == 0) errors.Add("Current-run scene infographic specs are missing.");
        if (context.ReviewJson.Count == 0) errors.Add("Current-run scene reviews are missing.");

        var strategy = sceneValidationStrategyResolver.Resolve(intelligence.EventType);
        var requirements = strategy.GetRequirements(intelligence);
        if (requirements.Count == 0) warnings.Add($"Scene validation strategy '{strategy.EventType}' returned no requirements.");

        if (!HasReadableSceneValidationText(context))
        {
            errors.Add($"{strategy.EventType} scene validation found no current-run infographic spec, narration, SRT, review, scene-plan, or enriched-scene-plan text.");
            return;
        }

        ValidateVisualSourceResolverMetadata(context, errors);

        var result = strategy.Validate(context);
        warnings.AddRange(result.Warnings);
        errors.AddRange(result.Errors);
    }


    private static void ValidateVisualSourceResolverMetadata(SceneValidationContext context, List<string> errors)
    {
        var required = (context.Intelligence.RequiredVisualObjects ?? [])
            .Concat(context.Intelligence.PrimaryObjects)
            .Concat(context.Intelligence.SecondaryObjects)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var (path, json) in context.InfographicSpecs)
        {
            using var doc = JsonDocument.Parse(json);
            var specText = json;
            if (required.Length > 0)
            {
                if (TryGetVisualSourceType(doc.RootElement, out var sourceType) && sourceType.Equals("GenericFallback", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Visual source validation failed for {Path.GetFileName(path)}: GenericFallback cannot be used when required visual objects exist.");

                foreach (var requiredObject in required)
                    if (!ContainsToken(specText, requiredObject))
                        errors.Add($"Visual source validation failed for {Path.GetFileName(path)}: required visual object '{requiredObject}' is missing from spec/metadata.");
            }

            foreach (var forbidden in context.Intelligence.ForbiddenObjectNames ?? [])
                if (ContainsToken(ExtractSpecGeneratedText(doc.RootElement), forbidden))
                    errors.Add($"Visual source validation failed for {Path.GetFileName(path)}: forbidden object name '{forbidden}' appears in generated spec content.");
        }
    }

    private static bool TryGetVisualSourceType(JsonElement root, out string sourceType)
    {
        sourceType = string.Empty;
        if (root.TryGetProperty("visualSourceResolution", out var resolution)
            && resolution.TryGetProperty("sourceType", out var sourceTypeProperty)
            && sourceTypeProperty.ValueKind == JsonValueKind.String)
        {
            sourceType = sourceTypeProperty.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sourceType);
        }
        if (root.TryGetProperty("strategyValidationFacts", out var facts)
            && facts.TryGetProperty("visualSourceType", out sourceTypeProperty)
            && sourceTypeProperty.ValueKind == JsonValueKind.String)
        {
            sourceType = sourceTypeProperty.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sourceType);
        }
        return false;
    }

    private static string ExtractSpecGeneratedText(JsonElement root)
    {
        var parts = new List<string>();
        foreach (var propertyName in new[] { "backgroundPrompt", "overlayText", "programmaticLayers", "accessibilityCues", "drawableVisualObjects" })
        {
            if (root.TryGetProperty(propertyName, out var property)) parts.Add(property.ToString());
        }
        return string.Join(' ', parts);
    }

    private static string ResolveCurrentRunFile(string root, string fileName)
    {
        var direct = Path.Combine(root, fileName);
        if (File.Exists(direct)) return direct;
        return Path.Combine(root, "question-engine", fileName);
    }

    private static string ResolveCurrentRunDirectory(string root, string directoryName)
    {
        var direct = Path.Combine(root, directoryName);
        if (Directory.Exists(direct)) return direct;
        return Path.Combine(root, "question-engine", directoryName);
    }

    private static SceneValidationContext ReadCurrentRunSceneValidationContext(ProductionEventIntelligence intelligence, string currentRunRoot, string sceneRoot)
    {
        return new(
            intelligence,
            currentRunRoot,
            sceneRoot,
            ReadMatchingFiles(sceneRoot, "scene-*-infographic-spec.json", SearchOption.TopDirectoryOnly),
            ReadMatchingFiles(sceneRoot, "scene-*-narration.txt", SearchOption.AllDirectories),
            ReadMatchingFiles(sceneRoot, "scene-*.srt", SearchOption.AllDirectories),
            ReadMatchingFiles(sceneRoot, "scene-*-review.json", SearchOption.TopDirectoryOnly),
            ReadScenePlanFiles(currentRunRoot),
            ReadSupplementalSceneFiles(sceneRoot));
    }

    private static bool HasReadableSceneValidationText(SceneValidationContext context)
        => context.InfographicSpecs.Count > 0
            || context.NarrationTexts.Count > 0
            || context.SrtFiles.Count > 0
            || context.ReviewJson.Count > 0
            || context.ScenePlanJson.Count > 0
            || context.SupplementalFiles.Count > 0;

    private static IReadOnlyDictionary<string, string> ReadScenePlanFiles(string currentRunRoot)
    {
        var candidates = new[]
        {
            ResolveCurrentRunFile(currentRunRoot, "question-driven-scene-plan.json"),
            ResolveCurrentRunFile(currentRunRoot, "question-driven-scene-plan.enriched.json"),
            ResolveCurrentRunFile(currentRunRoot, "question-driven-narration.json")
        };
        return ReadExistingFiles(candidates);
    }

    private static IReadOnlyDictionary<string, string> ReadSupplementalSceneFiles(string sceneRoot)
    {
        if (!Directory.Exists(sceneRoot)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return ReadExistingFiles(Directory.EnumerateFiles(sceneRoot, "*overlay*.json", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(sceneRoot, "*overlay*.txt", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(sceneRoot, "shortform-polish-validation.json", SearchOption.AllDirectories)));
    }

    private static IReadOnlyDictionary<string, string> ReadMatchingFiles(string root, string searchPattern, SearchOption searchOption)
        => Directory.Exists(root)
            ? ReadExistingFiles(Directory.EnumerateFiles(root, searchPattern, searchOption))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ReadExistingFiles(IEnumerable<string> paths)
        => paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private static bool HasBestViewingWindowEvidence(ProductionEventIntelligence intelligence, string text)
    {
        if (string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal)) return true;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (ContainsDaytimeLocalPeakOnly(intelligence, text)) return false;
        if (ContainsToken(text, intelligence.BestViewingWindowLocal)) return true;

        var normalizedText = NormalizeTimingText(text);
        var normalizedWindow = NormalizeTimingText(intelligence.BestViewingWindowLocal);
        if (!string.IsNullOrWhiteSpace(normalizedWindow) && normalizedText.Contains(normalizedWindow, StringComparison.OrdinalIgnoreCase)) return true;

        var cue = ExtractTimingCue(intelligence.BestViewingWindowLocal);
        if (!string.IsNullOrWhiteSpace(cue) && normalizedText.Contains(NormalizeTimingText(cue), StringComparison.OrdinalIgnoreCase)) return true;

        var isMeteor = intelligence.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase);
        if (isMeteor && normalizedText.Contains("midnight to pre-dawn", StringComparison.OrdinalIgnoreCase)) return true;
        if (isMeteor && normalizedText.Contains("best viewing window", StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrWhiteSpace(cue) && normalizedText.Contains(NormalizeTimingText(cue), StringComparison.OrdinalIgnoreCase))) return true;

        return false;
    }

    private static bool ContainsDaytimeLocalPeakOnly(ProductionEventIntelligence intelligence, string text)
    {
        if (!intelligence.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return false;
        var usesLocalPeakTime = !string.IsNullOrWhiteSpace(intelligence.LocalPeakTime) && ContainsToken(text, intelligence.LocalPeakTime);
        var usesKnownDaytimeOffsetPeak = Regex.IsMatch(text, @"\b11:30\s+\+05:30\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!usesLocalPeakTime && !usesKnownDaytimeOffsetPeak) return false;
        return !ContainsToken(text, intelligence.BestViewingWindowLocal)
            && !NormalizeTimingText(text).Contains(NormalizeTimingText(ExtractTimingCue(intelligence.BestViewingWindowLocal)), StringComparison.OrdinalIgnoreCase)
            && !NormalizeTimingText(text).Contains("midnight to pre-dawn", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractTimingCue(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\b\d{2}:\d{2}[–-]\d{2}:\d{2}\s+[A-Z]{2,5}\b");
        return match.Success ? match.Value : string.Empty;
    }

    private static string NormalizeTimingText(string value)
        => Regex.Replace((value ?? string.Empty).Replace('–', '-').Trim(), @"\s+", " ");

    private static bool ContainsAnyToken(string haystack, params string[] needles)
        => needles.Any(needle => ContainsToken(haystack, needle));

    private static TitleValidationDiagnostics BuildTitleValidationDiagnostics(ProductionEventIntelligence intelligence, string root, string fallbackText)
    {
        var aliases = BuildEventTitleAliases(intelligence);
        if (aliases.Count == 0) return new(false, false, false, false, false, false, false, true);

        var infographicFiles = Directory.Exists(root) ? EnumerateSceneInfographicSpecFiles(root).ToArray() : Array.Empty<string>();
        var reviewFiles = Directory.Exists(root) ? EnumerateSceneReviewMetadataFiles(root).ToArray() : Array.Empty<string>();

        var titleFoundInCaptionText = ContainsAnyAlias(ReadJsonPropertyText(infographicFiles, ["captionText"]), aliases);
        var titleFoundInViewerTakeaway = ContainsAnyAlias(ReadJsonPropertyText(infographicFiles, ["viewerTakeaway"]), aliases);
        var titleFoundInOverlayText = ContainsAnyAlias(ReadJsonPropertyText(infographicFiles, ["overlayText"]), aliases);
        var titleFoundInMetadata = ContainsAnyAlias(ReadJsonPropertyText(infographicFiles, ["eventShortTitle", "eventTitle", "title", "shortTitle", "astronomyEventShortTitle"]), aliases);
        var titleFoundInReview = ContainsAnyAlias(string.Join('\n', reviewFiles.Select(File.ReadAllText)), aliases);
        var titleFoundInOcr = ContainsAnyAlias(fallbackText, aliases);
        var titleFound = titleFoundInCaptionText
            || titleFoundInViewerTakeaway
            || titleFoundInOverlayText
            || titleFoundInMetadata
            || titleFoundInReview
            || titleFoundInOcr;

        return new(
            titleFoundInCaptionText,
            titleFoundInViewerTakeaway,
            titleFoundInOverlayText,
            titleFoundInMetadata,
            titleFoundInReview,
            titleFoundInOcr,
            titleFound,
            false);
    }

    private static IReadOnlyList<string> BuildEventTitleAliases(ProductionEventIntelligence intelligence)
        => new[] { intelligence.Title, intelligence.ShortTitle }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value.Length)
            .ToArray();

    private static string ReadJsonPropertyText(IEnumerable<string> files, IReadOnlyCollection<string> propertyNames)
    {
        var values = new List<string>();
        foreach (var file in files)
        {
            using var doc = TryParseJson(File.ReadAllText(file));
            if (doc is null) continue;
            CollectJsonPropertyText(doc.RootElement, propertyNames, values);
        }
        return string.Join('\n', values);
    }

    private static void CollectJsonPropertyText(JsonElement element, IReadOnlyCollection<string> propertyNames, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (propertyNames.Contains(property.Name)) CollectJsonTextValues(property.Value, values);
                    CollectJsonPropertyText(property.Value, propertyNames, values);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectJsonPropertyText(item, propertyNames, values);
                break;
        }
    }

    private static void CollectJsonTextValues(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectJsonTextValues(item, values);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectJsonTextValues(property.Value, values);
                break;
        }
    }

    private static bool ContainsAnyAlias(string haystack, IReadOnlyList<string> aliases)
        => !string.IsNullOrWhiteSpace(haystack) && aliases.Any(alias => ContainsToken(haystack, alias));

    private static IEnumerable<string> EnumerateSceneInfographicSpecFiles(string root)
        => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith("-infographic-spec.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> EnumerateSceneReviewMetadataFiles(string root)
        => Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).EndsWith("-review.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ReadFinalValidationText(string root)
        => Directory.Exists(root)
            ? string.Join('\n', EnumerateFinalValidationScanFiles(root).Take(300).Select(File.ReadAllText))
            : string.Empty;

    private static string ReadAllText(string root)
    {
        if (!Directory.Exists(root)) return string.Empty;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".md", ".srt" };
        return string.Join('\n', Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(IsCurrentProductionTextSource)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .Select(path => File.ReadAllText(path)));
    }

    private static bool IsCurrentProductionTextSource(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Contains("validation", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals("phase-manifest.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(fileName, @"^phase-\d{2}-validation\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        if (fileName.Equals("production-quality-validation-before-assembly.json", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals("production-quality-validation-final.json", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;

        if (ContainsTokenExactWhitespace(haystack, needle)) return true;

        var normalizedHaystack = NormalizeLooseMatchText(haystack);
        var normalizedNeedle = NormalizeLooseMatchText(needle);
        return !string.IsNullOrWhiteSpace(normalizedNeedle) && ContainsTokenExactWhitespace(normalizedHaystack, normalizedNeedle);
    }

    private static bool ContainsTokenExactWhitespace(string haystack, string needle)
    {
        var trimmed = needle.Trim();
        if (trimmed.Length == 0) return false;
        var escaped = Regex.Escape(trimmed);
        escaped = Regex.Replace(escaped, @"\s+", @"\s+");

        var startsWithToken = char.IsLetterOrDigit(trimmed[0]) || trimmed[0] == '_';
        var endsWithToken = char.IsLetterOrDigit(trimmed[^1]) || trimmed[^1] == '_';
        var pattern = $"{(startsWithToken ? @"(?<![\p{L}\p{N}_])" : string.Empty)}{escaped}{(endsWithToken ? @"(?![\p{L}\p{N}_])" : string.Empty)}";
        return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeLooseMatchText(string value)
        => Regex.Replace(Regex.Replace(value ?? string.Empty, @"[^\p{L}\p{N}_]+", " "), @"\s+", " ").Trim();

    private static async Task WriteValidationAsync(string path, ProductionEventIntelligence intelligence, List<string> warnings, List<string> errors, CancellationToken cancellationToken, string? strategyId = null, IReadOnlyList<ForbiddenLeakageHit>? forbiddenLeakageHits = null, TitleValidationDiagnostics? titleValidationDiagnostics = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            intelligence.Title,
            intelligence.EventType,
            strategyId = strategyId ?? intelligence.StrategyId,
            isValid = errors.Count == 0,
            warnings,
            errors,
            forbiddenLeakageHits = forbiddenLeakageHits ?? [],
            titleValidationDiagnostics,
            titleFoundInCaptionText = titleValidationDiagnostics?.TitleFoundInCaptionText,
            titleFoundInViewerTakeaway = titleValidationDiagnostics?.TitleFoundInViewerTakeaway,
            titleFoundInOverlayText = titleValidationDiagnostics?.TitleFoundInOverlayText,
            titleFoundInMetadata = titleValidationDiagnostics?.TitleFoundInMetadata,
            titleFoundInReview = titleValidationDiagnostics?.TitleFoundInReview,
            titleFoundInOcr = titleValidationDiagnostics?.TitleFoundInOcr
        }, JsonOptions), cancellationToken);
    }

    private sealed record TitleValidationDiagnostics(
        bool TitleFoundInCaptionText,
        bool TitleFoundInViewerTakeaway,
        bool TitleFoundInOverlayText,
        bool TitleFoundInMetadata,
        bool TitleFoundInReview,
        bool TitleFoundInOcr,
        bool TitleFound,
        bool TitleValidationSkipped);
}
