namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryProductionCertifier
{
    public DocumentaryProductionCertificationResult Certify(DocumentaryProductionCertificationRequest request)
    {
        var reasons=DocumentaryProductionCertificationValidator.ValidateRequest(request);
        if(reasons.Count!=0)return new(DocumentaryProductionCertificationStatus.Rejected,reasons,null);
        var execution=request.PipelineExecutionRecord;var correlation=request.Metadata.CorrelationId;
        var variantRecords=new List<DocumentaryProductionVariantCertificationRecord>();
        foreach(var variant in request.MediaProject.Variants)
        {
            var executionVariant=execution.VariantRecords.Single(x=>x.VariantType==variant.VariantType);
            var output=execution.OutputManifest.Assets.Single(x=>x.AssetId==executionVariant.OutputAssetId);
            var links=new List<DocumentaryProductionTraceabilityLink>();
            foreach(var scene in variant.Scenes.OrderBy(x=>x.Sequence))
            {
                var sceneAsset=execution.ExecutionPlan.AssetPlans.Single(x=>x.AssetType==DocumentaryMediaAssetType.SceneVideo&&x.SceneId==scene.SceneId);
                foreach(var reference in scene.KnowledgeReferences.OrderBy(x=>x.Sequence))
                {
                    var sequence=links.Count;
                    links.Add(new($"{output.AssetId}.trace.{sequence}",variant.VariantType,scene.SceneId,sceneAsset.AssetId,output.AssetId,reference.ReferenceId,reference.PayloadId,reference.SourceItemId,reference.ArtifactIdentity,reference.ArtifactVersion,reference.JsonPointer,sequence,correlation));
                }
            }
            var frozen=DocumentaryProductionCertificationInventory.Copy(links);
            variantRecords.Add(new($"{execution.ExecutionId}.{variant.VariantType}.certification",variant.VariantId,variant.VariantType,output.AssetId,output.Checksum!,variant.SceneCount,frozen,frozen.Count,true,true,correlation));
        }
        var evidence=new DocumentaryProductionCertificationEvidence($"{request.Metadata.CertificationRunId}.evidence",true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,"1.0",correlation);
        var records=DocumentaryProductionCertificationInventory.Copy(variantRecords);
        var record=new DocumentaryProductionCertificationRecord($"{execution.ExecutionId}.production-certification",request.ExportMaterializationRecord,request.MediaProject,execution,request.Policy,request.Metadata,evidence,records,request.ExportMaterializationRecord.MaterializationId,request.MediaProject.MediaProjectId,execution.ExecutionId,request.MediaProject.TopicId,request.MediaProject.CertificationId,request.MediaProject.ProvenanceId,request.MediaProject.PackageId,request.MediaProject.ReleaseCandidateId,request.MediaProject.ConvergenceId,records.Count,records.Count,records.Sum(x=>x.TraceabilityLinkCount),true);
        DocumentaryProductionCertificationValidator.ValidateRecord(record);
        var result=new DocumentaryProductionCertificationResult(DocumentaryProductionCertificationStatus.Certified,Array.Empty<DocumentaryProductionCertificationRejectionReason>(),record);
        DocumentaryProductionCertificationValidator.ValidateResult(result);return result;
    }
}
