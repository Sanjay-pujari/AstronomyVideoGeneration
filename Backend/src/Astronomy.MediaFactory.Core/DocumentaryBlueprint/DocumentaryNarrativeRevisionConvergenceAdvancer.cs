namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceAdvancer
{
    public DocumentaryNarrativeRevisionConvergenceState Advance(DocumentaryNarrativeRevisionConvergenceAdvanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = request.CurrentState;
        DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(state);
        if (!state.RequiresAnotherCycle) throw new InvalidOperationException("A terminal convergence state cannot be advanced.");
        var cycle = request.CompletedCycleResult;
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Eq(cycle.SourceDraftId, state.CurrentDraftId) || !Eq(cycle.SourceDraftVersion, state.CurrentDraftVersion))
            throw new ArgumentException("The cycle source must match the current draft exactly.", nameof(request));
        if (!DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(
                cycle.Plan.SourceDraft,
                state.CurrentDraft))
            throw new ArgumentException(
                "The cycle source draft must be value-equivalent to the current convergence draft.",
                nameof(request));
        if (state.Cycles.Any(x => Eq(x.CycleId, cycle.CycleId))) throw new ArgumentException("The cycle has already been appended.", nameof(request));
        var correlation = state.Metadata.CorrelationId;
        if (!new[] { request.CorrelationId, cycle.CorrelationId, cycle.Plan.Metadata.CorrelationId,
                cycle.Plan.RevisionRequest.Metadata.CorrelationId, cycle.Plan.WorkPackage.Metadata.CorrelationId,
                cycle.Submission.Metadata.CorrelationId, cycle.BindingRequest.Metadata.CorrelationId }.All(x => Eq(correlation, x)))
            throw new ArgumentException("All convergence correlations must match exactly.", nameof(request));

        var cycles = state.Cycles.Append(cycle).ToArray();
        var noProgress = cycle.ValidationComparison.HasImproved || cycle.ValidationComparison.HasRegressed ||
            cycle.Status == DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully
                ? 0 : state.ConsecutiveNoProgressCycleCount + 1;
        var status = DocumentaryNarrativeRevisionConvergenceStateValidator.DeriveStatus(state.Policy, cycles, noProgress);
        return new DocumentaryNarrativeRevisionConvergenceState(state.ConvergenceId, state.OriginalDraft,
            state.InitialValidationResult, cycle.RevisionResult.RevisedDraft, cycle.RevisedValidationResult,
            cycles, state.Policy, state.Metadata, status, DocumentaryNarrativeRevisionConvergenceStateValidator.Next(status), noProgress);
    }
}
