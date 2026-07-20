using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Events;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Observational;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Orbital;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Temporal;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration.Converters;

internal abstract class AstronomyStringValueJsonConverter<T>(Func<string, T> create, Func<T, string> getValue, string displayName) : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"{displayName} must be a JSON string.");
        try { return create(reader.GetString()!); } catch (ArgumentException ex) { throw new JsonException($"Invalid {displayName} JSON value.", ex); }
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(getValue(value));
}

internal sealed class AstronomyKnowledgeTypeIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyKnowledgeTypeId>(v => new AstronomyKnowledgeTypeId(v), v => v.Value, "astronomy knowledge type ID");
internal sealed class AstronomyPhysicalPropertyIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyPhysicalPropertyId>(v => new AstronomyPhysicalPropertyId(v), v => v.Value, "astronomy physical property ID");
internal sealed class AstronomyOrbitalParameterIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyOrbitalParameterId>(v => new AstronomyOrbitalParameterId(v), v => v.Value, "astronomy orbital parameter ID");
internal sealed class AstronomyObservationalQuantityIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyObservationalQuantityId>(v => new AstronomyObservationalQuantityId(v), v => v.Value, "astronomy observational quantity ID");
internal sealed class AstronomyEventIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyEventId>(v => new AstronomyEventId(v), v => v.Value, "astronomy event ID");
internal sealed class AstronomyEventCircumstanceIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyEventCircumstanceId>(v => new AstronomyEventCircumstanceId(v), v => v.Value, "astronomy event circumstance ID");
internal sealed class AstronomyEventGeometryQuantityIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyEventGeometryQuantityId>(v => new AstronomyEventGeometryQuantityId(v), v => v.Value, "astronomy event geometry quantity ID");
internal sealed class AstronomyTemporalPatternIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyTemporalPatternId>(v => new AstronomyTemporalPatternId(v), v => v.Value, "astronomy temporal pattern ID");
internal sealed class AstronomyCyclePhaseIdJsonConverter() : AstronomyStringValueJsonConverter<AstronomyCyclePhaseId>(v => new AstronomyCyclePhaseId(v), v => v.Value, "astronomy cycle phase ID");
