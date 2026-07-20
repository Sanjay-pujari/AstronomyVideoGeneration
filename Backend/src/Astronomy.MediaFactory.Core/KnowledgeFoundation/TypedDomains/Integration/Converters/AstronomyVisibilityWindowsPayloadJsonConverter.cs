using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal sealed class AstronomyVisibilityWindowsPayloadJsonConverter : JsonConverter<AstronomyVisibilityWindowsPayload>
{
    public override AstronomyVisibilityWindowsPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Visibility-windows payload must be a JSON object.");
        EnsureNoDuplicateProperties(root);

        var typeId = DeserializeRequired<AstronomyKnowledgeTypeId>(root, nameof(AstronomyVisibilityWindowsPayload.TypeId), options);
        var observationContext = DeserializeRequired<AstronomyObservationContext>(root, nameof(AstronomyVisibilityWindowsPayload.ObservationContext), options);
        var windows = DeserializeRequired<AstronomyVisibilityWindow[]>(root, nameof(AstronomyVisibilityWindowsPayload.Windows), options);

        try
        {
            return new AstronomyVisibilityWindowsPayload(typeId, observationContext, windows);
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            throw new JsonException("Visibility-windows payload is invalid.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, AstronomyVisibilityWindowsPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        WriteProperty(writer, options, nameof(AstronomyVisibilityWindowsPayload.TypeId), value.TypeId);
        WriteProperty(writer, options, nameof(AstronomyVisibilityWindowsPayload.ObservationContext), value.ObservationContext);
        WriteProperty(writer, options, nameof(AstronomyVisibilityWindowsPayload.Windows), value.Windows);
        writer.WriteEndObject();
    }

    private static T DeserializeRequired<T>(JsonElement root, string clrPropertyName, JsonSerializerOptions options)
    {
        var jsonPropertyName = GetJsonPropertyName(options, clrPropertyName);
        if (!root.TryGetProperty(jsonPropertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Visibility-windows payload property '{jsonPropertyName}' is required.");
        }

        try
        {
            return property.Deserialize<T>(options) ?? throw new JsonException($"Visibility-windows payload property '{jsonPropertyName}' cannot be null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new JsonException($"Visibility-windows payload property '{jsonPropertyName}' is invalid.", exception);
        }
    }

    private static void WriteProperty<T>(Utf8JsonWriter writer, JsonSerializerOptions options, string clrPropertyName, T value)
    {
        writer.WritePropertyName(GetJsonPropertyName(options, clrPropertyName));
        JsonSerializer.Serialize(writer, value, options);
    }

    private static string GetJsonPropertyName(JsonSerializerOptions options, string clrPropertyName) => options.PropertyNamingPolicy?.ConvertName(clrPropertyName) ?? clrPropertyName;

    private static void EnsureNoDuplicateProperties(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException($"Duplicate visibility-windows payload property '{property.Name}' is not allowed.");
        }
    }
}
