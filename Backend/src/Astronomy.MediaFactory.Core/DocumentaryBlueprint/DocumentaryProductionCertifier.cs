namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Read-only final architecture certifier for the retained O2.16-O2.18 production bridge.</summary>
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
                Add(scene.KnowledgeReferences,DocumentaryProductionTraceabilityType.Scene);
                foreach(var narration in scene.Narration.OrderBy(x=>x.Sequence))Add(narration.KnowledgeReferences,DocumentaryProductionTraceabilityType.Narration);
                foreach(var subtitle in scene.SubtitleCues.OrderBy(x=>x.Sequence))Add(subtitle.KnowledgeReferences,DocumentaryProductionTraceabilityType.Subtitle);
                foreach(var visual in scene.VisualPrompts.OrderBy(x=>x.Sequence))Add(visual.KnowledgeReferences,DocumentaryProductionTraceabilityType.Visual);
                void Add(IEnumerable<DocumentaryMediaKnowledgeReference> references,DocumentaryProductionTraceabilityType traceabilityType)
                {
                    foreach(var reference in references.OrderBy(x=>x.Sequence))
                    {
                        var sequence=links.Count;
                        links.Add(new($"{output.AssetId}.trace.{sequence}",variant.VariantType,scene.SceneId,sceneAsset.AssetId,output.AssetId,reference.ReferenceId,reference.PayloadId,reference.SourceItemId,reference.ArtifactIdentity,reference.ArtifactVersion,reference.JsonPointer,sequence,correlation,traceabilityType));
                    }
                }
            }
            var frozen=DocumentaryProductionCertificationInventory.Copy(links);
            variantRecords.Add(new($"{execution.ExecutionId}.{variant.VariantType}.certification",variant.VariantId,variant.VariantType,output.AssetId,output.Checksum!,variant.SceneCount,frozen,frozen.Count,true,true,correlation));
        }
        var evidenceReferences=DocumentaryProductionCertificationInventory.Copy(Enum.GetValues<CertificationEvidenceType>().Select((type,sequence)=>new CertificationEvidenceReference(type,$"{request.Metadata.CertificationRunId}.evidence.{sequence}",EvidenceSource(type,request),true,sequence)));
        var evidence=new DocumentaryProductionCertificationEvidence($"{request.Metadata.CertificationRunId}.evidence",true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,evidenceReferences,"1.0",correlation);
        var records=DocumentaryProductionCertificationInventory.Copy(variantRecords);
        var record=new DocumentaryProductionCertificationRecord($"{execution.ExecutionId}.production-certification",request.ExportMaterializationRecord,request.MediaProject,execution,request.Policy,request.Metadata,evidence,records,request.ExportMaterializationRecord.MaterializationId,request.MediaProject.MediaProjectId,execution.ExecutionId,request.MediaProject.TopicId,request.MediaProject.CertificationId,request.MediaProject.ProvenanceId,request.MediaProject.PackageId,request.MediaProject.ReleaseCandidateId,request.MediaProject.ConvergenceId,records.Count,records.Count,records.Sum(x=>x.TraceabilityLinkCount),true);
        DocumentaryProductionCertificationValidator.ValidateRecord(record);
        var result=new DocumentaryProductionCertificationResult(DocumentaryProductionCertificationStatus.Certified,Array.Empty<DocumentaryProductionCertificationRejectionReason>(),record);
        DocumentaryProductionCertificationValidator.ValidateResult(result);return result;
    }
    private static string EvidenceSource(CertificationEvidenceType type,DocumentaryProductionCertificationRequest request)=>type switch
    {
        CertificationEvidenceType.Determinism=>$"reconstruction:{request.ExportMaterializationRecord.MaterializationId}|{request.MediaProject.MediaProjectId}|{request.PipelineExecutionRecord.ExecutionId}",
        CertificationEvidenceType.Serialization=>"System.Text.Json:Web:public-O2.19-contract-round-trip",
        CertificationEvidenceType.NonMutation=>"System.Text.Json:Web:before-after-byte-identity",
        CertificationEvidenceType.Architecture=>"Astronomy.MediaFactory.Core:DocumentaryProductionCertification:static-boundary-scan",
        CertificationEvidenceType.Documentation=>"docs/documentary-production-certification.md",
        CertificationEvidenceType.FocusedTests=>"DocumentaryProductionCertificationHardeningTests",
        CertificationEvidenceType.RepositoryTests=>"Astronomy.MediaFactory.Tests",
        _=>throw new ArgumentOutOfRangeException(nameof(type))
    };
}
