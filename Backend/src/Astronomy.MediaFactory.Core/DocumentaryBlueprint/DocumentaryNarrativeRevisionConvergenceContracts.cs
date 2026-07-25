using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryNarrativeRevisionConvergenceStatus
{
    NotStarted,
    InProgress,
    ConvergedSuccessfully,
    StoppedByCycleLimit,
    StoppedByNoProgress,
    StoppedByRegression,
    RequiresManualEscalation
}

public enum DocumentaryNarrativeRevisionConvergenceNextAction
{
    None,
    PlanNextRevisionCycle,
    ObtainExternalRevisionSubmission,
    PerformManualReview,
    AcceptCurrentDraft,
    TerminateRevisionProcess
}

public sealed record DocumentaryNarrativeRevisionConvergencePolicy
{
    public DocumentaryNarrativeRevisionConvergencePolicy(int maximumCycleCount, bool stopOnRegression,
        int maximumConsecutiveNoProgressCycles, bool requireCleanValidationForSuccess,
        bool requireNoUnresolvedRevisionItemsForSuccess, string policySchemaVersion)
    {
        if (maximumCycleCount < 1) throw new ArgumentOutOfRangeException(nameof(maximumCycleCount));
        if (maximumConsecutiveNoProgressCycles < 1) throw new ArgumentOutOfRangeException(nameof(maximumConsecutiveNoProgressCycles));
        if (!requireCleanValidationForSuccess) throw new ArgumentException("Policy 1.0 requires clean validation for success.", nameof(requireCleanValidationForSuccess));
        if (!requireNoUnresolvedRevisionItemsForSuccess) throw new ArgumentException("Policy 1.0 requires no unresolved revision items for success.", nameof(requireNoUnresolvedRevisionItemsForSuccess));
        MaximumCycleCount = maximumCycleCount;
        StopOnRegression = stopOnRegression;
        MaximumConsecutiveNoProgressCycles = maximumConsecutiveNoProgressCycles;
        RequireCleanValidationForSuccess = requireCleanValidationForSuccess;
        RequireNoUnresolvedRevisionItemsForSuccess = requireNoUnresolvedRevisionItemsForSuccess;
        PolicySchemaVersion = policySchemaVersion == "1.0" ? policySchemaVersion : throw new ArgumentException("Policy schema version must be 1.0.", nameof(policySchemaVersion));
    }
    public int MaximumCycleCount { get; }
    public bool StopOnRegression { get; }
    public int MaximumConsecutiveNoProgressCycles { get; }
    public bool RequireCleanValidationForSuccess { get; }
    public bool RequireNoUnresolvedRevisionItemsForSuccess { get; }
    public string PolicySchemaVersion { get; }
}

public sealed record DocumentaryNarrativeRevisionConvergenceMetadata
{
    public DocumentaryNarrativeRevisionConvergenceMetadata(DateTimeOffset createdUtc, string createdBy,
        string convergenceSchemaVersion, string correlationId)
    {
        CreatedUtc = createdUtc != default ? createdUtc : throw new ArgumentException("A non-default creation timestamp is required.", nameof(createdUtc));
        CreatedBy = Guard.Required(createdBy, nameof(createdBy));
        ConvergenceSchemaVersion = convergenceSchemaVersion == "1.0" ? convergenceSchemaVersion : throw new ArgumentException("Convergence schema version must be 1.0.", nameof(convergenceSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc { get; }
    public string CreatedBy { get; }
    public string ConvergenceSchemaVersion { get; }
    public string CorrelationId { get; }
}

public sealed class DocumentaryNarrativeRevisionConvergenceState
{
    public DocumentaryNarrativeRevisionConvergenceState(string convergenceId, DocumentaryNarrativeDraft originalDraft,
        DocumentaryNarrativeDraftValidationResult initialValidationResult, DocumentaryNarrativeDraft currentDraft,
        DocumentaryNarrativeDraftValidationResult currentValidationResult, IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> cycles,
        DocumentaryNarrativeRevisionConvergencePolicy policy, DocumentaryNarrativeRevisionConvergenceMetadata metadata,
        DocumentaryNarrativeRevisionConvergenceStatus status, DocumentaryNarrativeRevisionConvergenceNextAction nextAction,
        int consecutiveNoProgressCycleCount)
    {
        ConvergenceId = Guard.Required(convergenceId, nameof(convergenceId));
        OriginalDraft = originalDraft ?? throw new ArgumentNullException(nameof(originalDraft));
        InitialValidationResult = initialValidationResult ?? throw new ArgumentNullException(nameof(initialValidationResult));
        CurrentDraft = currentDraft ?? throw new ArgumentNullException(nameof(currentDraft));
        CurrentValidationResult = currentValidationResult ?? throw new ArgumentNullException(nameof(currentValidationResult));
        Cycles = Copy(cycles, nameof(cycles)); Policy = policy ?? throw new ArgumentNullException(nameof(policy)); Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Guard.Enum(status, nameof(status)); Guard.Enum(nextAction, nameof(nextAction));
        if (consecutiveNoProgressCycleCount < 0) throw new ArgumentOutOfRangeException(nameof(consecutiveNoProgressCycleCount));
        Status = status; NextAction = nextAction; ConsecutiveNoProgressCycleCount = consecutiveNoProgressCycleCount;
    }
    private static IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> Copy(IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> values, string name)
    { ArgumentNullException.ThrowIfNull(values, name); if (values.Any(x => x is null)) throw new ArgumentException("Cycles cannot contain null elements.", name); return new ReadOnlyCollection<DocumentaryNarrativeRevisionCycleResult>(values.ToArray()); }
    public string ConvergenceId { get; }
    public DocumentaryNarrativeDraft OriginalDraft { get; }
    public string OriginalDraftId => OriginalDraft.DraftId;
    public string OriginalDraftVersion => OriginalDraft.Version;
    public DocumentaryNarrativeDraftValidationResult InitialValidationResult { get; }
    public int InitialFindingCount => InitialValidationResult.Findings.Count;
    public DocumentaryNarrativeDraft CurrentDraft { get; }
    public string CurrentDraftId => CurrentDraft.DraftId;
    public string CurrentDraftVersion => CurrentDraft.Version;
    public DocumentaryNarrativeDraftValidationResult CurrentValidationResult { get; }
    public int CurrentFindingCount => CurrentValidationResult.Findings.Count;
    public IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> Cycles { get; }
    public DocumentaryNarrativeRevisionConvergencePolicy Policy { get; }
    public DocumentaryNarrativeRevisionConvergenceMetadata Metadata { get; }
    public DocumentaryNarrativeRevisionConvergenceStatus Status { get; }
    public DocumentaryNarrativeRevisionConvergenceNextAction NextAction { get; }
    public int CompletedCycleCount => Cycles.Count;
    public int TotalAppliedChangeCount => Cycles.Sum(x => x.AppliedChangeCount);
    public int TotalResolvedFindingCount => Cycles.Sum(x => x.ValidationComparison.ResolvedFindingCount);
    public int TotalIntroducedFindingCount => Cycles.Sum(x => x.ValidationComparison.IntroducedFindingCount);
    public int ConsecutiveNoProgressCycleCount { get; }
    public bool HasImprovedFromInitial => CurrentFindingCount < InitialFindingCount;
    public bool HasRegressedFromInitial => CurrentFindingCount > InitialFindingCount;
    public bool IsClean => CurrentFindingCount == 0;
    public bool RequiresAnotherCycle => Status is DocumentaryNarrativeRevisionConvergenceStatus.NotStarted or DocumentaryNarrativeRevisionConvergenceStatus.InProgress;
    public bool RequiresManualEscalation => Status == DocumentaryNarrativeRevisionConvergenceStatus.RequiresManualEscalation;
}

public sealed class DocumentaryNarrativeRevisionConvergenceAdvanceRequest
{
    public DocumentaryNarrativeRevisionConvergenceAdvanceRequest(DocumentaryNarrativeRevisionConvergenceState currentState,
        DocumentaryNarrativeRevisionCycleResult completedCycleResult, DateTimeOffset advancedUtc, string advancedBy,
        string advanceSchemaVersion, string correlationId)
    {
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        CompletedCycleResult = completedCycleResult ?? throw new ArgumentNullException(nameof(completedCycleResult));
        AdvancedUtc = advancedUtc != default ? advancedUtc : throw new ArgumentException("A non-default advancement timestamp is required.", nameof(advancedUtc));
        AdvancedBy = Guard.Required(advancedBy, nameof(advancedBy));
        AdvanceSchemaVersion = advanceSchemaVersion == "1.0" ? advanceSchemaVersion : throw new ArgumentException("Advance schema version must be 1.0.", nameof(advanceSchemaVersion));
        CorrelationId = Guard.Required(correlationId, nameof(correlationId));
    }
    public DocumentaryNarrativeRevisionConvergenceState CurrentState { get; }
    public DocumentaryNarrativeRevisionCycleResult CompletedCycleResult { get; }
    public DateTimeOffset AdvancedUtc { get; }
    public string AdvancedBy { get; }
    public string AdvanceSchemaVersion { get; }
    public string CorrelationId { get; }
}

public sealed class DocumentaryNarrativeRevisionConvergenceSummary
{
    public DocumentaryNarrativeRevisionConvergenceSummary(string convergenceId, string originalDraftId, string originalDraftVersion,
        string currentDraftId, string currentDraftVersion, int initialFindingCount, int currentFindingCount,
        int completedCycleCount, int totalAppliedChangeCount, int totalResolvedFindingCount, int totalRemainingFindingCount,
        int totalIntroducedFindingCount, IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus> cycleStatuses,
        IReadOnlyList<int> findingCountHistory, IReadOnlyList<int> appliedChangeCountHistory,
        IReadOnlyList<int> unresolvedRevisionItemCountHistory, bool hasImproved, bool hasRegressed, bool isClean)
    {
        ConvergenceId=Guard.Required(convergenceId,nameof(convergenceId)); OriginalDraftId=Guard.Required(originalDraftId,nameof(originalDraftId)); OriginalDraftVersion=Guard.Required(originalDraftVersion,nameof(originalDraftVersion)); CurrentDraftId=Guard.Required(currentDraftId,nameof(currentDraftId)); CurrentDraftVersion=Guard.Required(currentDraftVersion,nameof(currentDraftVersion));
        InitialFindingCount=initialFindingCount; CurrentFindingCount=currentFindingCount; CompletedCycleCount=completedCycleCount; TotalAppliedChangeCount=totalAppliedChangeCount; TotalResolvedFindingCount=totalResolvedFindingCount; TotalRemainingFindingCount=totalRemainingFindingCount; TotalIntroducedFindingCount=totalIntroducedFindingCount;
        CycleStatuses=Copy(cycleStatuses,nameof(cycleStatuses)); FindingCountHistory=Copy(findingCountHistory,nameof(findingCountHistory)); AppliedChangeCountHistory=Copy(appliedChangeCountHistory,nameof(appliedChangeCountHistory)); UnresolvedRevisionItemCountHistory=Copy(unresolvedRevisionItemCountHistory,nameof(unresolvedRevisionItemCountHistory)); HasImproved=hasImproved; HasRegressed=hasRegressed; IsClean=isClean;
    }
    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values,string name) { ArgumentNullException.ThrowIfNull(values,name); return new ReadOnlyCollection<T>(values.ToArray()); }
    public string ConvergenceId{get;} public string OriginalDraftId{get;} public string OriginalDraftVersion{get;} public string CurrentDraftId{get;} public string CurrentDraftVersion{get;}
    public int InitialFindingCount{get;} public int CurrentFindingCount{get;} public int CompletedCycleCount{get;} public int TotalAppliedChangeCount{get;} public int TotalResolvedFindingCount{get;} public int TotalRemainingFindingCount{get;} public int TotalIntroducedFindingCount{get;}
    public IReadOnlyList<DocumentaryNarrativeRevisionCycleStatus> CycleStatuses{get;} public IReadOnlyList<int> FindingCountHistory{get;} public IReadOnlyList<int> AppliedChangeCountHistory{get;} public IReadOnlyList<int> UnresolvedRevisionItemCountHistory{get;}
    public bool HasImproved{get;} public bool HasRegressed{get;} public bool IsClean{get;}
}
