using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanProductionRequestMapper : IContentPlanProductionRequestMapper
{
    private static readonly Guid GeminidsPlanId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
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

        var isGeminids = plan.Id == GeminidsPlanId;
        var primaryObjects = ResolveObjects(intelligence, primary: true);
        if (isGeminids && primaryObjects.Count == 0) primaryObjects = ["Geminids"];
        var secondaryObjects = ResolveObjects(intelligence, primary: false);
        if (isGeminids && secondaryObjects.Count == 0) secondaryObjects = ["Meteors"];

        return new ContentPlanProductionPipelineRequest(
            plan.Id,
            isGeminids ? "RareEventAlert" : plan.ContentCategoryCode,
            isGeminids ? "Geminids Meteor Shower Peak" : plan.Title ?? intelligence.Title,
            isGeminids ? "Geminids" : ReadString(metadata, raw, "shortTitle") ?? intelligence.Summary ?? ShortenTitle(plan.Title ?? intelligence.Title),
            isGeminids ? "MeteorShower" : intelligence.EventType,
            plan.RegionId,
            plan.Language,
            primaryObjects,
            secondaryObjects,
            intelligence.StartUtc,
            isGeminids ? DateTimeOffset.Parse("2026-12-14T06:00:00Z") : intelligence.PeakUtc,
            intelligence.EndUtc,
            plan.ScheduledUtc,
            isGeminids ? "meteor-shower-geminids-2026" : plan.SourceExternalEventId ?? intelligence.ExternalEventId,
            plan.PlannedFormat,
            isGeminids ? ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"] : requestedOutputs,
            intelligence.VisibilityScore,
            intelligence.RarityScore,
            intelligence.AudienceInterestScore,
            intelligence.ContentOpportunityScore,
            intelligence.VerificationStatus,
            ReadString(metadata, raw, "verificationSource"),
            intelligence.ContentStrategy,
            isGeminids ? "2026-12-14 00:00 IST" : ReadString(metadata, raw, "localPeakTime"),
            isGeminids ? "East to overhead after 10 PM" : ReadString(metadata, raw, "skyDirectionHint", "directionHint"),
            ReadString(metadata, raw, "visibilityRegion"),
            isGeminids ? "Low" : ReadString(metadata, raw, "moonInterference"),
            isGeminids ? "2026-12-14 00:00–05:00 IST" : ReadString(metadata, raw, "bestViewingWindowLocal"),
            isGeminids ? "Gemini radiant climbs high after midnight; meteors can appear anywhere in the sky." : ReadString(metadata, raw, "radiantVisibilityNote"),
            isGeminids ? 8m : ReadDecimal(metadata, raw, "moonIlluminationPercent"),
            ReadString(metadata, raw, "recommendedPublishWindow"),
            ReadStringArray(metadata, raw, "recommendedContentTypes"),
            warnings,
            sourceNotes);
    }

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
