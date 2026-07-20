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
        var root = AstronomyJsonObjectReader.RequireObject(document.RootElement, "Visibility-windows payload");
        AstronomyJsonObjectReader.EnsureNoDuplicateProperties(root);

        var typeId = AstronomyJsonObjectReader.DeserializeRequired<AstronomyKnowledgeTypeId>(root, nameof(AstronomyVisibilityWindowsPayload.TypeId), options);
        var observationContext = AstronomyJsonObjectReader.DeserializeRequired<AstronomyObservationContext>(root, nameof(AstronomyVisibilityWindowsPayload.ObservationContext), options);
        var windows = AstronomyJsonObjectReader.DeserializeRequired<AstronomyVisibilityWindow[]>(root, nameof(AstronomyVisibilityWindowsPayload.Windows), options);

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

    private static void WriteProperty<T>(Utf8JsonWriter writer, JsonSerializerOptions options, string clrPropertyName, T value)
    {
        writer.WritePropertyName(AstronomyJsonObjectReader.GetJsonPropertyName(clrPropertyName, options));
        JsonSerializer.Serialize(writer, value, options);
    }

}
