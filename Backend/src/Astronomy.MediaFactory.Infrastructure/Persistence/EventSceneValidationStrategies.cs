using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class EventSceneValidationStrategyResolver(IEnumerable<IEventSceneValidationStrategy> strategies) : IEventSceneValidationStrategyResolver
{
    public IEventSceneValidationStrategy Resolve(string eventType)
        => strategies.FirstOrDefault(strategy => strategy.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase))
            ?? strategies.First(strategy => strategy is GenericEventSceneValidationStrategy);
}

public abstract class EventSceneValidationStrategyBase : IEventSceneValidationStrategy
{
    public abstract string EventType { get; }
    public abstract IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence);
    public abstract SceneValidationResult Validate(SceneValidationContext context);

    protected static SceneValidationResult Result(List<string> warnings, List<string> errors) => new(errors.Count == 0, warnings, errors);
    protected static SceneValidationRequirement Requirement(string code, string description) => new(code, description);

    protected static string AllText(SceneValidationContext context)
        => string.Join('\n', context.InfographicSpecs.Values
            .Concat(context.NarrationTexts.Values)
            .Concat(context.SrtFiles.Values)
            .Concat(context.ReviewJson.Values)
            .Concat(context.ScenePlanJson.Values)
            .Concat(context.SupplementalFiles.Values));

    protected static string AssetText(SceneValidationContext context)
        => string.Join('\n', context.InfographicSpecs.Values.Concat(context.ReviewJson.Values));

    protected static string OutputContentText(SceneValidationContext context)
        => string.Join('\n', context.InfographicSpecs.Values.Select(ExtractGeneratedJsonText)
            .Concat(context.ReviewJson.Values.Select(ExtractGeneratedJsonText))
            .Concat(context.NarrationTexts.Values)
            .Concat(context.SrtFiles.Values)
            .Concat(context.ScenePlanJson.Values.Select(ExtractGeneratedJsonText))
            .Concat(context.SupplementalFiles.Values.Select(ExtractGeneratedJsonText)));

    private static string ExtractGeneratedJsonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var values = new List<string>();
            CollectGeneratedJsonText(doc.RootElement, string.Empty, values, parentIsGeneratedContent: false);
            return string.Join(' ', values);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static void CollectGeneratedJsonText(JsonElement element, string propertyName, List<string> values, bool parentIsGeneratedContent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsValidationMetadataField(property.Name)) continue;
                    CollectGeneratedJsonText(property.Value, property.Name, values, parentIsGeneratedContent || IsGeneratedContentField(property.Name));
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectGeneratedJsonText(item, propertyName, values, parentIsGeneratedContent || IsGeneratedContentField(propertyName));
                break;
            case JsonValueKind.String:
                if (parentIsGeneratedContent || IsGeneratedContentField(propertyName)) values.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Number:
                if (parentIsGeneratedContent || IsGeneratedContentField(propertyName)) values.Add(element.ToString());
                break;
        }
    }

    private static bool IsGeneratedContentField(string propertyName)
        => propertyName.Equals("title", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("subtitle", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("viewerTakeaway", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("narrationText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("captionText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("backgroundPrompt", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("overlayText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("programmaticLayers", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("accessibilityCues", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("labels", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("arrows", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("localAssetObjects", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("directionMarkers", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("timingMarkers", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("steps", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("scenePurpose", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("viewerQuestion", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidationMetadataField(string propertyName)
        => propertyName.Equals("strategyValidationFacts", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("requiredVisualObjects", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenTerms", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenObjectNames", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("validationRules", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("checks", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("issues", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("recommendations", StringComparison.OrdinalIgnoreCase);


    protected static bool ContainsToken(string haystack, string? needle)
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

    protected static bool ContainsAnyToken(string haystack, params string[] needles)
        => needles.Any(needle => ContainsToken(haystack, needle));

    protected static void RequireAny(List<string> errors, string text, string error, params string[] tokens)
    {
        if (!ContainsAnyToken(text, tokens)) errors.Add(error);
    }

    protected static void RequireAllObjects(List<string> errors, string text, ProductionEventIntelligence intelligence, string label)
    {
        var objects = intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects).Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var objectName in objects)
            if (!ContainsToken(text, objectName)) errors.Add($"{label} must include actual object name '{objectName}'.");
    }

    protected static void RequireTimeAndDirection(List<string> errors, string text, ProductionEventIntelligence intelligence, string label)
    {
        if (!HasViewingWindowEvidence(intelligence, text) && !ContainsToken(text, intelligence.LocalPeakTime)) errors.Add($"{label} must include the correct viewing time/window.");
        if (!string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint) && !ContainsToken(text, intelligence.SkyDirectionHint)) errors.Add($"{label} must include the correct sky direction.");
    }

    protected static void RejectForbiddenLeakage(List<string> errors, string text, ProductionEventIntelligence intelligence, string label, params string[] extraForbidden)
    {
        foreach (var forbidden in intelligence.ForbiddenTerms
                     .Concat(intelligence.ForbiddenObjectNames ?? [])
                     .Concat(extraForbidden)
                     .Where(f => !string.IsNullOrWhiteSpace(f))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ContainsToken(text, forbidden)) errors.Add($"{label} contains forbidden unrelated term '{forbidden}'.");
        }
    }

    protected static bool HasViewingWindowEvidence(ProductionEventIntelligence intelligence, string text)
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
        return isMeteor && normalizedText.Contains("best viewing window", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cue) && normalizedText.Contains(NormalizeTimingText(cue), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDaytimeLocalPeakOnly(ProductionEventIntelligence intelligence, string text)
    {
        if (!intelligence.EventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return false;
        var usesLocalPeakTime = !string.IsNullOrWhiteSpace(intelligence.LocalPeakTime) && ContainsToken(text, intelligence.LocalPeakTime);
        var usesKnownDaytimeOffsetPeak = Regex.IsMatch(text, @"\b11:30\s+\+05:30\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!usesLocalPeakTime && !usesKnownDaytimeOffsetPeak) return false;
        return !ContainsToken(text, intelligence.BestViewingWindowLocal)
            && !NormalizeTimingText(text).Contains(NormalizeTimingText(ExtractTimingCue(intelligence.BestViewingWindowLocal ?? string.Empty)), StringComparison.OrdinalIgnoreCase)
            && !NormalizeTimingText(text).Contains("midnight to pre-dawn", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractTimingCue(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\b\d{2}:\d{2}[–-]\d{2}:\d{2}\s+[A-Z]{2,5}\b");
        return match.Success ? match.Value : string.Empty;
    }

    private static string NormalizeTimingText(string value)
        => Regex.Replace((value ?? string.Empty).Replace('–', '-').Trim(), @"\s+", " ");
}

public sealed class MeteorShowerSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "MeteorShower";

    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("bestViewingWindowLocal", "Include bestViewingWindowLocal or a normalized equivalent."),
        Requirement("meteorStreaks", "Show and describe meteor streaks."),
        Requirement("radiantHint", "Include a radiant hint."),
        Requirement("darkSky", "Use dark-sky viewing guidance."),
        Requirement("noUnrelatedLeakage", "Do not leak Venus, Jupiter, conjunction, or other forbidden objects.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        var assetText = AssetText(context);
        if (!HasViewingWindowEvidence(context.Intelligence, allText)) errors.Add("MeteorShower scene validation must include bestViewingWindowLocal or a normalized equivalent.");
        RequireAny(errors, assetText, "MeteorShower scene validation must include meteor streaks.", "meteor streak", "meteor streaks", "streak");
        RequireAny(errors, allText, "MeteorShower scene validation must include a radiant hint.", "radiant");
        RequireAny(errors, allText, "MeteorShower scene validation must include dark-sky guidance.", "dark sky", "dark", "light pollution");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "MeteorShower scene validation", "Venus", "Jupiter", "conjunction", "planet pairing");
        return Result(warnings, errors);
    }
}

public sealed class PlanetPairingSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "PlanetPairing";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("actualObjects", "Include actual primary/secondary object names."),
        Requirement("pairingLanguage", "Use close approach or pairing language."),
        Requirement("directionTime", "Include correct direction and time."),
        Requirement("noUnrelatedLeakage", "Do not leak unrelated objects.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        RequireAllObjects(errors, allText, context.Intelligence, "PlanetPairing scene validation");
        RequireAny(errors, allText, "PlanetPairing scene validation must include close approach or pairing language.", "close approach", "pairing", "close together", "near", "nearby");
        RequireTimeAndDirection(errors, allText, context.Intelligence, "PlanetPairing scene validation");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "PlanetPairing scene validation");
        return Result(warnings, errors);
    }
}

public sealed class ConjunctionSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "Conjunction";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("actualObjects", "Include actual conjunction objects."),
        Requirement("conjunctionLanguage", "Use conjunction/alignment language."),
        Requirement("directionTime", "Include correct direction and time.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        RequireAllObjects(errors, allText, context.Intelligence, "Conjunction scene validation");
        RequireAny(errors, allText, "Conjunction scene validation must include conjunction/alignment language.", "conjunction", "alignment", "align");
        RequireTimeAndDirection(errors, allText, context.Intelligence, "Conjunction scene validation");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "Conjunction scene validation");
        return Result(warnings, errors);
    }
}

public sealed class NamedFullMoonSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "NamedFullMoon";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("fullMoon", "Use Moon/full moon language."),
        Requirement("moonriseOrViewingTime", "Include moonrise/viewing time if available."),
        Requirement("noMeteorLeakage", "Do not leak meteor/radiant language.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        var outputText = OutputContentText(context);
        RequireAny(errors, allText, "NamedFullMoon scene validation must use Moon/full moon language.", "Moon", "full moon");
        RequireNamedFullMoonDrawableSpecs(errors, context);
        if (!HasViewingWindowEvidence(context.Intelligence, allText) && !ContainsToken(allText, context.Intelligence.LocalPeakTime)) errors.Add("NamedFullMoon scene validation must include moonrise/viewing time when available.");
        RejectForbiddenLeakage(errors, outputText, context.Intelligence, "NamedFullMoon scene validation", "meteor", "radiant", "Venus", "Jupiter", "planet conjunction");
        return Result(warnings, errors);
    }

    private static void RequireNamedFullMoonDrawableSpecs(List<string> errors, SceneValidationContext context)
    {
        if (context.InfographicSpecs.Count == 0)
        {
            errors.Add("NamedFullMoon scene validation requires generated infographic specs with drawable Moon metadata.");
            return;
        }

        foreach (var (path, json) in context.InfographicSpecs)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("drawableVisualObjects", out var objects) || objects.ValueKind != JsonValueKind.Array)
            {
                if (!HasMoonSpecEvidence(json, context)) errors.Add($"NamedFullMoon scene validation requires drawableVisualObjects or Moon/short-title spec evidence in {Path.GetFileName(path)}.");
                continue;
            }

            var hasMoon = objects.EnumerateArray().Any(obj =>
                JsonStringEquals(obj, "objectType", "Moon")
                && JsonStringEquals(obj, "phase", "FullMoon")
                && JsonStringContains(obj, "size", "large")
                && JsonBoolEquals(obj, "glow", true));
            if (!hasMoon && !HasMoonSpecEvidence(json, context)) errors.Add($"NamedFullMoon scene validation requires Moon phase=FullMoon size=large glow=true or Moon/short-title spec evidence in {Path.GetFileName(path)}.");
        }
    }

    private static bool HasMoonSpecEvidence(string json, SceneValidationContext context)
    {
        if (!ContainsToken(json, "Moon") && !ContainsToken(json, "full moon")) return false;
        return ContainsToken(json, context.Intelligence.ShortTitle)
            || ContainsToken(json, context.Intelligence.Title)
            || ContainsToken(json, "Full Moon");
    }

    private static bool JsonStringEquals(JsonElement obj, string propertyName, string expected)
        => obj.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool JsonStringContains(JsonElement obj, string propertyName, string expected)
        => obj.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && (property.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool JsonBoolEquals(JsonElement obj, string propertyName, bool expected)
        => obj.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean() == expected;
}

public sealed class NewMoonSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "NewMoon";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("darkSky", "Describe dark-sky/stargazing opportunity."),
        Requirement("noVisibleFullMoon", "Do not describe a visible full moon.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        RequireAny(errors, allText, "NewMoon scene validation must describe dark-sky or stargazing opportunity.", "dark sky", "stargazing", "dark", "Milky Way");
        if (ContainsToken(allText, "visible full moon") || ContainsToken(allText, "bright full moon")) errors.Add("NewMoon scene validation must not describe a visible full moon.");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "NewMoon scene validation");
        return Result(warnings, errors);
    }
}

public sealed class LunarEclipseSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "LunarEclipse";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("eclipseMoonPhaseTiming", "Include eclipse/Moon/phase/timing language."),
        Requirement("redCopperMoon", "Include red/copper Moon if relevant.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        RequireAny(errors, allText, "LunarEclipse scene validation must include eclipse language.", "eclipse", "lunar eclipse");
        RequireAny(errors, allText, "LunarEclipse scene validation must include Moon language.", "Moon");
        RequireAny(errors, allText, "LunarEclipse scene validation must include phase/timing language.", "phase", "timing", "watch during", "time");
        if (ContainsAnyToken(context.Intelligence.Title, "total", "blood") || ContainsAnyToken(allText, "totality", "umbra")) RequireAny(errors, allText, "LunarEclipse scene validation should include red/copper Moon language when relevant.", "red", "copper", "blood");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "LunarEclipse scene validation");
        return Result(warnings, errors);
    }
}

public sealed class SolarEclipseSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "SolarEclipse";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("eclipseSunTiming", "Include eclipse/Sun/timing language."),
        Requirement("eyeSafety", "Include certified eye-safety warning.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        RequireAny(errors, allText, "SolarEclipse scene validation must include eclipse language.", "eclipse", "solar eclipse");
        RequireAny(errors, allText, "SolarEclipse scene validation must include Sun language.", "Sun");
        RequireAny(errors, allText, "SolarEclipse scene validation must include timing language.", "timing", "watch during", "time");
        if (!(ContainsToken(allText, "certified") && ContainsAnyToken(allText, "eclipse glasses", "solar filter", "eye protection"))) errors.Add("SolarEclipse scene validation must include a certified eye-safety warning.");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "SolarEclipse scene validation");
        return Result(warnings, errors);
    }
}

public sealed class GenericEventSceneValidationStrategy : EventSceneValidationStrategyBase
{
    public override string EventType => "AstronomyEvent";
    public override IReadOnlyList<SceneValidationRequirement> GetRequirements(ProductionEventIntelligence intelligence) =>
    [
        Requirement("titleOrObjects", "Use event title or resolved object names."),
        Requirement("timeDirection", "Use event time and direction when available.")
    ];

    public override SceneValidationResult Validate(SceneValidationContext context)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var allText = AllText(context);
        if (!ContainsToken(allText, context.Intelligence.Title) && !ContainsToken(allText, context.Intelligence.ShortTitle) && !(context.Intelligence.ResolvedObjectNames ?? []).Any(o => ContainsToken(allText, o))) errors.Add("Generic scene validation must include event title, short title, or resolved object names.");
        if (!HasViewingWindowEvidence(context.Intelligence, allText) && !ContainsToken(allText, context.Intelligence.LocalPeakTime)) warnings.Add("Generic scene validation should include the event viewing time/window when available.");
        if (!string.IsNullOrWhiteSpace(context.Intelligence.SkyDirectionHint) && !ContainsToken(allText, context.Intelligence.SkyDirectionHint)) warnings.Add("Generic scene validation should include the sky direction when available.");
        RejectForbiddenLeakage(errors, OutputContentText(context), context.Intelligence, "Generic scene validation");
        return Result(warnings, errors);
    }
}
