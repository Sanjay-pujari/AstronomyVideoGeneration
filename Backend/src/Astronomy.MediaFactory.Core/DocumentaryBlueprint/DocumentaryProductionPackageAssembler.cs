using System.Globalization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProductionPackageAssembler
{
    public DocumentaryProductionPackageAssemblyResult Assemble(DocumentaryProductionPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var c=request.ReleaseCandidate; var reasons=new List<DocumentaryProductionPackageRejectionReason>();
        if(!c.IsAccepted)reasons.Add(DocumentaryProductionPackageRejectionReason.ReleaseCandidateNotAccepted);
        if(!c.IsClean)reasons.Add(DocumentaryProductionPackageRejectionReason.ReleaseCandidateNotClean);
        if(!c.IsFullyResolved)reasons.Add(DocumentaryProductionPackageRejectionReason.ReleaseCandidateNotFullyResolved);
        if(c.ReleaseCandidateId!=$"{c.DraftId}.narrative-release-candidate.{c.DraftVersion}")reasons.Add(DocumentaryProductionPackageRejectionReason.ReleaseCandidateIdentityMismatch);
        if(!DocumentaryNarrativeRevisionConvergenceStateValidator.DraftsAreEquivalent(c.NarrativeDraft,c.ConvergenceState.CurrentDraft))reasons.Add(DocumentaryProductionPackageRejectionReason.NarrativeDraftLineageMismatch);
        if(!DocumentaryNarrativeRevisionConvergenceStateValidator.ValidationResultsAreEquivalent(c.FinalValidationResult,c.ConvergenceState.CurrentValidationResult)||c.FinalValidationResult.DraftId!=c.DraftId)reasons.Add(DocumentaryProductionPackageRejectionReason.ValidationLineageMismatch);
        if(c.ConvergenceState.Status!=DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully||c.ConvergenceState.NextAction!=DocumentaryNarrativeRevisionConvergenceNextAction.AcceptCurrentDraft)reasons.Add(DocumentaryProductionPackageRejectionReason.ConvergenceLineageMismatch);
        if(c.AcceptanceDecision.Status!=DocumentaryNarrativeAcceptanceStatus.Accepted||c.AcceptanceDecision.PrimaryReason!=DocumentaryNarrativeAcceptanceReason.ConvergedAndClean||c.AcceptanceDecision.SupportingReasons.Count!=0||c.AcceptanceDecision.ConvergenceId!=c.ConvergenceId)reasons.Add(DocumentaryProductionPackageRejectionReason.AcceptanceLineageMismatch);
        var correlation=c.Metadata.CorrelationId;if(!new[]{c.AcceptanceDecision.Metadata.CorrelationId,c.ConvergenceState.Metadata.CorrelationId,request.Metadata.CorrelationId}.All(x=>string.Equals(x,correlation,StringComparison.Ordinal)))reasons.Add(DocumentaryProductionPackageRejectionReason.CorrelationMismatch);
        if(!request.Policy.RequiredSections.SequenceEqual(DocumentaryProductionPackageInventory.Sections))reasons.Add(DocumentaryProductionPackageRejectionReason.RequiredSectionMissing);
        if(c.NarrativeDraft is null||c.FinalValidationResult is null||c.ConvergenceState is null||c.AcceptanceDecision is null||c.ConvergenceState.Cycles is null)reasons.Add(DocumentaryProductionPackageRejectionReason.RequiredEvidenceMissing);
        if(request.Policy.PolicySchemaVersion!="1.0"||!request.Policy.RequireAcceptedReleaseCandidate||!request.Policy.RequireCleanNarrative||!request.Policy.RequireFullyResolvedNarrative||!request.Policy.RequireFinalValidationEvidence||!request.Policy.RequireRevisionHistory||!request.Policy.RequireConvergenceEvidence||!request.Policy.RequireAcceptanceEvidence)reasons.Add(DocumentaryProductionPackageRejectionReason.PolicyRejected);
        if(reasons.Count>0)return new(DocumentaryProductionPackageStatus.Rejected,reasons,null);
        var packageId=$"{c.ReleaseCandidateId}.production-package";var manifestId=$"{packageId}.manifest";
        var entries=new[]{
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptedNarrative,nameof(DocumentaryNarrativeDraft),c.DraftId,c.DraftVersion,0,true),
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.FinalValidationEvidence,nameof(DocumentaryNarrativeDraftValidationResult),c.FinalValidationResult.DraftId,c.DraftVersion,1,true),
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.RevisionHistory,"DocumentaryNarrativeRevisionCycleHistory",$"{c.ConvergenceId}.cycles",c.CompletedCycleCount.ToString(CultureInfo.InvariantCulture),2,true),
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.ConvergenceEvidence,nameof(DocumentaryNarrativeRevisionConvergenceState),c.ConvergenceId,c.ConvergenceState.Metadata.ConvergenceSchemaVersion,3,true),
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.AcceptanceEvidence,nameof(DocumentaryNarrativeAcceptanceDecision),$"{c.ConvergenceId}.acceptance",c.AcceptanceDecision.Metadata.AcceptanceSchemaVersion,4,true),
            new DocumentaryProductionPackageManifestEntry(DocumentaryProductionPackageSection.PackageManifest,nameof(DocumentaryProductionPackageManifest),manifestId,"1.0",5,true)};
        var manifest=new DocumentaryProductionPackageManifest(manifestId,packageId,entries,"1.0",correlation);
        var package=new DocumentaryProductionPackage(packageId,c,c.NarrativeDraft,c.FinalValidationResult,c.ConvergenceState.Cycles,c.ConvergenceState,c.AcceptanceDecision,manifest,request.Policy,request.Metadata,DocumentaryProductionPackageInventory.Sections);
        return new(DocumentaryProductionPackageStatus.Complete,Array.Empty<DocumentaryProductionPackageRejectionReason>(),package);
    }
}
