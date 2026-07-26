namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeAcceptanceEvaluator
{
    public DocumentaryNarrativeAcceptanceDecision Evaluate(DocumentaryNarrativeAcceptanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state=request.ConvergenceState; DocumentaryNarrativeRevisionConvergenceStateValidator.Validate(state);
        if(!string.Equals(state.Metadata.CorrelationId,request.Metadata.CorrelationId,StringComparison.Ordinal)) throw new ArgumentException("Acceptance and convergence correlations must match.",nameof(request));
        var unresolved=state.Cycles.Count==0?0:state.Cycles[^1].UnresolvedRevisionItemCount;
        var (status,primary)=Decide(state,request.Policy,unresolved);
        var evidence=new[]{
            (state.CurrentFindingCount>0,DocumentaryNarrativeAcceptanceReason.ValidationFindingsRemain),
            (unresolved>0,DocumentaryNarrativeAcceptanceReason.UnresolvedRevisionItemsRemain),
            (state.Status==DocumentaryNarrativeRevisionConvergenceStatus.StoppedByCycleLimit,DocumentaryNarrativeAcceptanceReason.CycleLimitReached),
            (state.Status==DocumentaryNarrativeRevisionConvergenceStatus.StoppedByNoProgress,DocumentaryNarrativeAcceptanceReason.NoProgressReached),
            (state.Status==DocumentaryNarrativeRevisionConvergenceStatus.StoppedByRegression,DocumentaryNarrativeAcceptanceReason.RegressionDetected),
            (state.Status==DocumentaryNarrativeRevisionConvergenceStatus.RequiresManualEscalation,DocumentaryNarrativeAcceptanceReason.ManualReviewRequired),
            (status==DocumentaryNarrativeAcceptanceStatus.Rejected&&primary==DocumentaryNarrativeAcceptanceReason.PolicyRejected,DocumentaryNarrativeAcceptanceReason.PolicyRejected)};
        return new DocumentaryNarrativeAcceptanceDecision(state.ConvergenceId,status,primary,evidence.Where(x=>x.Item1&&x.Item2!=primary).Select(x=>x.Item2).ToArray(),state.CurrentDraftId,state.CurrentDraftVersion,state.CurrentFindingCount,state.CompletedCycleCount,unresolved,request.Policy,request.Metadata);
    }
    private static (DocumentaryNarrativeAcceptanceStatus,DocumentaryNarrativeAcceptanceReason) Decide(DocumentaryNarrativeRevisionConvergenceState s,DocumentaryNarrativeAcceptancePolicy p,int unresolved)=>s.Status switch
    {
        DocumentaryNarrativeRevisionConvergenceStatus.NotStarted or DocumentaryNarrativeRevisionConvergenceStatus.InProgress => (DocumentaryNarrativeAcceptanceStatus.Rejected,DocumentaryNarrativeAcceptanceReason.NonTerminalConvergenceState),
        DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully when s.CurrentFindingCount==0&&unresolved==0 => (DocumentaryNarrativeAcceptanceStatus.Accepted,DocumentaryNarrativeAcceptanceReason.ConvergedAndClean),
        DocumentaryNarrativeRevisionConvergenceStatus.RequiresManualEscalation when p.AllowManualApprovalForManualEscalation => (DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval,DocumentaryNarrativeAcceptanceReason.ManualReviewRequired),
        DocumentaryNarrativeRevisionConvergenceStatus.StoppedByCycleLimit when p.AllowManualApprovalForCycleLimit => (DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval,DocumentaryNarrativeAcceptanceReason.CycleLimitReached),
        DocumentaryNarrativeRevisionConvergenceStatus.StoppedByNoProgress when p.AllowManualApprovalForNoProgress => (DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval,DocumentaryNarrativeAcceptanceReason.NoProgressReached),
        DocumentaryNarrativeRevisionConvergenceStatus.StoppedByRegression when p.RejectRegressionStop => (DocumentaryNarrativeAcceptanceStatus.Rejected,DocumentaryNarrativeAcceptanceReason.RegressionDetected),
        DocumentaryNarrativeRevisionConvergenceStatus.StoppedByRegression when p.AllowManualApprovalForRegression => (DocumentaryNarrativeAcceptanceStatus.HeldForManualApproval,DocumentaryNarrativeAcceptanceReason.RegressionDetected),
        _ => (DocumentaryNarrativeAcceptanceStatus.Rejected,DocumentaryNarrativeAcceptanceReason.PolicyRejected)
    };
}
