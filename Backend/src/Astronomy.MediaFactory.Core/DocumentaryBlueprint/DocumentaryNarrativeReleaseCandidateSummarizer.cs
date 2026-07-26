namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeReleaseCandidateSummarizer
{
    public DocumentaryNarrativeReleaseCandidateSummary Summarize(DocumentaryNarrativeReleaseCandidate releaseCandidate)
    { ArgumentNullException.ThrowIfNull(releaseCandidate); DocumentaryNarrativeReleaseCandidateValidator.Validate(releaseCandidate); var s=releaseCandidate.ConvergenceState; return new DocumentaryNarrativeReleaseCandidateSummary(releaseCandidate.ReleaseCandidateId,releaseCandidate.DraftId,releaseCandidate.DraftVersion,releaseCandidate.OriginalDraftId,releaseCandidate.OriginalDraftVersion,releaseCandidate.ConvergenceId,s.CompletedCycleCount,releaseCandidate.FinalFindingCount,s.TotalAppliedChangeCount,s.TotalResolvedFindingCount,s.TotalIntroducedFindingCount,s.Cycles.Select(x=>x.Status).ToArray(),new[]{s.InitialFindingCount}.Concat(s.Cycles.Select(x=>x.RevisedFindingCount)).ToArray(),releaseCandidate.AcceptanceDecision.Metadata.EvaluatedUtc,releaseCandidate.AcceptanceDecision.Metadata.EvaluatedBy,releaseCandidate.IsClean,releaseCandidate.IsFullyResolved); }
}
