using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Positional;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal abstract class AstronomyDiscriminatedJsonConverter<TBase> : JsonConverter<TBase> where TBase : class
{
    private const string TypePropertyName = "type";
    private const string ValuePropertyName = "value";
    protected abstract bool TryGetType(TBase value, out string discriminator, out Type concreteType);
    protected abstract bool TryGetConcreteType(string discriminator, out Type concreteType);

    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException($"{typeof(TBase).Name} envelope must be a JSON object.");
        string? discriminator = null; JsonElement? value = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals(TypePropertyName)) { if (discriminator is not null) throw new JsonException($"Duplicate {typeof(TBase).Name} type property is not allowed."); if (property.Value.ValueKind != JsonValueKind.String) throw new JsonException($"{typeof(TBase).Name} type must be a JSON string."); discriminator = property.Value.GetString(); }
            else if (property.NameEquals(ValuePropertyName)) { if (value.HasValue) throw new JsonException($"Duplicate {typeof(TBase).Name} value property is not allowed."); value = property.Value.Clone(); }
        }
        if (string.IsNullOrWhiteSpace(discriminator)) throw new JsonException($"{typeof(TBase).Name} type is required.");
        if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null) throw new JsonException($"{typeof(TBase).Name} value is required.");
        if (!TryGetConcreteType(discriminator, out var concreteType)) throw new JsonException($"Unknown {typeof(TBase).Name} discriminator '{discriminator}'.");
        return (TBase?)JsonSerializer.Deserialize(value.Value.GetRawText(), concreteType, CreateOptions(options)) ?? throw new JsonException($"{typeof(TBase).Name} value cannot be null.");
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException($"{typeof(TBase).Name} cannot be null.");
        if (!TryGetType(value, out var discriminator, out var concreteType)) throw new JsonException($"Unsupported {typeof(TBase).Name} runtime value '{value.GetType().Name}'.");
        writer.WriteStartObject(); writer.WriteString(TypePropertyName, discriminator); writer.WritePropertyName(ValuePropertyName); JsonSerializer.Serialize(writer, value, concreteType, CreateOptions(options)); writer.WriteEndObject();
    }

    private JsonSerializerOptions CreateOptions(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--) if (clone.Converters[i].GetType() == GetType()) clone.Converters.RemoveAt(i);
        return clone;
    }
}

internal sealed class AstronomyPhysicalPropertyValueJsonConverter : AstronomyDiscriminatedJsonConverter<AstronomyPhysicalPropertyValue>
{
    protected override bool TryGetType(AstronomyPhysicalPropertyValue value, out string discriminator, out Type concreteType)
    {
        (discriminator, concreteType) = value switch
        {
            AstronomyScalarPhysicalPropertyValue => ("scalar", typeof(AstronomyScalarPhysicalPropertyValue)),
            AstronomyRangePhysicalPropertyValue => ("range", typeof(AstronomyRangePhysicalPropertyValue)),
            AstronomyTextPhysicalPropertyValue => ("text", typeof(AstronomyTextPhysicalPropertyValue)),
            AstronomyBooleanPhysicalPropertyValue => ("boolean", typeof(AstronomyBooleanPhysicalPropertyValue)),
            _ => (string.Empty, typeof(object))
        };
        return !string.IsNullOrEmpty(discriminator);
    }
    protected override bool TryGetConcreteType(string discriminator, out Type concreteType) { concreteType = discriminator switch { "scalar" => typeof(AstronomyScalarPhysicalPropertyValue), "range" => typeof(AstronomyRangePhysicalPropertyValue), "text" => typeof(AstronomyTextPhysicalPropertyValue), "boolean" => typeof(AstronomyBooleanPhysicalPropertyValue), _ => typeof(object) }; return concreteType != typeof(object); }
}

internal sealed class AstronomyPositionValueJsonConverter : AstronomyDiscriminatedJsonConverter<AstronomyPositionValue>
{
    protected override bool TryGetType(AstronomyPositionValue value, out string discriminator, out Type concreteType)
    {
        (discriminator, concreteType) = value switch
        {
            AstronomyAngularPositionValue => ("angular", typeof(AstronomyAngularPositionValue)),
            AstronomySphericalPositionValue => ("spherical", typeof(AstronomySphericalPositionValue)),
            AstronomyCartesianPositionValue => ("cartesian", typeof(AstronomyCartesianPositionValue)),
            _ => (string.Empty, typeof(object))
        };
        return !string.IsNullOrEmpty(discriminator);
    }
    protected override bool TryGetConcreteType(string discriminator, out Type concreteType) { concreteType = discriminator switch { "angular" => typeof(AstronomyAngularPositionValue), "spherical" => typeof(AstronomySphericalPositionValue), "cartesian" => typeof(AstronomyCartesianPositionValue), _ => typeof(object) }; return concreteType != typeof(object); }
}

internal sealed class AstronomyEventTemporalExtentJsonConverter : AstronomyDiscriminatedJsonConverter<AstronomyEventTemporalExtent>
{
    protected override bool TryGetType(AstronomyEventTemporalExtent value, out string discriminator, out Type concreteType)
    {
        (discriminator, concreteType) = value switch
        {
            AstronomyInstantEventTemporalExtent => ("instant", typeof(AstronomyInstantEventTemporalExtent)),
            AstronomyIntervalEventTemporalExtent => ("interval", typeof(AstronomyIntervalEventTemporalExtent)),
            _ => (string.Empty, typeof(object))
        };
        return !string.IsNullOrEmpty(discriminator);
    }
    protected override bool TryGetConcreteType(string discriminator, out Type concreteType) { concreteType = discriminator switch { "instant" => typeof(AstronomyInstantEventTemporalExtent), "interval" => typeof(AstronomyIntervalEventTemporalExtent), _ => typeof(object) }; return concreteType != typeof(object); }
}

internal sealed class AstronomyTemporalAnchorJsonConverter : AstronomyDiscriminatedJsonConverter<AstronomyTemporalAnchor>
{
    protected override bool TryGetType(AstronomyTemporalAnchor value, out string discriminator, out Type concreteType)
    {
        (discriminator, concreteType) = value switch
        {
            AstronomyUtcTemporalAnchor => ("utcInstant", typeof(AstronomyUtcTemporalAnchor)),
            AstronomyEpochTemporalAnchor => ("epoch", typeof(AstronomyEpochTemporalAnchor)),
            AstronomyCalendarDateTemporalAnchor => ("calendarDate", typeof(AstronomyCalendarDateTemporalAnchor)),
            AstronomyDayOfYearTemporalAnchor => ("dayOfYear", typeof(AstronomyDayOfYearTemporalAnchor)),
            AstronomyMonthTemporalAnchor => ("month", typeof(AstronomyMonthTemporalAnchor)),
            AstronomyCustomTemporalAnchor => ("custom", typeof(AstronomyCustomTemporalAnchor)),
            _ => (string.Empty, typeof(object))
        };
        return !string.IsNullOrEmpty(discriminator);
    }
    protected override bool TryGetConcreteType(string discriminator, out Type concreteType) { concreteType = discriminator switch { "utcInstant" => typeof(AstronomyUtcTemporalAnchor), "epoch" => typeof(AstronomyEpochTemporalAnchor), "calendarDate" => typeof(AstronomyCalendarDateTemporalAnchor), "dayOfYear" => typeof(AstronomyDayOfYearTemporalAnchor), "month" => typeof(AstronomyMonthTemporalAnchor), "custom" => typeof(AstronomyCustomTemporalAnchor), _ => typeof(object) }; return concreteType != typeof(object); }
}
