using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.EntityFrameworkCore;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

/// <summary>Joins the certified event repository to the approved evergreen loader. No arbitrary path is read here.</summary>
public sealed class Phase7CertifiedKnowledgeSource(MediaFactoryDbContext db, IEvergreenAstronomyKnowledgeLoader evergreenLoader) : IPhase7CertifiedKnowledgeSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CertifiedKnowledgePayload?> ResolveAsync(string eventId, string language, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var item = await db.AstronomyEventIntelligences.AsNoTracking().Include(x => x.ReferenceSources)
            .FirstOrDefaultAsync(x => (x.ExternalEventId == eventId || x.EventCode == eventId || x.Id.ToString() == eventId)
                && x.Language == language, token);
        if (item is null) return null;
        if (!Certified(item.VerificationStatus)) throw new InvalidDataException("P7KNOWLEDGE_EVENT_NOT_CERTIFIED");

        var relativePath = MetadataString(item.MetadataJson, "relativePath");
        EvergreenAstronomyKnowledgeLoadResult? evergreen = null;
        if (!string.IsNullOrWhiteSpace(relativePath)) evergreen = await evergreenLoader.LoadByRelativePathAsync(relativePath, token);
        if (evergreen is not null && !evergreen.Package.LocalizedContent.ContainsKey(language.Split('-', '_')[0]))
            throw new InvalidDataException("P7KNOWLEDGE_LANGUAGE_MISMATCH");

        var family = ResolveAuthoritativeFamily(item.EventType, item.RecommendedCategory);
        if (evergreen is not null && !string.Equals(family, NormalizeFamily(evergreen.Package.FamilyCode), StringComparison.Ordinal))
            throw new InvalidDataException("P7KNOWLEDGE_SUBJECT_FAMILY_MISMATCH");
        var eventSources = item.ReferenceSources.Select(s => EventSource(s, language)).ToList();
        if (evergreen is not null) eventSources.AddRange(evergreen.Package.Sources.Select(s => EvergreenSource(s, evergreen.Package, language)));
        var allSources = eventSources.DistinctBy(s => s.SourceId).OrderBy(s => s.SourceId, StringComparer.Ordinal).ToArray();
        var sources = allSources.Where(s => s.Reviewed && s.Certified).ToArray();
        var raw = item.RawDataJson ?? "";
        var evergreenJson = evergreen is null ? null : JsonSerializer.Serialize(evergreen.Package, Json);
        var registryId = $"event-source-registry-{item.Id:N}";
        var draft = new CertifiedKnowledgePayload(item.Id.ToString(), eventId, family, item.EventType, item.Language, raw,
            item.MetadataJson, evergreenJson, registryId, sources.Select(s => s.SourceId).ToArray(), item.VerificationStatus)
        {
            CertifiedEventFamily = family, EvergreenRelativePath = evergreen?.RelativePath,
            EvergreenPayloadId = evergreen?.Package.KnowledgeId, EvergreenChecksum = evergreen?.Checksum,
            ReviewedSources = sources, AllResolvedSources=allSources, CertifiedSupportingSources=sources,
            RejectedSources=allSources.Where(s=>s.Disposition=="Rejected").ToArray(),
            UnverifiedSources=allSources.Where(s=>!s.Certified&&s.Disposition!="Rejected").ToArray(),
            CertificationStatus = evergreen is null ? item.VerificationStatus : evergreen.Package.ReviewStatus
        };
        return draft with { PayloadChecksum = Phase7Determinism.Hash(new { draft.PayloadId, draft.EventId, draft.CertifiedEventFamily, draft.Language, draft.RawDataJson, draft.MetadataJson, draft.EvergreenChecksum, sources }) };
    }

    public async Task<Phase7CertifiedKnowledgeSourceResult> ResolveResultAsync(string eventId, string language, CancellationToken token = default)
    {
        try
        {
            var payload=await ResolveAsync(eventId,language,token);
            return payload is null ? Failure("P7KNOWLEDGE_EVENT_MISSING","Certified event intelligence was not found.")
                : new(true,payload,"P7KNOWLEDGE_VALID",[],payload.Warnings);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(JsonException ex){return Failure("P7KNOWLEDGE_METADATA_INVALID",ex.Message);}
        catch(InvalidDataException ex)
        {
            var code=ex.Message.Split(':',2)[0];
            return Failure(code.StartsWith("P7KNOWLEDGE_",StringComparison.Ordinal)?code:"P7KNOWLEDGE_EVERGREEN_LOAD_FAILED",ex.Message);
        }
        catch(IOException ex){return Failure("P7KNOWLEDGE_EVERGREEN_LOAD_FAILED",ex.Message);}
        static Phase7CertifiedKnowledgeSourceResult Failure(string code,string error)=>new(false,null,code,[error],[]);
    }

    private static CertifiedNarrationSource EventSource(AstronomyReferenceSource s, string language)
    {
        var knowledge = JsonArray(s.EvidenceJson, "supportedKnowledgeIds");
        var claims = JsonArray(s.EvidenceJson, "supportedClaimIds");
        var domains = JsonArray(s.EvidenceJson, "supportedDomains");
        var rawFields = JsonArray(s.EvidenceJson, "supportedApprovedFieldPaths");
        var invalidFields = rawFields.Where(x => !Phase7CanonicalFieldPathPolicy.TryCanonicalize(x, out _))
            .Select(x => $"P7KNOWLEDGE_SOURCE_FIELD_PATH_INVALID:{x}").Order(StringComparer.Ordinal).ToArray();
        var fields = rawFields
            .Select(x => Phase7CanonicalFieldPathPolicy.TryCanonicalize(x, out var canonical) ? canonical : "")
            .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var reviewStatus = EvidenceString(s.EvidenceJson,"reviewStatus");
        var certificationStatus = EvidenceString(s.EvidenceJson,"certificationStatus") ?? EvidenceString(s.EvidenceJson,"verificationStatus");
        var reviewed = ApprovedReviewState(reviewStatus);
        var certified = certificationStatus is not null && (certificationStatus.Equals("Certified",StringComparison.OrdinalIgnoreCase)||certificationStatus.Equals("Verified",StringComparison.OrdinalIgnoreCase));
        var draft = new CertifiedNarrationSource(s.Id.ToString(), s.SourceType, s.SourceName, s.SourceName,
            s.SourceUrl ?? s.Citation ?? "", reviewed, certified, knowledge, claims, domains, language,
            Math.Clamp(s.ConfidenceScore ?? .8m, 0m, 1m), "");
        draft=draft with { SupportedApprovedFieldPaths=fields,RegistryDiagnostics=invalidFields,Disposition=certified&&reviewed?"CertifiedSupporting":reviewStatus?.Equals("Rejected",StringComparison.OrdinalIgnoreCase)==true?"Rejected":certified?"RejectedReviewState":"Unverified" };
        return draft with { Checksum = Phase7Determinism.Hash(draft with { Checksum = "" }) };
    }
    private static CertifiedNarrationSource EvergreenSource(EvergreenKnowledgeSource s, EvergreenAstronomyKnowledgePackage p, string language)
    {
        var supportedKnowledge = p.Objects.Where(o => o.SourceIds.Contains(s.SourceId, StringComparer.OrdinalIgnoreCase)).Select(o => o.ObjectId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var confidence = s.Confidence.Equals("High", StringComparison.OrdinalIgnoreCase) ? .98m : .85m;
        var accepted = ApprovedEvergreenState(s.ReviewStatus);
        var supportedFields = new Phase7KnowledgeSectionAdapterRegistry().Adapters
            .Where(a => a.SupportedSectionNames.Any(section => s.SupportedSections.Contains(section, StringComparer.OrdinalIgnoreCase)))
            .SelectMany(a => a.ApprovedFieldPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var draft = new CertifiedNarrationSource(s.SourceId, s.SourceType, s.Title, s.Authority, s.Reference,
            accepted, accepted,
            supportedKnowledge, [], s.SupportedSections.Select(CanonicalDomain).Distinct().ToArray(), language, confidence, "");
        draft = draft with { SupportedApprovedFieldPaths = supportedFields, Disposition = accepted ? "CertifiedSupporting" : "RejectedReviewState" };
        return draft with { Checksum = Phase7Determinism.Hash(draft with { Checksum = "" }) };
    }
    private static string ResolveAuthoritativeFamily(string eventType, string category)
    {
        var direct = NormalizeFamily(eventType);
        var known = new HashSet<string>(StringComparer.Ordinal) { "CONSTELLATION","METEOR_SHOWER","LUNAR_PHASE","ISS_PASS","CLOSE_APPROACH","DEEP_SKY_OBJECT","STAR_FORMING_REGION","CONJUNCTION","GROUPING","OCCULTATION","TRANSIT","OPPOSITION","ELONGATION","ECLIPSE","COMET","SATELLITE","GALAXY","NEBULA","CLUSTER","PLANET","MOON","STAR" };
        if (known.Contains(direct)) return direct;
        var resolved = EventFamilyResolver.Resolve(eventType, category, [], [], "").ToString().ToUpperInvariant();
        return resolved switch { "PLANETGROUPING" => "GROUPING", "DEEPSKYOBJECT" => "DEEP_SKY_OBJECT", "METEORSHOWER" => "METEOR_SHOWER", _ => resolved };
    }
    private static string NormalizeFamily(string value) => value.Trim().Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
    private static bool Certified(string value) => value.Equals("Certified", StringComparison.OrdinalIgnoreCase) || value.Equals("Verified", StringComparison.OrdinalIgnoreCase);
    private static bool ApprovedReviewState(string? value) => value is not null && new[] { "Approved", "Reviewed", "Verified", "Certified" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static bool ApprovedEvergreenState(string? value) => value is not null && new[] { "Reviewed", "Verified", "Certified" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static string? MetadataString(string? json, string key) { if (string.IsNullOrWhiteSpace(json)) return null; using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null; }
    private static string[] JsonArray(string? json, string key) { if (string.IsNullOrWhiteSpace(json)) return []; try { using var d=JsonDocument.Parse(json); return d.RootElement.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array ? v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).ToArray() : []; } catch(JsonException) { return []; } }
    private static string? EvidenceString(string? json,string key){if(string.IsNullOrWhiteSpace(json))return null;using var d=JsonDocument.Parse(json);return d.RootElement.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;}
    private static string CanonicalDomain(string value) => NarrationKnowledgeDomains.TryParse(value, out var key) ? key.ToString() : value;
}
