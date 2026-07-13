using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;

namespace Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;

public enum SemanticEvidenceCategoryV1 { VerifiedEventData, VerifiedObjectData, DomainScientificKnowledge, CulturalContext, EditorialContext, EventIdentityContext, LegacyCompatibilityData }
public enum SemanticEvidenceStrengthV1 { None = 0, Weak = 1, Moderate = 2, Strong = 3, Authoritative = 4 }
public enum SemanticSourceMultiplicityV1 { FirstApprovedByPriority, HighestEvidenceStrength, RequireAgreement, CombineStructuredFields, RejectMultiple, PreferEventDataThenDomainContext }
public enum SemanticSourceConflictPolicyV1 { BlockRequired, PreferAuthoritative, PreferVerifiedEventData, RecordAndUseHighestStrength, OmitOptional, RequireAgreement }
public enum SemanticSourceMissingPolicyV1 { Block, OmitCapability, FutureUnavailable }
public enum SemanticSourceTrustRequirementV1 { Optional, Required, CertifiedRequired }
public enum SemanticSourceCertificationStatusV1 { Certified, CertifiedWithOptionalGaps, MissingCapabilityPolicy, MissingApprovedSource, EvidenceCategoryMismatch, RequiredSourceUnavailable, CompatibilityOnly, FutureUnavailable, InvalidPolicy }

public sealed record SemanticSourceDiagnosticMetadataV1(string SourceSprint, string CertificationState, string Notes);

public sealed record ApprovedSemanticSourceV1
{
    public ApprovedSemanticSourceV1(string sourceId, SemanticEvidenceCategoryV1 evidenceCategory, SemanticEvidenceStrengthV1 minimumStrength, int priority, bool eventSpecific, bool structured, bool verifiedRequired, bool supportsLocalization, bool supportsUnits, bool supportsProvenance, bool compatibilityOnly, bool activeInV1, string diagnosticDescription)
    { SourceId=sourceId; EvidenceCategory=evidenceCategory; MinimumStrength=minimumStrength; Priority=priority; EventSpecific=eventSpecific; Structured=structured; VerifiedRequired=verifiedRequired; SupportsLocalization=supportsLocalization; SupportsUnits=supportsUnits; SupportsProvenance=supportsProvenance; CompatibilityOnly=compatibilityOnly; ActiveInV1=activeInV1; DiagnosticDescription=diagnosticDescription; }
    public string SourceId { get; init; }
    public SemanticEvidenceCategoryV1 EvidenceCategory { get; init; }
    public SemanticEvidenceStrengthV1 MinimumStrength { get; init; }
    public int Priority { get; init; }
    public bool EventSpecific { get; init; }
    public bool Structured { get; init; }
    public bool VerifiedRequired { get; init; }
    public bool SupportsLocalization { get; init; }
    public bool SupportsUnits { get; init; }
    public bool SupportsProvenance { get; init; }
    public bool CompatibilityOnly { get; init; }
    public bool ActiveInV1 { get; init; }
    public string DiagnosticDescription { get; init; }
}

public sealed record SemanticSourceDescriptorV1(string SourceId, SemanticEvidenceCategoryV1 EvidenceCategory, SemanticEvidenceStrengthV1 EvidenceStrength, bool EventSpecific, bool Structured, bool Verified, bool SupportsLocalization, bool SupportsUnits, bool SupportsProvenance, bool CompatibilityOnly);
public sealed record SemanticSourceApprovalResultV1(SemanticCapabilityId CapabilityId, string SourceId, bool Approved, SemanticEvidenceCategoryV1 EvidenceCategory, SemanticEvidenceStrengthV1 EffectiveStrength, bool CompatibilityOnly, string DiagnosticCode, string DiagnosticMessage);
public sealed record SemanticSourcePolicyValidationResult(bool IsValid, IReadOnlyCollection<string> Errors, IReadOnlyCollection<string> Warnings)
{
    [JsonConstructor] public SemanticSourcePolicyValidationResult(bool isValid, ImmutableArray<string> errors, ImmutableArray<string> warnings) : this(isValid, (IReadOnlyCollection<string>)(errors.IsDefault ? [] : errors), warnings.IsDefault ? [] : warnings) { }
}
public sealed record SemanticSourcePolicyResolutionV1(SemanticCapabilityId CapabilityId, SemanticSourceCertificationStatusV1 Status, string DiagnosticCode, string DiagnosticMessage);

public sealed record SemanticSourcePolicyV1
{
    public SemanticSourcePolicyV1(SemanticCapabilityId semanticCapabilityId, string policyVersion, IReadOnlyCollection<SemanticEvidenceCategoryV1> allowedEvidenceCategories, IReadOnlyCollection<ApprovedSemanticSourceV1> approvedSources, SemanticEvidenceStrengthV1 minimumEvidenceStrength, bool eventSpecificVerificationRequired, bool domainKnowledgeAllowed, bool culturalContextAllowed, bool editorialContextAllowed, bool rawJsonCompatibilityAllowed, SemanticSourceMultiplicityV1 multipleCandidatePolicy, SemanticSourceConflictPolicyV1 conflictPolicy, SemanticSourceMissingPolicyV1 missingRequiredBehavior, SemanticSourceMissingPolicyV1 missingOptionalBehavior, bool allowDerivedValues, IReadOnlyCollection<string> approvedDerivationRuleIds, IReadOnlyCollection<string> deprecatedSourceIds, bool activeInV1, SemanticSourceDiagnosticMetadataV1 diagnosticMetadata)
        : this(semanticCapabilityId, policyVersion, allowedEvidenceCategories.ToImmutableArray(), approvedSources.ToImmutableArray(), minimumEvidenceStrength, eventSpecificVerificationRequired, domainKnowledgeAllowed, culturalContextAllowed, editorialContextAllowed, rawJsonCompatibilityAllowed, multipleCandidatePolicy, conflictPolicy, missingRequiredBehavior, missingOptionalBehavior, allowDerivedValues, approvedDerivationRuleIds.ToImmutableArray(), deprecatedSourceIds.ToImmutableArray(), activeInV1, diagnosticMetadata) { }
    [JsonConstructor]
    public SemanticSourcePolicyV1(SemanticCapabilityId semanticCapabilityId, string policyVersion, ImmutableArray<SemanticEvidenceCategoryV1> allowedEvidenceCategories, ImmutableArray<ApprovedSemanticSourceV1> approvedSources, SemanticEvidenceStrengthV1 minimumEvidenceStrength, bool eventSpecificVerificationRequired, bool domainKnowledgeAllowed, bool culturalContextAllowed, bool editorialContextAllowed, bool rawJsonCompatibilityAllowed, SemanticSourceMultiplicityV1 multipleCandidatePolicy, SemanticSourceConflictPolicyV1 conflictPolicy, SemanticSourceMissingPolicyV1 missingRequiredBehavior, SemanticSourceMissingPolicyV1 missingOptionalBehavior, bool allowDerivedValues, ImmutableArray<string> approvedDerivationRuleIds, ImmutableArray<string> deprecatedSourceIds, bool activeInV1, SemanticSourceDiagnosticMetadataV1 diagnosticMetadata)
    { SemanticCapabilityId=semanticCapabilityId; PolicyVersion=policyVersion; AllowedEvidenceCategories=allowedEvidenceCategories.IsDefault?[]:allowedEvidenceCategories; ApprovedSources=approvedSources.IsDefault?[]:approvedSources; MinimumEvidenceStrength=minimumEvidenceStrength; EventSpecificVerificationRequired=eventSpecificVerificationRequired; DomainKnowledgeAllowed=domainKnowledgeAllowed; CulturalContextAllowed=culturalContextAllowed; EditorialContextAllowed=editorialContextAllowed; RawJsonCompatibilityAllowed=rawJsonCompatibilityAllowed; MultipleCandidatePolicy=multipleCandidatePolicy; ConflictPolicy=conflictPolicy; MissingRequiredBehavior=missingRequiredBehavior; MissingOptionalBehavior=missingOptionalBehavior; AllowDerivedValues=allowDerivedValues; ApprovedDerivationRuleIds=approvedDerivationRuleIds.IsDefault?[]:approvedDerivationRuleIds; DeprecatedSourceIds=deprecatedSourceIds.IsDefault?[]:deprecatedSourceIds; ActiveInV1=activeInV1; DiagnosticMetadata=diagnosticMetadata; }
    public SemanticCapabilityId SemanticCapabilityId { get; init; }
    public string PolicyVersion { get; init; }
    public ImmutableArray<SemanticEvidenceCategoryV1> AllowedEvidenceCategories { get; init; }
    public ImmutableArray<ApprovedSemanticSourceV1> ApprovedSources { get; init; }
    public SemanticEvidenceStrengthV1 MinimumEvidenceStrength { get; init; }
    public bool EventSpecificVerificationRequired { get; init; }
    public bool DomainKnowledgeAllowed { get; init; }
    public bool CulturalContextAllowed { get; init; }
    public bool EditorialContextAllowed { get; init; }
    public bool RawJsonCompatibilityAllowed { get; init; }
    public SemanticSourceMultiplicityV1 MultipleCandidatePolicy { get; init; }
    public SemanticSourceConflictPolicyV1 ConflictPolicy { get; init; }
    public SemanticSourceMissingPolicyV1 MissingRequiredBehavior { get; init; }
    public SemanticSourceMissingPolicyV1 MissingOptionalBehavior { get; init; }
    public bool AllowDerivedValues { get; init; }
    public ImmutableArray<string> ApprovedDerivationRuleIds { get; init; }
    public ImmutableArray<string> DeprecatedSourceIds { get; init; }
    public bool ActiveInV1 { get; init; }
    public SemanticSourceDiagnosticMetadataV1 DiagnosticMetadata { get; init; }
    public bool Equals(SemanticSourcePolicyV1? other) => other is not null && SemanticCapabilityId.Equals(other.SemanticCapabilityId) && PolicyVersion==other.PolicyVersion && AllowedEvidenceCategories.SequenceEqual(other.AllowedEvidenceCategories) && ApprovedSources.SequenceEqual(other.ApprovedSources) && MinimumEvidenceStrength==other.MinimumEvidenceStrength && EventSpecificVerificationRequired==other.EventSpecificVerificationRequired && DomainKnowledgeAllowed==other.DomainKnowledgeAllowed && CulturalContextAllowed==other.CulturalContextAllowed && EditorialContextAllowed==other.EditorialContextAllowed && RawJsonCompatibilityAllowed==other.RawJsonCompatibilityAllowed && MultipleCandidatePolicy==other.MultipleCandidatePolicy && ConflictPolicy==other.ConflictPolicy && MissingRequiredBehavior==other.MissingRequiredBehavior && MissingOptionalBehavior==other.MissingOptionalBehavior && AllowDerivedValues==other.AllowDerivedValues && ApprovedDerivationRuleIds.SequenceEqual(other.ApprovedDerivationRuleIds) && DeprecatedSourceIds.SequenceEqual(other.DeprecatedSourceIds) && ActiveInV1==other.ActiveInV1 && Equals(DiagnosticMetadata, other.DiagnosticMetadata);
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(SemanticCapabilityId);
        hashCode.Add(PolicyVersion);
        hashCode.Add(MinimumEvidenceStrength);
        hashCode.Add(EventSpecificVerificationRequired);
        hashCode.Add(DomainKnowledgeAllowed);
        hashCode.Add(CulturalContextAllowed);
        hashCode.Add(EditorialContextAllowed);
        hashCode.Add(RawJsonCompatibilityAllowed);
        hashCode.Add(MultipleCandidatePolicy);
        hashCode.Add(ConflictPolicy);
        hashCode.Add(MissingRequiredBehavior);
        hashCode.Add(MissingOptionalBehavior);
        hashCode.Add(AllowDerivedValues);
        hashCode.Add(ActiveInV1);
        hashCode.Add(DiagnosticMetadata);
        foreach (var x in AllowedEvidenceCategories) hashCode.Add(x);
        foreach (var x in ApprovedSources) hashCode.Add(x);
        foreach (var x in ApprovedDerivationRuleIds) hashCode.Add(x);
        foreach (var x in DeprecatedSourceIds) hashCode.Add(x);
        return hashCode.ToHashCode();
    }
}
