using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyEventConsolidationService : IAstronomyEventConsolidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> WindowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BRIGHT_PLANET_VISIBILITY",
        "MOON_SPECIAL",
        "PLANET_GROUPING",
        "PLANET_CONJUNCTION"
    };

    public IReadOnlyList<DetectedAstronomyEventDto> Consolidate(IReadOnlyList<DetectedAstronomyEventDto> detectedEvents)
    {
        ArgumentNullException.ThrowIfNull(detectedEvents);
        if (detectedEvents.Count <= 1) return detectedEvents;

        var consolidated = new List<DetectedAstronomyEventDto>();
        foreach (var regionGroup in detectedEvents.GroupBy(ConsolidationGroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = regionGroup.OrderBy(e => e.StartUtc).ThenByDescending(e => e.VisibilityScore).ToList();
            var groupType = ordered.First().EventType;
            if (!WindowedTypes.Contains(groupType))
            {
                consolidated.AddRange(ordered);
                continue;
            }

            var current = new List<DetectedAstronomyEventDto>();
            foreach (var item in ordered)
            {
                if (current.Count == 0 || CanMerge(current[^1], item, current))
                {
                    current.Add(item);
                    continue;
                }

                consolidated.Add(ConsolidateWindow(current));
                current = [item];
            }

            if (current.Count > 0) consolidated.Add(ConsolidateWindow(current));
        }

        return consolidated
            .OrderByDescending(e => e.ViralPotentialScore)
            .ThenByDescending(e => e.VisibilityScore)
            .ThenBy(e => e.StartUtc)
            .ToArray();
    }

    private static string ConsolidationGroupKey(DetectedAstronomyEventDto dto)
    {
        var baseKey = $"{dto.RegionId ?? string.Empty}::{dto.EventType}";
        return dto.EventType.Equals("PLANET_CONJUNCTION", StringComparison.OrdinalIgnoreCase)
            ? $"{baseKey}::{ObjectPairKey(dto)}"
            : baseKey;
    }

    private static bool CanMerge(DetectedAstronomyEventDto previous, DetectedAstronomyEventDto next, IReadOnlyList<DetectedAstronomyEventDto> currentWindow)
    {
        if (!string.Equals(previous.RegionId, next.RegionId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(previous.EventType, next.EventType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsCloseDate(previous, next)) return false;

        return next.EventType.ToUpperInvariant() switch
        {
            "BRIGHT_PLANET_VISIBILITY" => HasSimilarObjectSet(currentWindow[^1], next, minimumJaccard: 0.50),
            "MOON_SPECIAL" => true,
            "PLANET_GROUPING" => HasSimilarObjectSet(currentWindow[^1], next, minimumJaccard: 0.60),
            "PLANET_CONJUNCTION" => SameObjectPair(currentWindow[^1], next),
            _ => false
        };
    }

    private static DetectedAstronomyEventDto ConsolidateWindow(IReadOnlyList<DetectedAstronomyEventDto> window)
    {
        if (window.Count == 1) return window[0];

        var ordered = window.OrderBy(e => e.StartUtc).ToArray();
        var first = ordered.First();
        var start = ordered.Min(e => e.StartUtc);
        var end = ordered.Max(e => e.EndUtc ?? e.StartUtc);
        var best = SelectPeakEvent(first.EventType, ordered);
        var objects = MergeObjects(ordered, first.EventType);
        var startDate = DateOnly.FromDateTime(start.UtcDateTime);
        var endDate = DateOnly.FromDateTime(end.UtcDateTime);
        var region = SanitizeCodePart(first.RegionId ?? "REGION");
        var pairCode = first.EventType.Equals("PLANET_CONJUNCTION", StringComparison.OrdinalIgnoreCase) ? $"_{SanitizeCodePart(ObjectPairLabel(objects))}" : string.Empty;
        var eventCode = $"{first.EventType}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}_{region}{pairCode}";
        var title = BuildTitle(first.EventType, first.LocationName, ordered, objects, best);
        var summary = BuildSummary(first.EventType, first.LocationName, ordered, objects, startDate, endDate, best);
        var rawDataJson = BuildRawDataJson(first.EventType, ordered, startDate, endDate, best);
        var rulesAppliedJson = JsonSerializer.Serialize(new
        {
            rule = "astronomy_event_consolidation",
            phase = "Phase7B.2",
            sourceEventType = first.EventType,
            sourceEventCount = ordered.Length,
            mergeCriteria = MergeCriteria(first.EventType)
        }, JsonOptions);
        var metadataJson = JsonSerializer.Serialize(new
        {
            consolidationVersion = "Phase7B.2",
            sourceEventCodes = ordered.Select(e => e.EventCode).ToArray(),
            first.MetadataJson
        }, JsonOptions);

        return new DetectedAstronomyEventDto(
            null,
            eventCode,
            first.EventType,
            title,
            summary,
            first.Description,
            start,
            best.PeakUtc ?? best.StartUtc,
            end,
            first.RegionId,
            first.LocationName,
            first.TimeZone,
            first.RecommendedCategory,
            first.Status,
            ordered.Max(e => e.VisibilityScore),
            ordered.Max(e => e.RarityScore),
            ordered.Max(e => e.StoryScore),
            ordered.Max(e => e.ViralPotentialScore),
            ordered.Max(e => e.ConfidenceScore),
            objects,
            rawDataJson,
            rulesAppliedJson,
            metadataJson);
    }

    private static DetectedAstronomyEventDto SelectPeakEvent(string eventType, IReadOnlyList<DetectedAstronomyEventDto> events)
    {
        if (eventType.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("PLANET_CONJUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            var withSeparations = events.Select(e => new { Event = e, Separation = ExtractAngularSeparation(e) }).Where(x => x.Separation.HasValue).ToArray();
            if (withSeparations.Length > 0) return withSeparations.OrderBy(x => x.Separation!.Value).ThenByDescending(x => x.Event.VisibilityScore).First().Event;
        }

        return events.OrderByDescending(e => e.VisibilityScore).ThenByDescending(e => e.ViralPotentialScore).ThenByDescending(e => e.StoryScore).First();
    }

    private static IReadOnlyList<DetectedAstronomyEventObjectDto> MergeObjects(IReadOnlyList<DetectedAstronomyEventDto> events, string eventType)
    {
        if (eventType.Equals("MOON_SPECIAL", StringComparison.OrdinalIgnoreCase))
        {
            var bestMoon = events.SelectMany(e => e.Objects)
                .Where(o => o.ObjectName.Equals("Moon", StringComparison.OrdinalIgnoreCase) || o.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.VisibilityScore ?? 0m)
                .FirstOrDefault();
            return [bestMoon ?? new DetectedAstronomyEventObjectDto(null, "Moon", "Moon", "Primary", "MOON", null, events.Max(e => e.VisibilityScore), null)];
        }

        return events.SelectMany(e => e.Objects)
            .GroupBy(ObjectKey, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) =>
            {
                var best = group.OrderByDescending(o => o.VisibilityScore ?? 0m).First();
                return best with { Id = null, ObjectRole = index == 0 ? "Primary" : "Companion" };
            })
            .OrderByDescending(o => o.VisibilityScore ?? 0m)
            .ThenBy(o => o.ObjectName)
            .ToArray();
    }

    private static string BuildTitle(string eventType, string? locationName, IReadOnlyList<DetectedAstronomyEventDto> events, IReadOnlyList<DetectedAstronomyEventObjectDto> objects, DetectedAstronomyEventDto peakEvent)
    {
        var location = string.IsNullOrWhiteSpace(locationName) ? "this region" : locationName;
        return eventType.ToUpperInvariant() switch
        {
            "BRIGHT_PLANET_VISIBILITY" => $"Bright planet visibility window over {location}",
            "MOON_SPECIAL" => $"{MoonPhaseLabel(events.First())} to {MoonPhaseLabel(events.Last())} Moon over {location}",
            "PLANET_GROUPING" => $"Planet grouping window over {location}",
            "PLANET_CONJUNCTION" => $"{ObjectPairLabel(objects)} conjunction peaks {FormatUtcDate(peakEvent.PeakUtc ?? peakEvent.StartUtc)} over {location}",
            _ => events.First().Title
        };
    }

    private static string BuildSummary(string eventType, string? locationName, IReadOnlyList<DetectedAstronomyEventDto> events, IReadOnlyList<DetectedAstronomyEventObjectDto> objects, DateOnly startDate, DateOnly endDate, DetectedAstronomyEventDto peakEvent)
    {
        var location = string.IsNullOrWhiteSpace(locationName) ? "this region" : locationName;
        var range = FormatDateRange(startDate, endDate);
        if (eventType.Equals("BRIGHT_PLANET_VISIBILITY", StringComparison.OrdinalIgnoreCase))
        {
            return $"Visible planets over {location} from {range}: {ObjectList(objects)}. Consolidated from {events.Count} daily detections.";
        }

        if (eventType.Equals("MOON_SPECIAL", StringComparison.OrdinalIgnoreCase))
        {
            var illuminations = events.Select(ExtractMoonIllumination).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            var illuminationText = illuminations.Length == 0 ? "illumination data unavailable" : $"illumination ranges from {illuminations.Min():0.#}% to {illuminations.Max():0.#}%";
            return $"Moon visibility over {location} from {range}; phases observed from {MoonPhaseLabel(events.First())} to {MoonPhaseLabel(events.Last())}, with {illuminationText}.";
        }

        if (eventType.Equals("PLANET_GROUPING", StringComparison.OrdinalIgnoreCase))
        {
            var nearest = MinAngularSeparation(events);
            var separationText = nearest.HasValue ? $"nearest angular separation about {nearest.Value:0.#}°" : "nearest angular separation unavailable";
            return $"Grouped planets over {location} from {range}: {ObjectList(objects)}; {separationText}. Consolidated from {events.Count} daily detections.";
        }

        if (eventType.Equals("PLANET_CONJUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            var minimumSeparation = MinAngularSeparation(events);
            var separationText = minimumSeparation.HasValue ? $"minimum angular separation about {minimumSeparation.Value:0.##}°" : "minimum angular separation unavailable";
            var peakUtc = peakEvent.PeakUtc ?? peakEvent.StartUtc;
            return $"{ObjectPairLabel(objects)} conjunction over {location}; {separationText} at peak on {FormatUtcDateTime(peakUtc)}. Visibility window runs from {FormatUtcDateTime(events.Min(e => e.StartUtc))} to {FormatUtcDateTime(events.Max(e => e.EndUtc ?? e.StartUtc))}. Consolidated from {events.Count} same-pair daily detections.";
        }

        return events.First().Summary ?? events.First().Description ?? events.First().Title;
    }

    private static string BuildRawDataJson(string eventType, IReadOnlyList<DetectedAstronomyEventDto> events, DateOnly startDate, DateOnly endDate, DetectedAstronomyEventDto peakEvent)
    {
        if (eventType.Equals("PLANET_CONJUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            var visibilityWindowStartUtc = events.Min(e => e.StartUtc);
            var visibilityWindowEndUtc = events.Max(e => e.EndUtc ?? e.StartUtc);
            return JsonSerializer.Serialize(new
            {
                peakDate = (peakEvent.PeakUtc ?? peakEvent.StartUtc).UtcDateTime.ToString("O"),
                minimumAngularSeparationDegrees = MinAngularSeparation(events),
                visibilityWindowStartUtc = visibilityWindowStartUtc.UtcDateTime.ToString("O"),
                visibilityWindowEndUtc = visibilityWindowEndUtc.UtcDateTime.ToString("O"),
                sourceEventCount = events.Count,
                sourceEventCodes = events.Select(e => e.EventCode).ToArray()
            }, JsonOptions);
        }

        var moonIlluminations = events.Select(ExtractMoonIllumination).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return JsonSerializer.Serialize(new
        {
            consolidatedDateRange = new { startDate, endDate },
            sourceEventCount = events.Count,
            sourceEventCodes = events.Select(e => e.EventCode).ToArray(),
            eventType,
            moonPhasesObserved = events.Select(MoonPhaseLabel).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            illuminationStart = moonIlluminations.Length > 0 ? moonIlluminations.First() : (double?)null,
            illuminationEnd = moonIlluminations.Length > 0 ? moonIlluminations.Last() : (double?)null,
            illuminationMin = moonIlluminations.Length > 0 ? moonIlluminations.Min() : (double?)null,
            illuminationMax = moonIlluminations.Length > 0 ? moonIlluminations.Max() : (double?)null,
            nearestAngularSeparationDegrees = MinAngularSeparation(events)
        }, JsonOptions);
    }

    private static object MergeCriteria(string eventType) => eventType.ToUpperInvariant() switch
    {
        "BRIGHT_PLANET_VISIBILITY" => new { sameRegionId = true, sameEventType = true, similarVisibleObjectSet = true, maxGapDays = 1 },
        "MOON_SPECIAL" => new { sameRegionId = true, sameEventType = true, maxGapDays = 1 },
        "PLANET_GROUPING" => new { sameRegionId = true, sameEventType = true, sameOrMostlySameObjectSet = true, maxGapDays = 1 },
        "PLANET_CONJUNCTION" => new { sameRegionId = true, sameEventType = true, sameObjectPair = true, maxGapDays = 1 },
        _ => new { sameRegionId = true, sameEventType = true }
    };

    private static bool IsCloseDate(DetectedAstronomyEventDto previous, DetectedAstronomyEventDto next)
    {
        var previousDate = DateOnly.FromDateTime((previous.EndUtc ?? previous.StartUtc).UtcDateTime);
        var nextDate = DateOnly.FromDateTime(next.StartUtc.UtcDateTime);
        var gap = nextDate.DayNumber - previousDate.DayNumber;
        return gap is >= 0 and <= 2;
    }

    private static bool HasSimilarObjectSet(DetectedAstronomyEventDto first, DetectedAstronomyEventDto second, double minimumJaccard)
    {
        var firstSet = ObjectSet(first);
        var secondSet = ObjectSet(second);
        if (firstSet.Count == 0 || secondSet.Count == 0) return false;
        var intersection = firstSet.Intersect(secondSet, StringComparer.OrdinalIgnoreCase).Count();
        var union = firstSet.Union(secondSet, StringComparer.OrdinalIgnoreCase).Count();
        return intersection > 0 && union > 0 && (double)intersection / union >= minimumJaccard;
    }

    private static bool SameObjectPair(DetectedAstronomyEventDto first, DetectedAstronomyEventDto second)
    {
        var firstSet = ObjectSet(first);
        var secondSet = ObjectSet(second);
        return firstSet.Count == 2 && secondSet.Count == 2 && firstSet.SetEquals(secondSet);
    }

    private static HashSet<string> ObjectSet(DetectedAstronomyEventDto dto) => dto.Objects.Select(ObjectKey).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ObjectPairKey(DetectedAstronomyEventDto dto) => string.Join("+", ObjectSet(dto).OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

    private static string ObjectKey(DetectedAstronomyEventObjectDto obj) => !string.IsNullOrWhiteSpace(obj.CatalogId) ? obj.CatalogId! : obj.ObjectName;

    private static string ObjectList(IReadOnlyList<DetectedAstronomyEventObjectDto> objects) => string.Join(", ", objects.Select(o => o.ObjectName).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string ObjectPairLabel(IReadOnlyList<DetectedAstronomyEventObjectDto> objects) => string.Join(" and ", objects.Select(o => o.ObjectName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Take(2));

    private static string FormatDateRange(DateOnly startDate, DateOnly endDate) => startDate == endDate ? $"{startDate:yyyy-MM-dd}" : $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";

    private static string FormatUtcDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd");

    private static string FormatUtcDateTime(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'");

    private static string MoonPhaseLabel(DetectedAstronomyEventDto dto) => ExtractString(dto.RawDataJson, "moonPhase") ?? dto.Title.Split(" Moon over ", StringSplitOptions.None).FirstOrDefault() ?? "Moon";

    private static double? ExtractMoonIllumination(DetectedAstronomyEventDto dto) => ExtractDouble(dto.RawDataJson, "moonIlluminationPercent");

    private static double? ExtractAngularSeparation(DetectedAstronomyEventDto dto) => ExtractDouble(dto.RulesAppliedJson, "angularSeparationDegrees");

    private static double? MinAngularSeparation(IEnumerable<DetectedAstronomyEventDto> events)
    {
        var separations = events.Select(ExtractAngularSeparation).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return separations.Length == 0 ? null : separations.Min();
    }

    private static string? ExtractString(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetProperty(document.RootElement, propertyName, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ExtractDouble(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetProperty(document.RootElement, propertyName, out var element) && element.TryGetDouble(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement element)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(propertyName))
            {
                element = property.Value;
                return true;
            }
        }

        element = default;
        return false;
    }

    private static string SanitizeCodePart(string value)
    {
        var chars = value.Trim().ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "REGION" : sanitized;
    }
}
