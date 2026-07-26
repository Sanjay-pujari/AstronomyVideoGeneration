using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryNarrativeRevisionConvergenceStateValidator
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    internal static bool DraftsAreEquivalent(
        DocumentaryNarrativeDraft left,
        DocumentaryNarrativeDraft right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(left.DraftId, right.DraftId, StringComparison.Ordinal) ||
            !string.Equals(left.Version, right.Version, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            JsonSerializer.Serialize(left, WebJson),
            JsonSerializer.Serialize(right, WebJson),
            StringComparison.Ordinal);
    }

    internal static void Validate(DocumentaryNarrativeRevisionConvergenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Eq(state.ConvergenceId, $"{state.OriginalDraftId}.revision-convergence.{state.OriginalDraftVersion}") ||
            !Eq(state.InitialValidationResult.DraftId, state.OriginalDraftId) ||
            !Eq(state.CurrentValidationResult.DraftId, state.CurrentDraftId))
            throw new ArgumentException("Convergence state identity or validation lineage is inconsistent.", nameof(state));

        for (var i = 0; i < state.Cycles.Count; i++)
        {
            var cycle = state.Cycles[i];
            var expectedId = i == 0 ? state.OriginalDraftId : state.Cycles[i - 1].TargetDraftId;
            var expectedVersion = i == 0 ? state.OriginalDraftVersion : state.Cycles[i - 1].TargetDraftVersion;
            var correlation = state.Metadata.CorrelationId;
            if (!Eq(cycle.SourceDraftId, expectedId) || !Eq(cycle.SourceDraftVersion, expectedVersion) ||
                !new[] { cycle.CorrelationId, cycle.Plan.Metadata.CorrelationId,
                    cycle.Plan.RevisionRequest.Metadata.CorrelationId, cycle.Plan.WorkPackage.Metadata.CorrelationId,
                    cycle.Submission.Metadata.CorrelationId, cycle.BindingRequest.Metadata.CorrelationId }.All(x => Eq(x, correlation)))
                throw new ArgumentException("Cycle history or correlation is inconsistent.", nameof(state));
        }

        if (state.Cycles.Select(x => x.CycleId).Distinct(StringComparer.Ordinal).Count() != state.Cycles.Count)
            throw new ArgumentException("Cycle history contains a duplicate identity.", nameof(state));
        var lastId = state.Cycles.Count == 0 ? state.OriginalDraftId : state.Cycles[^1].TargetDraftId;
        var lastVersion = state.Cycles.Count == 0 ? state.OriginalDraftVersion : state.Cycles[^1].TargetDraftVersion;
        if (!Eq(state.CurrentDraftId, lastId) || !Eq(state.CurrentDraftVersion, lastVersion))
            throw new ArgumentException("Current draft lineage is inconsistent.", nameof(state));
        if (state.Cycles.Count != 0 && !DraftsAreEquivalent(
                state.CurrentDraft,
                state.Cycles[^1].RevisionResult.RevisedDraft))
            throw new ArgumentException("Current draft value must match the latest revised draft.", nameof(state));

        var expectedNoProgress = 0;
        foreach (var cycle in state.Cycles)
            expectedNoProgress = cycle.ValidationComparison.HasImproved || cycle.ValidationComparison.HasRegressed ||
                cycle.Status == DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully ? 0 : expectedNoProgress + 1;
        var expectedStatus = state.Cycles.Count == 0
            ? state.InitialFindingCount == 0 ? DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully : DocumentaryNarrativeRevisionConvergenceStatus.NotStarted
            : DeriveStatus(state.Policy, state.Cycles, expectedNoProgress);
        if (state.ConsecutiveNoProgressCycleCount != expectedNoProgress || state.Status != expectedStatus)
            throw new ArgumentException("Convergence metrics or status are inconsistent.", nameof(state));
        if (state.NextAction is DocumentaryNarrativeRevisionConvergenceNextAction.None or
            DocumentaryNarrativeRevisionConvergenceNextAction.ObtainExternalRevisionSubmission || state.NextAction != Next(state.Status))
            throw new ArgumentException("The next action is inconsistent with status.", nameof(state));
    }

    internal static DocumentaryNarrativeRevisionConvergenceStatus DeriveStatus(DocumentaryNarrativeRevisionConvergencePolicy policy,
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
}
