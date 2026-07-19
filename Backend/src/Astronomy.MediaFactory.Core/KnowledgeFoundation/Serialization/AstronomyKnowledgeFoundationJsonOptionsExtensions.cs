using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;

public static class AstronomyKnowledgeFoundationJsonOptionsExtensions
{
    public static JsonSerializerOptions AddAstronomyKnowledgeFoundationJson(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AddIfMissing(options, new KnowledgeIdJsonConverter());
        AddIfMissing(options, new KnowledgeVersionJsonConverter());
        AddIfMissing(options, new KnowledgeLanguageTagJsonConverter());
        AddIfMissing(options, new KnowledgeTagJsonConverter());
        AddIfMissing(options, new KnowledgeValidityRangeJsonConverter());
        AddIfMissing(options, new KnowledgeAuditMetadataJsonConverter());
        if (!options.Converters.Any(c => c is JsonStringEnumConverter)) options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static JsonSerializerOptions AddAstronomyKnowledgePayload<TPayload>(this JsonSerializerOptions options, string discriminator)
        where TPayload : IAstronomyKnowledgePayload
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(discriminator) || discriminator.Any(char.IsWhiteSpace) || discriminator.Any(char.IsControl)) throw new ArgumentException("Knowledge payload discriminator must be a stable non-empty token.", nameof(discriminator));
        options.AddAstronomyKnowledgeFoundationJson();
        AddIfMissing(options, new AstronomyKnowledgePayloadJsonConverter<TPayload>(discriminator));
        AddIfMissing(options, new AstronomyKnowledgeStatementJsonConverter<TPayload>());
        return options;
    }

    private static void AddIfMissing<TConverter>(JsonSerializerOptions options, TConverter converter) where TConverter : JsonConverter
    {
        if (!options.Converters.Any(existing => existing.GetType() == typeof(TConverter))) options.Converters.Add(converter);
    }
}
