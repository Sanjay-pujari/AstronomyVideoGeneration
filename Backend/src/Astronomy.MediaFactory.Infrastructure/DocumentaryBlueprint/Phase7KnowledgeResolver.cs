using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

/// <summary>Bounded adapter extraction and conflict-aware certified knowledge merge.</summary>
public sealed class Phase7KnowledgeResolver : IPhase7KnowledgeResolver
{
    private readonly Phase7KnowledgeSectionAdapterRegistry registry;
    private readonly IPhase7KnowledgeMergeClassifier classifier;
    private readonly IPhase7SourceEligibilityPolicy sourceEligibility;
    private readonly ILogger<Phase7KnowledgeResolver> logger;
    public Phase7KnowledgeResolver() : this(new Phase7KnowledgeSectionAdapterRegistry(),new Phase7KnowledgeMergeClassifier(),new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry) : this(registry,new Phase7KnowledgeMergeClassifier(),new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry,IPhase7KnowledgeMergeClassifier classifier) : this(registry,classifier,new Phase7SourceEligibilityPolicy()) { }
    public Phase7KnowledgeResolver(Phase7KnowledgeSectionAdapterRegistry registry,IPhase7KnowledgeMergeClassifier classifier,IPhase7SourceEligibilityPolicy sourceEligibility,
        ILogger<Phase7KnowledgeResolver>? logger=null) { this.registry=registry;this.classifier=classifier;this.sourceEligibility=sourceEligibility;this.logger=logger??NullLogger<Phase7KnowledgeResolver>.Instance; }

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile)
        => Resolve(payload, profile, null);

    public ResolvedNarrationKnowledge Resolve(CertifiedKnowledgePayload payload, FamilyNarrationProfile profile, string? diagnosticPath)
    {
        var sourcePool = Phase7KnowledgeSourcePool.Get(payload);
        var issues = new List<string>(); var warnings = new List<string>(payload.Warnings.Concat(sourcePool.SelectMany(x => x.RegistryDiagnostics))); var candidates = new List<Phase7AdapterClaimCandidate>();
        var adapterDiagnostics=new List<Phase7KnowledgeAdapterDiagnostic>(); var unknownSections=new List<string>(); var unknownProperties=new List<string>(); var entities=new List<Phase7KnowledgeEntity>();
        if (!IsEventCertified(payload.VerificationStatus)) issues.Add("P7KNOWLEDGE_EVENT_NOT_CERTIFIED");
        if (!IsEvergreenCertified(payload.CertificationStatus)) issues.Add("P7KNOWLEDGE_EVERGREEN_NOT_CERTIFIED");
        if (sourcePool.Count == 0) issues.Add("P7KNOWLEDGE_SOURCE_REGISTRY_EMPTY");
        Read(payload.EvergreenJson, Phase7KnowledgeOrigin.Evergreen, payload.EvergreenPayloadId ?? payload.PayloadId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties,entities);
        Read(payload.RawDataJson, Phase7KnowledgeOrigin.Event, payload.EventId, payload, candidates, warnings, issues,adapterDiagnostics,unknownSections,unknownProperties,entities);
        var diagnosticFailureType=WriteCultureDiagnostic(diagnosticPath,payload,profile,candidates,[]);

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
                : candidate.Domain==NarrationKnowledgeDomainKey.CultureAndMythology
                    ? CulturalDisposition(candidate,payload,mandatoryNames.Contains(domain),optionalNames.Contains(domain))
                : mandatoryNames.Contains(domain) ? Phase7ClaimDisposition.Required
                : optionalNames.Contains(domain) ? Phase7ClaimDisposition.Optional : Phase7ClaimDisposition.Deferred;
            return ResolveClaim(candidate, disposition, payload, issues, warnings);
        }).OrderBy(x=>x.Claim.ClaimId,StringComparer.Ordinal).ToArray();
        var claims=resolvedClaims.Select(x=>x.Claim).ToArray();
        var finalDiagnosticFailureType=WriteCultureDiagnostic(diagnosticPath,payload,profile,candidates,resolvedClaims);
        diagnosticFailureType??=finalDiagnosticFailureType;
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
            var domainIssues=mandatory&&status!=KnowledgeDomainStatus.Available
                ? new List<string> {$"P7KNOWLEDGE_MANDATORY_DOMAIN_{status.ToString().ToUpperInvariant()}:{key}"} : [];
            if(mandatory&&status==KnowledgeDomainStatus.RequiresHumanReview)
                domainIssues.AddRange(resolvedClaims.Where(x=>x.Claim.Domain==key.ToString()&&x.Claim.Disposition==Phase7ClaimDisposition.HumanReview)
                    .Select(x=>$"P7KNOWLEDGE_MANDATORY_DOMAIN_CLAIM_REQUIRESHUMANREVIEW:{key}:{x.Claim.ClaimId}:{x.Diagnostic.HumanReviewReason}"));
            return new NarrationKnowledgeDomain(key.ToString(),status,selected,domainIssues);
        }).ToArray();
        issues.AddRange(domains.SelectMany(x=>x.Warnings));
        var result = new ResolvedNarrationKnowledge(payload.PayloadId,payload.PayloadChecksum,payload.SourceRegistryId,
            Phase7Determinism.Hash(sourcePool),payload.Language,domains,
            Localized(payload.EvergreenJson,payload.Language,"narrationVocabulary"),Localized(payload.EvergreenJson,payload.Language,"doNotBlindlyTranslate").Keys.ToArray(),
            LocalizedScalar(payload.EvergreenJson,payload.Language,"pronunciation"),sourcePool.Select(x=>x.SourceId).Order(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),"");
        var all=sourcePool;
        // Evidence is materialized from the exact selection used to build SourceIds.
        var supportEvidence = resolvedClaims.SelectMany(x=>x.Evidence).Select(e=>
        {
            var decision=decisions.SingleOrDefault(d=>d.SelectedClaimIds.Contains(e.ClaimId));
            if(decision is null)return e with{SourceSelectionReason=e.SelectionReason,MergeSelectionReason="NoMerge"};
            var decisionId="p7merge-"+Phase7Determinism.Hash(new{decision.SemanticIdentity,classification=decision.Classification.ToString(),decision.SelectedClaimIds})[..24];
            var reason=decision.Classification switch
            {
                Phase7KnowledgeMergeClassification.Equivalent=>"EquivalentCombinedEvidence",
                Phase7KnowledgeMergeClassification.EventSpecificSpecialization when e.SemanticIdentity.EndsWith(".general",StringComparison.Ordinal)=>"SpecializationGeneralEvidence",
                Phase7KnowledgeMergeClassification.EventSpecificSpecialization=>"SpecializationExecutionEvidence",
                Phase7KnowledgeMergeClassification.EventMorePrecise=>"EventMorePreciseSelected",
                Phase7KnowledgeMergeClassification.EvergreenMorePrecise=>"EvergreenMorePreciseSelected",
                Phase7KnowledgeMergeClassification.Incomparable when e.SemanticIdentity.EndsWith(".general",StringComparison.Ordinal)=>"ScopedIncomparableGeneral",
                Phase7KnowledgeMergeClassification.Incomparable=>"ScopedIncomparableExecution",
                _=>"NoMerge"
            };
            return e with{SourceSelectionReason=e.SelectionReason,MergeSelectionReason=reason,MergeDecisionId=decisionId};
        }).OrderBy(x => x.ClaimId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray();
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
            ClaimResolutionDiagnostics=resolvedClaims.Select(x=>
            {
                var decision=decisions.SingleOrDefault(d=>d.SelectedClaimIds.Contains(x.Claim.ClaimId));
                var safeCulturalCandidate=claims.Any(c=>c.Domain==nameof(NarrationKnowledgeDomainKey.CultureAndMythology)
                    && c.Disposition==Phase7ClaimDisposition.Required&&!c.RequiresHumanReview);
                return x.Diagnostic with
                {
                    MergeDecision=decision?.Classification.ToString()??"NoMerge",
                    MergeDecisionId=decision is null?"":"p7merge-"+Phase7Determinism.Hash(new{decision.SemanticIdentity,classification=decision.Classification.ToString(),decision.SelectedClaimIds})[..24],
                    SelectedClaimIds=decision?.SelectedClaimIds??[x.Claim.ClaimId],
                    EquivalentSafeCulturalCandidateExists=x.Claim.Domain==nameof(NarrationKnowledgeDomainKey.CultureAndMythology)&&safeCulturalCandidate
                };
            }).OrderBy(x=>x.SemanticIdentity,StringComparer.Ordinal).ToArray(),
            ClaimSupportEvidence=supportEvidence,KnowledgeEntities=entities.GroupBy(x=>x.KnowledgeId,StringComparer.Ordinal).Select(x=>x.First()).OrderBy(x=>x.KnowledgeId,StringComparer.Ordinal).ToArray() };
        result=result with { DeterministicChecksum=Phase7Determinism.Hash(result with { DeterministicChecksum="" }) };
        // Diagnostic warnings are deliberately appended after the authority checksum is
        // computed: observability must not alter the resolved knowledge identity.
        return diagnosticFailureType is null ? result : result with
        {
            Warnings=result.Warnings.Append($"P7CULTURE_DEBUG_WRITE_FAILED:{diagnosticFailureType}")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
    }

    private string? WriteCultureDiagnostic(string? path,CertifiedKnowledgePayload payload,FamilyNarrationProfile profile,
        IReadOnlyList<Phase7AdapterClaimCandidate> extracted,IReadOnlyList<ResolvedClaim> resolved)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        const string relativePath="07-narration/debug/culture-required-evidence-debug.json";
        var normalizedPath=path.Replace('\\','/');
        var ownershipConfirmed=normalizedPath.EndsWith(relativePath,StringComparison.Ordinal);
        var parent=Path.GetDirectoryName(path)!;
        logger.LogInformation("P7CULTURE_DEBUG_PATH ExecutionRootOwnershipConfirmed={ExecutionRootOwnershipConfirmed} RelativeDiagnosticPath={RelativeDiagnosticPath} ParentDirectoryExistsBeforeCreation={ParentDirectoryExistsBeforeCreation}",
            ownershipConfirmed,relativePath,Directory.Exists(parent));
        logger.LogInformation("P7CULTURE_DEBUG_WRITE_ATTEMPTED RelativeDiagnosticPath={RelativeDiagnosticPath}",relativePath);
        try
        {
            var sourcePool=Phase7KnowledgeSourcePool.Get(payload);
            var culture=extracted.Where(x=>x.Domain==NarrationKnowledgeDomainKey.CultureAndMythology)
                .OrderBy(x=>x.ApprovedFieldPath,StringComparer.Ordinal).ThenBy(x=>x.Origin).ThenBy(x=>x.SemanticIdentity,StringComparer.Ordinal).ToArray();
            var mandatory=profile.MandatoryKnowledgeDomains.Contains(nameof(NarrationKnowledgeDomainKey.CultureAndMythology),StringComparer.Ordinal);
            var optional=profile.OptionalKnowledgeDomains.Contains(nameof(NarrationKnowledgeDomainKey.CultureAndMythology),StringComparer.Ordinal);
            var candidateRows=culture.Select(c=>
            {
                var disposition=c.RequiresHumanReview?Phase7ClaimDisposition.HumanReview:CulturalDisposition(c,payload,mandatory,optional);
                return new { candidateId=CandidateId(c),origin=c.Origin.ToString(),knowledgeId=c.KnowledgeId,semanticIdentity=c.SemanticIdentity,
                    approvedFieldPath=c.ApprovedFieldPath,value=c.Text,traditionIdentity=Phase7CulturalClaimPolicy.ResolveCulturalTradition(c.ApprovedFieldPath),
                    sourceIds=c.SourceIds.Order(StringComparer.Ordinal).ToArray(),requiresQualification=c.RequiresQualification,
                    qualificationReasons=c.QualificationReasons.Order(StringComparer.Ordinal).ToArray(),requiresHumanReview=c.RequiresHumanReview,
                    humanReviewReason=c.HumanReviewReason,isNamedTraditionSummary=IsNamedSummary(c),mandatoryDomain=mandatory,
                    culturalDispositionResult=disposition.ToString()+"; "+SourceInheritance(payload,c) };
            }).ToArray();
            var sourceRows=culture.SelectMany(c=>c.SourceIds.DefaultIfEmpty("").Select(id=>(c,id)))
                .OrderBy(x=>x.id,StringComparer.Ordinal).ThenBy(x=>x.c.ApprovedFieldPath,StringComparer.Ordinal).Select(x=>
            {
                var source=sourcePool.SingleOrDefault(s=>s.SourceId==x.id);
                var result=source is null?null:sourceEligibility.Classify(new(source,payload.Language,x.c.KnowledgeId,x.c.SemanticIdentity,x.c.ApprovedFieldPath,true,false,false));
                return new {candidateId=CandidateId(x.c),sourceId=x.id,sourceExistsInPool=source is not null,language=source?.Language??"",
                    reviewState=source?.ReviewState??"",authorityState=source?.AuthorityState??"",reviewed=source?.Reviewed??false,
                    certified=source?.Certified??false,confidence=source?.Confidence??0,supportedClaimIds=source?.SupportedClaimIds??[],
                    supportedKnowledgeIds=source?.SupportedKnowledgeIds??[],supportedApprovedFieldPaths=source?.SupportedApprovedFieldPaths??[],
                    provenancePrecision=result?.Precision.ToString()??"None",eligibility=result?.Eligibility.ToString()??"Rejected",
                    reasonCode=result?.ReasonCode??(string.IsNullOrEmpty(x.id)?"CandidateSourceIdMissing":"SourceNotInCertifiedRegistry")};
            }).ToArray();
            var finalRows=resolved.Where(x=>x.Claim.Domain==nameof(NarrationKnowledgeDomainKey.CultureAndMythology))
                .OrderBy(x=>x.Diagnostic.ApprovedFieldPath,StringComparer.Ordinal).ThenBy(x=>x.Claim.SemanticIdentity,StringComparer.Ordinal).Select(x=>new {
                    claimId=x.Claim.ClaimId,semanticIdentity=x.Claim.SemanticIdentity,approvedFieldPath=x.Diagnostic.ApprovedFieldPath,
                    traditionIdentity=x.Diagnostic.TraditionIdentity,disposition=x.Claim.Disposition.ToString(),requiresHumanReview=x.Claim.RequiresHumanReview,
                    humanReviewReason=x.Diagnostic.HumanReviewReason,sourceIds=x.Claim.SourceIds,provenancePrecision=x.Claim.ProvenancePrecision,
                    acceptedAsRequiredAuthority=x.Claim.Disposition==Phase7ClaimDisposition.Required&&x.Evidence.Any(e=>e.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim),
                    resolutionReason=x.Diagnostic.ResolutionReason }).ToArray();
            var requiredCandidates=culture.Where(c=>!c.RequiresHumanReview&&CulturalDisposition(c,payload,mandatory,optional)==Phase7ClaimDisposition.Required).ToArray();
            var noExact=culture.Where(c=>c.SourceIds.Count>0&&!c.SourceIds.Any(id=>sourceRows.Any(s=>s.candidateId==CandidateId(c)&&s.sourceId==id&&s.eligibility==nameof(Phase7SourceEligibility.EligibleForRequiredClaim)))).Select(CandidateId).Distinct().Order().ToArray();
            var knownPaths=new[]{"cultureAndMythology.greek.summary","cultureAndMythology.roman.summary","cultureAndMythology.arabic.summary","cultureAndMythology.chinese.summary","cultureAndMythology.indianHindu.summary"};
            var doc=new {contractVersion="p7-culture-debug.v1",runtimeTypes=new {resolver=GetType().FullName,adapterRegistry=registry.GetType().FullName,
                cultureAdapter=registry.Find("cultureAndMythology")?.GetType().FullName,sourceEligibilityPolicy=sourceEligibility.GetType().FullName,familyProfileResolver=typeof(FamilyNarrationProfileResolver).FullName},
                profile=new {profileId=profile.ProfileId,profileVersion=profile.ContractVersion,eventFamily=payload.EventFamily,mandatoryKnowledgeDomains=profile.MandatoryKnowledgeDomains,optionalKnowledgeDomains=profile.OptionalKnowledgeDomains},
                cultureCandidates=candidateRows,sourceEvaluations=sourceRows,finalCultureClaims=finalRows,
                summary=new {candidateCount=culture.Length,namedTraditionSummaryCount=culture.Count(IsNamedSummary),humanReviewCount=culture.Count(x=>x.RequiresHumanReview),
                    requiredCandidateCount=requiredCandidates.Length,requiredEligibleClaimCount=finalRows.Count(x=>x.acceptedAsRequiredAuthority),
                    missingSourceIdCandidates=culture.Where(x=>x.SourceIds.Count==0).Select(CandidateId).Order().ToArray(),noExactEvidenceCandidates=noExact,
                    nonAuthoritativeSourceCandidates=culture.Where(c=>sourceRows.Any(s=>s.candidateId==CandidateId(c)&&s.sourceExistsInPool&&!s.certified)).Select(CandidateId).Distinct().Order().ToArray(),
                    lowConfidenceCandidates=culture.Where(c=>sourceRows.Any(s=>s.candidateId==CandidateId(c)&&s.sourceExistsInPool&&s.confidence<0.8m)).Select(CandidateId).Distinct().Order().ToArray(),
                    missingNamedTraditionSummaries=knownPaths.Where(p=>!culture.Any(c=>c.ApprovedFieldPath.Equals(p,StringComparison.OrdinalIgnoreCase))).Select(p=>p+": "+PathPresence(payload,p)).ToArray()}};
            Directory.CreateDirectory(parent);
            logger.LogInformation("P7CULTURE_DEBUG_DIRECTORY_READY RelativeDiagnosticPath={RelativeDiagnosticPath} ParentDirectoryExistsAfterCreation={ParentDirectoryExistsAfterCreation}",relativePath,Directory.Exists(parent));
            File.WriteAllText(path,JsonSerializer.Serialize(doc,new JsonSerializerOptions{WriteIndented=true}));
            var length=new FileInfo(path).Length;
            logger.LogInformation("P7CULTURE_DEBUG_WRITTEN RelativeDiagnosticPath={RelativeDiagnosticPath} FinalFileLength={FinalFileLength}",relativePath,length);
            return null;
        }
        catch(Exception ex)
        {
            logger.LogWarning(ex,"P7CULTURE_DEBUG_WRITE_FAILED DiagnosticPath={DiagnosticPath}",relativePath);
            // Unix reports a file blocking a directory component as IOException,
            // whereas Windows reports DirectoryNotFoundException. Keep the safe
            // warning deterministic across platforms without exposing the path.
            return ex is IOException && !Directory.Exists(parent)
                ? nameof(DirectoryNotFoundException)
                : ex.GetType().Name;
        }
    }
    private static string CandidateId(Phase7AdapterClaimCandidate c)=>"p7candidate-"+Phase7Determinism.Hash(new{c.KnowledgeId,c.SemanticIdentity,c.Origin,c.ApprovedFieldPath})[..24];
    private static bool IsNamedSummary(Phase7AdapterClaimCandidate c)=>c.ApprovedFieldPath.EndsWith(".summary",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrEmpty(Phase7CulturalClaimPolicy.ResolveCulturalTradition(c.ApprovedFieldPath));
    private static string PathPresence(CertifiedKnowledgePayload p,string path)=>JsonPathExists(p.RawDataJson,path) ? JsonPathExists(p.EvergreenJson,path)?"both":"event RawDataJson" : JsonPathExists(p.EvergreenJson,path)?"evergreen JSON":"neither";
    private static bool JsonPathExists(string? json,string path){if(string.IsNullOrWhiteSpace(json))return false;using var d=JsonDocument.Parse(json);var e=d.RootElement;foreach(var part in path.Split('.'))if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty(part,out e))return false;return true;}
    private static string SourceInheritance(CertifiedKnowledgePayload p,Phase7AdapterClaimCandidate c)
    {
        var json=c.Origin==Phase7KnowledgeOrigin.Event?p.RawDataJson:p.EvergreenJson;var parts=c.ApprovedFieldPath.Split('.');
        if(string.IsNullOrWhiteSpace(json)||parts.Length<3)return "sourceIds: unavailable; path presence: "+PathPresence(p,c.ApprovedFieldPath);
        using var d=JsonDocument.Parse(json);var root=d.RootElement;var culture=root.TryGetProperty(parts[0],out var a)?a:default;var tradition=culture.ValueKind==JsonValueKind.Object&&culture.TryGetProperty(parts[1],out var b)?b:default;
        static bool Sources(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("sourceIds",out var s)&&s.ValueKind==JsonValueKind.Array&&s.GetArrayLength()>0;
        return $"sourceIds inherited from summary object: false; named-tradition parent: {Sources(tradition).ToString().ToLowerInvariant()}; cultureAndMythology parent: {(!Sources(tradition)&&Sources(culture)).ToString().ToLowerInvariant()}; path presence: {PathPresence(p,c.ApprovedFieldPath)}";
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
    private sealed record ResolvedClaim(CertifiedNarrationClaim Claim,IReadOnlyList<Phase7ClaimSupportEvidence> Evidence,
        Phase7ClaimResolutionDiagnostic Diagnostic);

    /// <summary>
    /// A mandatory cultural domain is an at-least-one authority requirement.  It is
    /// deliberately not inherited by every tradition branch in that domain.
    /// </summary>
    private Phase7ClaimDisposition CulturalDisposition(Phase7AdapterClaimCandidate candidate,CertifiedKnowledgePayload payload,bool mandatory,bool optional)
    {
        if(!mandatory)return optional?Phase7ClaimDisposition.Optional:Phase7ClaimDisposition.Deferred;
        // Only a named tradition's bounded summary is an authority-bearing cultural
        // claim. Sensitive correspondences and uncategorised material are classified
        // by the adapter as review claims and never arrive here.
        if(!candidate.RequiresQualification || string.IsNullOrEmpty(Phase7CulturalClaimPolicy.ResolveCulturalTradition(candidate.ApprovedFieldPath))
            || !candidate.ApprovedFieldPath.EndsWith(".summary",StringComparison.OrdinalIgnoreCase))
            return Phase7ClaimDisposition.Optional;
        var requiredEvidence=Phase7KnowledgeSourcePool.Get(payload)
            // Required cultural authority must be explicit claim provenance. An empty
            // candidate source list must never borrow an otherwise eligible registry entry.
            .Where(x=>candidate.SourceIds.Contains(x.SourceId,StringComparer.Ordinal))
            .Any(source=>sourceEligibility.Classify(new(source,payload.Language,candidate.KnowledgeId,candidate.SemanticIdentity,
                candidate.ApprovedFieldPath,true,false,false)).Eligibility==Phase7SourceEligibility.EligibleForRequiredClaim);
        return requiredEvidence?Phase7ClaimDisposition.Required:Phase7ClaimDisposition.Optional;
    }
    private ResolvedClaim ResolveClaim(Phase7AdapterClaimCandidate c,Phase7ClaimDisposition disposition,CertifiedKnowledgePayload p,List<string> issues,List<string> warnings)
    {
        var sourcePool=Phase7KnowledgeSourcePool.Get(p);
        var required=disposition==Phase7ClaimDisposition.Required;
        var reviewedAllowed=disposition is Phase7ClaimDisposition.Optional or Phase7ClaimDisposition.HumanReview;
        var allEvaluated=disposition==Phase7ClaimDisposition.Deferred
            ? Array.Empty<(CertifiedNarrationSource Source,Phase7SourceEligibilityResult Result)>()
            : sourcePool
            .Where(x=>c.SourceIds.Count==0||c.SourceIds.Contains(x.SourceId,StringComparer.Ordinal))
            .Select(x=>(Source:x,Result:sourceEligibility.Classify(new(x,p.Language,c.KnowledgeId,c.SemanticIdentity,c.ApprovedFieldPath,required,reviewedAllowed,disposition==Phase7ClaimDisposition.HumanReview)))).ToArray();
        var evaluated=allEvaluated
            .Where(x=>required?x.Result.Eligibility==Phase7SourceEligibility.EligibleForRequiredClaim:x.Result.Eligibility is Phase7SourceEligibility.EligibleForRequiredClaim or Phase7SourceEligibility.EligibleForOptionalClaim).ToArray();
        var precision=evaluated.Select(x=>x.Result.Precision).DefaultIfEmpty(Phase7ProvenancePrecision.None).Min();
        var chosen=evaluated.Where(x=>x.Result.Precision==precision).ToArray();
        if(required&&chosen.Length==0) issues.Add($"P7KNOWLEDGE_REQUIRED_CLAIM_UNSUPPORTED:{c.SemanticIdentity}");
        if(disposition==Phase7ClaimDisposition.Optional&&chosen.Length==0) warnings.Add($"P7KNOWLEDGE_OPTIONAL_CLAIM_UNSUPPORTED:{c.SemanticIdentity}");
        var id=Phase7Determinism.SemanticClaimId(c.KnowledgeId,c.SemanticIdentity,p.Language,p.EvergreenPayloadId??p.PayloadId);
        var cultural=c.Domain is NarrationKnowledgeDomainKey.CultureAndMythology or NarrationKnowledgeDomainKey.RegionalTraditions;
        var draft=new CertifiedNarrationClaim(id,c.Domain.ToString(),c.Text,chosen.Select(x=>x.Source.SourceId).Distinct().Order(StringComparer.Ordinal).ToArray(),[c.KnowledgeId,c.SemanticIdentity],chosen.Length==0?.5m:chosen.Min(x=>x.Source.Confidence),
            Has(c.Text,"approximately","roughly","generally","typically"),c.Domain is NarrationKnowledgeDomainKey.Observation or NarrationKnowledgeDomainKey.Visibility, c.Domain==NarrationKnowledgeDomainKey.Timing,
            cultural,cultural,c.Domain==NarrationKnowledgeDomainKey.AstrologyClarification,c.RequiresQualification,c.RequiresHumanReview,p.Language,"") { SemanticIdentity=c.SemanticIdentity,Disposition=disposition,ProvenancePrecision=precision.ToString(),Uncertain=chosen.Length==0 };
        var claim=draft with { Checksum=Phase7Determinism.Hash(draft with { Checksum="" }) };
        var evidence=chosen.Select(x=>new Phase7ClaimSupportEvidence(id,c.SemanticIdentity,x.Source.SourceId,c.KnowledgeId,
            Phase7CanonicalFieldPathDiagnostics.Canonicalize(c.ApprovedFieldPath,
                nameof(Phase7KnowledgeResolver), c.Origin.ToString(), c.AdapterId),x.Result.Precision,c.AdapterId,c.Origin,SelectionReason(disposition,x.Result.Eligibility),null,claim.Confidence)
            { AdapterVersion=c.AdapterVersion,SourceEligibility=x.Result.Eligibility,RequiresHumanReview=disposition==Phase7ClaimDisposition.HumanReview,
              QualificationReason=c.RequiresQualification?QualificationReasons(c,claim,warnings):"",AuthorityScope=c.SemanticIdentity.EndsWith(".general",StringComparison.Ordinal)?"GeneralAuthority":c.SemanticIdentity.EndsWith(".execution",StringComparison.Ordinal)?"ExecutionScopedAuthority":c.Origin.ToString(),
              SourceSelectionReason=SelectionReason(disposition,x.Result.Eligibility) }).ToArray();
        var resolutionReason=c.RequiresHumanReview?c.HumanReviewReason:chosen.Length==0?"NoEligibleExactEvidence":"AcceptedExactEligibleEvidence";
        var diagnostic=new Phase7ClaimResolutionDiagnostic(c.Domain.ToString(),required,c.KnowledgeId,c.SemanticIdentity,
            Phase7CanonicalFieldPathDiagnostics.Canonicalize(c.ApprovedFieldPath,
                nameof(Phase7KnowledgeResolver), c.Origin.ToString(), c.AdapterId),c.Text,disposition,c.RequiresHumanReview,c.HumanReviewReason,
            c.RequiresQualification,c.QualificationReasons,claim.SourceIds,
            allEvaluated.OrderBy(x=>x.Source.SourceId,StringComparer.Ordinal).ToDictionary(x=>x.Source.SourceId,x=>$"{x.Result.Eligibility}:{x.Result.ReasonCode}",StringComparer.Ordinal),precision,resolutionReason);
        diagnostic=diagnostic with {
            CandidateId="p7candidate-"+Phase7Determinism.Hash(new{c.KnowledgeId,c.SemanticIdentity,c.Origin,c.ApprovedFieldPath})[..24],
            ClaimId=claim.ClaimId, KnowledgeEntityId=c.KnowledgeId,
            TraditionIdentity=Phase7CulturalClaimPolicy.ResolveCulturalTradition(c.ApprovedFieldPath),Origin=c.Origin,
            InitialRequiresHumanReview=c.RequiresHumanReview,AdapterHumanReviewReason=c.HumanReviewReason,
            PolicyHumanReviewReason=c.RequiresHumanReview?c.HumanReviewReason:"",
            IntendedDisposition=disposition,FinalDisposition=claim.Disposition,
            AcceptanceOrRejectionReason=resolutionReason,SelectedClaimIds=[claim.ClaimId]
        };
        return new(claim,evidence,diagnostic);
    }
    private static string SelectionReason(Phase7ClaimDisposition disposition,Phase7SourceEligibility eligibility)=>
        disposition switch
        {
            Phase7ClaimDisposition.Required=>"CertifiedRequiredEvidence",
            Phase7ClaimDisposition.HumanReview when eligibility==Phase7SourceEligibility.EligibleForOptionalClaim=>"ReviewedOptionalHumanReviewEvidence",
            Phase7ClaimDisposition.Deferred=>"DeferredAuditEvidence",
            _=>"CertifiedOptionalEvidence"
        };
    private static string QualificationReasons(Phase7AdapterClaimCandidate candidate,CertifiedNarrationClaim claim,List<string> warnings)
    {
        var reasons=new SortedSet<string>(StringComparer.Ordinal);
        if(candidate.Approximate==true||claim.IsApproximate)reasons.Add("ApproximationQualification");
        if(claim.IsLocationDependent)reasons.Add("LocationQualification");
        if(claim.IsDateTimeDependent)reasons.Add("DateTimeQualification");
        if(claim.IsCultural||claim.IsMythological)reasons.Add("CulturalTraditionQualification");
        if(claim.IsAstrologyRelated)reasons.Add("AstrologyClarificationQualification");
        if(candidate.Uncertainty.HasValue||claim.Uncertain)reasons.Add("UncertaintyQualification");
        if(claim.RequiresHumanReview)reasons.Add("HumanReviewQualification");
        if(reasons.Count==0){reasons.Add("GovernedQualification");warnings.Add($"P7KNOWLEDGE_QUALIFICATION_REASON_GENERIC:{candidate.SemanticIdentity}");}
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
