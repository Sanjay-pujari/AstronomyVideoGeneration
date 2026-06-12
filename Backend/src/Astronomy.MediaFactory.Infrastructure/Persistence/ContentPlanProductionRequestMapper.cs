using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanProductionRequestMapper : IContentPlanProductionRequestMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ContentPlanProductionPipelineRequest Map(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(intelligence);

        var metadata = ParseObject(intelligence.MetadataJson);
        var raw = ParseObject(intelligence.RawDataJson);
        var warnings = ReadStringArray(metadata, raw, "warnings");
        var sourceNotes = ReadStringArray(metadata, raw, "sourceNotes", "sources", "notes");
        var requestedOutputs = ReadRequestedOutputs(plan.RequestedOutputTypesJson);
        if (requestedOutputs.Count == 0)
            requestedOutputs = ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"];

        var primaryObjects = ResolveObjects(intelligence, primary: true);
        var secondaryObjects = ResolveObjects(intelligence, primary: false);

        return new ContentPlanProductionPipelineRequest(
            plan.Id,
            ResolveContentCategoryCode(plan, intelligence),
            FirstNonBlank(plan.Title, intelligence.Title, "Astronomy event"),
            ReadString(metadata, raw, "shortTitle") ?? intelligence.Summary ?? ShortenTitle(FirstNonBlank(plan.Title, intelligence.Title, "Astronomy event")),
            FirstNonBlank(intelligence.EventType, plan.PrimaryAstronomyEventTypeCode, "AstronomyEvent"),
            plan.RegionId,
            plan.Language,
            primaryObjects,
            secondaryObjects,
            intelligence.StartUtc,
            intelligence.PeakUtc,
            intelligence.EndUtc,
            plan.ScheduledUtc,
            FirstNonBlank(plan.SourceExternalEventId, intelligence.ExternalEventId, intelligence.EventCode),
            plan.PlannedFormat,
            requestedOutputs,
            intelligence.VisibilityScore,
            intelligence.RarityScore,
            intelligence.AudienceInterestScore,
            intelligence.ContentOpportunityScore,
            intelligence.VerificationStatus,
            ReadString(metadata, raw, "verificationSource"),
            intelligence.ContentStrategy,
            ReadString(metadata, raw, "localPeakTime"),
            ReadString(metadata, raw, "skyDirectionHint", "directionHint"),
            ReadString(metadata, raw, "visibilityRegion"),
            ReadString(metadata, raw, "moonInterference"),
            ReadString(metadata, raw, "bestViewingWindowLocal"),
            ReadString(metadata, raw, "radiantVisibilityNote"),
            ReadDecimal(metadata, raw, "moonIlluminationPercent"),
            ReadString(metadata, raw, "recommendedPublishWindow"),
            ReadStringArray(metadata, raw, "recommendedContentTypes"),
            warnings,
            sourceNotes);
    }

    private static string ResolveContentCategoryCode(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence)
    {
        if (string.Equals(intelligence.EventType, "PLANET_GROUPING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intelligence.EventType, "PlanetGrouping", StringComparison.OrdinalIgnoreCase))
        {
            return "PlanetGrouping";
        }

        return FirstNonBlank(plan.ContentCategoryCode, intelligence.RecommendedCategory, "RareEventAlert");
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static List<string> ReadRequestedOutputs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.EnumerateArray().Select(ToText).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                JsonValueKind.String => SplitList(doc.RootElement.GetString()),
                _ => []
            };
        }
        catch (JsonException)
        {
            return SplitList(json);
        }
    }

    private static IReadOnlyList<string> ResolveObjects(AstronomyEventIntelligence intelligence, bool primary)
    {
        var objects = intelligence.Objects?
            .Where(o => primary ? IsPrimaryObject(o) : !IsPrimaryObject(o))
            .Select(o => o.ObjectName)
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (objects.Length > 0) return objects;
        if (primary && intelligence.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)) return [ShortenTitle(intelligence.Title)];
        if (!primary && intelligence.EventType.Equals("MeteorShower", StringComparison.OrdinalIgnoreCase)) return ["Meteors"];
        return [];
    }

    private static bool IsPrimaryObject(AstronomyEventObject obj)
        => (obj.ObjectRole?.Equals("Primary", StringComparison.OrdinalIgnoreCase) == true)
            || (obj.ObjectRole?.Equals("Radiant", StringComparison.OrdinalIgnoreCase) == true)
            || obj.ObjectType.Equals("Radiant", StringComparison.OrdinalIgnoreCase);

    private static JsonElement? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? metadata, JsonElement? raw, params string[] names)
        => names.Select(name => ReadString(metadata, name) ?? ReadString(raw, name)).FirstOrDefault(NotBlank);

    private static string? ReadString(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value) return null;
        return TryFind(value, name, out var found) ? ToText(found) : null;
    }

    private static decimal? ReadDecimal(JsonElement? metadata, JsonElement? raw, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadDecimal(metadata, name) ?? ReadDecimal(raw, name);
            if (value.HasValue) return value;
        }
        return null;
    }

    private static decimal? ReadDecimal(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value || !TryFind(value, name, out var found)) return null;
        if (found.ValueKind == JsonValueKind.Number && found.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(ToText(found), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? metadata, JsonElement? raw, params string[] names)
    {
        foreach (var name in names)
        {
            var values = ReadStringArray(metadata, name).Concat(ReadStringArray(raw, name)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length > 0) return values;
        }
        return [];
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value || !TryFind(value, name, out var found)) return [];
        if (found.ValueKind == JsonValueKind.Array) return found.EnumerateArray().Select(ToText).Where(NotBlank).ToArray();
        return SplitList(ToText(found));
    }

    private static bool TryFind(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ToText(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString();
    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static List<string> SplitList(string? value) => (value ?? string.Empty).Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(NotBlank).ToList();

    private static string ShortenTitle(string? title)
    {
        var clean = (title ?? "Event").Replace("Meteor Shower Peak", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(clean) ? title ?? "Event" : clean;
    }
}
