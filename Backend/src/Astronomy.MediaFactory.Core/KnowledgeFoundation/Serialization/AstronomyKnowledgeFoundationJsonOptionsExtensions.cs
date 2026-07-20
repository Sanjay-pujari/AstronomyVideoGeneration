using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Serialization;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;

public static class AstronomyKnowledgeFoundationJsonOptionsExtensions
{
    private const int MaxDiscriminatorLength = 128;

    public static JsonSerializerOptions AddAstronomyKnowledgeFoundationJson(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AddIfMissing(options, new KnowledgeIdJsonConverter());
        AddIfMissing(options, new KnowledgeVersionJsonConverter());
        AddIfMissing(options, new KnowledgeLanguageTagJsonConverter());
        AddIfMissing(options, new KnowledgeTagJsonConverter());
        AddIfMissing(options, new KnowledgeValidityRangeJsonConverter());
        AddIfMissing(options, new KnowledgeAuditMetadataJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictKnowledgeStatementKindJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictKnowledgeFoundationStatusJsonConverter());
        if (!options.Converters.Any(c => c is JsonStringEnumConverter)) options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    public static JsonSerializerOptions AddAstronomyEvidenceAndConfidenceJson(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddAstronomyKnowledgeFoundationJson();
        AddIfMissing(options, new EvidenceIdJsonConverter());
        AddIfMissing(options, new ConfidenceAssessmentIdJsonConverter());
        AddIfMissing(options, new KnowledgeConfidenceScoreJsonConverter());
        AddIfMissing(options, new EvidenceExternalIdentifierJsonConverter());
        AddIfMissing(options, new AstronomyEvidenceSourceReferenceJsonConverter());
        AddIfMissing(options, new EvidenceAttributionJsonConverter());
        AddIfMissing(options, new EvidenceTemporalMetadataJsonConverter());
        AddIfMissing(options, new AstronomyEvidenceRecordJsonConverter());
        AddIfMissing(options, new KnowledgeStatementEvidenceReferenceJsonConverter());
        AddIfMissing(options, new AstronomyKnowledgeStatementEvidenceSetJsonConverter());
        AddIfMissing(options, new ConfidenceAssessorReferenceJsonConverter());
        AddIfMissing(options, new ConfidenceAssessmentFactorJsonConverter());
        AddIfMissing(options, new AstronomyKnowledgeConfidenceAssessmentJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictAstronomyEvidenceTypeJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictAstronomyEvidenceSourceTypeJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictEvidenceFoundationStatusJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictKnowledgeEvidenceRoleJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictKnowledgeConfidenceLevelJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictConfidenceAssessmentMethodJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictConfidenceAssessorTypeJsonConverter());
        InsertBeforeEnumFallbackIfMissing(options, new StrictConfidenceFactorDirectionJsonConverter());
        return options;
    }

    public static JsonSerializerOptions AddAstronomyKnowledgePayload<TPayload>(this JsonSerializerOptions options, string discriminator)
        where TPayload : IAstronomyKnowledgePayload
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateDiscriminator(discriminator);
        options.AddAstronomyKnowledgeFoundationJson();
        ValidatePayloadRegistration<TPayload>(options, discriminator);
        AddIfMissing(options, new AstronomyKnowledgePayloadJsonConverter<TPayload>(discriminator));
        AddIfMissing(options, new AstronomyKnowledgeStatementJsonConverter<TPayload>());
        return options;
    }

    private static void ValidateDiscriminator(string discriminator)
    {
        if (string.IsNullOrWhiteSpace(discriminator) || discriminator.Length > MaxDiscriminatorLength || discriminator.Any(char.IsWhiteSpace) || discriminator.Any(char.IsControl))
            throw new ArgumentException($"Knowledge payload discriminator must be a stable non-empty token of {MaxDiscriminatorLength} characters or fewer with no whitespace or control characters.", nameof(discriminator));
    }

    private static void ValidatePayloadRegistration<TPayload>(JsonSerializerOptions options, string discriminator) where TPayload : IAstronomyKnowledgePayload
    {
        var payloadType = typeof(TPayload);
        foreach (var registration in options.Converters.OfType<IAstronomyKnowledgePayloadConverterRegistration>())
        {
            if (registration.PayloadType == payloadType && string.Equals(registration.Discriminator, discriminator, StringComparison.Ordinal)) return;
            if (registration.PayloadType == payloadType)
                throw new InvalidOperationException($"Knowledge payload type '{payloadType.FullName}' is already registered with discriminator '{registration.Discriminator}' and cannot be registered with '{discriminator}'.");
            if (string.Equals(registration.Discriminator, discriminator, StringComparison.Ordinal))
                throw new InvalidOperationException($"Knowledge payload discriminator '{discriminator}' is already registered for payload type '{registration.PayloadType.FullName}' and cannot be reused for '{payloadType.FullName}'.");
        }
    }

    private static void AddIfMissing<TConverter>(JsonSerializerOptions options, TConverter converter) where TConverter : JsonConverter
    {
        if (!options.Converters.Any(existing => existing.GetType() == typeof(TConverter))) options.Converters.Add(converter);
    }

    private static void InsertBeforeEnumFallbackIfMissing<TConverter>(JsonSerializerOptions options, TConverter converter) where TConverter : JsonConverter
    {
        if (options.Converters.Any(existing => existing.GetType() == typeof(TConverter))) return;

        var fallbackIndex = options.Converters
            .Select((item, index) => new { item, index })
            .Where(entry => entry.item is JsonStringEnumConverter)
            .Select(entry => entry.index)
            .DefaultIfEmpty(options.Converters.Count)
            .First();

        options.Converters.Insert(fallbackIndex, converter);
    }
}
