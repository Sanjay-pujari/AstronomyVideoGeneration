using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;

public static class AstronomyTypedKnowledgeJsonOptionsExtensions
{
    private static readonly JsonStringEnumConverter SafeEnumJsonConverter = new(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

    public static JsonSerializerOptions AddAstronomyTypedKnowledgeJson(this JsonSerializerOptions options, IAstronomyTypedPayloadRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);
        options.AddAstronomyEvidenceAndConfidenceJson();
        AddIfMissing(options, new AstronomyClassificationSchemeIdJsonConverter());
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
        AddIfMissing(options, new AstronomyOrbitalParametersPayloadJsonConverter());
        AddIfMissing(options, new AstronomyObservationConditionsPayloadJsonConverter());
        AddIfMissing(options, new AstronomyVisibilityWindowsPayloadJsonConverter());
        if (!options.Converters.Any(c => c is AstronomyTypedKnowledgePayloadJsonConverter)) options.Converters.Add(new AstronomyTypedKnowledgePayloadJsonConverter(registry));
        AddIfMissing(options, new AstronomyKnowledgeStatementJsonConverter<ITypedAstronomyKnowledgePayload>());
        // This integration-owned enum converter is appended after payload converters and added only once.
        // If callers inserted an earlier enum converter, System.Text.Json precedence still honors that earlier converter.
        if (!options.Converters.Any(existing => existing is JsonStringEnumConverter)) options.Converters.Add(SafeEnumJsonConverter);
        return options;
    }

    private static void AddIfMissing<TConverter>(JsonSerializerOptions options, TConverter converter) where TConverter : JsonConverter
    {
        if (!options.Converters.Any(existing => existing.GetType() == typeof(TConverter))) options.Converters.Add(converter);
    }
}
