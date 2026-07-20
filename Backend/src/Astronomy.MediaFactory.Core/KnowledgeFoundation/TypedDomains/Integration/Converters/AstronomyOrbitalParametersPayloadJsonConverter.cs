using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal sealed class AstronomyOrbitalParametersPayloadJsonConverter : JsonConverter<AstronomyOrbitalParametersPayload>
{
    public override AstronomyOrbitalParametersPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Orbital parameters payload must be a JSON object.");
        EnsureNoDuplicateProperties(root);

        var typeId = DeserializeRequired<AstronomyKnowledgeTypeId>(root, nameof(AstronomyOrbitalParametersPayload.TypeId), options);
        var referenceContext = DeserializeRequired<AstronomyOrbitalReferenceContext>(root, nameof(AstronomyOrbitalParametersPayload.ReferenceContext), options);
        var parameters = DeserializeRequired<AstronomyOrbitalParameter[]>(root, nameof(AstronomyOrbitalParametersPayload.Parameters), options);

        try
        {
            return new AstronomyOrbitalParametersPayload(typeId, referenceContext, parameters);
        }
        catch (Exception exception) when (exception is ArgumentException)
        {
            throw new JsonException("Orbital parameters payload is invalid.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, AstronomyOrbitalParametersPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        WriteProperty(writer, options, nameof(AstronomyOrbitalParametersPayload.TypeId), value.TypeId);
        WriteProperty(writer, options, nameof(AstronomyOrbitalParametersPayload.ReferenceContext), value.ReferenceContext);
        WriteProperty(writer, options, nameof(AstronomyOrbitalParametersPayload.Parameters), value.Parameters);
        writer.WriteEndObject();
    }

    private static T DeserializeRequired<T>(JsonElement root, string clrPropertyName, JsonSerializerOptions options)
    {
        var jsonPropertyName = GetJsonPropertyName(options, clrPropertyName);
        if (!root.TryGetProperty(jsonPropertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Orbital parameters payload property '{jsonPropertyName}' is required.");
        }

        try
        {
            return property.Deserialize<T>(options) ?? throw new JsonException($"Orbital parameters payload property '{jsonPropertyName}' cannot be null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new JsonException($"Orbital parameters payload property '{jsonPropertyName}' is invalid.", exception);
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
            if (!names.Add(property.Name)) throw new JsonException($"Duplicate orbital parameters payload property '{property.Name}' is not allowed.");
        }
    }
}
