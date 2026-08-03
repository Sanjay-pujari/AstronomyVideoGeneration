using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

internal static class Phase7KnowledgePolicyFacts
{
    public static bool Active(CertifiedNarrationClaim claim) => claim.Disposition is
        Phase7ClaimDisposition.Required or Phase7ClaimDisposition.Optional or Phase7ClaimDisposition.HumanReview;
    public static bool Eligible(Phase7ClaimSupportEvidence evidence) => evidence.SourceEligibility is
        Phase7SourceEligibility.EligibleForRequiredClaim or Phase7SourceEligibility.EligibleForOptionalClaim;
    public static bool Qualified(Phase7KnowledgeAuthority authority, CertifiedNarrationClaim claim) =>
        claim.RequiresQualification && authority.ClaimSupportEvidence.Any(x => x.ClaimId == claim.ClaimId &&
            !string.IsNullOrWhiteSpace(x.QualificationReason));
    public static bool Scoped(Phase7KnowledgeAuthority authority, CertifiedNarrationClaim claim) =>
        authority.ClaimSupportEvidence.Any(x => x.ClaimId == claim.ClaimId &&
            !string.IsNullOrWhiteSpace(x.AuthorityScope) && x.AuthorityScope != "GeneralAuthority") ||
        authority.MergeDecisions.Any(x => x.SelectedClaimIds.Contains(claim.ClaimId) &&
            (x.EventScope.HasExplicitEvidence || x.EvergreenScope.HasExplicitEvidence));
    public static string? Identity(Phase7KnowledgeAuthority authority, CertifiedNarrationClaim claim,
        IReadOnlyList<string> markers)
    {
        var evidence=authority.ClaimSupportEvidence.Where(x=>x.ClaimId==claim.ClaimId).OrderBy(x=>x.SourceId,StringComparer.Ordinal);
        var values=evidence.SelectMany(x=>new[]{x.ApprovedFieldPath,x.KnowledgeId,x.SemanticIdentity,x.AuthorityScope})
            .Concat(claim.KnowledgeReferenceIds).Concat(authority.Sources.Where(s=>claim.SourceIds.Contains(s.SourceId)).SelectMany(s=>new[]{s.SourceId,s.Title,s.PublisherOrAuthority}));
        foreach(var value in values.Where(x=>!string.IsNullOrWhiteSpace(x)))
        foreach(var marker in markers)
        {
            var index=value.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
            if(index<0)continue;
            var identity=new string(value[index..].TakeWhile(c=>char.IsLetterOrDigit(c)||c is '-' or '_').ToArray()).Trim('-', '_').ToLowerInvariant();
            if(identity.Length>marker.Length)return identity;
        }
        return null;
    }
}

public sealed class Phase7LocationTimeSafetyPolicy : IPhase7LocationTimeSafetyPolicy
{
    public Phase7LocationTimeSafetyResult Evaluate(Phase7KnowledgeAuthority authority, ResolvedNarrationKnowledge resolution, FamilyNarrationProfile profile)
    {
        var claims=authority.Claims.Where(x=>Phase7KnowledgePolicyFacts.Active(x)&&(x.IsLocationDependent||x.IsDateTimeDependent||x.Domain is "Timing" or "Visibility" or "Astrophotography")).OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray();
        var errors=new SortedSet<string>(StringComparer.Ordinal);var warnings=new SortedSet<string>(StringComparer.Ordinal);
        foreach(var claim in claims)
        {
            var scoped=Phase7KnowledgePolicyFacts.Scoped(authority,claim);var qualified=Phase7KnowledgePolicyFacts.Qualified(authority,claim);
            var paths=authority.ClaimSupportEvidence.Where(x=>x.ClaimId==claim.ClaimId).Select(x=>x.ApprovedFieldPath).ToArray();
            var exactLocal=paths.Any(x=>x.Contains("localTime",StringComparison.OrdinalIgnoreCase));
            var universal=paths.Any(x=>x.Contains("universal",StringComparison.OrdinalIgnoreCase));
            var exposure=claim.Domain=="Astrophotography"||paths.Any(x=>x.Contains("exposure",StringComparison.OrdinalIgnoreCase));
            if(exactLocal&&!scoped)errors.Add($"P7KNOWLEDGE_EXACT_LOCAL_TIME_UNSCOPED:{claim.ClaimId}");
            if(universal&&(claim.IsDateTimeDependent||claim.IsLocationDependent))errors.Add($"P7KNOWLEDGE_UNIVERSAL_VIEWING_TIME:{claim.ClaimId}");
            if(universal&&exposure)errors.Add($"P7KNOWLEDGE_UNIVERSAL_EXPOSURE_SETTING:{claim.ClaimId}");
            if(claim.IsLocationDependent&&!scoped&&!qualified)errors.Add($"P7KNOWLEDGE_LOCATION_SCOPE_REQUIRED:{claim.ClaimId}");
            if(claim.IsDateTimeDependent&&!scoped&&!qualified)errors.Add($"P7KNOWLEDGE_DATETIME_SCOPE_REQUIRED:{claim.ClaimId}");
        }
        foreach(var rule in profile.SafetyRules.Where(x=>x.Contains("location",StringComparison.OrdinalIgnoreCase)||x.Contains("time",StringComparison.OrdinalIgnoreCase)))
            warnings.Add($"P7KNOWLEDGE_FAMILY_SAFETY_RULE_APPLIED:{rule}");
        return new(errors.Count==0,errors.ToArray(),warnings.ToArray(),claims.Select(x=>x.ClaimId).ToArray());
    }
}

public sealed class Phase7CulturalKnowledgeSafetyPolicy : IPhase7CulturalKnowledgeSafetyPolicy
{
    public Phase7CulturalKnowledgeSafetyResult Evaluate(Phase7KnowledgeAuthority authority, ResolvedNarrationKnowledge resolution, FamilyNarrationProfile profile)
    {
        var claims=authority.Claims.Where(x=>Phase7KnowledgePolicyFacts.Active(x)&&(x.IsCultural||x.IsMythological)).OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray();
        var errors=new SortedSet<string>(StringComparer.Ordinal);var warnings=new SortedSet<string>(StringComparer.Ordinal);var identities=new SortedDictionary<string,string>(StringComparer.Ordinal);
        var approved=new HashSet<string>(new[]{"CultureAndMythology","RegionalTraditions"},StringComparer.Ordinal);
        foreach(var rule in profile.CulturalRules.Where(x=>x.StartsWith("ApprovedDomain:",StringComparison.OrdinalIgnoreCase)))approved.Add(rule[(rule.IndexOf(':')+1)..].Trim());
        foreach(var claim in claims)
        {
            if(!approved.Contains(claim.Domain))errors.Add($"P7KNOWLEDGE_CULTURAL_DOMAIN_INVALID:{claim.ClaimId}");
            var identity=Phase7KnowledgePolicyFacts.Identity(authority,claim,["tradition-","culture-","mythology-"]);
            if(identity is null)errors.Add($"P7KNOWLEDGE_TRADITION_IDENTITY_REQUIRED:{claim.ClaimId}");else identities[claim.ClaimId]=identity;
            var evidence=authority.ClaimSupportEvidence.Where(x=>x.ClaimId==claim.ClaimId).ToArray();
            if(evidence.Length==0||evidence.Any(x=>!Phase7KnowledgePolicyFacts.Eligible(x)))errors.Add($"P7KNOWLEDGE_CULTURAL_EVIDENCE_INELIGIBLE:{claim.ClaimId}");
            if(claim.RequiresHumanReview&&claim.Disposition!=Phase7ClaimDisposition.HumanReview)errors.Add($"P7KNOWLEDGE_CULTURAL_REVIEW_STATUS_LOST:{claim.ClaimId}");
            if(claim.SemanticIdentity.Contains("compar",StringComparison.OrdinalIgnoreCase)&&!evidence.Any(x=>x.ApprovedFieldPath.Contains("comparativeRelationship",StringComparison.OrdinalIgnoreCase)&&x.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim))
                errors.Add($"P7KNOWLEDGE_CULTURAL_COMPARISON_UNCERTIFIED:{claim.ClaimId}");
        }
        if(claims.Length>0)foreach(var rule in profile.CulturalRules)warnings.Add($"P7KNOWLEDGE_CULTURAL_RULE_APPLIED:{rule}");
        return new(errors.Count==0,errors.ToArray(),warnings.ToArray(),claims.Select(x=>x.ClaimId).ToArray(),identities);
    }
}

public sealed class Phase7AstrologySeparationPolicy : IPhase7AstrologySeparationPolicy
{
    private static readonly string[] Systems=["western-zodiac","rashi","nakshatra","historical-astrology","traditional-symbolism","system-","tradition-"];
    public Phase7AstrologySeparationResult Evaluate(Phase7KnowledgeAuthority authority, ResolvedNarrationKnowledge resolution, FamilyNarrationProfile profile)
    {
        var claims=authority.Claims.Where(x=>Phase7KnowledgePolicyFacts.Active(x)&&x.IsAstrologyRelated).OrderBy(x=>x.ClaimId,StringComparer.Ordinal).ToArray();
        var errors=new SortedSet<string>(StringComparer.Ordinal);var warnings=new SortedSet<string>(StringComparer.Ordinal);var identities=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var claim in claims)
        {
            if(claim.Domain!="AstrologyClarification")errors.Add($"P7KNOWLEDGE_ASTROLOGY_DOMAIN_INVALID:{claim.ClaimId}");
            var identity=Phase7KnowledgePolicyFacts.Identity(authority,claim,Systems);
            if(identity is null)errors.Add($"P7KNOWLEDGE_ASTROLOGY_SYSTEM_REQUIRED:{claim.ClaimId}");else identities[claim.ClaimId]=identity;
            if(!Phase7KnowledgePolicyFacts.Qualified(authority,claim))errors.Add($"P7KNOWLEDGE_ASTROLOGY_QUALIFICATION_REQUIRED:{claim.ClaimId}");
            var evidence=authority.ClaimSupportEvidence.Where(x=>x.ClaimId==claim.ClaimId).ToArray();
            if(evidence.Length==0||evidence.Any(x=>!Phase7KnowledgePolicyFacts.Eligible(x)))errors.Add($"P7KNOWLEDGE_ASTROLOGY_EVIDENCE_INELIGIBLE:{claim.ClaimId}");
            if((claim.SemanticIdentity.Contains("equivalent",StringComparison.OrdinalIgnoreCase)||claim.SemanticIdentity.Contains("equals",StringComparison.OrdinalIgnoreCase))&&!Phase7KnowledgePolicyFacts.Qualified(authority,claim))
                errors.Add($"P7KNOWLEDGE_ASTROLOGY_EQUIVALENCE_UNQUALIFIED:{claim.ClaimId}");
        }
        if(claims.Length>0)foreach(var rule in profile.CulturalRules.Concat(profile.TerminologyRules))warnings.Add($"P7KNOWLEDGE_ASTROLOGY_RULE_APPLIED:{rule}");
        return new(errors.Count==0,errors.ToArray(),warnings.ToArray(),claims.Select(x=>x.ClaimId).ToArray(),identities);
    }
}

public sealed class Phase7KnowledgeDiagnosticsReconciler : IPhase7KnowledgeDiagnosticsReconciler
{
    public Phase7KnowledgeDiagnosticsReconciliation Reconcile(Phase7KnowledgeAuthority a,ResolvedNarrationKnowledge r,Phase7KnowledgeDiagnostics d,Phase7LocationTimeSafetyResult location,Phase7CulturalKnowledgeSafetyResult cultural,Phase7AstrologySeparationResult astrology)
    {
        var claims=a.Claims;var evidence=a.ClaimSupportEvidence;var expected=new SortedDictionary<string,string>(StringComparer.Ordinal);var actual=new SortedDictionary<string,string>(StringComparer.Ordinal);
        void Add(string n,object e,object v){expected[n]=Convert.ToString(e,System.Globalization.CultureInfo.InvariantCulture)??"";actual[n]=Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)??"";}
        int Merge(Phase7KnowledgeMergeClassification x)=>a.MergeDecisions.Count(m=>m.Classification==x);
        int Domain(IEnumerable<string> names,KnowledgeDomainStatus status)=>names.Count(n=>r.Domains.Any(x=>x.Domain==n&&x.Status==status));
        bool RequiredEvidence(Phase7ClaimSupportEvidence x)=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Required);
        Add(nameof(d.AcceptedClaimCount),claims.Count,d.AcceptedClaimCount);Add(nameof(d.AcceptedRequiredCount),claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Required),d.AcceptedRequiredCount);Add(nameof(d.AcceptedOptionalCount),claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Optional),d.AcceptedOptionalCount);Add(nameof(d.HumanReviewClaimCount),claims.Count(x=>x.Disposition==Phase7ClaimDisposition.HumanReview),d.HumanReviewClaimCount);Add(nameof(d.DeferredClaimCount),claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Deferred),d.DeferredClaimCount);Add(nameof(d.RejectedClaimCount),0,d.RejectedClaimCount);Add(nameof(d.RequiredClaimCount),claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Required),d.RequiredClaimCount);
        Add(nameof(d.ExactClaimProvenanceCount),evidence.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactClaim),d.ExactClaimProvenanceCount);Add(nameof(d.ExactEntityProvenanceCount),evidence.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactKnowledgeEntity),d.ExactEntityProvenanceCount);Add(nameof(d.ExactFieldProvenanceCount),evidence.Count(x=>x.ProvenancePrecision==Phase7ProvenancePrecision.ExactApprovedField),d.ExactFieldProvenanceCount);
        Add(nameof(d.RequiredExactClaimCount),evidence.Count(x=>RequiredEvidence(x)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactClaim),d.RequiredExactClaimCount);Add(nameof(d.RequiredExactEntityCount),evidence.Count(x=>RequiredEvidence(x)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactKnowledgeEntity),d.RequiredExactEntityCount);Add(nameof(d.RequiredExactFieldCount),evidence.Count(x=>RequiredEvidence(x)&&x.ProvenancePrecision==Phase7ProvenancePrecision.ExactApprovedField),d.RequiredExactFieldCount);
        Add(nameof(d.OptionalAuthoritativeEvidenceCount),evidence.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition==Phase7ClaimDisposition.Optional)&&x.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim),d.OptionalAuthoritativeEvidenceCount);Add(nameof(d.OptionalReviewedEvidenceCount),evidence.Count(x=>claims.Any(c=>c.ClaimId==x.ClaimId&&c.Disposition is Phase7ClaimDisposition.Optional or Phase7ClaimDisposition.HumanReview)&&x.SourceEligibility==Phase7SourceEligibility.EligibleForOptionalClaim),d.OptionalReviewedEvidenceCount);Add(nameof(d.NoProvenanceClaimCount),claims.Count(x=>x.ProvenancePrecision==nameof(Phase7ProvenancePrecision.None)),d.NoProvenanceClaimCount);
        foreach(var pair in new[]{(nameof(d.EquivalentMergeCount),Merge(Phase7KnowledgeMergeClassification.Equivalent),d.EquivalentMergeCount),(nameof(d.SpecializationMergeCount),Merge(Phase7KnowledgeMergeClassification.EventSpecificSpecialization),d.SpecializationMergeCount),(nameof(d.EventMorePreciseCount),Merge(Phase7KnowledgeMergeClassification.EventMorePrecise),d.EventMorePreciseCount),(nameof(d.EvergreenMorePreciseCount),Merge(Phase7KnowledgeMergeClassification.EvergreenMorePrecise),d.EvergreenMorePreciseCount),(nameof(d.ContradictionCount),Merge(Phase7KnowledgeMergeClassification.Contradictory),d.ContradictionCount),(nameof(d.IncomparableCount),Merge(Phase7KnowledgeMergeClassification.Incomparable),d.IncomparableCount)})Add(pair.Item1,pair.Item2,pair.Item3);
        Add(nameof(d.AllSourceCount),a.SourceAuditSummary.AllResolvedSourceCount,d.AllSourceCount);Add(nameof(d.CertifiedSupportingSourceCount),a.Sources.Count(x=>x.Certified&&x.Disposition=="CertifiedSupporting"),d.CertifiedSupportingSourceCount);Add(nameof(d.ReviewedNonCertifiedSourceCount),a.Sources.Count(x=>x.Reviewed&&!x.Certified),d.ReviewedNonCertifiedSourceCount);Add(nameof(d.RejectedSourceCount),a.SourceAuditSummary.RejectedSourceCount,d.RejectedSourceCount);Add(nameof(d.UnverifiedSourceCount),a.SourceAuditSummary.UncertifiedSourceCount,d.UnverifiedSourceCount);
        Add(nameof(d.MandatoryAvailableDomainCount),Domain(a.MandatoryDomains,KnowledgeDomainStatus.Available),d.MandatoryAvailableDomainCount);Add(nameof(d.MandatoryHumanReviewDomainCount),Domain(a.MandatoryDomains,KnowledgeDomainStatus.RequiresHumanReview),d.MandatoryHumanReviewDomainCount);Add(nameof(d.MandatoryDeferredDomainCount),Domain(a.MandatoryDomains,KnowledgeDomainStatus.Deferred),d.MandatoryDeferredDomainCount);Add(nameof(d.MandatoryMissingDomainCount),Domain(a.MandatoryDomains,KnowledgeDomainStatus.Missing),d.MandatoryMissingDomainCount);Add(nameof(d.OptionalAvailableDomainCount),Domain(a.OptionalDomains,KnowledgeDomainStatus.Available),d.OptionalAvailableDomainCount);Add(nameof(d.OptionalHumanReviewDomainCount),Domain(a.OptionalDomains,KnowledgeDomainStatus.RequiresHumanReview),d.OptionalHumanReviewDomainCount);Add(nameof(d.OptionalDeferredDomainCount),Domain(a.OptionalDomains,KnowledgeDomainStatus.Deferred),d.OptionalDeferredDomainCount);Add(nameof(d.OptionalNotApplicableDomainCount),Domain(a.OptionalDomains,KnowledgeDomainStatus.NotApplicable),d.OptionalNotApplicableDomainCount);
        Add(nameof(d.KnowledgeEntityCount),a.KnowledgeEntities.Count,d.KnowledgeEntityCount);Add(nameof(d.ExtractedCandidateCount),a.AdapterDiagnostics.Sum(x=>x.ExtractedClaimCount),d.ExtractedCandidateCount);Add(nameof(d.UnsupportedClaimCount),a.AdapterDiagnostics.Sum(x=>x.UnsupportedClaimCount),d.UnsupportedClaimCount);Add(nameof(d.UnknownSectionCount),r.UnknownSections.Count,d.UnknownSectionCount);Add(nameof(d.UnknownPropertyCount),r.UnknownProperties.Count,d.UnknownPropertyCount);
        Add(nameof(d.LocationTimeSafetyPassed),location.Passed,d.LocationTimeSafetyPassed);Add(nameof(d.CulturalSafetyPassed),cultural.Passed,d.CulturalSafetyPassed);Add(nameof(d.AstrologySeparationPassed),astrology.Passed,d.AstrologySeparationPassed);Add(nameof(d.WarningCount),a.Warnings.Count,d.WarningCount);Add(nameof(d.BlockingIssueCount),a.BlockingIssues.Count,d.BlockingIssueCount);
        var differences=expected.Keys.Where(k=>expected[k]!=actual[k]).Select(k=>$"{k}: expected={expected[k]}; actual={actual[k]}").Order(StringComparer.Ordinal).ToArray();
        return new(differences.Length==0,differences,expected,actual);
    }
}
