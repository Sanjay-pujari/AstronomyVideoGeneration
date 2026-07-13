namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

public sealed record AstronomyEventAliasV1(string Alias, string CanonicalEventType);

public sealed class AstronomyEventAliasCatalogV1
{
    private static readonly string[] CanonicalTypes =
    [
        "PlanetPairing", "PlanetGrouping", "MeteorShower", "FullMoon", "NamedFullMoon",
        "SolarEclipse", "LunarEclipse", "Occultation", "Constellation", "DeepSkyObject"
    ];

    private static readonly string[] FutureTypes =
    [
        "PlanetProfile", "Comet", "ScientificExplainer", "Opposition", "Elongation", "Transit", "LunarPhase"
    ];

    private static readonly AstronomyEventAliasV1[] DefaultAliases =
    [
        new("PlanetaryConjunction", "PlanetPairing"),
        new("Solar Eclipse", "SolarEclipse"),
        new("Lunar Eclipse", "LunarEclipse"),
        new("Meteor Shower", "MeteorShower"),
        new("Full Moon", "FullMoon"),
        new("Named Full Moon", "NamedFullMoon"),
        new("Deep Sky Object", "DeepSkyObject")
    ];

    private readonly Dictionary<string, string> _aliases;

    public AstronomyEventAliasCatalogV1()
        : this(DefaultAliases)
    {
    }

    public AstronomyEventAliasCatalogV1(IEnumerable<AstronomyEventAliasV1> aliases)
    {
        Aliases = aliases.Select(a => new AstronomyEventAliasV1(Clean(a.Alias), Clean(a.CanonicalEventType))).ToArray();
        _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in Aliases)
        {
            if (!_aliases.ContainsKey(alias.Alias))
                _aliases.Add(alias.Alias, alias.CanonicalEventType);
        }
    }

    public IReadOnlyList<string> SupportedCanonicalEventTypes => CanonicalTypes;
    public IReadOnlyList<string> FutureEventTypes => FutureTypes;
    public IReadOnlyList<AstronomyEventAliasV1> Aliases { get; }

    public EventIdentityNormalizationResult Normalize(string? eventType)
    {
        var input = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();
        if (input is null)
            return new(input, null, [], false, ["Event type is missing."]);

        if (CanonicalTypes.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = CanonicalTypes.Single(t => t.Equals(input, StringComparison.OrdinalIgnoreCase));
            return new(input, canonical, [], true, [$"Canonical event type '{canonical}' resolved without alias."]);
        }

        if (FutureTypes.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = FutureTypes.Single(t => t.Equals(input, StringComparison.OrdinalIgnoreCase));
            return new(input, canonical, [], false, [$"Event type '{canonical}' is reserved for a future V1 taxonomy stage."]);
        }

        if (_aliases.TryGetValue(input, out var mapped))
            return new(input, mapped, [input], true, [$"Alias '{input}' normalized to '{mapped}'."]);

        return new(input, null, [], false, [$"Unsupported astronomy event type '{input}'."]);
    }

    public CanonicalEventIdentityValidationResult Validate()
    {
        var errors = new List<string>();
        var allIds = CanonicalTypes.Concat(FutureTypes).ToArray();
        AddDuplicateErrors(errors, CanonicalTypes, "canonical id");
        AddDuplicateErrors(errors, allIds, "approved taxonomy id");

        foreach (var group in Aliases.GroupBy(a => a.Alias, StringComparer.OrdinalIgnoreCase))
        {
            var targets = group.Select(a => a.CanonicalEventType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (group.Count() > 1)
                errors.Add($"Duplicate alias '{group.Key}' targets: {string.Join(", ", targets)}.");
            if (targets.Length > 1)
                errors.Add($"Ambiguous alias '{group.Key}' targets multiple canonical ids: {string.Join(", ", targets)}.");
        }

        foreach (var alias in Aliases)
        {
            if (!allIds.Contains(alias.CanonicalEventType, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Alias '{alias.Alias}' targets unsupported canonical id '{alias.CanonicalEventType}'.");
            if (Aliases.Any(a => a.Alias.Equals(alias.CanonicalEventType, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Alias cycle detected at '{alias.Alias}' -> '{alias.CanonicalEventType}'.");
        }

        return new(errors.Count == 0, errors, []);
    }

    private static void AddDuplicateErrors(List<string> errors, IEnumerable<string> values, string label)
    {
        foreach (var group in values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate {label} '{group.Key}'.");
    }

    private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
