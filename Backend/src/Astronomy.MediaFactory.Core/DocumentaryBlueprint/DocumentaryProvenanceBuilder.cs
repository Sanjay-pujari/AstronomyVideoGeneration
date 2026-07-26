namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProvenanceBuilder
{
    public DocumentaryProvenanceBuildResult Build(DocumentaryProvenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var package=request.ProductionPackage;var correlation=package.Metadata.CorrelationId;
        var reasons=DocumentaryProvenanceValidator.ValidatePackageForProvenance(package,request.Policy,request.Metadata);
        if(reasons.Count>0)return new(DocumentaryProvenanceStatus.Rejected,reasons,null);
        var graph=DocumentaryProvenanceValidator.CreateCanonicalGraph(package,correlation);
        reasons=DocumentaryProvenanceValidator.ValidateGraph(package,graph.Nodes,graph.Edges,correlation);
        if(reasons.Count>0)return new(DocumentaryProvenanceStatus.Rejected,reasons,null);
        var record=new DocumentaryProvenanceRecord($"{package.PackageId}.provenance",package,graph.Nodes,graph.Edges,request.Policy,request.Metadata,package.PackageId,package.Manifest.ManifestId,package.ReleaseCandidateId,package.ConvergenceId,package.OriginalDraftId,package.OriginalDraftVersion,package.CurrentDraftId,package.CurrentDraftVersion,package.CompletedCycleCount,true);
        return new(DocumentaryProvenanceStatus.Complete,Array.Empty<DocumentaryProvenanceRejectionReason>(),record);
    }
}
