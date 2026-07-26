namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProductionPackageSummarizer
{
    public DocumentaryProductionPackageSummary Summarize(DocumentaryProductionPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var state=package.ConvergenceState;
        return new(package.PackageId,package.ReleaseCandidateId,package.OriginalDraftId,package.OriginalDraftVersion,
            package.CurrentDraftId,package.CurrentDraftVersion,package.ConvergenceId,package.CompletedCycleCount,
            package.FinalFindingCount,package.UnresolvedRevisionItemCount,package.IncludedSections,package.Manifest.Entries.Count,
            state.TotalAppliedChangeCount,state.TotalResolvedFindingCount,
            state.Cycles.Sum(x=>x.ValidationComparison.RemainingFindingCount),state.TotalIntroducedFindingCount,
            package.Metadata.CreatedUtc,package.Metadata.CreatedBy,package.IsAccepted,package.IsClean,package.IsFullyResolved,package.IsComplete);
    }
}
