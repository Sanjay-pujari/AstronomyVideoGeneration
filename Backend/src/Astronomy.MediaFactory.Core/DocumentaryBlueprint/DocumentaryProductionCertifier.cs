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
            var links=new List<DocumentaryProductionTraceabilityLink>();var plans=execution.ExecutionPlan.AssetPlans;
            foreach(var scene in variant.Scenes.OrderBy(x=>x.Sequence))
            {
                var sceneAsset=plans.Single(x=>x.AssetType==DocumentaryMediaAssetType.SceneVideo&&x.SceneId==scene.SceneId);
                Add(scene.KnowledgeReferences,DocumentaryProductionTraceabilityType.Scene,sceneAsset);
                foreach(var narration in scene.Narration.OrderBy(x=>x.Sequence))Add(narration.KnowledgeReferences,DocumentaryProductionTraceabilityType.Narration,plans.Single(x=>x.AssetType==DocumentaryMediaAssetType.NarrationAudio&&x.SourceInstructionId==narration.NarrationId));
                var subtitleAsset=plans.Single(x=>x.AssetType==DocumentaryMediaAssetType.SubtitleDocument&&x.SceneId==scene.SceneId);
                foreach(var subtitle in scene.SubtitleCues.OrderBy(x=>x.Sequence))Add(subtitle.KnowledgeReferences,DocumentaryProductionTraceabilityType.Subtitle,subtitleAsset);
                foreach(var visual in scene.VisualPrompts.OrderBy(x=>x.Sequence))Add(visual.KnowledgeReferences,DocumentaryProductionTraceabilityType.Visual,plans.Single(x=>IsVisual(x.AssetType)&&x.SourceInstructionId==visual.VisualPromptId));
                void Add(IEnumerable<DocumentaryMediaKnowledgeReference> references,DocumentaryProductionTraceabilityType type,DocumentaryMediaAssetPlan source)
                {
                    foreach(var reference in references.OrderBy(x=>x.Sequence))
                    {
                        var sequence=links.Count;
                        links.Add(new($"{output.AssetId}.trace.{sequence}",type,variant.VariantType,scene.SceneId,source.AssetId,sceneAsset.AssetId,output.AssetId,reference.ReferenceId,reference.PayloadId,reference.SourceItemId,reference.ArtifactIdentity,reference.ArtifactVersion,reference.JsonPointer,sequence,correlation));
                    }
                }
            }
            var frozen=DocumentaryProductionCertificationInventory.Copy(links);
            variantRecords.Add(new($"{execution.ExecutionId}.{variant.VariantType}.certification",variant.VariantId,variant.VariantType,output.AssetId,output.Checksum!,variant.SceneCount,frozen,frozen.Count,true,true,correlation));
        }
        // References are retained from the caller; the certifier never creates or verifies audit evidence.
        var evidence=new DocumentaryProductionCertificationEvidence($"{request.Metadata.CertificationRunId}.evidence",true,true,true,true,true,true,true,true,true,true,true,true,true,true,true,request.EvidencePackage.EvidenceReferences,"1.0",correlation);
        var records=DocumentaryProductionCertificationInventory.Copy(variantRecords);
        var record=new DocumentaryProductionCertificationRecord($"{execution.ExecutionId}.production-certification",request.ExportMaterializationRecord,request.MediaProject,execution,request.Policy,request.Metadata,request.EvidencePackage,evidence,records,request.ExportMaterializationRecord.MaterializationId,request.MediaProject.MediaProjectId,execution.ExecutionId,request.MediaProject.TopicId,request.MediaProject.CertificationId,request.MediaProject.ProvenanceId,request.MediaProject.PackageId,request.MediaProject.ReleaseCandidateId,request.MediaProject.ConvergenceId,records.Count,records.Count,records.Sum(x=>x.TraceabilityLinkCount),true);
        DocumentaryProductionCertificationValidator.ValidateRecord(record);
        var result=new DocumentaryProductionCertificationResult(DocumentaryProductionCertificationStatus.Certified,Array.Empty<DocumentaryProductionCertificationRejectionReason>(),record);
        DocumentaryProductionCertificationValidator.ValidateResult(result);return result;
    }
    private static bool IsVisual(DocumentaryMediaAssetType type)=>type is DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.SkySimulationImage or DocumentaryMediaAssetType.StarChartImage or DocumentaryMediaAssetType.TelescopeViewImage or DocumentaryMediaAssetType.ScientificDiagramImage or DocumentaryMediaAssetType.HistoricalIllustrationImage;
}
