using System.Text.Json;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Confidence;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Serialization;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Evidence.Serialization;

public sealed class StrictAstronomyEvidenceTypeJsonConverter : JsonStringEnumConverter<AstronomyEvidenceType> { public StrictAstronomyEvidenceTypeJsonConverter() : base(null, false) { } }
public sealed class StrictAstronomyEvidenceSourceTypeJsonConverter : JsonStringEnumConverter<AstronomyEvidenceSourceType> { public StrictAstronomyEvidenceSourceTypeJsonConverter() : base(null, false) { } }
public sealed class StrictEvidenceFoundationStatusJsonConverter : JsonStringEnumConverter<EvidenceFoundationStatus> { public StrictEvidenceFoundationStatusJsonConverter() : base(null, false) { } }
public sealed class StrictKnowledgeEvidenceRoleJsonConverter : JsonStringEnumConverter<KnowledgeEvidenceRole> { public StrictKnowledgeEvidenceRoleJsonConverter() : base(null, false) { } }
public sealed class StrictKnowledgeConfidenceLevelJsonConverter : JsonStringEnumConverter<KnowledgeConfidenceLevel> { public StrictKnowledgeConfidenceLevelJsonConverter() : base(null, false) { } }
public sealed class StrictConfidenceAssessmentMethodJsonConverter : JsonStringEnumConverter<ConfidenceAssessmentMethod> { public StrictConfidenceAssessmentMethodJsonConverter() : base(null, false) { } }
public sealed class StrictConfidenceAssessorTypeJsonConverter : JsonStringEnumConverter<ConfidenceAssessorType> { public StrictConfidenceAssessorTypeJsonConverter() : base(null, false) { } }
public sealed class StrictConfidenceFactorDirectionJsonConverter : JsonStringEnumConverter<ConfidenceFactorDirection> { public StrictConfidenceFactorDirectionJsonConverter() : base(null, false) { } }

public sealed class EvidenceIdJsonConverter : JsonConverter<EvidenceId>
{
    public override EvidenceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { if (reader.TokenType != JsonTokenType.String) throw new JsonException("Evidence ID must be a JSON string."); try { return new EvidenceId(reader.GetString()!); } catch (ArgumentException ex) { throw new JsonException("Invalid evidence ID JSON value.", ex); } }
    public override void Write(Utf8JsonWriter writer, EvidenceId value, JsonSerializerOptions options) { if (string.IsNullOrWhiteSpace(value.Value)) throw new JsonException("Evidence ID must be valid before serialization."); writer.WriteStringValue(value.Value); }
}

public sealed class ConfidenceAssessmentIdJsonConverter : JsonConverter<ConfidenceAssessmentId>
{
    public override ConfidenceAssessmentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { if (reader.TokenType != JsonTokenType.String) throw new JsonException("Confidence assessment ID must be a JSON string."); try { return new ConfidenceAssessmentId(reader.GetString()!); } catch (ArgumentException ex) { throw new JsonException("Invalid confidence assessment ID JSON value.", ex); } }
    public override void Write(Utf8JsonWriter writer, ConfidenceAssessmentId value, JsonSerializerOptions options) { if (string.IsNullOrWhiteSpace(value.Value)) throw new JsonException("Confidence assessment ID must be valid before serialization."); writer.WriteStringValue(value.Value); }
}

public sealed class KnowledgeConfidenceScoreJsonConverter : JsonConverter<KnowledgeConfidenceScore>
{
    public override KnowledgeConfidenceScore Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value)) throw new JsonException("Knowledge confidence score must be a JSON number."); try { return new KnowledgeConfidenceScore(value); } catch (ArgumentException ex) { throw new JsonException("Invalid knowledge confidence score JSON value.", ex); } }
    public override void Write(Utf8JsonWriter writer, KnowledgeConfidenceScore value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
}

public sealed class EvidenceExternalIdentifierJsonConverter : DomainConverter<EvidenceExternalIdentifier>
{
    protected override string[] Names => ["scheme", "value"];
    protected override EvidenceExternalIdentifier Create(JsonElement e, JsonSerializerOptions o) => new(ReqString(e,"scheme"), ReqString(e,"value"));
    protected override void WriteCore(Utf8JsonWriter w, EvidenceExternalIdentifier v, JsonSerializerOptions o) { w.WriteString("scheme", v.Scheme); w.WriteString("value", v.Value); }
}
public sealed class AstronomyEvidenceSourceReferenceJsonConverter : DomainConverter<AstronomyEvidenceSourceReference>
{
    protected override string[] Names => ["sourceId","sourceType","displayName","canonicalUri","externalIdentifier"];
    protected override AstronomyEvidenceSourceReference Create(JsonElement e, JsonSerializerOptions o) => new(ReqString(e,"sourceId"), Req<AstronomyEvidenceSourceType>(e,"sourceType",o), ReqString(e,"displayName"), OptUri(e,"canonicalUri"), Opt<EvidenceExternalIdentifier>(e,"externalIdentifier",o));
    protected override void WriteCore(Utf8JsonWriter w, AstronomyEvidenceSourceReference v, JsonSerializerOptions o) { w.WriteString("sourceId", v.SourceId); w.WritePropertyName("sourceType"); JsonSerializer.Serialize(w,v.SourceType,o); w.WriteString("displayName",v.DisplayName); if (v.CanonicalUri is null) w.WriteNull("canonicalUri"); else w.WriteString("canonicalUri", v.CanonicalUri.AbsoluteUri); w.WritePropertyName("externalIdentifier"); JsonSerializer.Serialize(w,v.ExternalIdentifier,o); }
}
public sealed class EvidenceAttributionJsonConverter : DomainConverter<EvidenceAttribution>
{
    protected override string[] Names => ["contributors","organizationName","publisherName","publicationTitle","editionOrVersion","displayCitation"];
    protected override EvidenceAttribution Create(JsonElement e, JsonSerializerOptions o) => new(ReqList<string>(e,"contributors",o), OptString(e,"organizationName"), OptString(e,"publisherName"), OptString(e,"publicationTitle"), OptString(e,"editionOrVersion"), OptString(e,"displayCitation"));
    protected override void WriteCore(Utf8JsonWriter w, EvidenceAttribution v, JsonSerializerOptions o) { W(w,"contributors",v.Contributors,o); w.WriteString("organizationName",v.OrganizationName); w.WriteString("publisherName",v.PublisherName); w.WriteString("publicationTitle",v.PublicationTitle); w.WriteString("editionOrVersion",v.EditionOrVersion); w.WriteString("displayCitation",v.DisplayCitation); }
}
public sealed class EvidenceTemporalMetadataJsonConverter : DomainConverter<EvidenceTemporalMetadata>
{
    protected override string[] Names => ["observedAtUtc","publishedAtUtc","retrievedAtUtc","applicability"];
    protected override EvidenceTemporalMetadata Create(JsonElement e, JsonSerializerOptions o) => new(Opt<DateTimeOffset>(e,"observedAtUtc",o), Opt<DateTimeOffset>(e,"publishedAtUtc",o), Opt<DateTimeOffset>(e,"retrievedAtUtc",o), Opt<KnowledgeValidityRange>(e,"applicability",o) ?? new KnowledgeValidityRange());
    protected override void WriteCore(Utf8JsonWriter w, EvidenceTemporalMetadata v, JsonSerializerOptions o) { W(w,"observedAtUtc",v.ObservedAtUtc,o); W(w,"publishedAtUtc",v.PublishedAtUtc,o); W(w,"retrievedAtUtc",v.RetrievedAtUtc,o); W(w,"applicability",v.Applicability,o); }
}
public sealed class AstronomyEvidenceRecordJsonConverter : DomainConverter<AstronomyEvidenceRecord>
{
    protected override string[] Names => ["id","type","status","source","temporalMetadata","audit","attribution","title","summary","externalIdentifiers","tags"];
    protected override AstronomyEvidenceRecord Create(JsonElement e, JsonSerializerOptions o) => new(Req<EvidenceId>(e,"id",o), Req<AstronomyEvidenceType>(e,"type",o), Req<EvidenceFoundationStatus>(e,"status",o), Req<AstronomyEvidenceSourceReference>(e,"source",o), Req<EvidenceTemporalMetadata>(e,"temporalMetadata",o), Req<KnowledgeAuditMetadata>(e,"audit",o), Opt<EvidenceAttribution>(e,"attribution",o), OptString(e,"title"), OptString(e,"summary"), ReqList<EvidenceExternalIdentifier>(e,"externalIdentifiers",o), ReqList<KnowledgeTag>(e,"tags",o));
    protected override void WriteCore(Utf8JsonWriter w, AstronomyEvidenceRecord v, JsonSerializerOptions o) { W(w,"id",v.Id,o); W(w,"type",v.Type,o); W(w,"status",v.Status,o); W(w,"source",v.Source,o); W(w,"temporalMetadata",v.TemporalMetadata,o); W(w,"audit",v.Audit,o); W(w,"attribution",v.Attribution,o); w.WriteString("title",v.Title); w.WriteString("summary",v.Summary); W(w,"externalIdentifiers",v.ExternalIdentifiers,o); W(w,"tags",v.Tags,o); }
}
public sealed class KnowledgeStatementEvidenceReferenceJsonConverter : DomainConverter<KnowledgeStatementEvidenceReference>
{
    protected override string[] Names => ["knowledgeId","knowledgeVersion","evidenceId","role","note"];
    protected override KnowledgeStatementEvidenceReference Create(JsonElement e, JsonSerializerOptions o) => new(Req<KnowledgeId>(e,"knowledgeId",o), Req<KnowledgeVersion>(e,"knowledgeVersion",o), Req<EvidenceId>(e,"evidenceId",o), Req<KnowledgeEvidenceRole>(e,"role",o), OptString(e,"note"));
    protected override void WriteCore(Utf8JsonWriter w, KnowledgeStatementEvidenceReference v, JsonSerializerOptions o) { W(w,"knowledgeId",v.KnowledgeId,o); W(w,"knowledgeVersion",v.KnowledgeVersion,o); W(w,"evidenceId",v.EvidenceId,o); W(w,"role",v.Role,o); w.WriteString("note",v.Note); }
}
public sealed class AstronomyKnowledgeStatementEvidenceSetJsonConverter : DomainConverter<AstronomyKnowledgeStatementEvidenceSet>
{
    protected override string[] Names => ["knowledgeId","knowledgeVersion","associations"];
    protected override AstronomyKnowledgeStatementEvidenceSet Create(JsonElement e, JsonSerializerOptions o) => new(Req<KnowledgeId>(e,"knowledgeId",o), Req<KnowledgeVersion>(e,"knowledgeVersion",o), ReqList<KnowledgeStatementEvidenceReference>(e,"associations",o));
    protected override void WriteCore(Utf8JsonWriter w, AstronomyKnowledgeStatementEvidenceSet v, JsonSerializerOptions o) { W(w,"knowledgeId",v.KnowledgeId,o); W(w,"knowledgeVersion",v.KnowledgeVersion,o); W(w,"associations",v.Associations,o); }
}
public sealed class ConfidenceAssessorReferenceJsonConverter : DomainConverter<ConfidenceAssessorReference>
{
    protected override string[] Names => ["assessorId","assessorType","displayName","organization","modelOrSystemVersion"];
    protected override ConfidenceAssessorReference Create(JsonElement e, JsonSerializerOptions o) => new(ReqString(e,"assessorId"), Req<ConfidenceAssessorType>(e,"assessorType",o), ReqString(e,"displayName"), OptString(e,"organization"), OptString(e,"modelOrSystemVersion"));
    protected override void WriteCore(Utf8JsonWriter w, ConfidenceAssessorReference v, JsonSerializerOptions o) { w.WriteString("assessorId",v.AssessorId); W(w,"assessorType",v.AssessorType,o); w.WriteString("displayName",v.DisplayName); w.WriteString("organization",v.Organization); w.WriteString("modelOrSystemVersion",v.ModelOrSystemVersion); }
}
public sealed class ConfidenceAssessmentFactorJsonConverter : DomainConverter<ConfidenceAssessmentFactor>
{
    protected override string[] Names => ["code","direction","note"];
    protected override ConfidenceAssessmentFactor Create(JsonElement e, JsonSerializerOptions o) => new(ReqString(e,"code"), Req<ConfidenceFactorDirection>(e,"direction",o), OptString(e,"note"));
    protected override void WriteCore(Utf8JsonWriter w, ConfidenceAssessmentFactor v, JsonSerializerOptions o) { w.WriteString("code",v.Code); W(w,"direction",v.Direction,o); w.WriteString("note",v.Note); }
}
public sealed class AstronomyKnowledgeConfidenceAssessmentJsonConverter : DomainConverter<AstronomyKnowledgeConfidenceAssessment>
{
    protected override string[] Names => ["id","knowledgeId","knowledgeVersion","level","score","method","assessor","audit","evidenceIds","factors","rationale"];
    protected override AstronomyKnowledgeConfidenceAssessment Create(JsonElement e, JsonSerializerOptions o) => new(Req<ConfidenceAssessmentId>(e,"id",o), Req<KnowledgeId>(e,"knowledgeId",o), Req<KnowledgeVersion>(e,"knowledgeVersion",o), Req<KnowledgeConfidenceLevel>(e,"level",o), Opt<KnowledgeConfidenceScore>(e,"score",o), Req<ConfidenceAssessmentMethod>(e,"method",o), Req<ConfidenceAssessorReference>(e,"assessor",o), Req<KnowledgeAuditMetadata>(e,"audit",o), ReqList<EvidenceId>(e,"evidenceIds",o), ReqList<ConfidenceAssessmentFactor>(e,"factors",o), OptString(e,"rationale"));
    protected override void WriteCore(Utf8JsonWriter w, AstronomyKnowledgeConfidenceAssessment v, JsonSerializerOptions o) { W(w,"id",v.Id,o); W(w,"knowledgeId",v.KnowledgeId,o); W(w,"knowledgeVersion",v.KnowledgeVersion,o); W(w,"level",v.Level,o); W(w,"score",v.Score,o); W(w,"method",v.Method,o); W(w,"assessor",v.Assessor,o); W(w,"audit",v.Audit,o); W(w,"evidenceIds",v.EvidenceIds,o); W(w,"factors",v.Factors,o); w.WriteString("rationale",v.Rationale); }
}

public abstract class DomainConverter<T> : JsonConverter<T>
{
    protected abstract string[] Names { get; }
    public sealed override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) { try { using var d=JsonDocument.ParseValue(ref reader); var e=d.RootElement; if(e.ValueKind!=JsonValueKind.Object) throw new JsonException($"{typeof(T).Name} must be a JSON object."); Guard(e); return Create(e,options); } catch (JsonException) { throw; } catch (ArgumentException ex) { throw new JsonException($"Invalid {typeof(T).Name} JSON value.", ex); } }
    public sealed override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) { writer.WriteStartObject(); WriteCore(writer,value,options); writer.WriteEndObject(); }
    protected abstract T Create(JsonElement e, JsonSerializerOptions o); protected abstract void WriteCore(Utf8JsonWriter w, T v, JsonSerializerOptions o);
    private void Guard(JsonElement e){ var seen=new HashSet<string>(StringComparer.Ordinal); foreach(var p in e.EnumerateObject()){ if(!Names.Contains(p.Name,StringComparer.Ordinal)) throw new JsonException($"Unknown JSON property '{p.Name}'."); if(!seen.Add(p.Name)) throw new JsonException($"Duplicate JSON property '{p.Name}'."); } }
    protected static string ReqString(JsonElement e,string n)=> e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String ? p.GetString()! : throw new JsonException($"Required JSON string property '{n}' is missing or null.");
    protected static string? OptString(JsonElement e,string n)=> !e.TryGetProperty(n,out var p)||p.ValueKind==JsonValueKind.Null ? null : p.ValueKind==JsonValueKind.String ? p.GetString() : throw new JsonException($"JSON property '{n}' must be a string or null.");
    protected static Uri? OptUri(JsonElement e,string n)=> !e.TryGetProperty(n,out var p)||p.ValueKind==JsonValueKind.Null ? null : p.ValueKind==JsonValueKind.String&&Uri.TryCreate(p.GetString(),UriKind.Absolute,out var u) ? u : throw new JsonException($"JSON property '{n}' must be an absolute URI string or null.");
    protected static TVal Req<TVal>(JsonElement e,string n,JsonSerializerOptions o)=> e.TryGetProperty(n,out var p)&&p.ValueKind!=JsonValueKind.Null ? JsonSerializer.Deserialize<TVal>(p.GetRawText(),o)! : throw new JsonException($"Required JSON property '{n}' is missing or null.");
    protected static TVal? Opt<TVal>(JsonElement e,string n,JsonSerializerOptions o)=> !e.TryGetProperty(n,out var p)||p.ValueKind==JsonValueKind.Null ? default : JsonSerializer.Deserialize<TVal>(p.GetRawText(),o);
    protected static IReadOnlyList<TVal> ReqList<TVal>(JsonElement e,string n,JsonSerializerOptions o)=> e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.Array ? (JsonSerializer.Deserialize<List<TVal>>(p.GetRawText(),o) ?? throw new JsonException()) : throw new JsonException($"Required JSON array property '{n}' is missing or null.");
    protected static void W<TVal>(Utf8JsonWriter w,string n,TVal v,JsonSerializerOptions o){ w.WritePropertyName(n); JsonSerializer.Serialize(w,v,o); }
}
