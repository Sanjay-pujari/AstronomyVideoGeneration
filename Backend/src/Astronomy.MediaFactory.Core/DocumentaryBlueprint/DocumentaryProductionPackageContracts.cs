using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryProductionPackageStatus { Complete, Rejected }
public enum DocumentaryProductionPackageRejectionReason
{
    ReleaseCandidateNotAccepted, ReleaseCandidateNotClean, ReleaseCandidateNotFullyResolved,
    ReleaseCandidateIdentityMismatch, NarrativeDraftLineageMismatch, ValidationLineageMismatch,
    ConvergenceLineageMismatch, AcceptanceLineageMismatch, CorrelationMismatch,
    RequiredSectionMissing, RequiredEvidenceMissing, PolicyRejected
}
public enum DocumentaryProductionPackageSection
{
    AcceptedNarrative, FinalValidationEvidence, RevisionHistory, ConvergenceEvidence,
    AcceptanceEvidence, PackageManifest
}

internal static class DocumentaryProductionPackageInventory
{
    internal const string Schema = "1.0";
    internal static readonly DocumentaryProductionPackageSection[] Sections = Enum.GetValues<DocumentaryProductionPackageSection>();
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
    internal static void ValidateSections(IReadOnlyList<DocumentaryProductionPackageSection> sections, string name)
    {
        ArgumentNullException.ThrowIfNull(sections, name);
        if (!sections.SequenceEqual(Sections))
            throw new ArgumentException("Sections must be the complete canonical schema 1.0 inventory.", name);
    }
}

public sealed class DocumentaryProductionPackagePolicy
{
    public DocumentaryProductionPackagePolicy(bool requireAcceptedReleaseCandidate, bool requireCleanNarrative,
        bool requireFullyResolvedNarrative, bool requireFinalValidationEvidence, bool requireRevisionHistory,
        bool requireConvergenceEvidence, bool requireAcceptanceEvidence,
        IReadOnlyList<DocumentaryProductionPackageSection> requiredSections, string policySchemaVersion)
    {
        if (!requireAcceptedReleaseCandidate || !requireCleanNarrative || !requireFullyResolvedNarrative ||
            !requireFinalValidationEvidence || !requireRevisionHistory || !requireConvergenceEvidence || !requireAcceptanceEvidence)
            throw new ArgumentException("Production package policy 1.0 requires all certified evidence.");
        DocumentaryProductionPackageInventory.ValidateSections(requiredSections, nameof(requiredSections));
        PolicySchemaVersion = policySchemaVersion == DocumentaryProductionPackageInventory.Schema ? policySchemaVersion :
            throw new ArgumentException("Policy schema version must be 1.0.", nameof(policySchemaVersion));
        RequireAcceptedReleaseCandidate=requireAcceptedReleaseCandidate; RequireCleanNarrative=requireCleanNarrative;
        RequireFullyResolvedNarrative=requireFullyResolvedNarrative; RequireFinalValidationEvidence=requireFinalValidationEvidence;
        RequireRevisionHistory=requireRevisionHistory; RequireConvergenceEvidence=requireConvergenceEvidence;
        RequireAcceptanceEvidence=requireAcceptanceEvidence;
        RequiredSections=DocumentaryProductionPackageInventory.Copy(requiredSections,nameof(requiredSections));
    }
    public bool RequireAcceptedReleaseCandidate{get;} public bool RequireCleanNarrative{get;} public bool RequireFullyResolvedNarrative{get;}
    public bool RequireFinalValidationEvidence{get;} public bool RequireRevisionHistory{get;} public bool RequireConvergenceEvidence{get;}
    public bool RequireAcceptanceEvidence{get;} public IReadOnlyList<DocumentaryProductionPackageSection> RequiredSections{get;} public string PolicySchemaVersion{get;}
}

public sealed class DocumentaryProductionPackageMetadata
{
    public DocumentaryProductionPackageMetadata(DateTimeOffset createdUtc,string createdBy,string packageSchemaVersion,string correlationId)
    { CreatedUtc=createdUtc!=default?createdUtc:throw new ArgumentException("A non-default timestamp is required.",nameof(createdUtc)); CreatedBy=Guard.Required(createdBy,nameof(createdBy)); PackageSchemaVersion=packageSchemaVersion=="1.0"?packageSchemaVersion:throw new ArgumentException("Package schema version must be 1.0.",nameof(packageSchemaVersion)); CorrelationId=Guard.Required(correlationId,nameof(correlationId)); }
    public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public string PackageSchemaVersion{get;} public string CorrelationId{get;}
}

public sealed class DocumentaryProductionPackageRequest
{
    public DocumentaryProductionPackageRequest(DocumentaryNarrativeReleaseCandidate releaseCandidate,DocumentaryProductionPackagePolicy policy,DocumentaryProductionPackageMetadata metadata)
    { ReleaseCandidate=releaseCandidate??throw new ArgumentNullException(nameof(releaseCandidate)); Policy=policy??throw new ArgumentNullException(nameof(policy)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); }
    public DocumentaryNarrativeReleaseCandidate ReleaseCandidate{get;} public DocumentaryProductionPackagePolicy Policy{get;} public DocumentaryProductionPackageMetadata Metadata{get;}
}

public sealed class DocumentaryProductionPackageManifestEntry
{
    public DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection section,string artifactType,string artifactIdentity,string artifactVersion,int sequence,bool isRequired)
    { Guard.Enum(section,nameof(section)); ArtifactType=Guard.Required(artifactType,nameof(artifactType)); ArtifactIdentity=Guard.Required(artifactIdentity,nameof(artifactIdentity)); ArtifactVersion=Guard.Required(artifactVersion,nameof(artifactVersion)); if(sequence<0)throw new ArgumentOutOfRangeException(nameof(sequence)); if(sequence!=(int)section)throw new ArgumentException("Sequence must match canonical section order.",nameof(sequence)); if(!isRequired)throw new ArgumentException("Schema 1.0 entries are required.",nameof(isRequired)); Section=section;Sequence=sequence;IsRequired=isRequired; }
    public DocumentaryProductionPackageSection Section{get;} public string ArtifactType{get;} public string ArtifactIdentity{get;} public string ArtifactVersion{get;} public int Sequence{get;} public bool IsRequired{get;}
}

public sealed class DocumentaryProductionPackageManifest
{
    public DocumentaryProductionPackageManifest(string manifestId,string packageId,IReadOnlyList<DocumentaryProductionPackageManifestEntry> entries,string manifestSchemaVersion,string correlationId)
    { ManifestId=Guard.Required(manifestId,nameof(manifestId));PackageId=Guard.Required(packageId,nameof(packageId)); if(!string.Equals(manifestId,$"{packageId}.manifest",StringComparison.Ordinal))throw new ArgumentException("Manifest identity is inconsistent.",nameof(manifestId)); ArgumentNullException.ThrowIfNull(entries); if(entries.Any(x=>x is null)||entries.Count!=6||!entries.Select(x=>x.Section).SequenceEqual(DocumentaryProductionPackageInventory.Sections)||!entries.Select(x=>x.Sequence).SequenceEqual(Enumerable.Range(0,6))||entries.Select(x=>x.Section).Distinct().Count()!=6||entries.Select(x=>x.Sequence).Distinct().Count()!=6)throw new ArgumentException("Manifest entries must be the canonical inventory.",nameof(entries)); if(entries[5].ArtifactIdentity!=manifestId||entries[5].ArtifactType!=nameof(DocumentaryProductionPackageManifest))throw new ArgumentException("Package manifest entry is inconsistent.",nameof(entries)); ManifestSchemaVersion=manifestSchemaVersion=="1.0"?manifestSchemaVersion:throw new ArgumentException("Manifest schema version must be 1.0.",nameof(manifestSchemaVersion));CorrelationId=Guard.Required(correlationId,nameof(correlationId));Entries=DocumentaryProductionPackageInventory.Copy(entries,nameof(entries)); }
    public string ManifestId{get;} public string PackageId{get;} public IReadOnlyList<DocumentaryProductionPackageManifestEntry> Entries{get;} public string ManifestSchemaVersion{get;} public string CorrelationId{get;}
}

public sealed class DocumentaryProductionPackage
{
    public DocumentaryProductionPackage(string packageId, DocumentaryNarrativeReleaseCandidate releaseCandidate,
        DocumentaryNarrativeDraft narrativeDraft, DocumentaryNarrativeDraftValidationResult finalValidationResult,
        IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> revisionCycles,
        DocumentaryNarrativeRevisionConvergenceState convergenceState, DocumentaryNarrativeAcceptanceDecision acceptanceDecision,
        DocumentaryProductionPackageManifest manifest, DocumentaryProductionPackagePolicy policy,
        DocumentaryProductionPackageMetadata metadata, IReadOnlyList<DocumentaryProductionPackageSection> includedSections)
    {
        PackageId=Guard.Required(packageId,nameof(packageId)); ReleaseCandidate=releaseCandidate??throw new ArgumentNullException(nameof(releaseCandidate));
        NarrativeDraft=narrativeDraft??throw new ArgumentNullException(nameof(narrativeDraft)); FinalValidationResult=finalValidationResult??throw new ArgumentNullException(nameof(finalValidationResult));
        ArgumentNullException.ThrowIfNull(revisionCycles); ConvergenceState=convergenceState??throw new ArgumentNullException(nameof(convergenceState)); AcceptanceDecision=acceptanceDecision??throw new ArgumentNullException(nameof(acceptanceDecision)); Manifest=manifest??throw new ArgumentNullException(nameof(manifest)); Policy=policy??throw new ArgumentNullException(nameof(policy)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata));
        DocumentaryProductionPackageInventory.ValidateSections(includedSections,nameof(includedSections));
        if(!DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(narrativeDraft,releaseCandidate.NarrativeDraft)||
           !DocumentaryNarrativeRevisionConvergenceStateValidator.ValidationResultsAreEquivalent(finalValidationResult,releaseCandidate.FinalValidationResult)||
           !DocumentaryProductionPackageValidator.ConvergenceStatesAreEquivalent(convergenceState,releaseCandidate.ConvergenceState)||
           !DocumentaryProductionPackageValidator.AcceptanceDecisionsAreEquivalent(acceptanceDecision,releaseCandidate.AcceptanceDecision)||
           !DocumentaryProductionPackageValidator.RevisionCyclesAreEquivalent(revisionCycles,convergenceState.Cycles))
            throw new ArgumentException("Certified artifacts must be deterministically equivalent.");
        DocumentaryProductionPackageValidator.ValidateComplete(packageId,releaseCandidate,manifest,metadata);
        RevisionCycles=revisionCycles; IncludedSections=DocumentaryProductionPackageInventory.Copy(includedSections,nameof(includedSections));
    }
    public string PackageId{get;} public DocumentaryNarrativeReleaseCandidate ReleaseCandidate{get;} public DocumentaryNarrativeDraft NarrativeDraft{get;} public DocumentaryNarrativeDraftValidationResult FinalValidationResult{get;} public IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> RevisionCycles{get;} public DocumentaryNarrativeRevisionConvergenceState ConvergenceState{get;} public DocumentaryNarrativeAcceptanceDecision AcceptanceDecision{get;} public DocumentaryProductionPackageManifest Manifest{get;} public DocumentaryProductionPackagePolicy Policy{get;} public DocumentaryProductionPackageMetadata Metadata{get;}
    public string OriginalDraftId=>ReleaseCandidate.OriginalDraftId; public string OriginalDraftVersion=>ReleaseCandidate.OriginalDraftVersion; public string CurrentDraftId=>ReleaseCandidate.DraftId; public string CurrentDraftVersion=>ReleaseCandidate.DraftVersion; public string ReleaseCandidateId=>ReleaseCandidate.ReleaseCandidateId; public string ConvergenceId=>ReleaseCandidate.ConvergenceId; public int CompletedCycleCount=>ReleaseCandidate.CompletedCycleCount; public int FinalFindingCount=>ReleaseCandidate.FinalFindingCount; public int UnresolvedRevisionItemCount=>RevisionCycles.Count==0?0:RevisionCycles[^1].UnresolvedRevisionItemCount; public IReadOnlyList<DocumentaryProductionPackageSection> IncludedSections{get;} public bool IsAccepted=>ReleaseCandidate.IsAccepted; public bool IsClean=>ReleaseCandidate.IsClean; public bool IsFullyResolved=>ReleaseCandidate.IsFullyResolved; public bool IsComplete=>IsAccepted&&IsClean&&IsFullyResolved&&FinalFindingCount==0&&UnresolvedRevisionItemCount==0;
}

public sealed class DocumentaryProductionPackageAssemblyResult
{
    public DocumentaryProductionPackageAssemblyResult(DocumentaryProductionPackageStatus status,IReadOnlyList<DocumentaryProductionPackageRejectionReason> rejectionReasons,DocumentaryProductionPackage? package)
    { Guard.Enum(status,nameof(status));ArgumentNullException.ThrowIfNull(rejectionReasons);if(rejectionReasons.Any(x=>!Enum.IsDefined(x))||rejectionReasons.Distinct().Count()!=rejectionReasons.Count||!rejectionReasons.SequenceEqual(rejectionReasons.OrderBy(x=>(int)x)))throw new ArgumentException("Rejection reasons must be defined, unique, and ordered.",nameof(rejectionReasons));if(status==DocumentaryProductionPackageStatus.Complete?(rejectionReasons.Count!=0||package is null):(rejectionReasons.Count==0||package is not null))throw new ArgumentException("Assembly outcome is inconsistent.");Status=status;RejectionReasons=DocumentaryProductionPackageInventory.Copy(rejectionReasons,nameof(rejectionReasons));Package=package; }
    public DocumentaryProductionPackageStatus Status{get;} public IReadOnlyList<DocumentaryProductionPackageRejectionReason> RejectionReasons{get;} public DocumentaryProductionPackage? Package{get;} public bool HasPackage=>Package is not null; public bool IsComplete=>Status==DocumentaryProductionPackageStatus.Complete; public bool IsRejected=>Status==DocumentaryProductionPackageStatus.Rejected;
}

public sealed class DocumentaryProductionPackageSummary
{
    public DocumentaryProductionPackageSummary(string packageId,string releaseCandidateId,string originalDraftId,string originalDraftVersion,string currentDraftId,string currentDraftVersion,string convergenceId,int completedCycleCount,int finalFindingCount,int unresolvedRevisionItemCount,IReadOnlyList<DocumentaryProductionPackageSection> includedSections,int manifestEntryCount,int totalAppliedChangeCount,int totalResolvedFindingCount,int totalRemainingFindingCount,int totalIntroducedFindingCount,DateTimeOffset createdUtc,string createdBy,bool isAccepted,bool isClean,bool isFullyResolved,bool isComplete)
    { PackageId=Guard.Required(packageId,nameof(packageId));ReleaseCandidateId=Guard.Required(releaseCandidateId,nameof(releaseCandidateId));OriginalDraftId=Guard.Required(originalDraftId,nameof(originalDraftId));OriginalDraftVersion=Guard.Required(originalDraftVersion,nameof(originalDraftVersion));CurrentDraftId=Guard.Required(currentDraftId,nameof(currentDraftId));CurrentDraftVersion=Guard.Required(currentDraftVersion,nameof(currentDraftVersion));ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId));if(new[]{completedCycleCount,finalFindingCount,unresolvedRevisionItemCount,manifestEntryCount,totalAppliedChangeCount,totalResolvedFindingCount,totalRemainingFindingCount,totalIntroducedFindingCount}.Any(x=>x<0))throw new ArgumentOutOfRangeException(nameof(completedCycleCount));DocumentaryProductionPackageInventory.ValidateSections(includedSections,nameof(includedSections));if(manifestEntryCount!=6||finalFindingCount!=0||unresolvedRevisionItemCount!=0||!isAccepted||!isClean||!isFullyResolved||!isComplete)throw new ArgumentException("Summary must describe a complete package.");if(createdUtc==default)throw new ArgumentException("A non-default timestamp is required.",nameof(createdUtc));CreatedBy=Guard.Required(createdBy,nameof(createdBy));CompletedCycleCount=completedCycleCount;FinalFindingCount=finalFindingCount;UnresolvedRevisionItemCount=unresolvedRevisionItemCount;IncludedSections=DocumentaryProductionPackageInventory.Copy(includedSections,nameof(includedSections));ManifestEntryCount=manifestEntryCount;TotalAppliedChangeCount=totalAppliedChangeCount;TotalResolvedFindingCount=totalResolvedFindingCount;TotalRemainingFindingCount=totalRemainingFindingCount;TotalIntroducedFindingCount=totalIntroducedFindingCount;CreatedUtc=createdUtc;IsAccepted=isAccepted;IsClean=isClean;IsFullyResolved=isFullyResolved;IsComplete=isComplete; }
    public string PackageId{get;} public string ReleaseCandidateId{get;} public string OriginalDraftId{get;} public string OriginalDraftVersion{get;} public string CurrentDraftId{get;} public string CurrentDraftVersion{get;} public string ConvergenceId{get;} public int CompletedCycleCount{get;} public int FinalFindingCount{get;} public int UnresolvedRevisionItemCount{get;} public IReadOnlyList<DocumentaryProductionPackageSection> IncludedSections{get;} public int ManifestEntryCount{get;} public int TotalAppliedChangeCount{get;} public int TotalResolvedFindingCount{get;} public int TotalRemainingFindingCount{get;} public int TotalIntroducedFindingCount{get;} public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public bool IsAccepted{get;} public bool IsClean{get;} public bool IsFullyResolved{get;} public bool IsComplete{get;}
}
