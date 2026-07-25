using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryNarrativeRevisionCycleStatus
{
    NoRevisionRequired,
    AwaitingExternalRevision,
    PartiallyCompleted,
    CompletedWithRemainingFindings,
    CompletedSuccessfully
}

public sealed record DocumentaryNarrativeRevisionCycleMetadata
{
    public DocumentaryNarrativeRevisionCycleMetadata(DateTimeOffset createdUtc, string createdBy, string cycleSchemaVersion, string correlationId)
    {
        CreatedUtc = createdUtc != default ? createdUtc : throw new ArgumentException("A non-default creation timestamp is required.", nameof(createdUtc));
        CreatedBy = Guard.Required(createdBy, nameof(createdBy));
        CycleSchemaVersion = cycleSchemaVersion == "1.0" ? cycleSchemaVersion : throw new ArgumentException("Cycle schema version must be 1.0.", nameof(cycleSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc { get; }
    public string CreatedBy { get; }
    public string CycleSchemaVersion { get; }
    public string CorrelationId { get; }
}

public sealed class DocumentaryNarrativeRevisionCyclePlan
{
    public DocumentaryNarrativeRevisionCyclePlan(string cycleId, DocumentaryNarrativeDraft sourceDraft,
        DocumentaryNarrativeDraftValidationResult sourceValidationResult, DocumentaryNarrativeRevisionRequest revisionRequest,
        DocumentaryNarrativeRevisionWorkPackage workPackage, DocumentaryNarrativeRevisionCycleMetadata metadata,
        DocumentaryNarrativeRevisionCycleStatus status)
    {
        CycleId = Guard.Required(cycleId, nameof(cycleId));
        SourceDraft = sourceDraft ?? throw new ArgumentNullException(nameof(sourceDraft));
        SourceValidationResult = sourceValidationResult ?? throw new ArgumentNullException(nameof(sourceValidationResult));
        RevisionRequest = revisionRequest ?? throw new ArgumentNullException(nameof(revisionRequest));
        WorkPackage = workPackage ?? throw new ArgumentNullException(nameof(workPackage));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Guard.Enum(status, nameof(status));
        if (status is not (DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired or DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision))
            throw new ArgumentException("A plan must have a planning status.", nameof(status));
        if (!Eq(SourceDraft.DraftId, SourceValidationResult.DraftId) || !Eq(SourceDraft.DraftId, RevisionRequest.DraftId) ||
            !Eq(SourceDraft.Version, RevisionRequest.DraftVersion) || !Eq(RevisionRequest.RevisionRequestId, WorkPackage.RevisionRequestId) ||
            !Eq(SourceDraft.DraftId, WorkPackage.DraftId) || !Eq(SourceDraft.Version, WorkPackage.DraftVersion))
            throw new ArgumentException("Cycle plan lineage must match exactly.");
        var expected = SourceValidationResult.Findings.Count == 0 ? DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired : DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision;
        if (status != expected) throw new ArgumentException("Plan status is inconsistent with source validation.", nameof(status));
        Status = status;
    }
    private static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    public string CycleId { get; }
    public DocumentaryNarrativeDraft SourceDraft { get; }
    public string SourceDraftId => SourceDraft.DraftId;
    public string SourceDraftVersion => SourceDraft.Version;
    public DocumentaryNarrativeDraftValidationResult SourceValidationResult { get; }
    public DocumentaryNarrativeRevisionRequest RevisionRequest { get; }
    public DocumentaryNarrativeRevisionWorkPackage WorkPackage { get; }
    public DocumentaryNarrativeRevisionCycleMetadata Metadata { get; }
    public DocumentaryNarrativeRevisionCycleStatus Status { get; }
    public int SourceFindingCount => SourceValidationResult.Findings.Count;
    public int RevisionItemCount => RevisionRequest.Items.Count;
    public int PassageWorkItemCount => WorkPackage.PassageWorkItems.Count;
    public int ManualReviewWorkItemCount => WorkPackage.ManualReviewWorkItems.Count;
    public bool RequiresExternalRevision => RevisionItemCount > 0;
}

public sealed class DocumentaryNarrativeRevisionCycleCompletionRequest
{
    public DocumentaryNarrativeRevisionCycleCompletionRequest(DocumentaryNarrativeRevisionCyclePlan plan,
        DocumentaryNarrativeRevisionSubmission submission, DocumentaryNarrativeRevisionMetadata revisionMetadata,
        DateTimeOffset completedUtc, string completedBy, string completionSchemaVersion, string correlationId)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Submission = submission ?? throw new ArgumentNullException(nameof(submission));
        RevisionMetadata = revisionMetadata ?? throw new ArgumentNullException(nameof(revisionMetadata));
        CompletedUtc = completedUtc != default ? completedUtc : throw new ArgumentException("A non-default completion timestamp is required.", nameof(completedUtc));
        CompletedBy = Guard.Required(completedBy, nameof(completedBy));
        CompletionSchemaVersion = completionSchemaVersion == "1.0" ? completionSchemaVersion : throw new ArgumentException("Completion schema version must be 1.0.", nameof(completionSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DocumentaryNarrativeRevisionCyclePlan Plan { get; }
    public DocumentaryNarrativeRevisionSubmission Submission { get; }
    public DocumentaryNarrativeRevisionMetadata RevisionMetadata { get; }
    public DateTimeOffset CompletedUtc { get; }
    public string CompletedBy { get; }
    public string CompletionSchemaVersion { get; }
    public string CorrelationId { get; }
}

public sealed record DocumentaryNarrativeRevisionValidationComparison
{
    public DocumentaryNarrativeRevisionValidationComparison(int sourceFindingCount, int revisedFindingCount,
        int resolvedFindingCount, int remainingFindingCount, int introducedFindingCount,
        IReadOnlyList<string> sourceRuleCodes, IReadOnlyList<string> revisedRuleCodes,
        IReadOnlyList<string> resolvedRuleCodes, IReadOnlyList<string> remainingRuleCodes,
        IReadOnlyList<string> introducedRuleCodes, bool hasImproved, bool hasRegressed, bool isClean)
    {
        if (sourceFindingCount < 0 || revisedFindingCount < 0 || resolvedFindingCount < 0 || remainingFindingCount < 0 || introducedFindingCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceFindingCount));
        SourceFindingCount = sourceFindingCount; RevisedFindingCount = revisedFindingCount;
        ResolvedFindingCount = resolvedFindingCount; RemainingFindingCount = remainingFindingCount; IntroducedFindingCount = introducedFindingCount;
        SourceRuleCodes = Copy(sourceRuleCodes, nameof(sourceRuleCodes)); RevisedRuleCodes = Copy(revisedRuleCodes, nameof(revisedRuleCodes));
        ResolvedRuleCodes = Copy(resolvedRuleCodes, nameof(resolvedRuleCodes)); RemainingRuleCodes = Copy(remainingRuleCodes, nameof(remainingRuleCodes));
        IntroducedRuleCodes = Copy(introducedRuleCodes, nameof(introducedRuleCodes));
        if (SourceRuleCodes.Count != sourceFindingCount || RevisedRuleCodes.Count != revisedFindingCount ||
            ResolvedRuleCodes.Count != resolvedFindingCount || RemainingRuleCodes.Count != remainingFindingCount || IntroducedRuleCodes.Count != introducedFindingCount)
            throw new ArgumentException("Finding counts and rule-code summaries must align.");
        HasImproved = hasImproved; HasRegressed = hasRegressed; IsClean = isClean;
    }
    private static IReadOnlyList<string> Copy(IReadOnlyList<string> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        if (values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Rule codes must be nonblank.", name);
        return new ReadOnlyCollection<string>(values.ToArray());
    }
    public int SourceFindingCount { get; }
    public int RevisedFindingCount { get; }
    public int ResolvedFindingCount { get; }
    public int RemainingFindingCount { get; }
    public int IntroducedFindingCount { get; }
    public IReadOnlyList<string> SourceRuleCodes { get; }
    public IReadOnlyList<string> RevisedRuleCodes { get; }
    public IReadOnlyList<string> ResolvedRuleCodes { get; }
    public IReadOnlyList<string> RemainingRuleCodes { get; }
    public IReadOnlyList<string> IntroducedRuleCodes { get; }
    public bool HasImproved { get; }
    public bool HasRegressed { get; }
    public bool IsClean { get; }
}

public sealed class DocumentaryNarrativeRevisionCycleResult
{
    public DocumentaryNarrativeRevisionCycleResult(DocumentaryNarrativeRevisionCyclePlan plan, DocumentaryNarrativeRevisionSubmission submission,
        DocumentaryNarrativeRevisionBindingRequest bindingRequest, DocumentaryNarrativeRevisionResult revisionResult,
        DocumentaryNarrativeDraftValidationResult revisedValidationResult, DocumentaryNarrativeRevisionValidationComparison validationComparison,
        DateTimeOffset completedUtc, string completedBy, string completionSchemaVersion, string correlationId,
        DocumentaryNarrativeRevisionCycleStatus status)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan)); Submission = submission ?? throw new ArgumentNullException(nameof(submission));
        BindingRequest = bindingRequest ?? throw new ArgumentNullException(nameof(bindingRequest)); RevisionResult = revisionResult ?? throw new ArgumentNullException(nameof(revisionResult));
        RevisedValidationResult = revisedValidationResult ?? throw new ArgumentNullException(nameof(revisedValidationResult)); ValidationComparison = validationComparison ?? throw new ArgumentNullException(nameof(validationComparison));
        CompletedUtc = completedUtc != default ? completedUtc : throw new ArgumentException("A non-default completion timestamp is required.", nameof(completedUtc));
        CompletedBy = Guard.Required(completedBy, nameof(completedBy)); CompletionSchemaVersion = completionSchemaVersion == "1.0" ? completionSchemaVersion : throw new ArgumentException("Completion schema version must be 1.0.", nameof(completionSchemaVersion)); CorrelationId = Guard.Required(correlationId, nameof(correlationId));
        Guard.Enum(status, nameof(status)); if (status == DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision) throw new ArgumentException("A completed result cannot await revision.", nameof(status)); Status = status;
    }
    public string CycleId => Plan.CycleId;
    public DocumentaryNarrativeRevisionCyclePlan Plan { get; }
    public DocumentaryNarrativeRevisionSubmission Submission { get; }
    public DocumentaryNarrativeRevisionBindingRequest BindingRequest { get; }
    public DocumentaryNarrativeRevisionResult RevisionResult { get; }
    public DocumentaryNarrativeDraftValidationResult RevisedValidationResult { get; }
    public DocumentaryNarrativeRevisionValidationComparison ValidationComparison { get; }
    public DateTimeOffset CompletedUtc { get; }
    public string CompletedBy { get; }
    public string CompletionSchemaVersion { get; }
    public string CorrelationId { get; }
    public DocumentaryNarrativeRevisionCycleStatus Status { get; }
    public string SourceDraftId => Plan.SourceDraftId;
    public string SourceDraftVersion => Plan.SourceDraftVersion;
    public string TargetDraftId => RevisionResult.TargetDraftId;
    public string TargetDraftVersion => RevisionResult.TargetDraftVersion;
    public int AppliedChangeCount => RevisionResult.AppliedChangeCount;
    public int UnresolvedRevisionItemCount => RevisionResult.UnresolvedItemCount;
    public int SourceFindingCount => Plan.SourceFindingCount;
    public int RevisedFindingCount => RevisedValidationResult.Findings.Count;
}
