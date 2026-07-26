namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeAcceptanceCoordinator
{
    public DocumentaryNarrativeAcceptanceResult Accept(DocumentaryNarrativeAcceptanceRequest request,DocumentaryNarrativeReleaseCandidateMetadata releaseMetadata)
    { ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(releaseMetadata); var decision=new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request); var candidate=decision.IsEligibleForReleaseCandidate?new DocumentaryNarrativeReleaseCandidateBuilder().Build(request.ConvergenceState,decision,releaseMetadata):null; return new DocumentaryNarrativeAcceptanceResult(decision,candidate); }
}
