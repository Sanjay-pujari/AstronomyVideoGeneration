namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceSummarizer
{
    public DocumentaryNarrativeRevisionConvergenceSummary Summarize(DocumentaryNarrativeRevisionConvergenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(state);
        return new DocumentaryNarrativeRevisionConvergenceSummary(state.ConvergenceId, state.OriginalDraftId,
            state.OriginalDraftVersion, state.CurrentDraftId, state.CurrentDraftVersion, state.InitialFindingCount,
            state.CurrentFindingCount, state.CompletedCycleCount, state.TotalAppliedChangeCount,
            state.TotalResolvedFindingCount, state.Cycles.Sum(cycle => cycle.ValidationComparison.RemainingFindingCount), state.TotalIntroducedFindingCount,
            state.Cycles.Select(x => x.Status).ToArray(),
            new[] { state.InitialFindingCount }.Concat(state.Cycles.Select(x => x.RevisedFindingCount)).ToArray(),
            state.Cycles.Select(x => x.AppliedChangeCount).ToArray(),
            state.Cycles.Select(x => x.UnresolvedRevisionItemCount).ToArray(),
            state.HasImprovedFromInitial, state.HasRegressedFromInitial, state.IsClean);
    }
}
