namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceAdvancer
{
    public DocumentaryNarrativeRevisionConvergenceState Advance(DocumentaryNarrativeRevisionConvergenceAdvanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = request.CurrentState;
        ValidateState(state);
        if (!state.RequiresAnotherCycle) throw new InvalidOperationException("A terminal convergence state cannot be advanced.");
        var cycle = request.CompletedCycleResult;
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Eq(cycle.SourceDraftId, state.CurrentDraftId) || !Eq(cycle.SourceDraftVersion, state.CurrentDraftVersion))
            throw new ArgumentException("The cycle source must match the current draft exactly.", nameof(request));
        if (!ReferenceEquals(cycle.Plan.SourceDraft, state.CurrentDraft))
            throw new ArgumentException("The cycle must retain the current source draft.", nameof(request));
        if (state.Cycles.Any(x => Eq(x.CycleId, cycle.CycleId))) throw new ArgumentException("The cycle has already been appended.", nameof(request));
        var correlation = state.Metadata.CorrelationId;
        if (!new[] { request.CorrelationId, cycle.CorrelationId, cycle.Plan.Metadata.CorrelationId,
                cycle.Submission.Metadata.CorrelationId, cycle.BindingRequest.Metadata.CorrelationId }.All(x => Eq(correlation, x)))
            throw new ArgumentException("All convergence correlations must match exactly.", nameof(request));

        var cycles = state.Cycles.Append(cycle).ToArray();
        var noProgress = cycle.ValidationComparison.HasImproved || cycle.ValidationComparison.HasRegressed ||
            cycle.Status == DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully
                ? 0 : state.ConsecutiveNoProgressCycleCount + 1;
        var status = DeriveStatus(state.Policy, cycles, noProgress);
        return new DocumentaryNarrativeRevisionConvergenceState(state.ConvergenceId, state.OriginalDraft,
            state.InitialValidationResult, cycle.RevisionResult.RevisedDraft, cycle.RevisedValidationResult,
            cycles, state.Policy, state.Metadata, status, Next(status), noProgress);
    }

    private static DocumentaryNarrativeRevisionConvergenceStatus DeriveStatus(DocumentaryNarrativeRevisionConvergencePolicy policy,
        IReadOnlyList<DocumentaryNarrativeRevisionCycleResult> cycles, int noProgress)
    {
        var latest = cycles[^1];
        if (latest.RevisedFindingCount == 0 && latest.UnresolvedRevisionItemCount == 0) return DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully;
        if (latest.ValidationComparison.HasRegressed && policy.StopOnRegression) return DocumentaryNarrativeRevisionConvergenceStatus.StoppedByRegression;
        if (latest.RevisionResult.UnresolvedItems.Any(x => !x.RequiresPassageText)) return DocumentaryNarrativeRevisionConvergenceStatus.RequiresManualEscalation;
        if (cycles.Count >= policy.MaximumCycleCount) return DocumentaryNarrativeRevisionConvergenceStatus.StoppedByCycleLimit;
        if (noProgress >= policy.MaximumConsecutiveNoProgressCycles) return DocumentaryNarrativeRevisionConvergenceStatus.StoppedByNoProgress;
        return DocumentaryNarrativeRevisionConvergenceStatus.InProgress;
    }

    internal static DocumentaryNarrativeRevisionConvergenceNextAction Next(DocumentaryNarrativeRevisionConvergenceStatus status) => status switch
    {
        DocumentaryNarrativeRevisionConvergenceStatus.NotStarted or DocumentaryNarrativeRevisionConvergenceStatus.InProgress => DocumentaryNarrativeRevisionConvergenceNextAction.PlanNextRevisionCycle,
        DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully => DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft,
        DocumentaryNarrativeRevisionConvergenceStatus.RequiresManualEscalation => DocumentaryNarrativeRevisionConvergenceNextAction.PerformManualReview,
        _ => DocumentaryNarrativeRevisionConvergenceNextAction.TerminateRevisionProcess
    };

    internal static void ValidateState(DocumentaryNarrativeRevisionConvergenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Eq(state.ConvergenceId, $"{state.OriginalDraftId}.revision-convergence.{state.OriginalDraftVersion}") ||
            !Eq(state.InitialValidationResult.DraftId, state.OriginalDraftId) || !Eq(state.CurrentValidationResult.DraftId, state.CurrentDraftId))
            throw new ArgumentException("Convergence state identity or validation lineage is inconsistent.", nameof(state));
        for (var i = 0; i < state.Cycles.Count; i++)
        {
            var cycle = state.Cycles[i];
            var expectedId = i == 0 ? state.OriginalDraftId : state.Cycles[i - 1].TargetDraftId;
            var expectedVersion = i == 0 ? state.OriginalDraftVersion : state.Cycles[i - 1].TargetDraftVersion;
            if (!Eq(cycle.SourceDraftId, expectedId) || !Eq(cycle.SourceDraftVersion, expectedVersion) ||
                !Eq(cycle.CorrelationId, state.Metadata.CorrelationId)) throw new ArgumentException("Cycle history is inconsistent.", nameof(state));
        }
        if (state.Cycles.Select(x => x.CycleId).Distinct(StringComparer.Ordinal).Count() != state.Cycles.Count)
            throw new ArgumentException("Cycle history contains a duplicate identity.", nameof(state));
        var lastId = state.Cycles.Count == 0 ? state.OriginalDraftId : state.Cycles[^1].TargetDraftId;
        var lastVersion = state.Cycles.Count == 0 ? state.OriginalDraftVersion : state.Cycles[^1].TargetDraftVersion;
        if (!Eq(state.CurrentDraftId, lastId) || !Eq(state.CurrentDraftVersion, lastVersion)) throw new ArgumentException("Current draft lineage is inconsistent.", nameof(state));
        var expectedNoProgress = 0;
        foreach (var cycle in state.Cycles)
            expectedNoProgress = cycle.ValidationComparison.HasImproved || cycle.ValidationComparison.HasRegressed ||
                cycle.Status == DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully ? 0 : expectedNoProgress + 1;
        var expectedStatus = state.Cycles.Count == 0
            ? state.InitialFindingCount == 0 ? DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully : DocumentaryNarrativeRevisionConvergenceStatus.NotStarted
            : DeriveStatus(state.Policy, state.Cycles, expectedNoProgress);
        if (state.ConsecutiveNoProgressCycleCount != expectedNoProgress || state.Status != expectedStatus)
            throw new ArgumentException("Convergence metrics or status are inconsistent.", nameof(state));
        if (state.NextAction != Next(state.Status)) throw new ArgumentException("The next action is inconsistent with status.", nameof(state));
    }
}
