namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryNarrativeReleaseCandidateValidator
{
    internal static void Validate(DocumentaryNarrativeReleaseCandidate candidate)
    {
        var s=candidate.ConvergenceState; DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(s);
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.Ordinal);
        if(candidate.AcceptanceDecision.Status!=DocumentaryNarrativeAcceptanceStatus.Accepted||!candidate.AcceptanceDecision.IsEligibleForReleaseCandidate||s.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||
           !DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(candidate.NarrativeDraft,s.CurrentDraft)||
           !Eq(candidate.FinalValidationResult.DraftId,candidate.NarrativeDraft.DraftId)||candidate.FinalFindingCount!=0||
           (s.Cycles.Count>0&&s.Cycles[^1].UnresolvedRevisionItemCount!=0)||!Eq(candidate.AcceptanceDecision.ConvergenceId,s.ConvergenceId)||
           !Eq(candidate.AcceptanceDecision.CurrentDraftId,s.CurrentDraftId)||!Eq(candidate.AcceptanceDecision.CurrentDraftVersion,s.CurrentDraftVersion)||
           !Eq(candidate.AcceptanceDecision.Metadata.CorrelationId,s.Metadata.CorrelationId)||!Eq(candidate.Metadata.CorrelationId,s.Metadata.CorrelationId)||
           !Eq(candidate.ReleaseCandidateId,$"{candidate.DraftId}.narrative-release-candidate.{candidate.DraftVersion}")) throw new ArgumentException("Release candidate lineage, evidence, identity, or correlation is inconsistent.",nameof(candidate));
    }
}
public sealed class DocumentaryNarrativeReleaseCandidateBuilder
{
    public DocumentaryNarrativeReleaseCandidate Build(DocumentaryNarrativeRevisionConvergenceState convergenceState,DocumentaryNarrativeAcceptanceDecision acceptanceDecision,DocumentaryNarrativeReleaseCandidateMetadata metadata)
    { ArgumentNullException.ThrowIfNull(convergenceState); ArgumentNullException.ThrowIfNull(acceptanceDecision); ArgumentNullException.ThrowIfNull(metadata); return new DocumentaryNarrativeReleaseCandidate($"{convergenceState.CurrentDraftId}.narrative-release-candidate.{convergenceState.CurrentDraftVersion}",convergenceState.CurrentDraft,convergenceState.CurrentValidationResult,acceptanceDecision,convergenceState,metadata); }
}
