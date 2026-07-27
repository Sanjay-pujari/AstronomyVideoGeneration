namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryProductionCertificationValidator
{
    public static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> ValidateRequest(DocumentaryProductionCertificationRequest? request)
    {
        var reasons=new HashSet<DocumentaryProductionCertificationRejectionReason>();
        if(request is null)return One(DocumentaryProductionCertificationRejectionReason.ExportMaterializationNotComplete);
        var m=request.ExportMaterializationRecord;var p=request.MediaProject;var e=request.PipelineExecutionRecord;
        if(m is null||!m.IsComplete)reasons.Add(DocumentaryProductionCertificationRejectionReason.ExportMaterializationNotComplete);
        if(p is null||!p.IsComplete)reasons.Add(DocumentaryProductionCertificationRejectionReason.MediaProjectionNotComplete);
        if(e is null||!e.IsComplete)reasons.Add(DocumentaryProductionCertificationRejectionReason.PipelineExecutionNotComplete);
        if(request.Policy is null||request.Policy.CertificationSchemaVersion!="1.0"||!request.Policy.RequiredVariantTypes.SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes))reasons.Add(DocumentaryProductionCertificationRejectionReason.ArchitectureBoundaryViolation);
        if(m is null||p is null||e is null||request.Metadata is null)return Ordered(reasons);
        if(p.MaterializationId!=m.MaterializationId||p.MediaProjectId!=e.MediaProjectId||!ReferenceEquals(e.MediaProject,p)||p.ExportSpecificationId!=m.ExportSpecificationId||p.CertificationId!=m.CertificationId||p.ProvenanceId!=m.ProvenanceId||p.PackageId!=m.PackageId||p.ReleaseCandidateId!=m.ReleaseCandidateId||p.ConvergenceId!=m.ConvergenceId||request.Metadata.CertificationRunId!=$"{e.ExecutionId}.production-certification.1")reasons.Add(DocumentaryProductionCertificationRejectionReason.IdentityChainMismatch);
        var c=request.Metadata.CorrelationId;
        IEnumerable<string> correlations=new[]{m.Metadata.CorrelationId,m.Manifest.CorrelationId,p.Metadata.CorrelationId,p.TopicProfile.CorrelationId,e.Metadata.CorrelationId,e.ExecutionPlan.CorrelationId,e.OutputManifest.CorrelationId}
            .Concat(m.Payloads.Select(x=>x.CorrelationId)).Concat(m.Payloads.SelectMany(x=>x.Dependencies).Select(x=>x.CorrelationId))
            .Concat(p.Variants.Select(x=>x.CorrelationId)).Concat(p.Variants.SelectMany(x=>x.Scenes).SelectMany(s=>new[]{s.CorrelationId,s.Timing.CorrelationId}))
            .Concat(p.Variants.SelectMany(x=>x.Scenes).SelectMany(x=>x.KnowledgeReferences.Concat(x.Narration.SelectMany(n=>n.KnowledgeReferences)).Concat(x.SubtitleCues.SelectMany(s=>s.KnowledgeReferences)).Concat(x.VisualPrompts.SelectMany(v=>v.KnowledgeReferences))).Select(x=>x.CorrelationId))
            .Concat(e.ExecutionPlan.AssetPlans.Select(x=>x.CorrelationId)).Concat(e.ExecutionPlan.AssetDependencies.Select(x=>x.CorrelationId)).Concat(e.VariantRecords.SelectMany(x=>x.AssetResults).Select(x=>x.CorrelationId)).Concat(e.VariantRecords.Select(x=>x.CorrelationId));
        if(correlations.Any(x=>x!=c))reasons.Add(DocumentaryProductionCertificationRejectionReason.CorrelationChainMismatch);
        if(!ReferenceEquals(m.CertificationRecord,p.CertificationRecord)||!ReferenceEquals(m.ProvenanceRecord,p.ProvenanceRecord)||!ReferenceEquals(m.ProductionPackage,p.ProductionPackage))reasons.Add(DocumentaryProductionCertificationRejectionReason.ProvenanceChainMismatch);
        if(p.TopicId!=p.TopicProfile.TopicId||e.TopicId!=p.TopicId||e.OutputManifest.TopicId!=p.TopicId)reasons.Add(DocumentaryProductionCertificationRejectionReason.TopicChainMismatch);
        ValidateVariants(p,e,m,c,reasons);
        return Ordered(reasons);
    }

    static void ValidateVariants(DocumentaryMediaProject project,DocumentaryMediaPipelineExecutionRecord execution,DocumentaryExportMaterializationRecord materialization,string correlation,HashSet<DocumentaryProductionCertificationRejectionReason> reasons)
    {
        if(!project.Variants.Select(x=>x.VariantType).SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes)||!execution.VariantRecords.Select(x=>x.VariantType).SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes))reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantInventoryMismatch);
        var payloads=materialization.Payloads.ToDictionary(x=>x.PayloadId,StringComparer.Ordinal);var plans=execution.ExecutionPlan.AssetPlans;var results=execution.VariantRecords.SelectMany(x=>x.AssetResults).ToList();
        foreach(var variant in project.Variants)
        {
            var vr=execution.VariantRecords.SingleOrDefault(x=>x.VariantType==variant.VariantType);
            if(vr is null||string.IsNullOrWhiteSpace(vr.OutputAssetId)){reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantOutputMissing);continue;}
            var output=execution.OutputManifest.Assets.SingleOrDefault(x=>x.AssetId==vr.OutputAssetId);
            if(output is null){reasons.Add(DocumentaryProductionCertificationRejectionReason.OutputManifestMismatch);continue;}
            if(vr.Status!=DocumentaryMediaPipelineStatus.Complete||output.AssetType!=DocumentaryMediaAssetType.VariantVideo||output.AssetFormat!=DocumentaryMediaAssetFormat.Mp4||output.Status!=DocumentaryMediaAssetStatus.Verified||string.IsNullOrWhiteSpace(output.Checksum))reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantOutputNotVerified);
            foreach(var scene in variant.Scenes)
            {
                foreach(var kr in scene.KnowledgeReferences)
                    if(!payloads.TryGetValue(kr.PayloadId,out var payload)||payload.PayloadType!=kr.PayloadType||payload.SourceItemId!=kr.SourceItemId||payload.ArtifactIdentity!=kr.ArtifactIdentity||payload.ArtifactVersion!=kr.ArtifactVersion||!kr.JsonPointer.StartsWith('/')||kr.CorrelationId!=correlation)reasons.Add(DocumentaryProductionCertificationRejectionReason.KnowledgeTraceabilityMismatch);
                CheckAssets(scene.Narration.Select(x=>x.NarrationId),DocumentaryMediaAssetType.NarrationAudio,DocumentaryProductionCertificationRejectionReason.NarrationTraceabilityMismatch);
                CheckAssets(new[]{scene.SceneId+".subtitle-cues"},DocumentaryMediaAssetType.SubtitleDocument,DocumentaryProductionCertificationRejectionReason.SubtitleTraceabilityMismatch);
                CheckAssets(scene.VisualPrompts.Select(x=>x.VisualPromptId),null,DocumentaryProductionCertificationRejectionReason.VisualTraceabilityMismatch);
                var scenePlan=plans.SingleOrDefault(x=>x.AssetType==DocumentaryMediaAssetType.SceneVideo&&x.SceneId==scene.SceneId);
                if(scenePlan is null||results.All(x=>x.AssetId!=scenePlan.AssetId||x.Status==DocumentaryMediaAssetStatus.Failed)||scenePlan.Dependencies.Any(d=>results.All(x=>x.AssetId!=d.TargetAssetId&&x.AssetId!=d.SourceAssetId)))reasons.Add(DocumentaryProductionCertificationRejectionReason.SceneAssetTraceabilityMismatch);
            }
            void CheckAssets(IEnumerable<string> ids,DocumentaryMediaAssetType? type,DocumentaryProductionCertificationRejectionReason reason){foreach(var id in ids){var plan=plans.SingleOrDefault(x=>x.SourceInstructionId==id&&(type is null?x.AssetType is >= DocumentaryMediaAssetType.VisualImage and <= DocumentaryMediaAssetType.HistoricalIllustrationImage:x.AssetType==type));if(plan is null||results.All(x=>x.AssetId!=plan.AssetId||x.Status==DocumentaryMediaAssetStatus.Failed))reasons.Add(reason);}}
        }
    }
    public static void ValidateRecord(DocumentaryProductionCertificationRecord record){ArgumentNullException.ThrowIfNull(record);if(!record.IsCertified||record.VariantCount!=4||record.VerifiedOutputCount!=4||record.VariantCertificationRecords.Count!=4||record.Evidence.GetType().GetProperties().Where(x=>x.PropertyType==typeof(bool)).Any(x=>!(bool)x.GetValue(record.Evidence)!))throw new ArgumentException("Certification record is invalid.");}
    public static void ValidateResult(DocumentaryProductionCertificationResult result){ArgumentNullException.ThrowIfNull(result);if(!result.RejectionReasons.SequenceEqual(result.RejectionReasons.Distinct().OrderBy(x=>(int)x))||(result.IsCertified?(result.CertificationRecord is null||result.RejectionReasons.Count!=0):(result.CertificationRecord is not null||result.RejectionReasons.Count==0)))throw new ArgumentException("Certification result is invalid.");}
    static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> Ordered(IEnumerable<DocumentaryProductionCertificationRejectionReason> x)=>DocumentaryProductionCertificationInventory.Copy(x.Distinct().OrderBy(v=>(int)v));
    static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> One(DocumentaryProductionCertificationRejectionReason x)=>Array.AsReadOnly(new[]{x});
}
