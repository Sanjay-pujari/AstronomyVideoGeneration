using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

public sealed class Phase7KnowledgeAuthorityValidator : IPhase7KnowledgeAuthorityValidator
{
    public Phase7KnowledgeValidation Validate(Phase7KnowledgeAuthority a, ResolvedNarrationKnowledge r,
        Phase7KnowledgeDiagnostics d, Phase7KnowledgeValidationMode mode=Phase7KnowledgeValidationMode.InMemoryCandidate,
        Phase7KnowledgeCompleteSetReadback? readback=null)
    {
        var gates=new List<Phase7KnowledgeValidationGate>();
        void Add(string name,bool pass,string code) => gates.Add(new(name,pass,pass?[]:[code],[]));
        var claimIds=a.Claims.Select(x=>x.ClaimId).ToArray();
        var semantic=a.Claims.Select(x=>x.SemanticIdentity).ToArray();
        var evidence=a.ClaimSupportEvidence.Where(x=>x.SourceEligibility==Phase7SourceEligibility.EligibleForRequiredClaim).ToArray();
        var required=a.Claims.Where(x=>x.Disposition==Phase7ClaimDisposition.Required).ToArray();
        var contradictions=a.MergeDecisions.Where(x=>x.Classification==Phase7KnowledgeMergeClassification.Contradictory).ToArray();
        var incomparable=a.MergeDecisions.Where(x=>x.Classification==Phase7KnowledgeMergeClassification.Incomparable).ToArray();
        var special=a.MergeDecisions.Where(x=>x.Classification==Phase7KnowledgeMergeClassification.EventSpecificSpecialization).ToArray();
        var physical=readback is {IsValid:true};
        Add("Phase6InputGate",!string.IsNullOrWhiteSpace(a.SourcePhase6AuthorityId)&&!string.IsNullOrWhiteSpace(a.SourcePhase6IndexId),"P7KNOWLEDGE_PHASE6_INPUT_INVALID");
        Add("EventCertificationGate",a.EventVerificationStatus is "Verified" or "Certified","P7KNOWLEDGE_EVENT_CERTIFICATION_INVALID");
        Add("EvergreenCertificationGate",(a.EvergreenReviewStatus is "NotLoaded" or "Reviewed" or "Verified" or "Certified")&&
            (string.IsNullOrEmpty(a.EvergreenPayloadId)==(a.EvergreenReviewStatus=="NotLoaded")),"P7KNOWLEDGE_EVERGREEN_CERTIFICATION_INVALID");
        Add("FamilyGate",!string.IsNullOrWhiteSpace(a.EventFamily),"P7KNOWLEDGE_FAMILY_INVALID");
        Add("LanguageGate",!string.IsNullOrWhiteSpace(a.Language),"P7KNOWLEDGE_LANGUAGE_INVALID");
        Add("ProfileGate",!string.IsNullOrWhiteSpace(a.ProfileId)&&!string.IsNullOrWhiteSpace(a.ProfileVersion),"P7KNOWLEDGE_PROFILE_INVALID");
        Add("SourceRegistryGate",!string.IsNullOrWhiteSpace(a.SourceRegistryId)&&!string.IsNullOrWhiteSpace(a.SourceRegistryChecksum),"P7KNOWLEDGE_SOURCE_REGISTRY_INVALID");
        Add("SourceEligibilityGate",required.All(x=>evidence.Any(e=>e.ClaimId==x.ClaimId)),"P7KNOWLEDGE_SOURCE_ELIGIBILITY_INVALID");
        Add("SourceAuditGate",a.SourceAuditSummary.AllResolvedSourceCount>=a.Sources.Count,"P7KNOWLEDGE_SOURCE_AUDIT_INVALID");
        Add("AdapterCoverageGate",a.AdapterDiagnostics.Count>0,"P7KNOWLEDGE_ADAPTER_COVERAGE_INVALID");
        Add("CanonicalFieldPathGate",a.ClaimSupportEvidence.All(x=>!string.IsNullOrWhiteSpace(x.ApprovedFieldPath)&&!x.ApprovedFieldPath.Contains("..")),"P7KNOWLEDGE_CANONICAL_PATH_INVALID");
        Add("KnowledgeIdentityGate",a.KnowledgeEntities.Select(x=>x.KnowledgeId).Distinct().Count()==a.KnowledgeEntities.Count,"P7KNOWLEDGE_KNOWLEDGE_IDENTITY_INVALID");
        Add("ClaimIdentityGate",claimIds.Distinct().Count()==claimIds.Length&&semantic.Distinct().Count()==semantic.Length,"P7KNOWLEDGE_CLAIM_IDENTITY_INVALID");
        Add("ClaimChecksumGate",a.Claims.All(x=>x.Checksum==Phase7Determinism.Hash(x with{Checksum=""})),"P7KNOWLEDGE_CLAIM_CHECKSUM_INVALID");
        Add("ClaimProvenanceGate",required.All(x=>evidence.Any(e=>e.ClaimId==x.ClaimId&&e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField)),"P7KNOWLEDGE_PROVENANCE_INVALID");
        Add("ClaimSupportEvidenceGate",a.ClaimSupportEvidence.All(x=>claimIds.Contains(x.ClaimId)&&a.Sources.Any(s=>s.SourceId==x.SourceId))&&
            a.Claims.All(claim=>claim.SourceIds.Order(StringComparer.Ordinal).SequenceEqual(a.ClaimSupportEvidence.Where(e=>e.ClaimId==claim.ClaimId).Select(e=>e.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))),"P7KNOWLEDGE_SUPPORT_EVIDENCE_INVALID");
        bool AuthoritativeDomain(string domain)=>required.Where(x=>x.Domain==domain&&!x.RequiresHumanReview)
            .Any(x=>x.Checksum==Phase7Determinism.Hash(x with{Checksum=""})&&evidence.Any(e=>e.ClaimId==x.ClaimId&&
                e.ProvenancePrecision is Phase7ProvenancePrecision.ExactClaim or Phase7ProvenancePrecision.ExactKnowledgeEntity or Phase7ProvenancePrecision.ExactApprovedField)&&
                !contradictions.Any(m=>m.SelectedClaimIds.Contains(x.ClaimId)));
        Add("MandatoryDomainGate",a.MandatoryDomains.All(AuthoritativeDomain),"P7KNOWLEDGE_MANDATORY_DOMAIN_MISSING");
        Add("OptionalDomainGate",a.OptionalDomains.All(x=>r.Domains.Any(y=>y.Domain==x&&y.Status is KnowledgeDomainStatus.Available or KnowledgeDomainStatus.NotApplicable or KnowledgeDomainStatus.Deferred or KnowledgeDomainStatus.RequiresHumanReview)),"P7KNOWLEDGE_OPTIONAL_DOMAIN_INVALID");
        Add("MergeDecisionGate",a.MergeDecisions.All(x=>x.Classification==Phase7KnowledgeMergeClassification.Contradictory?x.SelectedClaimIds.Count==0:x.SelectedClaimIds.All(claimIds.Contains)),"P7KNOWLEDGE_MERGE_INVALID");
        Add("TrueScopeGate",special.All(x=>x.EventScope.HasExplicitEvidence),"P7KNOWLEDGE_TRUE_SCOPE_INVALID");
        Add("SpecializationScopeGate",special.All(x=>x.EventScope.HasExplicitEvidence),"P7KNOWLEDGE_SPECIALIZATION_SCOPE_INVALID");
        Add("ContradictionGate",contradictions.Length==0,"P7KNOWLEDGE_CONTRADICTION_PRESENT");
        Add("IncomparableGate",incomparable.All(x=>x.SelectedClaimIds.Count==0||x.EventScope.HasExplicitEvidence||x.EvergreenScope.HasExplicitEvidence),"P7KNOWLEDGE_INCOMPARABLE_INVALID");
        Add("QualificationGate",required.Where(x=>x.RequiresQualification).All(x=>a.ClaimSupportEvidence.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.QualificationReason))),"P7KNOWLEDGE_QUALIFICATION_INVALID");
        bool Qualified(CertifiedNarrationClaim x)=>!x.RequiresQualification||a.ClaimSupportEvidence.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.QualificationReason));
        bool HasQualification(CertifiedNarrationClaim x)=>x.RequiresQualification&&a.ClaimSupportEvidence.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.QualificationReason));
        bool Scoped(CertifiedNarrationClaim x)=>a.ClaimSupportEvidence.Any(e=>e.ClaimId==x.ClaimId&&!string.IsNullOrWhiteSpace(e.AuthorityScope))||
            a.MergeDecisions.Any(m=>m.SelectedClaimIds.Contains(x.ClaimId)&&(m.EventScope.HasExplicitEvidence||m.EvergreenScope.HasExplicitEvidence));
        Add("LocationTimeSafetyGate",a.Claims.Where(x=>x.IsLocationDependent||x.IsDateTimeDependent).All(x=>Scoped(x)||HasQualification(x)),"P7KNOWLEDGE_LOCATION_TIME_SAFETY_INVALID");
        Add("CulturalSafetyGate",a.Claims.Where(x=>x.IsCultural||x.IsMythological).All(x=>x.RequiresQualification&&Qualified(x)&&(!x.RequiresHumanReview||x.Disposition==Phase7ClaimDisposition.HumanReview)),"P7KNOWLEDGE_CULTURAL_SAFETY_INVALID");
        Add("AstrologySeparationGate",a.Claims.Where(x=>x.IsAstrologyRelated).All(x=>x.RequiresQualification&&Qualified(x)&&x.Domain.Contains("astrolog",StringComparison.OrdinalIgnoreCase)),"P7KNOWLEDGE_ASTROLOGY_SEPARATION_INVALID");
        Add("DiagnosticsReconciliationGate",d.DiagnosticsReconciled&&d.ReconciliationDifferences.Count==0&&d.AuthorityId==a.AuthorityId&&d.AcceptedClaimCount==a.Claims.Count&&d.RequiredClaimCount==required.Length&&d.DeferredClaimCount==a.Claims.Count(x=>x.Disposition==Phase7ClaimDisposition.Deferred)&&d.BlockingIssueCount==a.BlockingIssues.Count&&d.WarningCount==a.Warnings.Count&&d.LocationTimeSafetyPassed&&d.CulturalSafetyPassed&&d.AstrologySeparationPassed,"P7KNOWLEDGE_DIAGNOSTICS_INVALID");
        if(mode==Phase7KnowledgeValidationMode.InMemoryCandidate)
        {
            gates.Add(new("ArtifactCompleteSetGate",false,[],["NotApplicable before physical writing."]));
            gates.Add(new("ArtifactInventoryGate",false,[],["NotApplicable before physical writing."]));
            gates.Add(new("PhysicalReadbackGate",false,[],["NotApplicable for an in-memory candidate."]));
        }
        else
        {
            Add("ArtifactCompleteSetGate",readback?.Artifacts.Count==3,"P7KNOWLEDGE_COMPLETE_SET_INVALID");
            Add("ArtifactInventoryGate",readback?.ExpectedInventory?.Artifacts.Count==3,"P7KNOWLEDGE_INVENTORY_INVALID");
            Add("PhysicalReadbackGate",physical,"P7KNOWLEDGE_PHYSICAL_READBACK_INVALID");
        }
        Add("LineageGate",!string.IsNullOrWhiteSpace(a.SourcePhase4Checksum)&&!string.IsNullOrWhiteSpace(a.SourcePhase5PublicationId)&&!string.IsNullOrWhiteSpace(a.SourcePhase6AuthorityChecksum),"P7KNOWLEDGE_LINEAGE_INVALID");
        Add("RuntimeCompatibilityGate",a.RuntimeCompatibilityEvidence.Count>0,"P7KNOWLEDGE_RUNTIME_INCOMPATIBLE");
        var errors=gates.SelectMany(x=>x.Errors).ToArray();
        var code=errors.Length==0?"P7KNOWLEDGE_VALID":errors[0];
        var draft=new Phase7KnowledgeValidation(Phase7KnowledgeContract.Version,a.ExecutionId,a.PlanId,a.EventId,a.AuthorityId,
            errors.Length==0,code,mode,gates,errors,a.Warnings,readback?.ExpectedInventory,"");
        return draft with{DeterministicChecksum=Phase7Determinism.Hash(draft)};
    }
}
