namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryNarrativeReleaseCandidateValidator
{
    internal static void Validate(DocumentaryNarrativeReleaseCandidate candidate)
    {
        var s=candidate.ConvergenceState; DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(s);
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.Ordinal);
        if(candidate.AcceptanceDecision.Status!=DocumentaryNarrativeAcceptanceStatus.Accepted||!candidate.AcceptanceDecision.IsEligibleForReleaseCandidate||s.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||
           candidate.AcceptanceDecision.PrimaryReason!=DocumentaryNarrativeAcceptanceReason.ConvergedAndClean||candidate.AcceptanceDecision.SupportingReasons.Count!=0||
           !DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(candidate.NarrativeDraft,s.CurrentDraft)||
           !DocumentaryNarrativeRevisionConvergenceStateValidator.ValidationResultsAreEquivalent(candidate.FinalValidationResult,s.CurrentValidationResult)||
           !Eq(candidate.FinalValidationResult.DraftId,candidate.NarrativeDraft.DraftId)||candidate.FinalFindingCount!=0||
           (s.Cycles.Count>0&&s.Cycles[^1].UnresolvedRevisionItemCount!=0)||!Eq(candidate.AcceptanceDecision.ConvergenceId,s.ConvergenceId)||
           !Eq(candidate.AcceptanceDecision.CurrentDraftId,s.CurrentDraftId)||!Eq(candidate.AcceptanceDecision.CurrentDraftVersion,s.CurrentDraftVersion)||
           candidate.AcceptanceDecision.CurrentFindingCount!=s.CurrentFindingCount||candidate.AcceptanceDecision.CompletedCycleCount!=s.CompletedCycleCount||
           candidate.AcceptanceDecision.UnresolvedRevisionItemCount!=(s.Cycles.Count==0?0:s.Cycles[^1].UnresolvedRevisionItemCount)||
           !Eq(candidate.AcceptanceDecision.Metadata.CorrelationId,s.Metadata.CorrelationId)||!Eq(candidate.Metadata.CorrelationId,s.Metadata.CorrelationId)||
           !Eq(candidate.ReleaseCandidateId,$"{candidate.DraftId}.narrative-release-candidate.{candidate.DraftVersion}")) throw new ArgumentException("Release candidate lineage, evidence, identity, or correlation is inconsistent.",nameof(candidate));
    }
}
public sealed class DocumentaryNarrativeReleaseCandidateBuilder
{
    public DocumentaryNarrativeReleaseCandidate Build(DocumentaryNarrativeRevisionConvergenceState convergenceState,DocumentaryNarrativeAcceptanceDecision acceptanceDecision,DocumentaryNarrativeReleaseCandidateMetadata metadata)
    { ArgumentNullException.ThrowIfNull(convergenceState); ArgumentNullException.ThrowIfNull(acceptanceDecision); ArgumentNullException.ThrowIfNull(metadata); return new DocumentaryNarrativeReleaseCandidate($"{convergenceState.CurrentDraftId}.narrative-release-candidate.{convergenceState.CurrentDraftVersion}",convergenceState.CurrentDraft,convergenceState.CurrentValidationResult,acceptanceDecision,convergenceState,metadata); }
}
