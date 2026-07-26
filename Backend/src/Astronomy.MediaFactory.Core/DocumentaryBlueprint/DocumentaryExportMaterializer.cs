using System.Text;
using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryExportMaterializer
{
    public DocumentaryExportMaterializationResult Materialize(DocumentaryExportMaterializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);var s=request.ExportSpecification;var reasons=new List<DocumentaryExportMaterializationRejectionReason>();
        try{DocumentaryExportSpecificationValidator.ValidateSpecification(s);if(!s.IsComplete)throw new ArgumentException();}catch(ArgumentException){reasons.Add(DocumentaryExportMaterializationRejectionReason.ExportSpecificationNotComplete);}
        if(s.ExportSpecificationId!=$"{s.CertificationId}.export-specification"||s.CertificationId!=s.CertificationRecord.CertificationId)reasons.Add(DocumentaryExportMaterializationRejectionReason.ExportSpecificationIdentityMismatch);
        if(s.Manifest.ManifestId!=$"{s.ExportSpecificationId}.manifest"||s.Manifest.ExportSpecificationId!=s.ExportSpecificationId)reasons.Add(DocumentaryExportMaterializationRejectionReason.ExportManifestIdentityMismatch);
        if(s.CertificationId!=s.CertificationRecord.CertificationId||s.CertificationRecord.ProvenanceId!=s.ProvenanceId)reasons.Add(DocumentaryExportMaterializationRejectionReason.CertificationIdentityMismatch);
        if(s.ProvenanceId!=s.ProvenanceRecord.ProvenanceId||s.ProvenanceRecord.PackageId!=s.PackageId)reasons.Add(DocumentaryExportMaterializationRejectionReason.ProvenanceIdentityMismatch);
        if(s.PackageId!=s.ProductionPackage.PackageId||s.CertificationRecord.PackageId!=s.PackageId)reasons.Add(DocumentaryExportMaterializationRejectionReason.PackageIdentityMismatch);
        if(!CorrelationsValid(s,request.Metadata.CorrelationId))reasons.Add(DocumentaryExportMaterializationRejectionReason.CorrelationMismatch);
        if(!DocumentaryExportMaterializationValidator.PolicyValid(request.Policy))reasons.Add(DocumentaryExportMaterializationRejectionReason.MaterializationPolicyRejected);
        if(request.SerializerProfile!=DocumentaryExportSerializerProfile.CanonicalWebJson)reasons.Add(DocumentaryExportMaterializationRejectionReason.SerializerProfileRejected);
        reasons=reasons.Distinct().OrderBy(x=>(int)x).ToList();if(reasons.Count>0)return new(DocumentaryExportMaterializationStatus.Rejected,reasons,null);
        var payloads=CreateCanonicalPayloadGraph(s,request.Metadata,request.SerializerProfile).Payloads;
        return FinalizeMaterialization(request,payloads);
    }
    internal static DocumentaryExportMaterializationResult FinalizeMaterialization(DocumentaryExportMaterializationRequest request,IReadOnlyList<DocumentaryExportPayload> candidatePayloads)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(candidatePayloads);
        var s=request.ExportSpecification;
        var reasons=DocumentaryExportMaterializationValidator.ValidatePayloads(s,request.Metadata,request.SerializerProfile,candidatePayloads).Distinct().OrderBy(x=>(int)x).ToArray();
        if(reasons.Length>0)return new(DocumentaryExportMaterializationStatus.Rejected,reasons,null);
        var id=$"{s.ExportSpecificationId}.materialization";var chars=candidatePayloads.Sum(x=>x.CharacterCount);var bytes=candidatePayloads.Sum(x=>x.ByteCount);
        var manifest=new DocumentaryExportPayloadManifest($"{id}.manifest",id,s.ExportSpecificationId,request.SerializerProfile,DocumentaryExportCharacterEncoding.Utf8,candidatePayloads,10,23,chars,bytes,"1.0",request.Metadata.CorrelationId);
        var record=new DocumentaryExportMaterializationRecord(id,s,s.CertificationRecord,s.ProvenanceRecord,s.ProductionPackage,request.Policy,request.Metadata,request.SerializerProfile,DocumentaryExportCharacterEncoding.Utf8,candidatePayloads,manifest,s.ExportSpecificationId,s.CertificationId,s.ProvenanceId,s.PackageId,s.ReleaseCandidateId,s.ConvergenceId,10,23,chars,bytes,true);
        return new(DocumentaryExportMaterializationStatus.Complete,Array.Empty<DocumentaryExportMaterializationRejectionReason>(),record);
    }
    internal static IReadOnlyList<DocumentaryExportPayload> CreatePayloads(DocumentaryExportSpecification s,DocumentaryExportMaterializationMetadata metadata,DocumentaryExportSerializerProfile profile)
    {
        object[] sources=[s.ProductionPackage.NarrativeDraft,s.ProductionPackage.FinalValidationResult,s.ProductionPackage.RevisionCycles,s.ProductionPackage.ConvergenceState,s.ProductionPackage.AcceptanceDecision,s.ProductionPackage.Manifest,s.ProvenanceRecord,s.CertificationRecord.Decision,s.CertificationRecord,s.Manifest];var result=new List<DocumentaryExportPayload>();
        for(var i=0;i<10;i++){var item=s.Items[i];var type=(DocumentaryExportPayloadType)i;var dependencies=DocumentaryExportMaterializationInventory.DependencyTargetsFor(type).Select((target,sequence)=>new DocumentaryExportPayloadDependency($"{type}.depends-on.{target}",type,target,sequence,metadata.CorrelationId)).ToArray();var content=JsonSerializer.Serialize(sources[i],sources[i].GetType(),DocumentaryExportMaterializationValidator.WebJsonOptions);var utf8=Encoding.UTF8.GetBytes(content);result.Add(new($"{type}.{item.ArtifactIdentity}.{item.ArtifactVersion}.payload",type,DocumentaryExportMaterializationInventory.PayloadContentTypeFor(type),profile,DocumentaryExportCharacterEncoding.Utf8,item.ItemId,item.ArtifactIdentity,item.ArtifactVersion,item.Sequence,dependencies,content,utf8,content.Length,utf8.Length,metadata.CorrelationId));}return result.AsReadOnly();
    }
    internal static DocumentaryExportPayloadGraphSpecification CreateCanonicalPayloadGraph(DocumentaryExportSpecification specification,DocumentaryExportMaterializationMetadata metadata,DocumentaryExportSerializerProfile profile)=>new(CreatePayloads(specification,metadata,profile));
    private static bool CorrelationsValid(DocumentaryExportSpecification s,string correlation)
    {
        var c=s.CertificationRecord;var p=s.ProductionPackage;var pr=s.ProvenanceRecord;
        return new[]{s.Metadata.CorrelationId,s.Manifest.CorrelationId,c.Metadata.CorrelationId,p.Metadata.CorrelationId,p.Manifest.CorrelationId,p.ReleaseCandidate.Metadata.CorrelationId,p.AcceptanceDecision.Metadata.CorrelationId,p.ConvergenceState.Metadata.CorrelationId,pr.Metadata.CorrelationId}.All(x=>DocumentaryExportMaterializationInventory.Eq(x,correlation))
            &&s.Items.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation)&&x.Dependencies.All(d=>DocumentaryExportMaterializationInventory.Eq(d.CorrelationId,correlation)))
            &&p.RevisionCycles.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation)&&DocumentaryExportMaterializationInventory.Eq(x.Plan.Metadata.CorrelationId,correlation)&&DocumentaryExportMaterializationInventory.Eq(x.Plan.RevisionRequest.Metadata.CorrelationId,correlation)&&DocumentaryExportMaterializationInventory.Eq(x.Plan.WorkPackage.Metadata.CorrelationId,correlation)&&DocumentaryExportMaterializationInventory.Eq(x.Submission.Metadata.CorrelationId,correlation)&&DocumentaryExportMaterializationInventory.Eq(x.BindingRequest.Metadata.CorrelationId,correlation))
            &&pr.ArtifactNodes.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation))&&pr.RelationshipEdges.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation))
            &&c.Decision.RuleResults.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation))&&c.Decision.Findings.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation))&&c.UpstreamCertificationEvidence.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation))&&c.DocumentationEvidence.All(x=>DocumentaryExportMaterializationInventory.Eq(x.CorrelationId,correlation));
    }
}
