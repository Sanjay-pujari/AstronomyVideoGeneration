namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryCertificationEvaluator
{
    public DocumentaryCertificationEvaluationResult Evaluate(DocumentaryCertificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);DocumentaryCertificationValidator.ValidatePolicy(request.Policy);
        var p=request.ProvenanceRecord.ProductionPackage;var provenance=request.ProvenanceRecord;var correlation=request.Metadata.CorrelationId;
        var semantic=DocumentaryProvenanceValidator.ValidatePackageForProvenance(p,provenance.Policy,provenance.Metadata);
        var graph=DocumentaryProvenanceValidator.ValidateGraph(p,provenance.ArtifactNodes,provenance.RelationshipEdges,p.Metadata.CorrelationId);
        bool Has(DocumentaryProvenanceRejectionReason reason)=>semantic.Contains(reason);
        var correlationOk=new[]{p.Metadata.CorrelationId,p.Manifest.CorrelationId,p.ReleaseCandidate.Metadata.CorrelationId,p.AcceptanceDecision.Metadata.CorrelationId,p.ConvergenceState.Metadata.CorrelationId,provenance.Metadata.CorrelationId,correlation}.All(x=>DocumentaryCertificationInventory.Eq(x,correlation))&&p.RevisionCycles.All(x=>DocumentaryCertificationInventory.Eq(x.CorrelationId,correlation))&&provenance.ArtifactNodes.All(x=>DocumentaryCertificationInventory.Eq(x.CorrelationId,correlation))&&provenance.RelationshipEdges.All(x=>DocumentaryCertificationInventory.Eq(x.CorrelationId,correlation))&&request.UpstreamCertificationEvidence.All(x=>DocumentaryCertificationInventory.Eq(x.CorrelationId,correlation))&&request.DocumentationEvidence.All(x=>DocumentaryCertificationInventory.Eq(x.CorrelationId,correlation));
        bool upstreamOk=request.UpstreamCertificationEvidence.Count==13&&request.UpstreamCertificationEvidence.Select((x,i)=>x.Sequence==i&&x.ObjectiveId==DocumentaryCertificationInventory.Objectives[i]&&x.ObjectiveVersion=="1.0"&&x.IsCertified).All(x=>x);
        bool docsOk=request.DocumentationEvidence.Count==4&&request.DocumentationEvidence.Select((x,i)=>x.Sequence==i&&x.DocumentId==DocumentaryCertificationInventory.DocumentIds[i]&&x.DocumentVersion=="1.0"&&x.RequiredStatements.SequenceEqual(DocumentaryCertificationInventory.Statements[i])).All(x=>x);
        var checks=new bool[]{
            p.IsComplete&&p.IsAccepted&&p.IsClean&&p.IsFullyResolved&&p.FinalFindingCount==0&&p.UnresolvedRevisionItemCount==0&&!Has(DocumentaryProvenanceRejectionReason.ProductionPackageNotComplete),
            provenance.IsComplete&&DocumentaryProvenanceValidator.RecordStructureIsCanonical(provenance),
            p.PackageId==$"{p.ReleaseCandidateId}.production-package",
            p.Manifest.ManifestId==$"{p.PackageId}.manifest"&&p.Manifest.PackageId==p.PackageId,
            provenance.ProvenanceId==$"{p.PackageId}.provenance",
            DocumentaryProductionPackageValidator.ManifestMatches(p.Manifest,p.ReleaseCandidate,p.PackageId),
            !graph.Contains(DocumentaryProvenanceRejectionReason.ArtifactInventoryMismatch)&&!graph.Contains(DocumentaryProvenanceRejectionReason.RequiredNodeMissing),
            !graph.Contains(DocumentaryProvenanceRejectionReason.RelationshipInventoryMismatch)&&!graph.Contains(DocumentaryProvenanceRejectionReason.RequiredEdgeMissing),
            !Has(DocumentaryProvenanceRejectionReason.DraftLineageMismatch),!Has(DocumentaryProvenanceRejectionReason.ValidationLineageMismatch),!Has(DocumentaryProvenanceRejectionReason.RevisionLineageMismatch),!Has(DocumentaryProvenanceRejectionReason.ConvergenceLineageMismatch),!Has(DocumentaryProvenanceRejectionReason.AcceptanceLineageMismatch),!Has(DocumentaryProvenanceRejectionReason.ReleaseCandidateLineageMismatch),
            correlationOk,DocumentaryCertificationValidator.JsonRoundTrips(p),DocumentaryCertificationValidator.JsonRoundTrips(provenance),DocumentaryCertificationValidator.Immutable(),DocumentaryCertificationValidator.OperationsValid(),DocumentaryCertificationValidator.ForbiddenCapabilitiesAbsent(),docsOk,upstreamOk};
        var evidence=new[]{p.PackageId,provenance.ProvenanceId,p.PackageId,p.Manifest.ManifestId,provenance.ProvenanceId,p.Manifest.ManifestId,provenance.ProvenanceId,provenance.ProvenanceId,p.OriginalDraftId,p.FinalValidationResult.DraftId,p.ConvergenceId,p.ConvergenceId,p.ReleaseCandidateId,p.ReleaseCandidateId,p.PackageId,p.PackageId,provenance.ProvenanceId,"o2.12-o2.14-public-surface","o2.12-o2.14-operation-inventory","o2.12-o2.14-public-surface","documentary-foundation-documentation","o2.1-o2.13-certification"};
        var results=checks.Select((passed,i)=>{var rule=(DocumentaryCertificationRule)i;var domain=DocumentaryCertificationInventory.DomainFor(rule);var fs=passed?Array.Empty<DocumentaryCertificationFinding>():[new DocumentaryCertificationFinding($"{rule}.{evidence[i]}",domain,rule,DocumentaryCertificationSeverity.Error,DocumentaryCertificationInventory.MessageCodeFor(rule),evidence[i],0,correlation)];return new DocumentaryCertificationRuleResult(rule,domain,passed,fs,i,correlation);}).ToArray();
        var findings=results.SelectMany(x=>x.Findings).ToArray();var status=findings.Length==0?DocumentaryCertificationStatus.Certified:DocumentaryCertificationStatus.NonCompliant;var decision=new DocumentaryCertificationDecision(status,results,findings,results.Count(x=>x.Passed),results.Count(x=>!x.Passed),22);
        DocumentaryCertificationRecord? record=null;if(status==DocumentaryCertificationStatus.Certified)record=new($"{provenance.ProvenanceId}.certification",provenance,p,request.Policy,request.Metadata,request.UpstreamCertificationEvidence,request.DocumentationEvidence,decision,p.PackageId,provenance.ProvenanceId,p.ReleaseCandidateId,p.ConvergenceId,22,0,22,true);
        return new(status,decision,record,provenance,request.Metadata);
    }
}
