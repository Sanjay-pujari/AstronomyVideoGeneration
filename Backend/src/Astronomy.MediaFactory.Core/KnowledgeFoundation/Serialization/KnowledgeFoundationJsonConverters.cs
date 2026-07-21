using System.Text.Json;
using JsonCodec = System.Text.Json.JsonSerializer;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;

public abstract class StrictExactStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"{typeof(TEnum).Name} must be a JSON string.");

        var text = reader.GetString();
        if (string.IsNullOrEmpty(text)
            || !Enum.TryParse<TEnum>(text, ignoreCase: false, out var value)
            || !Enum.IsDefined(value)
            || !string.Equals(text, value.ToString(), StringComparison.Ordinal))
            throw new JsonException($"Unknown {typeof(TEnum).Name} value '{text}'.");

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
            throw new JsonException($"{typeof(TEnum).Name} value is not defined.");

        writer.WriteStringValue(value.ToString());
    }
}

public sealed class StrictKnowledgeFoundationStatusJsonConverter : StrictExactStringEnumJsonConverter<KnowledgeFoundationStatus> { }

public sealed class StrictKnowledgeStatementKindJsonConverter : StrictExactStringEnumJsonConverter<KnowledgeStatementKind> { }

internal interface IAstronomyKnowledgePayloadConverterRegistration
{
    Type PayloadType { get; }
    string Discriminator { get; }
}

public sealed class KnowledgeIdJsonConverter : JsonConverter<KnowledgeId>
{
    public override KnowledgeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Knowledge ID must be a JSON string.");
        var value = reader.GetString()!;
        return Wrap(() => new KnowledgeId(value), "Invalid knowledge ID JSON value.");
    }
    public override void Write(Utf8JsonWriter writer, KnowledgeId value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value.Value)) throw new JsonException("Knowledge ID must be valid before serialization.");
        writer.WriteStringValue(value.Value);
    }
    private static KnowledgeId Wrap(Func<KnowledgeId> create, string message) { try { return create(); } catch (ArgumentException ex) { throw new JsonException(message, ex); } }
}

public sealed class KnowledgeVersionJsonConverter : JsonConverter<KnowledgeVersion>
{
    public override KnowledgeVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var revision) ? Wrap(() => new KnowledgeVersion(revision), "Invalid knowledge version JSON value.") : throw new JsonException("Knowledge version must be a JSON integer.");
    public override void Write(Utf8JsonWriter writer, KnowledgeVersion value, JsonSerializerOptions options)
    {
        if (value.Revision <= 0) throw new JsonException("Knowledge version must be valid before serialization.");
        writer.WriteNumberValue(value.Revision);
    }
    private static KnowledgeVersion Wrap(Func<KnowledgeVersion> create, string message) { try { return create(); } catch (ArgumentException ex) { throw new JsonException(message, ex); } }
}

public sealed class KnowledgeLanguageTagJsonConverter : JsonConverter<KnowledgeLanguageTag>
{
    public override KnowledgeLanguageTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Knowledge language tag must be a JSON string.");
        var value = reader.GetString()!;
        return Wrap(() => new KnowledgeLanguageTag(value), "Invalid knowledge language tag JSON value.");
    }
    public override void Write(Utf8JsonWriter writer, KnowledgeLanguageTag value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    private static KnowledgeLanguageTag Wrap(Func<KnowledgeLanguageTag> create, string message) { try { return create(); } catch (ArgumentException ex) { throw new JsonException(message, ex); } }
}

public sealed class KnowledgeTagJsonConverter : JsonConverter<KnowledgeTag>
{
    public override KnowledgeTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Knowledge tag must be a JSON string.");
        var value = reader.GetString()!;
        return Wrap(() => new KnowledgeTag(value), "Invalid knowledge tag JSON value.");
    }
    public override void Write(Utf8JsonWriter writer, KnowledgeTag value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    private static KnowledgeTag Wrap(Func<KnowledgeTag> create, string message) { try { return create(); } catch (ArgumentException ex) { throw new JsonException(message, ex); } }
}

public sealed class KnowledgeValidityRangeJsonConverter : JsonConverter<KnowledgeValidityRange>
{
    public override KnowledgeValidityRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var dto = JsonCodec.Deserialize<KnowledgeValidityRangeDto>(ref reader, options) ?? throw new JsonException("Knowledge validity range cannot be null.");
            return new KnowledgeValidityRange(dto.EffectiveFromUtc, dto.EffectiveToUtc);
        }
        catch (ArgumentException ex) { throw new JsonException("Invalid knowledge validity range JSON value.", ex); }
    }

    public override void Write(Utf8JsonWriter writer, KnowledgeValidityRange value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, new KnowledgeValidityRangeDto(value.EffectiveFromUtc, value.EffectiveToUtc), options);
    private sealed record KnowledgeValidityRangeDto(DateTimeOffset? EffectiveFromUtc, DateTimeOffset? EffectiveToUtc);
}

public sealed class KnowledgeAuditMetadataJsonConverter : JsonConverter<KnowledgeAuditMetadata>
{
    public override KnowledgeAuditMetadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var dto = JsonCodec.Deserialize<KnowledgeAuditMetadataDto>(ref reader, options) ?? throw new JsonException("Knowledge audit metadata cannot be null.");
            return new KnowledgeAuditMetadata(dto.CreatedUtc, dto.CreatedBy, dto.UpdatedUtc, dto.UpdatedBy);
        }
        catch (ArgumentException ex) { throw new JsonException("Invalid knowledge audit metadata JSON value.", ex); }
    }

    public override void Write(Utf8JsonWriter writer, KnowledgeAuditMetadata value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, new KnowledgeAuditMetadataDto(value.CreatedUtc, value.CreatedBy, value.UpdatedUtc, value.UpdatedBy), options);
    private sealed record KnowledgeAuditMetadataDto(DateTimeOffset CreatedUtc, string? CreatedBy, DateTimeOffset? UpdatedUtc, string? UpdatedBy);
}

public sealed class AstronomyKnowledgePayloadJsonConverter<TPayload>(string discriminator) : JsonConverter<TPayload>, IAstronomyKnowledgePayloadConverterRegistration where TPayload : IAstronomyKnowledgePayload
{
    private const string DiscriminatorPropertyName = "payloadKind";
    public Type PayloadType => typeof(TPayload);
    public string Discriminator => discriminator;
    public override bool HandleNull => true;
    public override TPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Knowledge payload must be a JSON object.");
        if (!document.RootElement.TryGetProperty(DiscriminatorPropertyName, out var kind) || kind.ValueKind != JsonValueKind.String) throw new JsonException("Knowledge payload discriminator is required.");
        if (!string.Equals(kind.GetString(), discriminator, StringComparison.Ordinal)) throw new JsonException($"Unknown or mismatched knowledge payload discriminator '{kind.GetString()}'.");
        var clone = new JsonSerializerOptions(options);
        RemoveConverter<AstronomyKnowledgePayloadJsonConverter<TPayload>>(clone);
        return JsonCodec.Deserialize<TPayload>(document.RootElement.GetRawText(), clone) ?? throw new JsonException("Knowledge payload cannot be null.");
    }
    public override void Write(Utf8JsonWriter writer, TPayload value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("Knowledge payload cannot be null.");
        var clone = new JsonSerializerOptions(options);
        RemoveConverter<AstronomyKnowledgePayloadJsonConverter<TPayload>>(clone);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, clone));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Knowledge payload must serialize as a JSON object.");
        if (document.RootElement.TryGetProperty(DiscriminatorPropertyName, out _)) throw new JsonException($"Knowledge payload property '{DiscriminatorPropertyName}' is reserved for the discriminator.");
        writer.WriteStartObject(); writer.WriteString(DiscriminatorPropertyName, discriminator);
        foreach (var property in document.RootElement.EnumerateObject()) property.WriteTo(writer);
        writer.WriteEndObject();
    }
    private static void RemoveConverter<TConverter>(JsonSerializerOptions options) where TConverter : JsonConverter { for (var i = options.Converters.Count - 1; i >= 0; i--) if (options.Converters[i] is TConverter) options.Converters.RemoveAt(i); }
}

public sealed class AstronomyKnowledgeStatementJsonConverter<TPayload> : JsonConverter<AstronomyKnowledgeStatement<TPayload>> where TPayload : IAstronomyKnowledgePayload
{
    public override AstronomyKnowledgeStatement<TPayload> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var dto = JsonCodec.Deserialize<StatementDto<TPayload>>(ref reader, options) ?? throw new JsonException("Knowledge statement cannot be null.");
            return new AstronomyKnowledgeStatement<TPayload>(Require(dto.Id, "id"), Require(dto.Version, "version"), dto.Kind ?? throw new JsonException("Knowledge statement kind is required."), dto.Status ?? throw new JsonException("Knowledge statement status is required."), dto.PrimarySubject ?? throw new JsonException("Knowledge statement primary subject is required."), dto.Payload ?? throw new JsonException("Knowledge statement payload is required."), dto.Audit ?? throw new JsonException("Knowledge statement audit metadata is required."), dto.FamilyContext, dto.LocalizationReferences ?? [], dto.Tags ?? [], dto.Validity ?? new KnowledgeValidityRange());
        }
        catch (ArgumentException ex) { throw new JsonException("Invalid knowledge statement JSON value.", ex); }
    }
    public override void Write(Utf8JsonWriter writer, AstronomyKnowledgeStatement<TPayload> value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, new StatementDto<TPayload>(value.Id, value.Version, value.Kind, value.Status, value.PrimarySubject, value.FamilyContext, value.Payload, value.LocalizationReferences, value.Tags, value.Validity, value.Audit), options);
    private static T Require<T>(T? value, string name) where T : struct => value ?? throw new JsonException($"Knowledge statement {name} is required.");
    private sealed record StatementDto<T>(KnowledgeId? Id, KnowledgeVersion? Version, KnowledgeStatementKind? Kind, KnowledgeFoundationStatus? Status, AstronomyEntityReference? PrimarySubject, AstronomyFamilyReference? FamilyContext, T? Payload, IReadOnlyList<KnowledgeLocalizationReference>? LocalizationReferences, IReadOnlyList<KnowledgeTag>? Tags, KnowledgeValidityRange? Validity, KnowledgeAuditMetadata? Audit) where T : IAstronomyKnowledgePayload;
}
