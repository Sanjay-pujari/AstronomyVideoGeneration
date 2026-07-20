using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public static class AstronomyTypedKnowledgeJsonOptionsExtensions
{
    public static JsonSerializerOptions AddAstronomyTypedKnowledgeJson(this JsonSerializerOptions options, IAstronomyTypedPayloadRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);
        options.AddAstronomyEvidenceAndConfidenceJson();
        AddIfMissing(options, new AstronomyKnowledgeTypeIdJsonConverter());
        AddIfMissing(options, new AstronomyPhysicalPropertyIdJsonConverter());
        AddIfMissing(options, new AstronomyOrbitalParameterIdJsonConverter());
        AddIfMissing(options, new AstronomyObservationalQuantityIdJsonConverter());
        AddIfMissing(options, new AstronomyEventIdJsonConverter());
        AddIfMissing(options, new AstronomyEventCircumstanceIdJsonConverter());
        AddIfMissing(options, new AstronomyEventGeometryQuantityIdJsonConverter());
        AddIfMissing(options, new AstronomyTemporalPatternIdJsonConverter());
        AddIfMissing(options, new AstronomyCyclePhaseIdJsonConverter());
        AddIfMissing(options, new AstronomyPhysicalPropertyValueJsonConverter());
        AddIfMissing(options, new AstronomyPositionValueJsonConverter());
        AddIfMissing(options, new AstronomyEventTemporalExtentJsonConverter());
        AddIfMissing(options, new AstronomyTemporalAnchorJsonConverter());
        if (!options.Converters.Any(c => c is AstronomyTypedKnowledgePayloadJsonConverter)) options.Converters.Add(new AstronomyTypedKnowledgePayloadJsonConverter(registry));
        AddIfMissing(options, new AstronomyKnowledgeStatementJsonConverter<ITypedAstronomyKnowledgePayload>());
        if (!options.Converters.Any(c => c is JsonStringEnumConverter)) options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void AddIfMissing<TConverter>(JsonSerializerOptions options, TConverter converter) where TConverter : JsonConverter
    {
        if (!options.Converters.Any(existing => existing.GetType() == typeof(TConverter))) options.Converters.Add(converter);
    }
}
