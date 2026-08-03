using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Bounded adapter extraction and conflict-aware certified knowledge merge.</summary>
public sealed class Phase7KnowledgeResolver : IPhase7KnowledgeResolver
{
    private readonly Phase7KnowledgeSectionAdapterRegistry registry;
    private readonly IPhase7KnowledgeMergeClassifier classifier;
    private readonly IPhase7SourceEligibilityPolicy sourceEligibility;
    public Phase7KnowledgeResolver() : this(new Phase7KnowledgeSectionAdapterRegistry(),new Phase7KnowledgeMergeClassifier(),new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry) : this(registry,new Phase7KnowledgeMergeClassifier(),new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry,IPhase7KnowledgeMergeClassifier classifier) : this(registry,classifier,new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry,IPhase7KnowledgeMergeClassifier classifier,IPhase7SourceEligibilityPolicy sourceEligibility) { this.registry=registry;this.classifier=classifier;this.sourceEligibility=sourceEligibility; }

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile)
    {
        var sourcePool = Phase7KnowledgeSourcePool.Get(payload);
        var issues = new List<string>(); var warnings = new List<string>(payload.Warnings.Concat(sourcePool.SelectMany(x => x.RegistryDiagnostics))); var candidates = new List<Phase7AdapterClaimCandidate>();
        var adapterDiagnostics=new List<Phase7KnowledgeAdapterDiagnostic>(); var unknownSections=new List<string>(); var unknownProperties=new List<string>(); var entities=new List<Phase7KnowledgeEntity>();
        if (!IsEventCertified(payload.VerificationStatus)) issues.Add("P7KNOWLEDGE_EVENT_NOT_CERTIFIED");
        if (!IsEvergreenCertified(payload.CertificationStatus)) issues.Add("P7KNOWLEDGE_EVERGREEN_NOT_CERTIFIED");
        if (sourcePool.Count == 0) issues.Add("P7KNOWLEDGE_SOURCE_REGISTRY_EMPTY");
        Read(payload.EvergreenJson, Phase7KnowledgeOrigin.Evergreen, payload.EvergreenPayloadId ?? payload.PayloadId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties,entities);
        Read(payload.RawDataJson, Phase7KnowledgeOrigin.Event, payload.EventId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties,entities);

        var merged = new Dictionary<string,Phase7AdapterClaimCandidate>(StringComparer.Ordinal);
        var decisions=new List<Phase7KnowledgeMergeDecision>();
        foreach (var candidate in candidates.OrderBy(x=>x.SemanticIdentity,StringComparer.Ordinal))
        {
            if (!merged.TryGetValue(candidate.SemanticIdentity,out var prior)) { merged.Add(candidate.SemanticIdentity,candidate); continue; }
            var evergreen=prior.Origin==Phase7KnowledgeOrigin.Evergreen?prior:candidate; var ev=prior.Origin==Phase7KnowledgeOrigin.Event?prior:candidate;
            var evergreenScope=Scope(evergreen); var eventScope=Scope(ev);
            var evergreenComparison=Comparison(evergreen); var eventComparison=Comparison(ev);
            var classified=classifier.Classify(new(candidate.SemanticIdentity,candidate.Domain,candidate.ApprovedFieldPath,evergreen,ev,
                evergreenScope,eventScope,evergreenComparison,eventComparison,new Dictionary<string,string>()));
            var scopeOutcome=new Phase7KnowledgeScopeComparer().Compare(evergreenScope,eventScope);
            var selected=classified.Classification switch { Phase7KnowledgeMergeClassification.EventMorePrecise=>ev, _=>evergreen };
            if (classified.Classification == Phase7KnowledgeMergeClassification.Equivalent)
                selected = selected with { SourceIds = evergreen.SourceIds.Concat(ev.SourceIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
            if(classified.Classification==Phase7KnowledgeMergeClassification.EventSpecificSpecialization ||
               classified.Classification==Phase7KnowledgeMergeClassification.Incomparable && scopeOutcome==Phase7KnowledgeScopeComparison.DistinctNonConflictingScopes)
            {
                merged.Remove(candidate.SemanticIdentity);
                var general=evergreen with { SemanticIdentity=$"{candidate.SemanticIdentity}.general" };
                var scoped=ev with { SemanticIdentity=$"{candidate.SemanticIdentity}.execution" };
                merged[general.SemanticIdentity]=general; merged[scoped.SemanticIdentity]=scoped;
            }
            else if(classified.Classification==Phase7KnowledgeMergeClassification.Incomparable)
            {
                merged.Remove(candidate.SemanticIdentity);
                warnings.Add($"P7KNOWLEDGE_INCOMPARABLE_DEFERRED:{candidate.SemanticIdentity}");
            }
            else if(classified.Classification==Phase7KnowledgeMergeClassification.Contradictory)
            {
                // A contradiction is diagnostic authority only: neither candidate may leak into accepted claims.
                merged.Remove(candidate.SemanticIdentity);
                issues.Add($"P7KNOWLEDGE_CONTRADICTION:{candidate.SemanticIdentity}");
            }
            else merged[candidate.SemanticIdentity]=selected;
            decisions.Add(new(candidate.SemanticIdentity,classified.Classification,evergreen,ev,[],classified.Reason,
                evergreenScope,eventScope,ComparisonEvidence(evergreenComparison,eventComparison),classified.Warnings,classified.BlockingIssues));
        }
        var mandatoryNames=profile.MandatoryKnowledgeDomains.ToHashSet(StringComparer.Ordinal);
        var optionalNames=profile.OptionalKnowledgeDomains.ToHashSet(StringComparer.Ordinal);
        // Disposition is governance input to source eligibility, not a property that can be
        // patched onto a claim after required-grade source selection has already happened.
        var resolvedClaims = merged.Values.Select(candidate =>
        {
            var domain=candidate.Domain.ToString();
            var disposition=candidate.RequiresHumanReview ? Phase7ClaimDisposition.HumanReview
                : mandatoryNames.Contains(domain) ? Phase7ClaimDisposition.Required
                : optionalNames.Contains(domain) ? Phase7ClaimDisposition.Optional : Phase7ClaimDisposition.Deferred;
            return ResolveClaim(candidate, disposition, payload, issues);
        }).OrderBy(x=>x.Claim.ClaimId,StringComparer.Ordinal).ToArray();
        var claims=resolvedClaims.Select(x=>x.Claim).ToArray();
        var claimIdsBySemantic = claims.ToDictionary(x => x.SemanticIdentity, x => x.ClaimId, StringComparer.Ordinal);
        decisions = decisions.Select(d => d.Classification == Phase7KnowledgeMergeClassification.Contradictory
            ? d
            : d with { SelectedClaimIds = d.Classification is Phase7KnowledgeMergeClassification.EventSpecificSpecialization or Phase7KnowledgeMergeClassification.Incomparable
                ? claimIdsBySemantic.Where(x=>x.Key.StartsWith(d.SemanticIdentity+".",StringComparison.Ordinal)).Select(x=>x.Value).Order(StringComparer.Ordinal).ToArray()
                : claimIdsBySemantic.TryGetValue(d.SemanticIdentity, out var claimId) ? [claimId] : [] }).ToList();
        if (claims.GroupBy(x=>x.ClaimId,StringComparer.Ordinal).Any(g=>g.Count()>1)) issues.Add("P7KNOWLEDGE_DUPLICATE_CLAIM_ID");
        if (claims.GroupBy(x=>x.SemanticIdentity,StringComparer.Ordinal).Any(g=>g.Count()>1)) issues.Add("P7KNOWLEDGE_DUPLICATE_SEMANTIC_IDENTITY");
        var required = profile.MandatoryKnowledgeDomains.Select(ParseDomain).ToHashSet();
        var domains = Enum.GetValues<NarrationKnowledgeDomainKey>().Select(key =>
        {
            var selected=claims.Where(x=>x.Domain==key.ToString()).ToArray(); var mandatory=required.Contains(key);
            var authoritative=selected.Where(x=>x.Disposition==Phase7ClaimDisposition.Required&&!x.RequiresHumanReview)
                .Any(x=>x.Checksum==Phase7Determinism.Hash(x with{Checksum=""})&&resolvedClaims.Any(rc=>rc.Claim.ClaimId==x.ClaimId&&rc.Evidence.Any(e=>e.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim)));
            var status=mandatory
                ? authoritative?KnowledgeDomainStatus.Available:selected.Any(x=>x.Disposition==Phase7ClaimDisposition.HumanReview)?KnowledgeDomainStatus.RequiresHumanReview:selected.Any(x=>x.Disposition==Phase7ClaimDisposition.Deferred)?KnowledgeDomainStatus.Deferred:KnowledgeDomainStatus.Missing
                : selected.Any(x=>x.Disposition is Phase7ClaimDisposition.Required or Phase7ClaimDisposition.Optional)?KnowledgeDomainStatus.Available:selected.Any(x=>x.Disposition==Phase7ClaimDisposition.HumanReview)?KnowledgeDomainStatus.RequiresHumanReview:selected.Any(x=>x.Disposition==Phase7ClaimDisposition.Deferred)?KnowledgeDomainStatus.Deferred:KnowledgeDomainStatus.NotApplicable;
            return new NarrationKnowledgeDomain(key.ToString(),status,selected,
                mandatory&&status!=KnowledgeDomainStatus.Available?[$"P7KNOWLEDGE_MANDATORY_DOMAIN_{status.ToString().ToUpperInvariant()}:{key}"]:[]);
        }).ToArray();
        issues.AddRange(domains.SelectMany(x=>x.Warnings));
        var result = new ResolvedNarrationKnowledge(payload.PayloadId,payload.PayloadChecksum,payload.SourceRegistryId,
            Phase7Determinism.Hash(sourcePool),payload.Language,domains,
            Localized(payload.EvergreenJson,payload.Language,"narrationVocabulary"),Localized(payload.EvergreenJson,payload.Language,"doNotBlindlyTranslate").Keys.ToArray(),
            LocalizedScalar(payload.EvergreenJson,payload.Language,"pronunciation"),sourcePool.Select(x=>x.SourceId).Order(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),"");
        var all=sourcePool;
        // Evidence is materialized from the exact selection used to build SourceIds.
        var supportEvidence = resolvedClaims.SelectMany(x=>x.Evidence).OrderBy(x => x.ClaimId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray();
        adapterDiagnostics = adapterDiagnostics.Select(d =>
        {
            var adapterClaims = claims.Where(c => merged.TryGetValue(c.SemanticIdentity, out var candidate)
                && candidate.AdapterId == d.AdapterId && candidate.Origin == d.Origin).ToArray();
            var counts = decisions.Where(x => x.EvergreenClaimCandidate.AdapterId == d.AdapterId || x.EventClaimCandidate.AdapterId == d.AdapterId)
                .GroupBy(x => x.Classification.ToString()).ToDictionary(x => x.Key, x => x.Count());
            return d with {
                ExactClaimProvenanceCount = adapterClaims.Count(x => x.ProvenancePrecision == nameof(Phase7ProvenancePrecision.ExactClaim)),
                ExactEntityProvenanceCount = adapterClaims.Count(x => x.ProvenancePrecision == nameof(Phase7ProvenancePrecision.ExactKnowledgeEntity)),
                ExactFieldProvenanceCount = adapterClaims.Count(x => x.ProvenancePrecision == nameof(Phase7ProvenancePrecision.ExactApprovedField)),
                UnsupportedClaimCount = adapterClaims.Count(x => x.ProvenancePrecision == nameof(Phase7ProvenancePrecision.None)),
                MergeDecisionCounts = counts
            };
        }).ToList();
        result=result with { AdapterDiagnostics=adapterDiagnostics,MergeDecisions=decisions,UnknownSections=unknownSections.Distinct().Order().ToArray(),UnknownProperties=unknownProperties.Distinct().Order().ToArray(),
            SourceAuditSummary=new(all.Count,payload.RejectedSources.Count,payload.UnverifiedSources.Count,candidates.Count(x=>x.SourceIds.Count==0)),
            ClaimSupportEvidence=supportEvidence,KnowledgeEntities=entities.GroupBy(x=>x.KnowledgeId,StringComparer.Ordinal).Select(x=>x.First()).OrderBy(x=>x.KnowledgeId,StringComparer.Ordinal).ToArray() };
        return result with { DeterministicChecksum=Phase7Determinism.Hash(result with { DeterministicChecksum="" }) };
    }

    private void Read(string? json,Phase7KnowledgeOrigin origin,string id,CertifiedKnowledgePayload payload,List<Phase7AdapterClaimCandidate> output,List<string> warnings,List<string> issues,List<Phase7KnowledgeAdapterDiagnostic> diagnostics,List<string> unknownSections,List<string> unknownProperties,List<Phase7KnowledgeEntity> entities)
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
                var context=new Phase7KnowledgeSectionContext(origin,id,payload.EvergreenPayloadId??payload.PayloadId,payload.PayloadChecksum,payload.Language,section.Name,section.Value,Phase7KnowledgeSourcePool.Get(payload),payload.EventFamily,payload.EventType);
                var extracted=adapter.Extract(context); output.AddRange(extracted.Claims); entities.AddRange(extracted.KnowledgeEntities); warnings.AddRange(extracted.Warnings); issues.AddRange(extracted.BlockingIssues); unknownProperties.AddRange(extracted.UnknownProperties);
                diagnostics.Add(new(adapter.AdapterId,adapter.AdapterVersion,section.Name,origin,extracted.Claims.Count+extracted.UnknownProperties.Count,extracted.Claims.Count,extracted.UnknownProperties.Count,extracted.UnknownProperties,0,0,0,0,extracted.Claims.Count(x=>x.SourceIds.Count==0),new Dictionary<string,int>(),payload.RejectedSources.Count,payload.UnverifiedSources.Count));
            }
        }
        catch(JsonException){issues.Add($"P7KNOWLEDGE_{origin.ToString().ToUpperInvariant()}_JSON_INVALID");}
    }
    private sealed record ResolvedClaim(CertifiedNarrationClaim Claim,IReadOnlyList<Phase7ClaimSupportEvidence> Evidence);
    private ResolvedClaim ResolveClaim(Phase7AdapterClaimCandidate c,Phase7ClaimDisposition disposition,CertifiedKnowledgePayload p,List<string> issues)
    {
        var sourcePool=Phase7KnowledgeSourcePool.Get(p);
        var required=disposition==Phase7ClaimDisposition.Required;
        var reviewedAllowed=disposition is Phase7ClaimDisposition.Optional or Phase7ClaimDisposition.HumanReview;
        var evaluated=disposition==Phase7ClaimDisposition.Deferred
            ? Array.Empty<(CertifiedNarrationSource Source,Phase7SourceEligibilityResult Result)>()
            : sourcePool
            .Where(x=>c.SourceIds.Count==0||c.SourceIds.Contains(x.SourceId,StringComparer.Ordinal))
            .Select(x=>(Source:x,Result:sourceEligibility.Classify(new(x,p.Language,c.KnowledgeId,c.SemanticIdentity,c.ApprovedFieldPath,required,reviewedAllowed,disposition==Phase7ClaimDisposition.HumanReview))))
            .Where(x=>required?x.Result.Eligibility==Phase7SourceEligibility.EligibleForRequiredClaim:x.Result.Eligibility is Phase7SourceEligibility.EligibleForRequiredClaim or Phase7SourceEligibility.EligibleForOptionalClaim).ToArray();
        var precision=evaluated.Select(x=>x.Result.Precision).DefaultIfEmpty(Phase7ProvenancePrecision.None).Min();
        var chosen=evaluated.Where(x=>x.Result.Precision==precision).ToArray();
        if(required&&chosen.Length==0) issues.Add($"P7KNOWLEDGE_REQUIRED_CLAIM_UNSUPPORTED:{c.SemanticIdentity}");
        var id=Phase7Determinism.SemanticClaimId(c.KnowledgeId,c.SemanticIdentity,p.Language,p.EvergreenPayloadId??p.PayloadId);
        var cultural=c.Domain is NarrationKnowledgeDomainKey.CultureAndMythology or NarrationKnowledgeDomainKey.RegionalTraditions;
        var draft=new CertifiedNarrationClaim(id,c.Domain.ToString(),c.Text,chosen.Select(x=>x.Source.SourceId).Distinct().Order(StringComparer.Ordinal).ToArray(),[c.KnowledgeId,c.SemanticIdentity],chosen.Length==0?.5m:chosen.Min(x=>x.Source.Confidence),
            Has(c.Text,"approximately","roughly","generally","typically"),c.Domain is NarrationKnowledgeDomainKey.Observation or NarrationKnowledgeDomainKey.Visibility, c.Domain==NarrationKnowledgeDomainKey.Timing,
            cultural,cultural,c.Domain==NarrationKnowledgeDomainKey.AstrologyClarification,c.RequiresQualification,c.RequiresHumanReview,p.Language,"") { SemanticIdentity=c.SemanticIdentity,Disposition=disposition,ProvenancePrecision=precision.ToString(),Uncertain=chosen.Length==0 };
        var claim=draft with { Checksum=Phase7Determinism.Hash(draft with { Checksum="" }) };
        var evidence=chosen.Select(x=>new Phase7ClaimSupportEvidence(id,c.SemanticIdentity,x.Source.SourceId,c.KnowledgeId,
            Phase7CanonicalFieldPathPolicy.Canonicalize(c.ApprovedFieldPath),x.Result.Precision,c.AdapterId,c.Origin,SelectionReason(disposition,x.Result.Eligibility),null,claim.Confidence)
            { AdapterVersion=c.AdapterVersion,SourceEligibility=x.Result.Eligibility,RequiresHumanReview=disposition==Phase7ClaimDisposition.HumanReview,
              QualificationReason=c.RequiresQualification?QualificationReasons(c,claim):"",AuthorityScope=c.SemanticIdentity.EndsWith(".general",StringComparison.Ordinal)?"GeneralAuthority":c.SemanticIdentity.EndsWith(".execution",StringComparison.Ordinal)?"ExecutionScopedAuthority":c.Origin.ToString() }).ToArray();
        return new(claim,evidence);
    }
    private static string SelectionReason(Phase7ClaimDisposition disposition,Phase7SourceEligibility eligibility)=>
        disposition switch
        {
            Phase7ClaimDisposition.Required=>"CertifiedRequiredEvidence",
            Phase7ClaimDisposition.HumanReview when eligibility==Phase7SourceEligibility.EligibleForOptionalClaim=>"ReviewedOptionalHumanReviewEvidence",
            Phase7ClaimDisposition.Deferred=>"DeferredAuditEvidence",
            _=>"CertifiedOptionalEvidence"
        };
    private static string QualificationReasons(Phase7AdapterClaimCandidate candidate,CertifiedNarrationClaim claim)
    {
        var reasons=new SortedSet<string>(StringComparer.Ordinal);
        if(candidate.Approximate==true||claim.Approximate)reasons.Add("ApproximationQualification");
        if(claim.IsLocationDependent)reasons.Add("LocationQualification");
        if(claim.IsDateTimeDependent)reasons.Add("DateTimeQualification");
        if(claim.IsCultural||claim.IsMythological)reasons.Add("CulturalTraditionQualification");
        if(claim.IsAstrologyRelated)reasons.Add("AstrologyClarificationQualification");
        if(candidate.Uncertainty.HasValue||claim.Uncertain)reasons.Add("UncertaintyQualification");
        if(claim.RequiresHumanReview)reasons.Add("HumanReviewQualification");
        if(reasons.Count==0)reasons.Add("UncertaintyQualification");
        return string.Join('|',reasons);
    }
    private static bool IsEventCertified(string value)=>value.Equals("Certified",StringComparison.OrdinalIgnoreCase)||value.Equals("Verified",StringComparison.OrdinalIgnoreCase);
    private static bool IsEvergreenCertified(string value)=>IsEventCertified(value)||value.Equals("Reviewed",StringComparison.OrdinalIgnoreCase);
    private static NarrationKnowledgeDomainKey ParseDomain(string value)=>NarrationKnowledgeDomains.TryParse(value,out var key)?key:throw new InvalidOperationException($"P7DOMAIN_UNKNOWN:{value}");
    private static bool Has(string value,params string[] terms)=>terms.Any(t=>value.Contains(t,StringComparison.OrdinalIgnoreCase));
    private static Phase7KnowledgeAuthorityScope Scope(Phase7AdapterClaimCandidate c) => new(c.ScopeType,c.Location,c.Latitude,c.Longitude,
        c.StartUtc,c.EndUtc,c.ReferenceDate,c.EventInstanceId,c.ObservationWindowId);
    private static Phase7KnowledgeComparisonMetadata Comparison(Phase7AdapterClaimCandidate c) => new(c.NormalizedValue,c.ValueType,c.Unit,
        c.Approximate,c.Uncertainty,c.Confidence);
    private static IReadOnlyDictionary<string,string> ComparisonEvidence(Phase7KnowledgeComparisonMetadata evergreen,Phase7KnowledgeComparisonMetadata ev)
    {
        var result=new SortedDictionary<string,string>(StringComparer.Ordinal);
        static void Add(SortedDictionary<string,string> target,string prefix,Phase7KnowledgeComparisonMetadata value)
        {
            if(value.NormalizedValue is not null)target[$"{prefix}.normalizedValue"]=value.NormalizedValue;
            if(value.ValueType is not null)target[$"{prefix}.valueType"]=value.ValueType;
            if(value.Unit is not null)target[$"{prefix}.unit"]=value.Unit;
            if(value.Approximation.HasValue)target[$"{prefix}.approximation"]=value.Approximation.Value.ToString();
            if(value.Uncertainty.HasValue)target[$"{prefix}.uncertainty"]=value.Uncertainty.Value.ToString();
            if(value.Confidence.HasValue)target[$"{prefix}.confidence"]=value.Confidence.Value.ToString();
        }
        Add(result,"evergreen",evergreen);Add(result,"event",ev);return result;
    }
    private static IReadOnlyDictionary<string,string> Localized(string? json,string language,string key){var m=new SortedDictionary<string,string>();if(string.IsNullOrWhiteSpace(json))return m;using var d=JsonDocument.Parse(json);if(!d.RootElement.TryGetProperty("localizedContent",out var l)||!l.TryGetProperty(language.Split('-','_')[0],out var c)||!c.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Array)return m;foreach(var x in v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String)){var s=x.GetString()!;m[s]=s;}return m;}
    private static IReadOnlyDictionary<string,string> LocalizedScalar(string? json,string language,string key){var m=new SortedDictionary<string,string>();if(string.IsNullOrWhiteSpace(json))return m;using var d=JsonDocument.Parse(json);if(d.RootElement.TryGetProperty("localizedContent",out var l)&&l.TryGetProperty(language.Split('-','_')[0],out var c)&&c.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String)m["subject"]=v.GetString()!;return m;}
}
