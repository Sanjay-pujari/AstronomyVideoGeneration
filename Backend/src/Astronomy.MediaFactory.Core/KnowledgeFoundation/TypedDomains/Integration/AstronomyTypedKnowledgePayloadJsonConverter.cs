using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public sealed class AstronomyTypedKnowledgePayloadJsonConverter : JsonConverter<ITypedAstronomyKnowledgePayload>
{
    private const string TypePropertyName = "type";
    private const string ValuePropertyName = "value";
    private readonly IAstronomyTypedPayloadRegistry registry;

    public AstronomyTypedKnowledgePayloadJsonConverter(IAstronomyTypedPayloadRegistry registry) => this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    public override bool HandleNull => true;

    public override ITypedAstronomyKnowledgePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Typed astronomy knowledge payload envelope must be a JSON object.");
        string? discriminator = null; JsonElement? value = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(TypePropertyName)) { if (discriminator is not null) throw new JsonException("Duplicate typed payload type property is not allowed."); if (property.Value.ValueKind != JsonValueKind.String) throw new JsonException("Typed payload type must be a JSON string."); discriminator = property.Value.GetString(); }
            else if (property.NameEquals(ValuePropertyName)) { if (value.HasValue) throw new JsonException("Duplicate typed payload value property is not allowed."); value = property.Value.Clone(); }
        }
        if (string.IsNullOrWhiteSpace(discriminator)) throw new JsonException("Typed payload type is required.");
        if (!value.HasValue || value.Value.ValueKind == JsonValueKind.Null) throw new JsonException("Typed payload value is required.");
        if (!registry.TryGetByDiscriminator(discriminator, out var descriptor)) throw new JsonException($"Unknown typed astronomy knowledge payload discriminator '{discriminator}'.");
        var clone = CreatePayloadOptions(options);
        var result = (ITypedAstronomyKnowledgePayload?)JsonSerializer.Deserialize(value.Value.GetRawText(), descriptor.PayloadType, clone) ?? throw new JsonException("Typed payload value cannot be null.");
        VerifyCompatibility(result, descriptor);
        return result;
    }

    public override void Write(Utf8JsonWriter writer, ITypedAstronomyKnowledgePayload value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("Typed astronomy knowledge payload cannot be null.");
        if (!registry.TryGetByPayloadType(value.GetType(), out var descriptor)) throw new JsonException($"Typed astronomy knowledge payload type '{value.GetType().Name}' is not registered.");
        VerifyCompatibility(value, descriptor);
        writer.WriteStartObject();
        writer.WriteString(TypePropertyName, descriptor.Discriminator);
        writer.WritePropertyName(ValuePropertyName);
        JsonSerializer.Serialize(writer, value, descriptor.PayloadType, CreatePayloadOptions(options));
        writer.WriteEndObject();
    }

    private static void VerifyCompatibility(ITypedAstronomyKnowledgePayload payload, AstronomyTypedPayloadDescriptor descriptor)
    {
        if (payload.Domain != descriptor.Domain) throw new JsonException($"Typed payload domain '{payload.Domain}' does not match registered domain '{descriptor.Domain}'.");
        if (payload.Family != descriptor.Family) throw new JsonException($"Typed payload family '{payload.Family}' does not match registered family '{descriptor.Family}'.");
    }

    private static JsonSerializerOptions CreatePayloadOptions(JsonSerializerOptions options)
    {
        var clone = new JsonSerializerOptions(options);
        for (var i = clone.Converters.Count - 1; i >= 0; i--) if (clone.Converters[i] is AstronomyTypedKnowledgePayloadJsonConverter) clone.Converters.RemoveAt(i);
        return clone;
    }
}
