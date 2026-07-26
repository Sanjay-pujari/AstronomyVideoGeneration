using System.Globalization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

internal sealed class DocumentaryExportGraphSpecification
{
    internal DocumentaryExportGraphSpecification(IReadOnlyList<DocumentaryExportSpecificationItem> items) =>
        Items=DocumentaryExportSpecificationInventory.Copy(items,nameof(items));
    internal IReadOnlyList<DocumentaryExportSpecificationItem> Items{get;}
}

public sealed class DocumentaryExportSpecificationBuilder
{
    public DocumentaryExportSpecificationBuildResult Build(DocumentaryExportSpecificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);var c=request.CertificationRecord;var p=c.ProductionPackage;var provenance=c.ProvenanceRecord;var reasons=new List<DocumentaryExportSpecificationRejectionReason>();
        try{DocumentaryExportSpecificationValidator.ValidateCertificationRecord(c);}catch(ArgumentException){reasons.Add(DocumentaryExportSpecificationRejectionReason.CertificationRecordNotCertified);}
        if(c.CertificationId!=$"{c.ProvenanceId}.certification"||c.ProvenanceId!=provenance.ProvenanceId)reasons.Add(DocumentaryExportSpecificationRejectionReason.CertificationIdentityMismatch);
        if(c.PackageId!=p.PackageId||provenance.PackageId!=p.PackageId||!ReferenceEquals(p,provenance.ProductionPackage)&&!DocumentaryProductionPackageValidator.PackagesAreEquivalent(p,provenance.ProductionPackage))reasons.Add(DocumentaryExportSpecificationRejectionReason.PackageIdentityMismatch);
        try{DocumentaryProvenanceValidator.ValidateRecord(provenance);if(provenance.ProvenanceId!=$"{p.PackageId}.provenance"||c.ProvenanceId!=provenance.ProvenanceId)throw new ArgumentException();}catch(ArgumentException){reasons.Add(DocumentaryExportSpecificationRejectionReason.ProvenanceIdentityMismatch);}
        if(!CorrelationsValid(c,request.Metadata.CorrelationId))reasons.Add(DocumentaryExportSpecificationRejectionReason.CorrelationMismatch);
        if(!DocumentaryExportSpecificationValidator.PolicyValid(request.Policy))reasons.Add(DocumentaryExportSpecificationRejectionReason.ExportPolicyRejected);
        if(request.Profile!=DocumentaryExportProfile.CertifiedKnowledgePackage)reasons.Add(DocumentaryExportSpecificationRejectionReason.ExportProfileRejected);
        reasons=reasons.Distinct().OrderBy(x=>(int)x).ToList();
        if(reasons.Count!=0)return new(DocumentaryExportSpecificationStatus.Rejected,reasons,null);
        var id=$"{c.CertificationId}.export-specification";var graph=CreateCanonicalGraph(c,id,request.Metadata);var items=graph.Items;
        reasons.AddRange(DocumentaryExportSpecificationValidator.ValidateItems(c,id,request.Metadata,items));
        if(reasons.Count!=0)return new(DocumentaryExportSpecificationStatus.Rejected,reasons,null);
        var manifest=new DocumentaryExportSpecificationManifest($"{id}.manifest",id,request.Profile,items,10,10,DocumentaryExportEncoding.StructuredJson,"1.0",request.Metadata.CorrelationId);
        var specification=new DocumentaryExportSpecification(id,c,provenance,p,request.Profile,request.Policy,request.Metadata,items,manifest,c.CertificationId,provenance.ProvenanceId,p.PackageId,p.ReleaseCandidateId,p.ConvergenceId,10,10,true);
        return new(DocumentaryExportSpecificationStatus.Complete,Array.Empty<DocumentaryExportSpecificationRejectionReason>(),specification);
    }
    internal static DocumentaryExportGraphSpecification CreateCanonicalGraph(DocumentaryCertificationRecord c,string id,DocumentaryExportSpecificationMetadata m)
    { ArgumentNullException.ThrowIfNull(c);Guard.Required(id,nameof(id));ArgumentNullException.ThrowIfNull(m);var p=c.ProductionPackage;var pr=c.ProvenanceRecord;var identities=new[]{p.CurrentDraftId,p.FinalValidationResult.DraftId,$"{p.ConvergenceId}.cycles",p.ConvergenceId,$"{p.ConvergenceId}.acceptance",p.Manifest.ManifestId,pr.ProvenanceId,$"{c.CertificationId}.decision",c.CertificationId,$"{id}.manifest"};var versions=new[]{p.CurrentDraftVersion,p.CurrentDraftVersion,p.CompletedCycleCount.ToString(CultureInfo.InvariantCulture),p.ConvergenceState.Metadata.ConvergenceSchemaVersion,p.AcceptanceDecision.Metadata.AcceptanceSchemaVersion,p.Manifest.ManifestSchemaVersion,pr.Metadata.ProvenanceSchemaVersion,c.Metadata.CertificationSchemaVersion,c.Metadata.CertificationSchemaVersion,m.ExportSchemaVersion};var result=new List<DocumentaryExportSpecificationItem>();for(var i=0;i<10;i++){var type=(DocumentaryExportItemType)i;var deps=DocumentaryExportSpecificationInventory.DependencyTargetsFor(type).Select((target,sequence)=>new DocumentaryExportItemDependency($"{type}.depends-on.{target}",type,target,sequence,m.CorrelationId)).ToArray();result.Add(new($"{type}.{identities[i]}.{versions[i]}",type,DocumentaryExportSpecificationInventory.RequirementFor(type),DocumentaryExportSpecificationInventory.ContentTypeFor(type),DocumentaryExportSpecificationInventory.EncodingFor(type),identities[i],versions[i],i,deps,m.CorrelationId));}return new(result); }
    private static bool CorrelationsValid(DocumentaryCertificationRecord c,string correlation){var p=c.ProductionPackage;var pr=c.ProvenanceRecord;return new[]{c.Metadata.CorrelationId,p.Metadata.CorrelationId,p.Manifest.CorrelationId,p.ReleaseCandidate.Metadata.CorrelationId,p.AcceptanceDecision.Metadata.CorrelationId,p.ConvergenceState.Metadata.CorrelationId,pr.Metadata.CorrelationId}.All(x=>DocumentaryExportSpecificationInventory.Eq(x,correlation))&&p.RevisionCycles.All(x=>DocumentaryExportSpecificationInventory.Eq(x.Metadata.CorrelationId,correlation))&&pr.ArtifactNodes.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation))&&pr.RelationshipEdges.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation))&&c.Decision.RuleResults.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation))&&c.Decision.Findings.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation))&&c.UpstreamCertificationEvidence.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation))&&c.DocumentationEvidence.All(x=>DocumentaryExportSpecificationInventory.Eq(x.CorrelationId,correlation));}
}
