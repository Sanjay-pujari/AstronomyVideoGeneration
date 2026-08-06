namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeAcceptanceCoordinator
{
    /// <summary>Practical lifecycle eligibility boundary used before Phase 7 persistence builds the full release aggregate.</summary>
    public bool Accept(bool draftPresent, bool validationPassed, bool convergenceSucceeded) =>
        draftPresent && validationPassed && convergenceSucceeded;

    public DocumentaryNarrativeAcceptanceResult Accept(DocumentaryNarrativeAcceptanceRequest request,DocumentaryNarrativeReleaseCandidateMetadata releaseMetadata)
    { ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(releaseMetadata); var decision=new DocumentaryNarrativeAcceptanceEvaluator().Evaluate(request); var candidate=decision.IsEligibleForReleaseCandidate?new DocumentaryNarrativeReleaseCandidateBuilder().Build(request.ConvergenceState,decision,releaseMetadata):null; return new DocumentaryNarrativeAcceptanceResult(decision,candidate); }
}
