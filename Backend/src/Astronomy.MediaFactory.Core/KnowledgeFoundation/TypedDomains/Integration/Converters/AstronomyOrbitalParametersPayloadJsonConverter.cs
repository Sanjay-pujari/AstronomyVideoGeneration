using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal sealed class AstronomyOrbitalParametersPayloadJsonConverter : JsonConverter<AstronomyOrbitalParametersPayload>
{
    public override AstronomyOrbitalParametersPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = AstronomyJsonObjectReader.RequireObject(document.RootElement, "Orbital parameters payload");
        AstronomyJsonObjectReader.EnsureNoDuplicateProperties(root);

        var typeId = AstronomyJsonObjectReader.DeserializeRequired<AstronomyKnowledgeTypeId>(root, nameof(AstronomyOrbitalParametersPayload.TypeId), options);
        var referenceContext = AstronomyJsonObjectReader.DeserializeRequired<AstronomyOrbitalReferenceContext>(root, nameof(AstronomyOrbitalParametersPayload.ReferenceContext), options);
        var parameters = AstronomyJsonObjectReader.DeserializeRequired<AstronomyOrbitalParameter[]>(root, nameof(AstronomyOrbitalParametersPayload.Parameters), options);

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

    private static void WriteProperty<T>(Utf8JsonWriter writer, JsonSerializerOptions options, string clrPropertyName, T value)
    {
        writer.WritePropertyName(AstronomyJsonObjectReader.GetJsonPropertyName(clrPropertyName, options));
        JsonSerializer.Serialize(writer, value, options);
    }

}
