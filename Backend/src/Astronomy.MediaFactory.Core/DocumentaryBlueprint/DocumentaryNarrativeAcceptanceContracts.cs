using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryNarrativeAcceptanceStatus { Accepted, HeldForManualApproval, Rejected }
public enum DocumentaryNarrativeAcceptanceReason { ConvergedAndClean, RequiresManualApproval, CycleLimitReached, NoProgressReached, RegressionDetected, ManualReviewRequired, ValidationFindingsRemain, UnresolvedRevisionItemsRemain, NonTerminalConvergenceState, PolicyRejected }

public sealed record DocumentaryNarrativeAcceptancePolicy
{
    public DocumentaryNarrativeAcceptancePolicy(bool requireConvergedSuccessfully, bool requireCleanValidation,
        bool requireNoUnresolvedRevisionItems, bool rejectRegressionStop, bool allowManualApprovalForCycleLimit,
        bool allowManualApprovalForNoProgress, bool allowManualApprovalForRegression,
        bool allowManualApprovalForManualEscalation, string policySchemaVersion)
    {
        if (!requireConvergedSuccessfully || !requireCleanValidation || !requireNoUnresolvedRevisionItems)
            throw new ArgumentException("Policy 1.0 requires strict automatic acceptance.");
        if (rejectRegressionStop && allowManualApprovalForRegression)
            throw new ArgumentException("Regression cannot be both rejected and eligible for manual approval.");
        PolicySchemaVersion = policySchemaVersion == "1.0" ? policySchemaVersion : throw new ArgumentException("Policy schema version must be 1.0.", nameof(policySchemaVersion));
        RequireConvergedSuccessfully=requireConvergedSuccessfully; RequireCleanValidation=requireCleanValidation;
        RequireNoUnresolvedRevisionItems=requireNoUnresolvedRevisionItems; RejectRegressionStop=rejectRegressionStop;
        AllowManualApprovalForCycleLimit=allowManualApprovalForCycleLimit; AllowManualApprovalForNoProgress=allowManualApprovalForNoProgress;
        AllowManualApprovalForRegression=allowManualApprovalForRegression; AllowManualApprovalForManualEscalation=allowManualApprovalForManualEscalation;
    }
    public bool RequireConvergedSuccessfully{get;} public bool RequireCleanValidation{get;} public bool RequireNoUnresolvedRevisionItems{get;}
    public bool RejectRegressionStop{get;} public bool AllowManualApprovalForCycleLimit{get;} public bool AllowManualApprovalForNoProgress{get;}
    public bool AllowManualApprovalForRegression{get;} public bool AllowManualApprovalForManualEscalation{get;} public string PolicySchemaVersion{get;}
}

public sealed record DocumentaryNarrativeAcceptanceMetadata
{
    public DocumentaryNarrativeAcceptanceMetadata(DateTimeOffset evaluatedUtc,string evaluatedBy,string acceptanceSchemaVersion,string correlationId)
    { EvaluatedUtc=evaluatedUtc!=default?evaluatedUtc:throw new ArgumentException("A non-default timestamp is required.",nameof(evaluatedUtc)); EvaluatedBy=Guard.Required(evaluatedBy,nameof(evaluatedBy)); AcceptanceSchemaVersion=acceptanceSchemaVersion=="1.0"?acceptanceSchemaVersion:throw new ArgumentException("Acceptance schema version must be 1.0.",nameof(acceptanceSchemaVersion)); CorrelationId=Guard.Required(correlationId,nameof(correlationId)); }
    public DateTimeOffset EvaluatedUtc{get;} public string EvaluatedBy{get;} public string AcceptanceSchemaVersion{get;} public string CorrelationId{get;}
}
public sealed record DocumentaryNarrativeReleaseCandidateMetadata
{
    public DocumentaryNarrativeReleaseCandidateMetadata(DateTimeOffset createdUtc,string createdBy,string releaseCandidateSchemaVersion,string correlationId)
    { CreatedUtc=createdUtc!=default?createdUtc:throw new ArgumentException("A non-default timestamp is required.",nameof(createdUtc)); CreatedBy=Guard.Required(createdBy,nameof(createdBy)); ReleaseCandidateSchemaVersion=releaseCandidateSchemaVersion=="1.0"?releaseCandidateSchemaVersion:throw new ArgumentException("Release candidate schema version must be 1.0.",nameof(releaseCandidateSchemaVersion)); CorrelationId=Guard.Required(correlationId,nameof(correlationId)); }
    public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public string ReleaseCandidateSchemaVersion{get;} public string CorrelationId{get;}
}
public sealed class DocumentaryNarrativeAcceptanceRequest
{
    public DocumentaryNarrativeAcceptanceRequest(DocumentaryNarrativeRevisionConvergenceState convergenceState,DocumentaryNarrativeAcceptancePolicy policy,DocumentaryNarrativeAcceptanceMetadata metadata)
    { ConvergenceState=convergenceState??throw new ArgumentNullException(nameof(convergenceState)); Policy=policy??throw new ArgumentNullException(nameof(policy)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); }
    public DocumentaryNarrativeRevisionConvergenceState ConvergenceState{get;} public DocumentaryNarrativeAcceptancePolicy Policy{get;} public DocumentaryNarrativeAcceptanceMetadata Metadata{get;}
}
public sealed class DocumentaryNarrativeAcceptanceDecision
{
    public DocumentaryNarrativeAcceptanceDecision(string convergenceId,DocumentaryNarrativeAcceptanceStatus status,DocumentaryNarrativeAcceptanceReason primaryReason,IReadOnlyList<DocumentaryNarrativeAcceptanceReason> supportingReasons,string currentDraftId,string currentDraftVersion,int currentFindingCount,int completedCycleCount,int unresolvedRevisionItemCount,DocumentaryNarrativeAcceptancePolicy policy,DocumentaryNarrativeAcceptanceMetadata metadata)
    { ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId)); Guard.Enum(status,nameof(status)); Guard.Enum(primaryReason,nameof(primaryReason)); ArgumentNullException.ThrowIfNull(supportingReasons); if(supportingReasons.Any(x=>!Enum.IsDefined(x))||supportingReasons.Distinct().Count()!=supportingReasons.Count||supportingReasons.Contains(primaryReason))throw new ArgumentException("Supporting reasons must be defined, unique, and exclude the primary reason.",nameof(supportingReasons)); CurrentDraftId=Guard.Required(currentDraftId,nameof(currentDraftId)); CurrentDraftVersion=Guard.Required(currentDraftVersion,nameof(currentDraftVersion)); if(currentFindingCount<0||completedCycleCount<0||unresolvedRevisionItemCount<0)throw new ArgumentOutOfRangeException(nameof(currentFindingCount)); ValidateDecision(status,primaryReason,supportingReasons,currentFindingCount,unresolvedRevisionItemCount); SupportingReasons=new ReadOnlyCollection<DocumentaryNarrativeAcceptanceReason>(supportingReasons.ToArray()); Status=status; PrimaryReason=primaryReason; CurrentFindingCount=currentFindingCount; CompletedCycleCount=completedCycleCount; UnresolvedRevisionItemCount=unresolvedRevisionItemCount; Policy=policy??throw new ArgumentNullException(nameof(policy)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); }

    private static void ValidateDecision(DocumentaryNarrativeAcceptanceStatus status, DocumentaryNarrativeAcceptanceReason primaryReason,
        IReadOnlyList<DocumentaryNarrativeAcceptanceReason> supportingReasons, int findingCount, int unresolvedCount)
    {
        var validPrimary = status switch
        {
            DocumentaryNarrativeAcceptanceStatus.Accepted => primaryReason == DocumentaryNarrativeAcceptanceReason.ConvergedAndClean && findingCount == 0 && unresolvedCount == 0,
            DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval => primaryReason is DocumentaryNarrativeAcceptanceReason.CycleLimitReached or DocumentaryNarrativeAcceptanceReason.NoProgressReached or DocumentaryNarrativeAcceptanceReason.RegressionDetected or DocumentaryNarrativeAcceptanceReason.ManualReviewRequired or DocumentaryNarrativeAcceptanceReason.RequiresManualApproval,
            DocumentaryNarrativeAcceptanceStatus.Rejected => primaryReason is DocumentaryNarrativeAcceptanceReason.RegressionDetected or DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState or DocumentaryNarrativeAcceptanceReason.PolicyRejected or DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain or DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain,
            _ => false
        };
        var contradictorySupport = supportingReasons.Any(reason => status switch
        {
            DocumentaryNarrativeAcceptanceStatus.Accepted => true,
            DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval => reason is DocumentaryNarrativeAcceptanceReason.ConvergedAndClean or DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState or DocumentaryNarrativeAcceptanceReason.PolicyRejected,
            DocumentaryNarrativeAcceptanceStatus.Rejected => reason is DocumentaryNarrativeAcceptanceReason.ConvergedAndClean or DocumentaryNarrativeAcceptanceReason.RequiresManualApproval,
            _ => true
        });
        if (!validPrimary || contradictorySupport)
            throw new ArgumentException("Acceptance status, reason, and evidence are inconsistent.");
    }
    public string ConvergenceId{get;} public DocumentaryNarrativeAcceptanceStatus Status{get;} public DocumentaryNarrativeAcceptanceReason PrimaryReason{get;} public IReadOnlyList<DocumentaryNarrativeAcceptanceReason> SupportingReasons{get;} public string CurrentDraftId{get;} public string CurrentDraftVersion{get;} public int CurrentFindingCount{get;} public int CompletedCycleCount{get;} public int UnresolvedRevisionItemCount{get;} public bool IsEligibleForReleaseCandidate=>Status==DocumentaryNarrativeAcceptanceStatus.Accepted; public bool RequiresManualApproval=>Status==DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval; public bool IsRejected=>Status==DocumentaryNarrativeAcceptanceStatus.Rejected; public DocumentaryNarrativeAcceptancePolicy Policy{get;} public DocumentaryNarrativeAcceptanceMetadata Metadata{get;}
}
public sealed class DocumentaryNarrativeReleaseCandidate
{
    public DocumentaryNarrativeReleaseCandidate(string releaseCandidateId,DocumentaryNarrativeDraft narrativeDraft,DocumentaryNarrativeDraftValidationResult finalValidationResult,DocumentaryNarrativeAcceptanceDecision acceptanceDecision,DocumentaryNarrativeRevisionConvergenceState convergenceState,DocumentaryNarrativeReleaseCandidateMetadata metadata)
    { ReleaseCandidateId=Guard.Required(releaseCandidateId,nameof(releaseCandidateId)); NarrativeDraft=narrativeDraft??throw new ArgumentNullException(nameof(narrativeDraft)); FinalValidationResult=finalValidationResult??throw new ArgumentNullException(nameof(finalValidationResult)); AcceptanceDecision=acceptanceDecision??throw new ArgumentNullException(nameof(acceptanceDecision)); ConvergenceState=convergenceState??throw new ArgumentNullException(nameof(convergenceState)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); DocumentaryNarrativeReleaseCandidateValidator.Validate(this); }
    public string ReleaseCandidateId{get;} public DocumentaryNarrativeDraft NarrativeDraft{get;} public string DraftId=>NarrativeDraft.DraftId; public string DraftVersion=>NarrativeDraft.Version; public string OriginalDraftId=>ConvergenceState.OriginalDraftId; public string OriginalDraftVersion=>ConvergenceState.OriginalDraftVersion; public string ConvergenceId=>ConvergenceState.ConvergenceId; public int CompletedCycleCount=>ConvergenceState.CompletedCycleCount; public DocumentaryNarrativeDraftValidationResult FinalValidationResult{get;} public int FinalFindingCount=>FinalValidationResult.Findings.Count; public DocumentaryNarrativeAcceptanceDecision AcceptanceDecision{get;} public DocumentaryNarrativeRevisionConvergenceState ConvergenceState{get;} public DocumentaryNarrativeReleaseCandidateMetadata Metadata{get;} public bool IsClean=>FinalFindingCount==0; public bool IsFullyResolved=>ConvergenceState.Cycles.Count==0||ConvergenceState.Cycles[^1].UnresolvedRevisionItemCount==0; public bool IsAccepted=>AcceptanceDecision.Status==DocumentaryNarrativeAcceptanceStatus.Accepted;
}
public sealed class DocumentaryNarrativeAcceptanceResult
{
    public DocumentaryNarrativeAcceptanceResult(DocumentaryNarrativeAcceptanceDecision decision,DocumentaryNarrativeReleaseCandidate? releaseCandidate)
    { Decision=decision??throw new ArgumentNullException(nameof(decision)); if((decision.Status==DocumentaryNarrativeAcceptanceStatus.Accepted)!=(releaseCandidate is not null))throw new ArgumentException("Only accepted decisions require a release candidate.",nameof(releaseCandidate)); ReleaseCandidate=releaseCandidate; }
    public DocumentaryNarrativeAcceptanceDecision Decision{get;} public DocumentaryNarrativeReleaseCandidate? ReleaseCandidate{get;} public bool HasReleaseCandidate=>ReleaseCandidate is not null;
}
public sealed class DocumentaryNarrativeReleaseCandidateSummary
{
    public DocumentaryNarrativeReleaseCandidateSummary(string releaseCandidateId,string draftId,string draftVersion,string originalDraftId,string originalDraftVersion,string convergenceId,int completedCycleCount,int finalFindingCount,int totalAppliedChangeCount,int totalResolvedFindingCount,int totalIntroducedFindingCount,IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus> cycleStatuses,IReadOnlyList<int> findingCountHistory,DateTimeOffset acceptedUtc,string acceptedBy,bool isClean,bool isFullyResolved)
    { ReleaseCandidateId=Guard.Required(releaseCandidateId,nameof(releaseCandidateId)); DraftId=Guard.Required(draftId,nameof(draftId)); DraftVersion=Guard.Required(draftVersion,nameof(draftVersion)); OriginalDraftId=Guard.Required(originalDraftId,nameof(originalDraftId)); OriginalDraftVersion=Guard.Required(originalDraftVersion,nameof(originalDraftVersion)); ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId)); AcceptedBy=Guard.Required(acceptedBy,nameof(acceptedBy)); if(acceptedUtc==default)throw new ArgumentException("A non-default timestamp is required.",nameof(acceptedUtc)); if(new[]{completedCycleCount,finalFindingCount,totalAppliedChangeCount,totalResolvedFindingCount,totalIntroducedFindingCount}.Any(x=>x<0))throw new ArgumentOutOfRangeException(nameof(completedCycleCount)); ArgumentNullException.ThrowIfNull(cycleStatuses); ArgumentNullException.ThrowIfNull(findingCountHistory); if(cycleStatuses.Any(x=>!Enum.IsDefined(x))||findingCountHistory.Any(x=>x<0))throw new ArgumentException("Histories contain invalid values."); if(cycleStatuses.Count!=completedCycleCount||findingCountHistory.Count!=completedCycleCount+1||findingCountHistory[^1]!=finalFindingCount)throw new ArgumentException("Histories must match cycle and finding counts."); if(finalFindingCount!=0||!isClean||!isFullyResolved)throw new ArgumentException("An accepted release summary must be clean and fully resolved."); CompletedCycleCount=completedCycleCount; FinalFindingCount=finalFindingCount; TotalAppliedChangeCount=totalAppliedChangeCount; TotalResolvedFindingCount=totalResolvedFindingCount; TotalIntroducedFindingCount=totalIntroducedFindingCount; CycleStatuses=new ReadOnlyCollection<DocumentaryNarrativeRevisionCycleStatus>(cycleStatuses.ToArray()); FindingCountHistory=new ReadOnlyCollection<int>(findingCountHistory.ToArray()); AcceptedUtc=acceptedUtc; IsClean=isClean; IsFullyResolved=isFullyResolved; }
    public string ReleaseCandidateId{get;} public string DraftId{get;} public string DraftVersion{get;} public string OriginalDraftId{get;} public string OriginalDraftVersion{get;} public string ConvergenceId{get;} public int CompletedCycleCount{get;} public int FinalFindingCount{get;} public int TotalAppliedChangeCount{get;} public int TotalResolvedFindingCount{get;} public int TotalIntroducedFindingCount{get;} public IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus> CycleStatuses{get;} public IReadOnlyList<int> FindingCountHistory{get;} public DateTimeOffset AcceptedUtc{get;} public string AcceptedBy{get;} public bool IsClean{get;} public bool IsFullyResolved{get;}
}
