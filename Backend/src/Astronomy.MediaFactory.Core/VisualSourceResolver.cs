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
    IReadOnlyDictionary<string, string> Metadata);

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
        var forbidden = NormalizeList((intelligence.ForbiddenObjectNames ?? []).Concat(intelligence.ForbiddenTerms));
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eventType"] = eventType,
            ["strategyId"] = strategyId,
            ["sceneNumber"] = request.EnrichedScene.SceneNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sceneQuestionType"] = request.EnrichedScene.QuestionType,
            ["eventShortTitle"] = intelligence.ShortTitle,
            ["eventTitle"] = intelligence.Title
        };

        if (IsMeteorShower(eventType, strategyId, intelligence.Title))
        {
            var required = NormalizeList(requiredVisualObjects.Concat(["meteor streaks", "radiant/dark sky"]));
            return new(
                VisualSourceType.Hybrid,
                required,
                [],
                "Hybrid meteor-shower scene: visible meteor streaks radiating from a subtle radiant across a dark open sky; preserve the real viewing window and avoid unrelated planets.",
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(["meteor streaks", "dark sky"])),
                forbidden,
                metadata);
        }

        if (IsNamedFullMoon(eventType, strategyId, intelligence.Title))
        {
            var shortTitle = FirstNonEmpty(intelligence.ShortTitle, intelligence.Title, "Full Moon");
            var required = NormalizeList(requiredVisualObjects.Concat(["Moon"]));
            return new(
                VisualSourceType.Hybrid,
                required,
                ["Moon.FullMoon"],
                $"Hybrid named full-Moon scene: large visible full moon, moon glow, moonrise/eastern horizon context, {shortTitle}, seasonal cinematic background.",
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(["large visible full moon", "moon glow", "moonrise", "eastern horizon", shortTitle])),
                forbidden,
                metadata);
        }

        if (IsPlanetPairingOrConjunction(eventType, strategyId, intelligence.Title))
        {
            var objects = NormalizeList(intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects));
            var required = NormalizeList(requiredVisualObjects.Concat(objects));
            var objectPhrase = string.Join(" and ", objects);
            metadata["labelObjects"] = string.Join(", ", objects);
            return new(
                required.Count > 2 ? VisualSourceType.Hybrid : VisualSourceType.ComputedAstronomyScene,
                required,
                objects.Select(o => $"Planet.{o}").ToArray(),
                $"Computed astronomy scene for {objectPhrase}: render only the actual listed objects with labels matching their exact names; show close-pairing/conjunction geometry, timing, and sky direction; do not add unrelated planets.",
                GenericFallbackAllowed: false,
                NormalizeList(required.Concat(objects).Concat(["close pairing", "labels match actual object names"])),
                forbidden,
                metadata);
        }

        var allowsFallback = requiredVisualObjects.Count == 0;
        return new(
            allowsFallback ? VisualSourceType.GenericFallback : VisualSourceType.AICinematicScene,
            requiredVisualObjects,
            [],
            allowsFallback
                ? "Generic editorial astronomy background is allowed because this scene has no required visible celestial object."
                : "AI cinematic astronomy scene must visibly include every required visual object; generic fallback is forbidden.",
            allowsFallback,
            requiredVisualObjects,
            forbidden,
            metadata);
    }

    private static bool IsMeteorShower(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "MeteorShower", "meteor shower") || ContainsAny(strategyId, "MeteorShower", "meteor shower") || ContainsAny(title, "meteor shower");

    private static bool IsNamedFullMoon(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "NamedFullMoon", "FullMoon", "Full Moon", "BlueMoon", "Blue Moon")
            || ContainsAny(strategyId, "NamedFullMoon", "FullMoon", "Full Moon")
            || ContainsAny(title, "Full Moon", "Blue Moon");

    private static bool IsPlanetPairingOrConjunction(string eventType, string strategyId, string title)
        => ContainsAny(eventType, "PlanetPairing", "Conjunction", "PlanetParade")
            || ContainsAny(strategyId, "PlanetPairing", "Conjunction", "PlanetParade")
            || ContainsAny(title, "close pairing", "pairing", "conjunction");

    private static bool ContainsAny(string? value, params string[] terms)
        => !string.IsNullOrWhiteSpace(value) && terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
