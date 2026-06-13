using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualSourceType
{
    ComputedAstronomyScene,
    ScientificAsset,
    AICinematicScene,
    Hybrid,
    GenericFallback
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualMinimumQuality
{
    Realistic
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualPreferredAssetKind
{
    ScientificRealImage,
    ScientificTexture,
    AICinematicRealistic
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CelestialObjectQuality
{
    Realistic
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisualObjectSourcePriority
{
    LocalAsset,
    ScientificAsset,
    AICinematic
}

public sealed record ResolvedCelestialObjectVisualSource(
    string ObjectType,
    string ObjectVisualSource,
    string AssetKey,
    string GeneratedRealisticPrompt,
    bool PrimitivePlaceholderUsed = false,
    CelestialObjectQuality CelestialObjectQuality = CelestialObjectQuality.Realistic,
    IReadOnlyList<VisualObjectSourcePriority>? ObjectSourcePriority = null);

public sealed record VisualSourceResolutionRequest(
    ProductionEventIntelligence Intelligence,
    string StrategyId,
    EnrichedQuestionSceneDto EnrichedScene,
    QuestionDrivenNarrationSceneDto NarrationScene,
    IReadOnlyList<string> RequiredVisualObjects);

public sealed record VisualSourceResolutionResult(
    VisualSourceType SourceType,
    IReadOnlyList<string> RequiredDrawableObjects,
    IReadOnlyList<string> ScientificAssetKeys,
    string AiCinematicPrompt,
    bool GenericFallbackAllowed,
    IReadOnlyList<string> ValidationRequiredTerms,
    IReadOnlyList<string> ForbiddenObjectNames,
    IReadOnlyDictionary<string, string> Metadata,
    bool RealisticObjectRequired = true,
    bool AllowPrimitivePlaceholder = false,
    VisualMinimumQuality MinimumVisualQuality = VisualMinimumQuality.Realistic,
    IReadOnlyList<VisualPreferredAssetKind>? PreferredAssetKind = null,
    bool PrimitivePlaceholderUsed = false,
    bool PrimitivePlaceholderAllowed = false,
    CelestialObjectQuality CelestialObjectQuality = CelestialObjectQuality.Realistic,
    IReadOnlyList<VisualObjectSourcePriority>? ObjectSourcePriority = null,
    IReadOnlyList<ResolvedCelestialObjectVisualSource>? ObjectVisualSources = null);

public interface IVisualSourceResolver
{
    VisualSourceResolutionResult Resolve(VisualSourceResolutionRequest request);
}

public sealed class DefaultVisualSourceResolver : IVisualSourceResolver
{
    public VisualSourceResolutionResult Resolve(VisualSourceResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Intelligence);

        var intelligence = request.Intelligence;
        var requiredVisualObjects = NormalizeList(request.RequiredVisualObjects.Count > 0
            ? request.RequiredVisualObjects
            : intelligence.RequiredVisualObjects ?? []);
        var eventType = FirstNonEmpty(intelligence.EventType, request.StrategyId, intelligence.StrategyId);
        var strategyId = FirstNonEmpty(request.StrategyId, intelligence.StrategyId, eventType);
        if (IsPlanetGrouping(eventType, strategyId, intelligence.Title)) strategyId = "PlanetGrouping";
        var forbidden = NormalizeList((intelligence.ForbiddenObjectNames ?? []).Concat(intelligence.ForbiddenTerms));
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eventType"] = eventType,
            ["strategyId"] = strategyId,
            ["sceneNumber"] = request.EnrichedScene.SceneNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sceneQuestionType"] = request.EnrichedScene.QuestionType,
            ["eventShortTitle"] = intelligence.ShortTitle,
            ["eventTitle"] = intelligence.Title,
            ["realisticObjectRequired"] = "true",
            ["allowPrimitivePlaceholder"] = "false",
            ["primitivePlaceholderAllowed"] = "false",
            ["minimumVisualQuality"] = VisualMinimumQuality.Realistic.ToString(),
            ["celestialObjectQuality"] = CelestialObjectQuality.Realistic.ToString(),
            ["objectSourcePriority"] = FormatObjectSourcePriority(DefaultObjectSourcePriority),
            ["primitivePlaceholderUsed"] = "false"
        };

        if (IsMeteorShower(eventType, strategyId, intelligence.Title))
        {
            var required = NormalizeList(requiredVisualObjects.Concat(["meteor streaks", "radiant/dark sky"]));
            var prompt = "Hybrid meteor-shower scene: realistic meteor streaks radiating from a subtle radiant across a dark open sky; preserve the real viewing window and avoid unrelated planets. Meteor trails may be cinematic streaks, not symbolic icons.";
            metadata["visualSourceType"] = VisualSourceType.Hybrid.ToString();
            metadata["generatedRealisticPrompt"] = prompt;
            return new(
                VisualSourceType.Hybrid,
                required,
                [],
                prompt,
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(["meteor streaks", "dark sky"])),
                forbidden,
                metadata,
                PreferredAssetKind: [VisualPreferredAssetKind.AICinematicRealistic],
                ObjectSourcePriority: DefaultObjectSourcePriority,
                ObjectVisualSources: BuildObjectVisualSources(required, prompt, _ => "Meteor.AICinematic", _ => "AICinematic:realistic meteor streaks with radiant and dark-sky context"));
        }

        if (IsNamedFullMoon(eventType, strategyId, intelligence.Title))
        {
            var shortTitle = FirstNonEmpty(intelligence.ShortTitle, intelligence.Title, "Full Moon");
            var required = NormalizeList(requiredVisualObjects.Concat(["Moon"]));
            var moonStyle = ResolveNamedFullMoonStyle(shortTitle, intelligence.Title);
            var prompt = $"Hybrid named full-Moon scene: render the Moon from a realistic full Moon visual source with crater texture and maria, moon glow, moonrise/eastern horizon context, {shortTitle}. {moonStyle}";
            metadata["visualSourceType"] = VisualSourceType.Hybrid.ToString();
            metadata["assetKey"] = "Moon.FullMoon";
            metadata["generatedRealisticPrompt"] = prompt;
            metadata["requiredCelestialObject"] = "Moon";
            return new(
                VisualSourceType.Hybrid,
                required,
                ["Moon.FullMoon"],
                prompt,
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(["large visible full moon", "moon texture", "craters", "maria", "moon glow", "moonrise", "eastern horizon", shortTitle])),
                forbidden,
                metadata,
                PreferredAssetKind: [VisualPreferredAssetKind.ScientificRealImage, VisualPreferredAssetKind.ScientificTexture, VisualPreferredAssetKind.AICinematicRealistic],
                ObjectSourcePriority: DefaultObjectSourcePriority,
                ObjectVisualSources: BuildObjectVisualSources(required, prompt, ResolveAssetKey, ResolveObjectVisualSource));
        }

        if (IsPlanetPairingConjunctionOrGrouping(eventType, strategyId, intelligence.Title))
        {
            var objects = NormalizeList(intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects));
            var isPlanetGrouping = IsPlanetGrouping(eventType, strategyId, intelligence.Title);
            var required = NormalizeList(requiredVisualObjects.Concat(objects).Concat(isPlanetGrouping ? new[] { "planet grouping", "guided scan path" } : Array.Empty<string>()));
            var objectPhrase = isPlanetGrouping ? string.Join(", ", objects) : string.Join(" and ", objects);
            var textureGuidance = string.Join(" ", objects.Select(ResolvePlanetTextureGuidance));
            var groupingGuidance = isPlanetGrouping
                ? "Use PlanetGroupingSceneStrategy: show the complete multi-planet arrangement, a guided scan path, timing, and sky direction; do not collapse it into a generic planet scene."
                : "Show close-pairing/conjunction geometry, timing, and sky direction; do not add unrelated planets.";
            var prompt = $"Computed astronomy scene for {objectPhrase}: render only the actual listed objects with labels matching their exact names; use real-looking planet textures, not generic colored circles. {textureGuidance} {groupingGuidance}";
            metadata["labelObjects"] = string.Join(", ", objects);
            metadata["visualSourceType"] = (required.Count > 2 ? VisualSourceType.Hybrid : VisualSourceType.ComputedAstronomyScene).ToString();
            metadata["assetKey"] = string.Join(", ", objects.Select(o => $"Planet.{o}"));
            metadata["generatedRealisticPrompt"] = prompt;
            metadata["sceneStrategy"] = isPlanetGrouping ? "PlanetGroupingSceneStrategy" : "PlanetPairingSceneStrategy";
            metadata["heroStrategy"] = isPlanetGrouping ? "PlanetGroupingHeroStrategy" : "PlanetPairingHeroStrategy";
            metadata["thumbnailStrategy"] = isPlanetGrouping ? "PlanetGroupingThumbnailStrategy" : "PlanetPairingThumbnailStrategy";
            return new(
                required.Count > 2 ? VisualSourceType.Hybrid : VisualSourceType.ComputedAstronomyScene,
                required,
                objects.Select(o => $"Planet.{o}").ToArray(),
                prompt,
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(objects).Concat(isPlanetGrouping ? ["real-looking planet textures", "planet grouping", "guided scan path", "labels match actual object names"] : ["real-looking planet textures", "close pairing", "labels match actual object names"])),
                forbidden,
                metadata,
                PreferredAssetKind: [VisualPreferredAssetKind.ScientificTexture, VisualPreferredAssetKind.AICinematicRealistic],
                ObjectSourcePriority: DefaultObjectSourcePriority,
                ObjectVisualSources: BuildObjectVisualSources(required, prompt, ResolveAssetKey, ResolveObjectVisualSource));
        }

        if (IsEclipse(eventType, strategyId, intelligence.Title))
        {
            var solar = ContainsAny(eventType, "Solar") || ContainsAny(strategyId, "Solar") || ContainsAny(intelligence.Title, "Solar");
            var required = NormalizeList(requiredVisualObjects.Concat([solar ? "Solar Eclipse" : "Lunar Eclipse", "Moon"]));
            var prompt = solar
                ? "Realistic solar-eclipse thumbnail scene: eclipsed Sun/Moon alignment with corona and safety-aware viewing context; do not show unrelated planets."
                : "Realistic lunar-eclipse thumbnail scene: red/copper eclipsed Moon with shadow geometry and night-sky context; do not show unrelated planets.";
            metadata["visualSourceType"] = VisualSourceType.Hybrid.ToString();
            metadata["assetKey"] = solar ? "Eclipse.Solar" : "Eclipse.Lunar";
            metadata["generatedRealisticPrompt"] = prompt;
            metadata["safetyAwareCopyRequired"] = solar.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new(
                VisualSourceType.Hybrid,
                required,
                [solar ? "Eclipse.Solar" : "Eclipse.Lunar"],
                prompt,
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(solar ? ["corona", "safe viewing"] : ["red Moon", "copper Moon"])),
                forbidden,
                metadata,
                PreferredAssetKind: [VisualPreferredAssetKind.ScientificRealImage, VisualPreferredAssetKind.AICinematicRealistic],
                ObjectSourcePriority: DefaultObjectSourcePriority,
                ObjectVisualSources: BuildObjectVisualSources(required, prompt, ResolveAssetKey, ResolveObjectVisualSource));
        }

        if (IsComet(eventType, strategyId, intelligence.Title, requiredVisualObjects))
        {
            var required = NormalizeList(requiredVisualObjects.Count > 0 ? requiredVisualObjects : ["Comet"]);
            var prompt = "AI cinematic realistic comet scene: visible comet nucleus, coma, and tail from a realistic comet image style; do not render a plain ellipse or a simple streak except as a separate motion-path annotation.";
            metadata["visualSourceType"] = VisualSourceType.AICinematicScene.ToString();
            metadata["generatedRealisticPrompt"] = prompt;
            metadata["assetKey"] = string.Join(", ", required.Select(ResolveAssetKey));
            return new(VisualSourceType.AICinematicScene, required, required.Select(ResolveAssetKey).ToArray(), prompt, false, NormalizeList(required.Concat(["nucleus", "coma", "tail"])), forbidden, metadata, PreferredAssetKind: [VisualPreferredAssetKind.ScientificRealImage, VisualPreferredAssetKind.AICinematicRealistic], ObjectSourcePriority: DefaultObjectSourcePriority, ObjectVisualSources: BuildObjectVisualSources(required, prompt, ResolveAssetKey, ResolveObjectVisualSource));
        }

        if (IsDeepSkyObject(eventType, strategyId, intelligence.Title, requiredVisualObjects))
        {
            var required = NormalizeList(requiredVisualObjects.Count > 0 ? requiredVisualObjects : ["Deep Sky Object"]);
            var prompt = "Scientific or AI realistic deep-sky object scene: use a real-looking nebula, galaxy, or star-cluster visual source with astrophotography detail; do not render a generic glow circle or dot.";
            metadata["visualSourceType"] = VisualSourceType.AICinematicScene.ToString();
            metadata["generatedRealisticPrompt"] = prompt;
            metadata["assetKey"] = string.Join(", ", required.Select(ResolveAssetKey));
            return new(VisualSourceType.AICinematicScene, required, required.Select(ResolveAssetKey).ToArray(), prompt, false, NormalizeList(required.Concat(["astrophotography detail", "nebula", "galaxy", "star cluster"])), forbidden, metadata, PreferredAssetKind: [VisualPreferredAssetKind.ScientificRealImage, VisualPreferredAssetKind.AICinematicRealistic], ObjectSourcePriority: DefaultObjectSourcePriority, ObjectVisualSources: BuildObjectVisualSources(required, prompt, ResolveAssetKey, ResolveObjectVisualSource));
        }

        var allowsFallback = requiredVisualObjects.Count == 0;
        metadata["visualSourceType"] = (allowsFallback ? VisualSourceType.GenericFallback : VisualSourceType.AICinematicScene).ToString();
        metadata["realisticObjectRequired"] = (!allowsFallback).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!allowsFallback) metadata["generatedRealisticPrompt"] = "AI cinematic astronomy scene must visibly include every required visual object using realistic sources; generic fallback and primitive placeholders are forbidden.";
        return new(
            allowsFallback ? VisualSourceType.GenericFallback : VisualSourceType.AICinematicScene,
            requiredVisualObjects,
            [],
            allowsFallback
                ? "Generic editorial astronomy background is allowed because this scene has no required visible celestial object."
                : "AI cinematic astronomy scene must visibly include every required visual object using realistic sources; generic fallback and primitive placeholders are forbidden.",
            allowsFallback,
            requiredVisualObjects,
            forbidden,
            metadata,
            RealisticObjectRequired: !allowsFallback,
            AllowPrimitivePlaceholder: false,
            PreferredAssetKind: allowsFallback ? [] : [VisualPreferredAssetKind.ScientificRealImage, VisualPreferredAssetKind.ScientificTexture, VisualPreferredAssetKind.AICinematicRealistic],
            ObjectSourcePriority: allowsFallback ? [] : DefaultObjectSourcePriority,
            ObjectVisualSources: allowsFallback ? [] : BuildObjectVisualSources(requiredVisualObjects, metadata.TryGetValue("generatedRealisticPrompt", out var fallbackPrompt) ? fallbackPrompt : string.Empty, ResolveAssetKey, ResolveObjectVisualSource));
    }

    private static readonly IReadOnlyList<VisualObjectSourcePriority> DefaultObjectSourcePriority =
        [VisualObjectSourcePriority.LocalAsset, VisualObjectSourcePriority.ScientificAsset, VisualObjectSourcePriority.AICinematic];

    private static IReadOnlyList<ResolvedCelestialObjectVisualSource> BuildObjectVisualSources(
        IEnumerable<string> objectTypes,
        string generatedRealisticPrompt,
        Func<string, string> resolveAssetKey,
        Func<string, string> resolveObjectVisualSource)
        => NormalizeList(objectTypes)
            .Select(objectType => new ResolvedCelestialObjectVisualSource(
                objectType,
                resolveObjectVisualSource(objectType),
                resolveAssetKey(objectType),
                BuildObjectPrompt(objectType, generatedRealisticPrompt),
                PrimitivePlaceholderUsed: false,
                ObjectSourcePriority: DefaultObjectSourcePriority))
            .ToArray();

    private static string ResolveAssetKey(string objectType)
    {
        var normalized = NormalizeObjectName(objectType);
        if (normalized.Equals("Moon", StringComparison.OrdinalIgnoreCase) || normalized.Contains("FullMoon", StringComparison.OrdinalIgnoreCase)) return "Moon.FullMoon";
        if (normalized.Equals("Sun", StringComparison.OrdinalIgnoreCase)) return "Sun.RealisticPhotosphere";
        if (IsPlanetName(normalized)) return $"Planet.{normalized}";
        if (ContainsAny(normalized, "Comet")) return "Comet.Realistic";
        if (ContainsAny(normalized, "Meteor")) return "Meteor.RealisticStreaks";
        if (ContainsAny(normalized, "Asteroid")) return "Asteroid.RealisticRockyBody";
        if (ContainsAny(normalized, "Nebula")) return $"DeepSky.Nebula.{SanitizeAssetName(normalized)}";
        if (ContainsAny(normalized, "Galaxy")) return $"DeepSky.Galaxy.{SanitizeAssetName(normalized)}";
        if (ContainsAny(normalized, "Cluster")) return $"DeepSky.Cluster.{SanitizeAssetName(normalized)}";
        if (ContainsAny(normalized, "Deep Sky")) return $"DeepSky.Object.{SanitizeAssetName(normalized)}";
        return $"Celestial.Realistic.{SanitizeAssetName(normalized)}";
    }

    private static string ResolveObjectVisualSource(string objectType)
        => $"LocalAsset:{ResolveAssetKey(objectType)}; ScientificAsset:{ResolveAssetKey(objectType)}; AICinematic:realistic {objectType}";

    private static string BuildObjectPrompt(string objectType, string scenePrompt)
        => $"Render {objectType} as a real-looking celestial object using local/scientific texture or cinematic-realistic generation; never use a primitive circle, icon, dot, or symbolic placeholder. {scenePrompt}".Trim();

    private static bool IsPlanetName(string value)
        => value.Equals("Mercury", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Venus", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Mars", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Saturn", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Uranus", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Neptune", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeObjectName(string value)
        => value.Trim() switch
        {
            var v when v.Equals("full moon", StringComparison.OrdinalIgnoreCase) => "Moon",
            var v when v.Equals("lunar disc", StringComparison.OrdinalIgnoreCase) => "Moon",
            var v when v.Contains("meteor", StringComparison.OrdinalIgnoreCase) => "Meteor",
            var v when v.Contains("comet", StringComparison.OrdinalIgnoreCase) => "Comet",
            var v when v.Contains("asteroid", StringComparison.OrdinalIgnoreCase) => "Asteroid",
            var v => v
        };

    private static string SanitizeAssetName(string value)
        => string.Concat(value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(part => new string(part.Where(char.IsLetterOrDigit).ToArray())));

    private static string FormatObjectSourcePriority(IEnumerable<VisualObjectSourcePriority> priorities)
        => string.Join(", ", priorities);

    private static bool IsMeteorShower(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "MeteorShower", "meteor shower") || ContainsAny(strategyId, "MeteorShower", "meteor shower") || ContainsAny(title, "meteor shower");

    private static bool IsNamedFullMoon(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "NamedFullMoon", "FullMoon", "Full Moon", "BlueMoon", "Blue Moon")
            || ContainsAny(strategyId, "NamedFullMoon", "FullMoon", "Full Moon")
            || ContainsAny(title, "Full Moon", "Blue Moon");

    private static bool IsPlanetPairingConjunctionOrGrouping(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "PlanetPairing", "Conjunction", "PlanetParade", "PlanetGrouping", "PLANET_GROUPING")
            || ContainsAny(strategyId, "PlanetPairing", "Conjunction", "PlanetParade", "PlanetGrouping")
            || ContainsAny(title, "close pairing", "pairing", "conjunction", "planet grouping", "planet group");

    private static bool IsPlanetGrouping(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "PlanetGrouping", "PLANET_GROUPING")
            || ContainsAny(strategyId, "PlanetGrouping")
            || ContainsAny(title, "planet grouping", "planet group");

    private static bool IsEclipse(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "Eclipse", "SolarEclipse", "LunarEclipse", "Solar Eclipse", "Lunar Eclipse")
            || ContainsAny(strategyId, "Eclipse", "SolarEclipse", "LunarEclipse", "Solar Eclipse", "Lunar Eclipse")
            || ContainsAny(title, "Eclipse", "Solar Eclipse", "Lunar Eclipse");

    private static bool IsComet(string eventType, string strategyId, string title, IReadOnlyList<string> requiredObjects)
        => ContainsAny(eventType, "Comet") || ContainsAny(strategyId, "Comet") || ContainsAny(title, "Comet") || requiredObjects.Any(value => ContainsAny(value, "Comet"));

    private static bool IsDeepSkyObject(string eventType, string strategyId, string title, IReadOnlyList<string> requiredObjects)
        => ContainsAny(eventType, "DeepSkyObject", "Deep Sky", "Nebula", "Galaxy", "StarCluster", "Star Cluster")
            || ContainsAny(strategyId, "DeepSkyObject", "Deep Sky", "Nebula", "Galaxy", "StarCluster", "Star Cluster")
            || ContainsAny(title, "Deep Sky", "Nebula", "Galaxy", "Star Cluster")
            || requiredObjects.Any(value => ContainsAny(value, "Deep Sky", "Nebula", "Galaxy", "Star Cluster"));

    private static string ResolveNamedFullMoonStyle(string shortTitle, string title)
    {
        var text = $"{shortTitle} {title}";
        if (ContainsAny(text, "Snow Moon")) return "Snow Moon presentation: cold winter/moonrise atmosphere around the same real textured full Moon.";
        if (ContainsAny(text, "Strawberry Moon")) return "Strawberry Moon presentation: warm reddish-golden summer moonrise atmosphere around the same real textured full Moon.";
        if (ContainsAny(text, "Blue Moon")) return "Blue Moon presentation: natural full Moon with a subtle cool-blue cinematic mood, not a fake blue Moon unless the content explicitly explains the naming.";
        if (ContainsAny(text, "Blood Moon")) return "Blood Moon presentation: red/copper Moon only when this is a lunar eclipse; otherwise keep a natural full Moon color.";
        if (ContainsAny(text, "Wolf Moon")) return "Wolf Moon presentation: cold winter atmosphere around the same real textured full Moon.";
        return "Named full Moon presentation changes the atmosphere and copy, not the physical Moon texture.";
    }

    private static string ResolvePlanetTextureGuidance(string planet)
        => planet.Trim().ToLowerInvariant() switch
        {
            "mars" => "Mars must look like Mars with rusty red terrain, darker albedo markings, and polar-cap hints when possible.",
            "jupiter" => "Jupiter must show banded cloud texture and a Great Red Spot style feature when possible.",
            "venus" => "Venus must appear bright, cloud-covered, white/yellow, and not as a flat dot.",
            "saturn" => "Saturn must include its rings and pale banded globe.",
            "mercury" => "Mercury should use a gray cratered rocky texture.",
            "uranus" => "Uranus should use a pale cyan-blue gaseous disk with subtle shading.",
            "neptune" => "Neptune should use a deeper blue gaseous disk with subtle atmospheric shading.",
            _ => $"{planet} must use a recognizable realistic planetary texture rather than a flat circle."
        };

    private static bool ContainsAny(string? value, params string[] terms)
        => !string.IsNullOrWhiteSpace(value) && terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
