using System.Text.Json;
using System.Text.RegularExpressions;
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
    public abstract bool CanHandle(string eventType, string title);
    public abstract MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence);

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
        ["Venus", "Jupiter", "conjunction", "planet pairing", "object pairing"],
        ["Use bestViewingWindowLocal instead of a daytime localPeakTime.", "Mention no telescope needed.", "Mention dark sky and moon interference."]);
}

public sealed class PlanetPairingStrategy : MediaEventStrategyBase
{
    public override string EventType => "PlanetPairing";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase) || title.Contains("planet pairing", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Objects", "Time", "Direction", "Separation", "CTA"], ["Intro", "Objects", "Geometry", "Timing", "Finding guide", "Viewing tips", "Photo tip", "CTA"], ["two bright planets", "twilight gradient", "horizon guide", "clean labels"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "clear, elegant, orientation-first", ["Close Pairing", "Look West", "Tonight"], ["meteor shower", "radiant", "eclipse shadow"], ["Name both planets and angular context."]);
}

public sealed class ConjunctionStrategy : MediaEventStrategyBase
{
    public override string EventType => "Conjunction";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase) || title.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "What aligns", "Best time", "Where", "How close", "CTA"], ["Intro", "Conjunction geometry", "Local timing", "Sky direction", "Finding guide", "Why it matters", "Viewing reminder", "CTA"], ["aligned objects", "subtle orbit lines", "horizon compass", "cinematic sky"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "precise, calm, factual", ["Close Conjunction", "Tonight", "Look Up"], ["meteor shower", "radiant"], ["Do not describe unrelated planets."]);
}

public sealed class NamedFullMoonStrategy : MediaEventStrategyBase
{
    public override string EventType => "NamedFullMoon";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("FullMoon", StringComparison.OrdinalIgnoreCase) || title.Contains("full moon", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Moon name", "Rise time", "Where", "Viewing tip", "CTA"], ["Intro", "Name and meaning", "Local moonrise", "Direction", "Visual expectations", "Photo tips", "Weather note", "CTA"], ["large moon", "warm horizon", "landscape silhouette", "clean lunar labels"], [nameof(ProductionEventIntelligence.LocalPeakTime), nameof(ProductionEventIntelligence.SkyDirectionHint)], "warm, cultural, observational", ["Full Moon Tonight", "Moonrise", "Look East"], ["meteor shower", "planet conjunction"], ["Use local moonrise or best viewing window."]);
}

public sealed class NewMoonStrategy : MediaEventStrategyBase
{
    public override string EventType => "NewMoon";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("NewMoon", StringComparison.OrdinalIgnoreCase) || title.Contains("new moon", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Dark sky", "Best night", "Where", "What to see", "CTA"], ["Intro", "Why new moon matters", "Local dark window", "Best targets", "Viewing tips", "Safety/weather", "Planning reminder", "CTA"], ["dark sky", "Milky Way hint", "star field", "open landscape"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal)], "quiet, inviting, dark-sky focused", ["Darkest Night", "New Moon", "Stargazing"], ["full moon glare", "conjunction-only visuals"], ["Emphasize dark-sky opportunity."]);
}

public sealed class LunarEclipseStrategy : MediaEventStrategyBase
{
    public override string EventType => "LunarEclipse";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("LunarEclipse", StringComparison.OrdinalIgnoreCase) || title.Contains("lunar eclipse", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Eclipse type", "Timing", "Where", "Viewing safety", "CTA"], ["Intro", "Eclipse geometry", "Local phases", "Sky direction", "Color/brightness expectations", "Viewing tips", "Weather reminder", "CTA"], ["Moon in shadow", "Earth shadow arc", "red lunar tint", "phase timeline"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal), nameof(ProductionEventIntelligence.SkyDirectionHint)], "dramatic, precise, reassuring", ["Lunar Eclipse", "Watch Time", "Moon Turns Red"], ["solar filter instructions", "meteor radiant"], ["Use phase times if available."]);
}

public sealed class SolarEclipseStrategy : MediaEventStrategyBase
{
    public override string EventType => "SolarEclipse";
    public override bool CanHandle(string eventType, string title) => eventType.Contains("SolarEclipse", StringComparison.OrdinalIgnoreCase) || title.Contains("solar eclipse", StringComparison.OrdinalIgnoreCase);
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "Eclipse type", "Timing", "Where visible", "Eye safety", "CTA"], ["Intro", "Eclipse geometry", "Local circumstances", "Visibility map", "Eye safety", "What to expect", "Weather reminder", "CTA"], ["Sun and Moon silhouette", "eclipse path", "certified eclipse glasses", "clean safety labels"], [nameof(ProductionEventIntelligence.BestViewingWindowLocal), nameof(ProductionEventIntelligence.VisibilityRegion)], "urgent, safety-first, precise", ["Solar Eclipse", "Eye Safety", "Visible From"], ["meteor shower", "naked-eye Sun viewing"], ["Never imply direct Sun viewing without certified protection."]);
}

public sealed class GenericAstronomyEventStrategy : MediaEventStrategyBase
{
    public override string EventType => "AstronomyEvent";
    public override bool CanHandle(string eventType, string title) => true;
    public override MediaEventStrategyDefinition BuildDefinition(ProductionEventIntelligence intelligence) => new(EventType, StandardQuestions, ["Hook", "What", "When", "Where", "How", "CTA"], ["Intro", "Event context", "Best time", "Sky direction", "Viewing guide", "Scientific context", "Reminder", "CTA"], ["cinematic night sky", "local horizon", "clean astronomy labels"], [nameof(ProductionEventIntelligence.LocalPeakTime)], "clear, factual, viewer-first", ["Sky Event", "Watch Time", "Look Up"], [], ["Avoid generic-only output; use event title, objects, timing, and direction."]);
}

public sealed class ProductionPipelineQualityValidator : IProductionPipelineQualityValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ProductionValidationResult> ValidateBeforeVideoAssemblyAsync(ProductionEventIntelligence intelligence, string eventWorkingRoot, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        ValidateTextQuality(intelligence, eventWorkingRoot, warnings, errors);
        ValidateScenePlan(intelligence, eventWorkingRoot, warnings, errors);
        await WriteValidationAsync(Path.Combine(eventWorkingRoot, "production-quality-validation-before-assembly.json"), intelligence, warnings, errors, cancellationToken);
        return new(errors.Count == 0, warnings, errors);
    }

    public async Task<ProductionValidationResult> ValidateFinalOutputAsync(ProductionEventIntelligence intelligence, string outputRoot, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        ValidateTextQuality(intelligence, outputRoot, warnings, errors);
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
        await WriteValidationAsync(Path.Combine(outputRoot, "production-quality-validation-final.json"), intelligence, warnings, errors, cancellationToken);
        return new(errors.Count == 0, warnings, errors);
    }

    private static void ValidateTextQuality(ProductionEventIntelligence intelligence, string root, List<string> warnings, List<string> errors)
    {
        var text = ReadAllText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            warnings.Add($"No readable production text was found under {root} yet.");
            return;
        }
        if (!ContainsToken(text, intelligence.ShortTitle) && !ContainsToken(text, intelligence.Title)) errors.Add("Output does not mention the event title or short title.");
        if (!string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal) && !ContainsToken(text, intelligence.BestViewingWindowLocal)) errors.Add("Output does not use bestViewingWindowLocal.");
        foreach (var forbidden in intelligence.ForbiddenTerms.Where(f => !string.IsNullOrWhiteSpace(f)))
            if (ContainsToken(text, forbidden)) errors.Add($"Output contains forbidden unrelated term '{forbidden}'.");
        if (intelligence.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase))
        {
            if (!ContainsToken(text, "meteor")) errors.Add("Meteor shower output must include meteor-specific terminology.");
            if (ContainsToken(text, "11:30 AM") || ContainsToken(text, "11:30AM")) errors.Add("Meteor shower output uses the incorrect daytime localPeakTime as best viewing time.");
            foreach (var required in new[] { "dark", "telescope", "moon" })
                if (!ContainsToken(text, required)) warnings.Add($"Meteor shower output should mention '{required}'.");
        }
    }

    private static void ValidateScenePlan(ProductionEventIntelligence intelligence, string eventWorkingRoot, List<string> warnings, List<string> errors)
    {
        var scenePlanPath = Path.Combine(eventWorkingRoot, "question-engine", "question-driven-scene-plan.json");
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

    private static string ReadAllText(string root)
    {
        if (!Directory.Exists(root)) return string.Empty;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".md" };
        return string.Join('\n', Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Take(200)
            .Select(path => File.ReadAllText(path)));
    }

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        var trimmed = needle.Trim();
        if (trimmed.Any(char.IsWhiteSpace) || trimmed.Any(ch => !char.IsLetterOrDigit(ch)))
            return haystack.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
        return Regex.IsMatch(haystack, $"(?<![\p{{L}}\p{{N}}]){Regex.Escape(trimmed)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static async Task WriteValidationAsync(string path, ProductionEventIntelligence intelligence, List<string> warnings, List<string> errors, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { generatedUtc = DateTimeOffset.UtcNow, intelligence.Title, intelligence.EventType, isValid = errors.Count == 0, warnings, errors }, JsonOptions), cancellationToken);
    }
}
