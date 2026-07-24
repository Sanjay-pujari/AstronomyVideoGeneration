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
        new("PlanetConjunction", "PlanetPairing"),
        new("PLANET_CONJUNCTION", "PlanetPairing"),
        new("PLANET_PAIRING", "PlanetPairing"),
        new("PLANET_GROUPING", "PlanetGrouping"),
        new("Solar Eclipse", "SolarEclipse"),
        new("Lunar Eclipse", "LunarEclipse"),
        new("Meteor Shower", "MeteorShower"),
        new("Full Moon", "FullMoon"),
        new("Named Full Moon", "NamedFullMoon"),
        new("Deep Sky Object", "DeepSkyObject"),
        new("BlackHoleOrScientificExplainer", "ScientificExplainer")
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

        // Alias keys are matched by a trimmed, case-insensitive comparer. Canonical ids are terminal values,
        // so supported lookups never recursively reprocess a canonical event type as another alias.
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
            if (AreSameAliasLookupKey(alias.Alias, alias.CanonicalEventType))
                errors.Add($"Self alias rejected: '{alias.Alias}' targets equivalent canonical id '{alias.CanonicalEventType}'.");
            if (IsCanonicalSelfAliasToAnotherSpelling(alias, allIds))
                errors.Add($"Canonical value must not be registered as an alias to another spelling of itself: '{alias.Alias}' -> '{alias.CanonicalEventType}'.");
            if (!allIds.Contains(alias.CanonicalEventType, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Alias '{alias.Alias}' targets unsupported canonical id '{alias.CanonicalEventType}'.");
        }

        AddAliasCycleErrors(errors);

        return new(errors.Count == 0, errors, []);
    }

    private void AddAliasCycleErrors(List<string> errors)
    {
        var aliasTargetsByNormalizedKey = Aliases
            .GroupBy(a => NormalizeAliasLookupKey(a.Alias), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => NormalizeAliasLookupKey(g.First().CanonicalEventType), StringComparer.OrdinalIgnoreCase);

        foreach (var alias in Aliases)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = NormalizeAliasLookupKey(alias.Alias);

            while (aliasTargetsByNormalizedKey.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                {
                    errors.Add($"Alias cycle detected at '{alias.Alias}'.");
                    break;
                }

                current = next;
            }
        }
    }

    private static bool AreSameAliasLookupKey(string left, string right) =>
        string.Equals(NormalizeAliasLookupKey(left), NormalizeAliasLookupKey(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsCanonicalSelfAliasToAnotherSpelling(AstronomyEventAliasV1 alias, IEnumerable<string> taxonomyIds) =>
        taxonomyIds.Contains(alias.Alias, StringComparer.OrdinalIgnoreCase) && AreSameAliasLookupKey(alias.Alias, alias.CanonicalEventType);

    private static string NormalizeAliasLookupKey(string value) => Clean(value).ToUpperInvariant();

    private static void AddDuplicateErrors(List<string> errors, IEnumerable<string> values, string label)
    {
        foreach (var group in values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate {label} '{group.Key}'.");
    }

    private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
