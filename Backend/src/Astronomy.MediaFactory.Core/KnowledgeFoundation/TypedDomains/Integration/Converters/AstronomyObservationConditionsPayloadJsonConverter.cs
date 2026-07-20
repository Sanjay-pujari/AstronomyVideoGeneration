using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal sealed class AstronomyObservationConditionsPayloadJsonConverter : JsonConverter<AstronomyObservationConditionsPayload>
{
    public override AstronomyObservationConditionsPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = AstronomyJsonObjectReader.RequireObject(document.RootElement, "Observation conditions payload");
        AstronomyJsonObjectReader.EnsureNoDuplicateProperties(root);

        var typeId = AstronomyJsonObjectReader.DeserializeRequired<AstronomyKnowledgeTypeId>(root, nameof(AstronomyObservationConditionsPayload.TypeId), options);
        var observationContext = AstronomyJsonObjectReader.DeserializeRequired<AstronomyObservationContext>(root, nameof(AstronomyObservationConditionsPayload.ObservationContext), options);
        var conditions = AstronomyJsonObjectReader.DeserializeRequired<AstronomyObservationConditions>(root, nameof(AstronomyObservationConditionsPayload.Conditions), options);
        var quantities = AstronomyJsonObjectReader.DeserializeRequired<AstronomyObservationalQuantity[]>(root, nameof(AstronomyObservationConditionsPayload.Quantities), options);
        var horizontalCoordinate = AstronomyJsonObjectReader.DeserializeOptional<AstronomyHorizontalObservationCoordinate?>(root, nameof(AstronomyObservationConditionsPayload.HorizontalCoordinate), options);
        var horizonSector = AstronomyJsonObjectReader.DeserializeOptional<AstronomyHorizonSector?>(root, nameof(AstronomyObservationConditionsPayload.HorizonSector), options);

        try
        {
            return new AstronomyObservationConditionsPayload(typeId, observationContext, conditions, quantities, horizontalCoordinate, horizonSector);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Observation conditions payload is invalid.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, AstronomyObservationConditionsPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.TypeId), value.TypeId);
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.ObservationContext), value.ObservationContext);
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.Conditions), value.Conditions);
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.Quantities), value.Quantities);
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.HorizontalCoordinate), value.HorizontalCoordinate);
        WriteProperty(writer, options, nameof(AstronomyObservationConditionsPayload.HorizonSector), value.HorizonSector);
        writer.WriteEndObject();
    }

    private static void WriteProperty<T>(Utf8JsonWriter writer, JsonSerializerOptions options, string clrPropertyName, T value)
    {
        writer.WritePropertyName(AstronomyJsonObjectReader.GetJsonPropertyName(clrPropertyName, options));
        JsonSerializer.Serialize(writer, value, options);
    }
}
