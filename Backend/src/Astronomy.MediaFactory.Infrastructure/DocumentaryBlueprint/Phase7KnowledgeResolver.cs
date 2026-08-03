using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Bounded adapter extraction and conflict-aware certified knowledge merge.</summary>
public sealed class Phase7KnowledgeResolver : IPhase7KnowledgeResolver
{
    private readonly Phase7KnowledgeSectionAdapterRegistry registry;
    private readonly IPhase7KnowledgeMergeClassifier classifier;
    public Phase7KnowledgeResolver() : this(new Phase7KnowledgeSectionAdapterRegistry(),new Phase7KnowledgeMergeClassifier()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry) : this(registry,new Phase7KnowledgeMergeClassifier()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry,IPhase7KnowledgeMergeClassifier classifier) { this.registry=registry;this.classifier=classifier; }

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile)
    {
        var issues = new List<string>(); var warnings = new List<string>(payload.Warnings); var candidates = new List<Phase7AdapterClaimCandidate>();
        var adapterDiagnostics=new List<Phase7KnowledgeAdapterDiagnostic>(); var unknownSections=new List<string>(); var unknownProperties=new List<string>();
        if (!IsEventCertified(payload.VerificationStatus)) issues.Add("P7KNOWLEDGE_EVENT_NOT_CERTIFIED");
        if (!IsEvergreenCertified(payload.CertificationStatus)) issues.Add("P7KNOWLEDGE_EVERGREEN_NOT_CERTIFIED");
        if (payload.ReviewedSources.Count == 0 || payload.ReviewedSources.Any(x=>!x.Certified)) issues.Add("P7KNOWLEDGE_SOURCE_REGISTRY_NOT_CERTIFIED");
        Read(payload.EvergreenJson, Phase7KnowledgeOrigin.Evergreen, payload.EvergreenPayloadId ?? payload.PayloadId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties);
        Read(payload.RawDataJson, Phase7KnowledgeOrigin.Event, payload.EventId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties);

        var merged = new Dictionary<string,Phase7AdapterClaimCandidate>(StringComparer.Ordinal);
        var decisions=new List<Phase7KnowledgeMergeDecision>();
        foreach (var candidate in candidates.OrderBy(x=>x.SemanticIdentity,StringComparer.Ordinal))
        {
            if (!merged.TryGetValue(candidate.SemanticIdentity,out var prior)) { merged.Add(candidate.SemanticIdentity,candidate); continue; }
            var evergreen=prior.Origin==Phase7KnowledgeOrigin.Evergreen?prior:candidate; var ev=prior.Origin==Phase7KnowledgeOrigin.Event?prior:candidate;
            var classified=classifier.Classify(new(candidate.SemanticIdentity,candidate.Domain,candidate.ApprovedFieldPath,evergreen,ev,new Dictionary<string,string>(),new Dictionary<string,string>()));
            var selected=classified.Classification switch { Phase7KnowledgeMergeClassification.EventMorePrecise=>ev, Phase7KnowledgeMergeClassification.EventSpecificSpecialization=>ev, _=>evergreen };
            if (classified.Classification == Phase7KnowledgeMergeClassification.Equivalent)
                selected = selected with { SourceIds = evergreen.SourceIds.Concat(ev.SourceIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
            if(classified.Classification==Phase7KnowledgeMergeClassification.Contradictory)
            {
                // A contradiction is diagnostic authority only: neither candidate may leak into accepted claims.
                merged.Remove(candidate.SemanticIdentity);
                issues.Add($"P7KNOWLEDGE_CONTRADICTION:{candidate.SemanticIdentity}");
            }
            else merged[candidate.SemanticIdentity]=selected;
            decisions.Add(new(candidate.SemanticIdentity,classified.Classification,evergreen,ev,[],classified.Reason,new Dictionary<string,string>(),classified.Warnings,classified.BlockingIssues));
        }
        var claims = merged.Values.Select(x=>Claim(x,payload,issues)).OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray();
        var claimIdsBySemantic = claims.ToDictionary(x => x.SemanticIdentity, x => x.ClaimId, StringComparer.Ordinal);
        decisions = decisions.Select(d => d.Classification == Phase7KnowledgeMergeClassification.Contradictory
            ? d
            : d with { SelectedClaimIds = claimIdsBySemantic.TryGetValue(d.SemanticIdentity, out var claimId) ? [claimId] : [] }).ToList();
        if (claims.GroupBy(x=>x.ClaimId,StringComparer.Ordinal).Any(g=>g.Count()>1)) issues.Add("P7KNOWLEDGE_DUPLICATE_CLAIM_ID");
        if (claims.GroupBy(x=>x.SemanticIdentity,StringComparer.Ordinal).Any(g=>g.Count()>1)) issues.Add("P7KNOWLEDGE_DUPLICATE_SEMANTIC_IDENTITY");
        var required = profile.MandatoryKnowledgeDomains.Select(ParseDomain).ToHashSet();
        var domains = Enum.GetValues<NarrationKnowledgeDomainKey>().Select(key =>
        {
            var selected=claims.Where(x=>x.Domain==key.ToString()).ToArray(); var mandatory=required.Contains(key);
            return new NarrationKnowledgeDomain(key.ToString(),selected.Length>0?KnowledgeDomainStatus.Available:mandatory?KnowledgeDomainStatus.Missing:KnowledgeDomainStatus.NotApplicable,selected,
                mandatory&&selected.Length==0?[$"P7KNOWLEDGE_MANDATORY_DOMAIN_MISSING:{key}"]:[]);
        }).ToArray();
        issues.AddRange(domains.SelectMany(x=>x.Warnings));
        var result = new ResolvedNarrationKnowledge(payload.PayloadId,payload.PayloadChecksum,payload.SourceRegistryId,
            Phase7Determinism.Hash(payload.ReviewedSources.OrderBy(x=>x.SourceId,StringComparer.Ordinal)),payload.Language,domains,
            Localized(payload.EvergreenJson,payload.Language,"narrationVocabulary"),Localized(payload.EvergreenJson,payload.Language,"doNotBlindlyTranslate").Keys.ToArray(),
            LocalizedScalar(payload.EvergreenJson,payload.Language,"pronunciation"),payload.ReviewedSources.Where(x=>x.Certified).Select(x=>x.SourceId).Order(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),"");
        var all=payload.AllResolvedSources.Count>0?payload.AllResolvedSources:payload.ReviewedSources;
        result=result with { AdapterDiagnostics=adapterDiagnostics,MergeDecisions=decisions,UnknownSections=unknownSections.Distinct().Order().ToArray(),UnknownProperties=unknownProperties.Distinct().Order().ToArray(),
            SourceAuditSummary=new(all.Count,payload.RejectedSources.Count,payload.UnverifiedSources.Count,candidates.Count(x=>x.SourceIds.Count==0)) };
        return result with { DeterministicChecksum=Phase7Determinism.Hash(result with { DeterministicChecksum="" }) };
    }

    private void Read(string? json,Phase7KnowledgeOrigin origin,string id,CertifiedKnowledgePayload payload,List<Phase7AdapterClaimCandidate> output,List<string> warnings,List<string> issues,List<Phase7KnowledgeAdapterDiagnostic> diagnostics,List<string> unknownSections,List<string> unknownProperties)
    {
        if(string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object){issues.Add($"P7KNOWLEDGE_{origin.ToString().ToUpperInvariant()}_JSON_INVALID");return;}
            foreach(var section in doc.RootElement.EnumerateObject())
            {
                var adapter=registry.Find(section.Name);
                if(adapter is null) { warnings.Add($"P7KNOWLEDGE_UNKNOWN_SECTION:{origin}:{section.Name}"); unknownSections.Add(section.Name); continue; }
                var context=new Phase7KnowledgeSectionContext(origin,id,payload.EvergreenPayloadId??payload.PayloadId,payload.PayloadChecksum,payload.Language,section.Name,section.Value,payload.ReviewedSources,payload.EventFamily,payload.EventType);
                var extracted=adapter.Extract(context); output.AddRange(extracted.Claims); warnings.AddRange(extracted.Warnings); issues.AddRange(extracted.BlockingIssues); unknownProperties.AddRange(extracted.UnknownProperties);
                diagnostics.Add(new(adapter.AdapterId,adapter.AdapterVersion,section.Name,origin,extracted.Claims.Count+extracted.UnknownProperties.Count,extracted.Claims.Count,extracted.UnknownProperties.Count,extracted.UnknownProperties,0,0,0,0,extracted.Claims.Count(x=>x.SourceIds.Count==0),new Dictionary<string,int>(),payload.RejectedSources.Count,payload.UnverifiedSources.Count));
            }
        }
        catch(JsonException){issues.Add($"P7KNOWLEDGE_{origin.ToString().ToUpperInvariant()}_JSON_INVALID");}
    }
    private static CertifiedNarrationClaim Claim(Phase7AdapterClaimCandidate c,CertifiedKnowledgePayload p,List<string> issues)
    {
        var certified=p.ReviewedSources.Where(x=>x.Certified&&x.Reviewed&&x.Language.Equals(p.Language,StringComparison.OrdinalIgnoreCase)).ToArray();
        var exactClaim=certified.Where(x=>x.SupportedClaimIds.Contains(c.SemanticIdentity,StringComparer.OrdinalIgnoreCase)).ToArray();
        var exactEntity=certified.Where(x=>x.SupportedKnowledgeIds.Contains(c.KnowledgeId,StringComparer.OrdinalIgnoreCase)).ToArray();
        var canonicalField = Phase7CanonicalFieldPathPolicy.Canonicalize(c.ApprovedFieldPath);
        var exactField=certified.Where(x=>x.SupportedApprovedFieldPaths.Any(path =>
            Phase7CanonicalFieldPathPolicy.TryCanonicalize(path, out var canonical) && canonical == canonicalField)).ToArray();
        var chosen=exactClaim.Length>0?exactClaim:exactEntity.Length>0?exactEntity:exactField;
        var precision=exactClaim.Length>0?Phase7ProvenancePrecision.ExactClaim:exactEntity.Length>0?Phase7ProvenancePrecision.ExactKnowledgeEntity:exactField.Length>0?Phase7ProvenancePrecision.ExactApprovedField:Phase7ProvenancePrecision.None;
        if(chosen.Length==0) issues.Add($"P7KNOWLEDGE_REQUIRED_CLAIM_UNSUPPORTED:{c.SemanticIdentity}");
        var id=Phase7Determinism.SemanticClaimId(c.KnowledgeId,c.SemanticIdentity,p.Language,p.EvergreenPayloadId??p.PayloadId);
        var cultural=c.Domain is NarrationKnowledgeDomainKey.CultureAndMythology or NarrationKnowledgeDomainKey.RegionalTraditions;
        var draft=new CertifiedNarrationClaim(id,c.Domain.ToString(),c.Text,chosen.Select(x=>x.SourceId).Distinct().Order(StringComparer.Ordinal).ToArray(),[c.KnowledgeId,c.SemanticIdentity],chosen.Length==0?.5m:chosen.Min(x=>x.Confidence),
            Has(c.Text,"approximately","roughly","generally","typically"),c.Domain is NarrationKnowledgeDomainKey.Observation or NarrationKnowledgeDomainKey.Visibility, c.Domain==NarrationKnowledgeDomainKey.Timing,
            cultural,cultural,c.Domain==NarrationKnowledgeDomainKey.AstrologyClarification,c.RequiresQualification,c.RequiresHumanReview,p.Language,"") { SemanticIdentity=c.SemanticIdentity,ProvenancePrecision=precision.ToString(),Uncertain=chosen.Length==0 };
        return draft with { Checksum=Phase7Determinism.Hash(draft with { Checksum="" }) };
    }
    private static bool IsEventCertified(string value)=>value.Equals("Certified",StringComparison.OrdinalIgnoreCase)||value.Equals("Verified",StringComparison.OrdinalIgnoreCase);
    private static bool IsEvergreenCertified(string value)=>IsEventCertified(value)||value.Equals("Reviewed",StringComparison.OrdinalIgnoreCase);
    private static NarrationKnowledgeDomainKey ParseDomain(string value)=>NarrationKnowledgeDomains.TryParse(value,out var key)?key:throw new InvalidOperationException($"P7DOMAIN_UNKNOWN:{value}");
    private static bool Has(string value,params string[] terms)=>terms.Any(t=>value.Contains(t,StringComparison.OrdinalIgnoreCase));
    private static IReadOnlyDictionary<string,string> Localized(string? json,string language,string key){var m=new SortedDictionary<string,string>();if(string.IsNullOrWhiteSpace(json))return m;using var d=JsonDocument.Parse(json);if(!d.RootElement.TryGetProperty("localizedContent",out var l)||!l.TryGetProperty(language.Split('-','_')[0],out var c)||!c.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Array)return m;foreach(var x in v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String)){var s=x.GetString()!;m[s]=s;}return m;}
    private static IReadOnlyDictionary<string,string> LocalizedScalar(string? json,string language,string key){var m=new SortedDictionary<string,string>();if(string.IsNullOrWhiteSpace(json))return m;using var d=JsonDocument.Parse(json);if(d.RootElement.TryGetProperty("localizedContent",out var l)&&l.TryGetProperty(language.Split('-','_')[0],out var c)&&c.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String)m["subject"]=v.GetString()!;return m;}
}
