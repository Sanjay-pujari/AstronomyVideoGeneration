namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProvenanceBuilder
{
    public DocumentaryProvenanceBuildResult Build(DocumentaryProvenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var package=request.ProductionPackage;var reasons=new List<DocumentaryProvenanceRejectionReason>();
        if(!package.IsComplete)reasons.Add(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete);
        try { DocumentaryProductionPackageValidator.ValidateComplete(package.PackageId,package.ReleaseCandidate,package.Manifest,package.Metadata); }
        catch(ArgumentException) { if(!reasons.Contains(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete))reasons.Add(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete); }
        if(package.PackageId!=$"{package.ReleaseCandidateId}.production-package")reasons.Add(DocumentaryProvenanceRejectionReason.PackageIdentityMismatch);
        if(package.Manifest.ManifestId!=$"{package.PackageId}.manifest"||package.Manifest.PackageId!=package.PackageId)reasons.Add(DocumentaryProvenanceRejectionReason.ManifestIdentityMismatch);
        if(package.OriginalDraftId!=package.ConvergenceState.OriginalDraftId||package.CurrentDraftId!=package.ConvergenceState.CurrentDraftId)reasons.Add(DocumentaryProvenanceRejectionReason.DraftLineageMismatch);
        if(package.ConvergenceState.InitialValidationResult.DraftId!=package.OriginalDraftId||package.FinalValidationResult.DraftId!=package.CurrentDraftId)reasons.Add(DocumentaryProvenanceRejectionReason.ValidationLineageMismatch);
        if(package.RevisionCycles.Count!=package.CompletedCycleCount)reasons.Add(DocumentaryProvenanceRejectionReason.RevisionLineageMismatch);
        var correlation=package.Metadata.CorrelationId;
        if(!new[]{package.Manifest.CorrelationId,package.ReleaseCandidate.Metadata.CorrelationId,package.AcceptanceDecision.Metadata.CorrelationId,package.ConvergenceState.Metadata.CorrelationId,request.Metadata.CorrelationId}.All(x=>string.Equals(x,correlation,StringComparison.Ordinal)))reasons.Add(DocumentaryProvenanceRejectionReason.CorrelationMismatch);
        reasons=reasons.Distinct().OrderBy(x=>(int)x).ToList();
        if(reasons.Count>0)return new(DocumentaryProvenanceStatus.Rejected,reasons,null);
        var nodes=DocumentaryProvenanceValidator.Nodes(package,correlation);var edges=DocumentaryProvenanceValidator.Edges(package,nodes,correlation);
        var record=new DocumentaryProvenanceRecord($"{package.PackageId}.provenance",package,nodes,edges,request.Policy,request.Metadata,package.PackageId,package.Manifest.ManifestId,package.ReleaseCandidateId,package.ConvergenceId,package.OriginalDraftId,package.OriginalDraftVersion,package.CurrentDraftId,package.CurrentDraftVersion,package.CompletedCycleCount,true);
        return new(DocumentaryProvenanceStatus.Complete,Array.Empty<DocumentaryProvenanceRejectionReason>(),record);
    }
}
