namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal static class DocumentaryProductionPackageValidator
{
    internal static void ValidateComplete(string packageId,DocumentaryNarrativeReleaseCandidate candidate,DocumentaryProductionPackageManifest manifest,DocumentaryProductionPackageMetadata metadata)
    {
        DocumentaryNarrativeReleaseCandidateValidator.Validate(candidate);
        var correlation=candidate.Metadata.CorrelationId;
        if(packageId!=$"{candidate.ReleaseCandidateId}.production-package"||manifest.PackageId!=packageId||
           !string.Equals(correlation,candidate.AcceptanceDecision.Metadata.CorrelationId,StringComparison.Ordinal)||
           !string.Equals(correlation,candidate.ConvergenceState.Metadata.CorrelationId,StringComparison.Ordinal)||
           !string.Equals(correlation,metadata.CorrelationId,StringComparison.Ordinal)||!string.Equals(correlation,manifest.CorrelationId,StringComparison.Ordinal)||
           !candidate.IsAccepted||!candidate.IsClean||!candidate.IsFullyResolved||candidate.FinalFindingCount!=0||
           candidate.AcceptanceDecision.PrimaryReason!=DocumentaryNarrativeAcceptanceReason.ConvergedAndClean||candidate.AcceptanceDecision.SupportingReasons.Count!=0||
           candidate.ConvergenceState.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||candidate.ConvergenceState.NextAction!=DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft)
            throw new ArgumentException("Production package identity, evidence, or correlation is inconsistent.");
    }
}
