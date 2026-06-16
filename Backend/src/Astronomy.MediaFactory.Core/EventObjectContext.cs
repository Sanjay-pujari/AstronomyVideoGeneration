namespace Astronomy.MediaFactory.Core;

public sealed record EventObjectContext(
    IReadOnlyList<string> ObjectNames,
    int ObjectCount,
    string ObjectListText,
    string ObjectPairText,
    string ObjectHeadlineText,
    string PrimaryObjectName,
    string SecondaryObjectName,
    bool HasMoon,
    bool HasPlanet,
    string EventObjectCategory,
    string ObjectNamesSource,
    IReadOnlyList<string> RemovedInvalidObjectNameCandidates,
    IReadOnlyList<string> HardcodedObjectTermsDetected,
    bool ObjectNameValidationPassed,
    bool RuntimeHardcodingDetected)
{
    public object ToDiagnostics() => new
    {
        objectNames = ObjectNames,
        objectCount = ObjectCount,
        objectListText = ObjectListText,
        objectPairText = ObjectPairText,
        objectHeadlineText = ObjectHeadlineText,
        primaryObjectName = PrimaryObjectName,
        secondaryObjectName = SecondaryObjectName,
        hasMoon = HasMoon,
        hasPlanet = HasPlanet,
        eventObjectCategory = EventObjectCategory
    };
}

public static class EventObjectContextBuilder
{
    public static readonly string[] BannedHardcodedObjectTerms =
    [
        "Jupiter and Venus", "Jupiter + Venus", "Jupiter & Venus", "Jupiter meets Venus", "Venus align",
        "Venus and Jupiter", "Mercury, Venus", "Mars and Jupiter", "Moon and Venus", "Moon + Venus"
    ];

    private static readonly string[] PlanetNames = ["Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"];
    private static readonly string[] ViewerInstructionMarkers = ["look for", "look toward", "look west", "look east", "look north", "look south", "best viewing", "after sunset", "before sunrise", "sky after", "where to look", "horizon"];

    public static EventObjectContext FromIntelligence(ProductionEventIntelligence? intelligence)
    {
        if (intelligence is null) return Build([], "none", [], "AstronomyEvent", null);
        var sources = new (string Name, IReadOnlyList<string>? Values)[]
        {
            ("resolvedObjectNames", intelligence.ResolvedObjectNames),
            ("primaryObjects+secondaryObjects", (intelligence.PrimaryObjects ?? []).Concat(intelligence.SecondaryObjects ?? []).ToArray()),
            ("requiredVisualObjects", intelligence.RequiredVisualObjects)
        };
        foreach (var source in sources)
        {
            var context = Build(source.Values ?? [], source.Name, [], intelligence.EventType, intelligence.Title);
            if (context.ObjectCount > 0) return context;
        }
        return Build([], "fallback", [], intelligence.EventType, intelligence.Title);
    }

    public static EventObjectContext FromJsonValues(string eventType, string? title, IEnumerable<string> resolvedObjectNames, IEnumerable<string> primaryObjects, IEnumerable<string> secondaryObjects, IEnumerable<string> requiredVisualObjects)
    {
        var candidates = resolvedObjectNames?.ToArray() ?? [];
        if (candidates.Length > 0) return Build(candidates, "resolvedObjectNames", [], eventType, title);
        candidates = (primaryObjects ?? []).Concat(secondaryObjects ?? []).ToArray();
        if (candidates.Length > 0) return Build(candidates, "primaryObjects+secondaryObjects", [], eventType, title);
        return Build(requiredVisualObjects ?? [], "requiredVisualObjects", [], eventType, title);
    }

    public static IReadOnlyList<string> DetectBannedHardcodedTerms(string text) => BannedHardcodedObjectTerms.Where(t => !string.IsNullOrWhiteSpace(text) && text.Contains(t, StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static EventObjectContext Build(IEnumerable<string> candidates, string source, IReadOnlyList<string> priorRemoved, string eventType, string? title)
    {
        var removed = new List<string>(priorRemoved);
        var names = new List<string>();
        foreach (var candidate in candidates ?? [])
        {
            var clean = Clean(candidate);
            if (IsCleanObjectName(clean)) names.Add(clean);
            else if (!string.IsNullOrWhiteSpace(candidate)) removed.Add(candidate);
        }
        names = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0 && IsLunarEvent(eventType)) names.Add("Moon");
        if (names.Count == 0 && IsMeteorEvent(eventType) && !string.IsNullOrWhiteSpace(title)) names.Add(CleanMeteorName(title!));
        var objectList = string.Join(", ", names);
        var pair = names.Count >= 2 ? $"{names[0]} + {names[1]}" : names.FirstOrDefault() ?? string.Empty;
        var headline = names.Count > 3 ? string.Join(" + ", names.Take(3)).ToUpperInvariant() + " + MORE" : string.Join(" + ", names).ToUpperInvariant();
        var hasMoon = names.Any(n => n.Equals("Moon", StringComparison.OrdinalIgnoreCase));
        var hasPlanet = names.Any(n => PlanetNames.Contains(n, StringComparer.OrdinalIgnoreCase));
        var category = names.Count == 1 ? "Single" : hasMoon && names.Count == 2 ? "MoonPair" : names.Count == 2 ? "Pair" : names.Count > 2 ? "Group" : "Unknown";
        return new(names, names.Count, objectList, pair, headline, names.FirstOrDefault() ?? string.Empty, names.Skip(1).FirstOrDefault() ?? string.Empty, hasMoon, hasPlanet, category, source, removed, [], removed.Count == 0, false);
    }

    private static string Clean(string value) => (value ?? string.Empty).Trim().TrimEnd('.', ';', ':', ',');
    private static bool IsCleanObjectName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 40 && !value.Contains('.') && !ViewerInstructionMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase)) && value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;
    private static bool IsLunarEvent(string eventType) => (eventType ?? string.Empty).Contains("moon", StringComparison.OrdinalIgnoreCase);
    private static bool IsMeteorEvent(string eventType) => (eventType ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase);
    private static string CleanMeteorName(string title) => title.Contains("Geminid", StringComparison.OrdinalIgnoreCase) ? "Geminids Meteor Shower" : title.Split('-', ';')[0].Trim();
}
