using System.Text.Json;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryProductionCertificationValidator
{
    public static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> ValidateRequest(DocumentaryProductionCertificationRequest? request)
    {
        var reasons=new HashSet<DocumentaryProductionCertificationRejectionReason>();
        if(request is null)return One(DocumentaryProductionCertificationRejectionReason.ExportMaterializationNotComplete);
        var m=request.ExportMaterializationRecord;var p=request.MediaProject;var e=request.PipelineExecutionRecord;
        if(m is null||!m.IsComplete||!DocumentaryExportMaterializationValidator.RecordStructurallyValid(m))reasons.Add(DocumentaryProductionCertificationRejectionReason.ExportMaterializationNotComplete);
        if(p is null||!p.IsComplete||!DocumentaryMediaProjectionValidator.ProjectValid(p))reasons.Add(DocumentaryProductionCertificationRejectionReason.MediaProjectionNotComplete);
        if(e is null||!e.IsComplete||!ExecutionValid(e))reasons.Add(DocumentaryProductionCertificationRejectionReason.PipelineExecutionNotComplete);
        if(request.Policy is null||request.Metadata is null||!PolicyValid(request.Policy))reasons.Add(DocumentaryProductionCertificationRejectionReason.ArchitectureBoundaryViolation);
        if(m is null||p is null||e is null||request.Metadata is null)return Ordered(reasons);
        if(!KnowledgeFoundationValid(m))reasons.Add(DocumentaryProductionCertificationRejectionReason.KnowledgeFoundationNotCertified);
        if(p.MaterializationId!=m.MaterializationId||p.MediaProjectId!=e.MediaProjectId||!Equivalent(e.MediaProject,p)||p.ExportSpecificationId!=m.ExportSpecificationId||p.CertificationId!=m.CertificationId||p.ProvenanceId!=m.ProvenanceId||p.PackageId!=m.PackageId||p.ReleaseCandidateId!=m.ReleaseCandidateId||p.ConvergenceId!=m.ConvergenceId||request.Metadata.CertificationRunId!=$"{e.ExecutionId}.production-certification.1")reasons.Add(DocumentaryProductionCertificationRejectionReason.IdentityChainMismatch);
        var c=request.Metadata.CorrelationId;
        if(AllCorrelations(m,p,e).Any(x=>x!=c))reasons.Add(DocumentaryProductionCertificationRejectionReason.CorrelationChainMismatch);
        if(!Equivalent(m.CertificationRecord,p.CertificationRecord)||!Equivalent(m.ProvenanceRecord,p.ProvenanceRecord)||!Equivalent(m.ProductionPackage,p.ProductionPackage)||!Equivalent(m.ExportSpecification,p.ExportSpecification))reasons.Add(DocumentaryProductionCertificationRejectionReason.ProvenanceChainMismatch);
        if(p.TopicId!=p.TopicProfile.TopicId||e.TopicId!=p.TopicId||e.OutputManifest.TopicId!=p.TopicId)reasons.Add(DocumentaryProductionCertificationRejectionReason.TopicChainMismatch);
        ValidateVariants(p,e,m,c,reasons);
        return Ordered(reasons);
    }

    private static bool PolicyValid(DocumentaryProductionCertificationPolicy p)=>p.CertificationSchemaVersion=="1.0"&&p.RequiredVariantTypes.SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes)&&new[]{p.RequireCertifiedKnowledgeFoundation,p.RequireCompleteExportMaterialization,p.RequireCompleteMediaProjection,p.RequireCompletePipelineExecution,p.RequireFourVerifiedVariants,p.RequireExactIdentityChain,p.RequireExactCorrelationChain,p.RequireExactProvenanceChain,p.RequireExactTopicChain,p.RequireKnowledgeTraceability,p.RequireNarrationTraceability,p.RequireSubtitleTraceability,p.RequireVisualTraceability,p.RequireSceneAssetTraceability,p.RequireDeterminism,p.RequireNonMutation,p.RequireSerializationStability,p.RequireArchitectureBoundaryCompliance,p.RequireDocumentationCompliance}.All(x=>x);
    private static bool ExecutionValid(DocumentaryMediaPipelineExecutionRecord record){try{DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record);return true;}catch(ArgumentException){return false;}}
    private static bool KnowledgeFoundationValid(DocumentaryExportMaterializationRecord m)
    {
        try
        {
            DocumentaryExportSpecificationValidator.ValidateCertificationRecord(m.CertificationRecord);
            DocumentaryProvenanceValidator.ValidateRecord(m.ProvenanceRecord);
            return m.ExportSpecification is not null&&m.ProductionPackage is not null&&m.CertificationRecord is not null&&m.ProvenanceRecord is not null&&m.ExportSpecificationId==m.ExportSpecification.ExportSpecificationId&&m.CertificationId==m.CertificationRecord.CertificationId&&m.ProvenanceId==m.ProvenanceRecord.ProvenanceId&&m.PackageId==m.ProductionPackage.PackageId;
        }
        catch(ArgumentException){return false;}
    }
    private static IEnumerable<string> AllCorrelations(DocumentaryExportMaterializationRecord m,DocumentaryMediaProject p,DocumentaryMediaPipelineExecutionRecord e)=>new[]{m.Metadata.CorrelationId,m.Manifest.CorrelationId,p.Metadata.CorrelationId,p.TopicProfile.CorrelationId,e.Metadata.CorrelationId,e.ExecutionPlan.CorrelationId,e.OutputManifest.CorrelationId}
        .Concat(m.Payloads.Select(x=>x.CorrelationId)).Concat(m.Payloads.SelectMany(x=>x.Dependencies).Select(x=>x.CorrelationId)).Concat(p.Variants.Select(x=>x.CorrelationId)).Concat(p.Variants.SelectMany(x=>x.Scenes).SelectMany(s=>new[]{s.CorrelationId,s.Timing.CorrelationId})).Concat(AllReferences(p).Select(x=>x.CorrelationId)).Concat(e.ExecutionPlan.AssetPlans.Select(x=>x.CorrelationId)).Concat(e.ExecutionPlan.AssetDependencies.Select(x=>x.CorrelationId)).Concat(e.VariantRecords.SelectMany(x=>x.AssetResults).Select(x=>x.CorrelationId)).Concat(e.VariantRecords.Select(x=>x.CorrelationId));

    static void ValidateVariants(DocumentaryMediaProject project,DocumentaryMediaPipelineExecutionRecord execution,DocumentaryExportMaterializationRecord materialization,string correlation,HashSet<DocumentaryProductionCertificationRejectionReason> reasons)
    {
        if(!project.Variants.Select(x=>x.VariantType).SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes)||!execution.VariantRecords.Select(x=>x.VariantType).SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes))reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantInventoryMismatch);
        var payloads=materialization.Payloads.ToDictionary(x=>x.PayloadId,StringComparer.Ordinal);var plans=execution.ExecutionPlan.AssetPlans;var results=execution.OutputManifest.Assets;
        foreach(var type in DocumentaryProductionCertificationInventory.VariantTypes)
        {
            var variant=project.Variants.SingleOrDefault(x=>x.VariantType==type);var vr=execution.VariantRecords.SingleOrDefault(x=>x.VariantType==type);
            if(variant is null){reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantInventoryMismatch);continue;}
            if(vr is null||string.IsNullOrWhiteSpace(vr.OutputAssetId)){reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantOutputMissing);continue;}
            var output=execution.OutputManifest.Assets.SingleOrDefault(x=>x.AssetId==vr.OutputAssetId);
            if(output is null){reasons.Add(DocumentaryProductionCertificationRejectionReason.OutputManifestMismatch);continue;}
            var isLong=variant.Format==DocumentaryVideoFormat.Long;var width=isLong?1920:1080;var height=isLong?1080:1920;
            if(vr.Status!=DocumentaryMediaPipelineStatus.Complete||output.AssetType!=DocumentaryMediaAssetType.VariantVideo||output.AssetFormat!=DocumentaryMediaAssetFormat.Mp4||output.Status!=DocumentaryMediaAssetStatus.Verified||string.IsNullOrWhiteSpace(output.Checksum)||output.Width!=width||output.Height!=height||variant.AspectRatio!=(isLong?"16:9":"9:16")||vr.CompletedSceneCount!=variant.SceneCount)reasons.Add(DocumentaryProductionCertificationRejectionReason.VariantOutputNotVerified);
            foreach(var scene in variant.Scenes)
            {
                CheckReferences(scene.KnowledgeReferences,DocumentaryProductionCertificationRejectionReason.KnowledgeTraceabilityMismatch);
                foreach(var n in scene.Narration){CheckReferences(n.KnowledgeReferences,DocumentaryProductionCertificationRejectionReason.NarrationTraceabilityMismatch);CheckAsset(n.NarrationId,DocumentaryMediaAssetType.NarrationAudio,DocumentaryProductionCertificationRejectionReason.NarrationTraceabilityMismatch);}
                foreach(var s in scene.SubtitleCues)CheckReferences(s.KnowledgeReferences,DocumentaryProductionCertificationRejectionReason.SubtitleTraceabilityMismatch);
                CheckAsset(scene.SceneId+".subtitle-cues",DocumentaryMediaAssetType.SubtitleDocument,DocumentaryProductionCertificationRejectionReason.SubtitleTraceabilityMismatch);
                foreach(var v in scene.VisualPrompts){CheckReferences(v.KnowledgeReferences,DocumentaryProductionCertificationRejectionReason.VisualTraceabilityMismatch);CheckAsset(v.VisualPromptId,null,DocumentaryProductionCertificationRejectionReason.VisualTraceabilityMismatch);}
                if(!SceneChainValid(scene,variant,vr,plans,results))reasons.Add(DocumentaryProductionCertificationRejectionReason.SceneAssetTraceabilityMismatch);
            }
            void CheckReferences(IEnumerable<DocumentaryMediaKnowledgeReference> refs,DocumentaryProductionCertificationRejectionReason reason){foreach(var kr in refs)if(!payloads.TryGetValue(kr.PayloadId,out var payload)||payload.PayloadType!=kr.PayloadType||payload.SourceItemId!=kr.SourceItemId||payload.ArtifactIdentity!=kr.ArtifactIdentity||payload.ArtifactVersion!=kr.ArtifactVersion||kr.CorrelationId!=correlation||!PointerResolves(payload.Content,kr.JsonPointer))reasons.Add(reason);}
            void CheckAsset(string id,DocumentaryMediaAssetType? assetType,DocumentaryProductionCertificationRejectionReason reason){var plan=plans.SingleOrDefault(x=>x.SourceInstructionId==id&&(assetType is null?IsVisual(x.AssetType):x.AssetType==assetType));if(plan is null||results.All(x=>x.AssetId!=plan.AssetId||!IsSuccessful(x.Status)))reasons.Add(reason);}
        }
    }
    private static bool SceneChainValid(DocumentaryMediaScene scene,DocumentaryMediaVariant variant,DocumentaryMediaVariantExecutionRecord vr,IReadOnlyList<DocumentaryMediaAssetPlan> plans,IReadOnlyList<DocumentaryMediaAssetResult> results)
    {
        var sceneVideo=plans.SingleOrDefault(x=>x.AssetType==DocumentaryMediaAssetType.SceneVideo&&x.SceneId==scene.SceneId);var variantVideo=plans.SingleOrDefault(x=>x.AssetType==DocumentaryMediaAssetType.VariantVideo&&x.VariantType==variant.VariantType);
        if(sceneVideo is null||variantVideo is null||vr.OutputAssetId!=variantVideo.AssetId||!AssetSuccessful(sceneVideo.AssetId)||!AssetSuccessful(variantVideo.AssetId))return false;
        var required=plans.Where(x=>x.SceneId==scene.SceneId&&x.AssetType is DocumentaryMediaAssetType.NarrationAudio or DocumentaryMediaAssetType.SubtitleDocument||x.SceneId==scene.SceneId&&IsVisual(x.AssetType)).Select(x=>x.AssetId).ToArray();
        return required.Length>=3&&required.All(AssetSuccessful)&&required.All(id=>sceneVideo.Dependencies.Any(d=>d.SourceAssetId==sceneVideo.AssetId&&d.TargetAssetId==id)||sceneVideo.Dependencies.Any(d=>d.TargetAssetId==sceneVideo.AssetId&&d.SourceAssetId==id))&&(variantVideo.Dependencies.Any(d=>d.SourceAssetId==variantVideo.AssetId&&d.TargetAssetId==sceneVideo.AssetId)||variantVideo.Dependencies.Any(d=>d.TargetAssetId==variantVideo.AssetId&&d.SourceAssetId==sceneVideo.AssetId));
        bool AssetSuccessful(string id)=>results.Any(x=>x.AssetId==id&&IsSuccessful(x.Status));
    }
    private static bool IsSuccessful(DocumentaryMediaAssetStatus status)=>status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified;
    private static bool IsVisual(DocumentaryMediaAssetType x)=>x is DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.SkySimulationImage or DocumentaryMediaAssetType.StarChartImage or DocumentaryMediaAssetType.TelescopeViewImage or DocumentaryMediaAssetType.ScientificDiagramImage or DocumentaryMediaAssetType.HistoricalIllustrationImage;
    private static bool PointerResolves(string content,string pointer){try{using var document=JsonDocument.Parse(content);var current=document.RootElement;if(pointer=="/")return true;if(string.IsNullOrEmpty(pointer)||pointer[0]!='/')return false;foreach(var token in pointer.Split('/').Skip(1).Select(x=>x.Replace("~1","/",StringComparison.Ordinal).Replace("~0","~",StringComparison.Ordinal))){if(current.ValueKind==JsonValueKind.Object){if(!current.TryGetProperty(token,out current))return false;}else if(current.ValueKind==JsonValueKind.Array&&int.TryParse(token,out var index)&&index>=0&&index<current.GetArrayLength())current=current[index];else return false;}return true;}catch(JsonException){return false;}}
    private static IEnumerable<DocumentaryMediaKnowledgeReference> AllReferences(DocumentaryMediaProject p)=>p.Variants.SelectMany(x=>x.Scenes).SelectMany(s=>s.KnowledgeReferences.Concat(s.Narration.SelectMany(n=>n.KnowledgeReferences)).Concat(s.SubtitleCues.SelectMany(c=>c.KnowledgeReferences)).Concat(s.VisualPrompts.SelectMany(v=>v.KnowledgeReferences)));
    private static bool Equivalent<T>(T left,T right)=>ReferenceEquals(left,right)||JsonSerializer.Serialize(left,JsonSerializerOptions.Web)==JsonSerializer.Serialize(right,JsonSerializerOptions.Web);
    public static void ValidateRecord(DocumentaryProductionCertificationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);var refs=record.Evidence.EvidenceReferences;
        if(!record.IsCertified||record.ProductionCertificationId!=$"{record.PipelineExecutionId}.production-certification"||record.VariantCount!=4||record.VerifiedOutputCount!=4||record.VariantCertificationRecords.Count!=4||record.TraceabilityLinkCount!=record.VariantCertificationRecords.Sum(x=>x.TraceabilityLinkCount)||!record.VariantCertificationRecords.Select(x=>x.VariantType).SequenceEqual(DocumentaryProductionCertificationInventory.VariantTypes)||refs.Count!=Enum.GetValues<CertificationEvidenceType>().Length||!refs.Select(x=>x.EvidenceType).SequenceEqual(Enum.GetValues<CertificationEvidenceType>())||refs.Select(x=>x.Sequence).Where((x,i)=>x!=i).Any()||refs.Any(x=>!x.Verified||string.IsNullOrWhiteSpace(x.EvidenceIdentity)||string.IsNullOrWhiteSpace(x.EvidenceSource))||!record.Evidence.KnowledgeFoundationCertified||!record.Evidence.ExportMaterializationComplete||!record.Evidence.MediaProjectionComplete||!record.Evidence.PipelineExecutionComplete||!record.Evidence.IdentityChainValid||!record.Evidence.CorrelationChainValid||!record.Evidence.ProvenanceChainValid||!record.Evidence.TopicChainValid||!record.Evidence.VariantInventoryValid||!record.Evidence.AllOutputsVerified||!record.Evidence.KnowledgeTraceabilityValid||!record.Evidence.NarrationTraceabilityValid||!record.Evidence.SubtitleTraceabilityValid||!record.Evidence.VisualTraceabilityValid||!record.Evidence.SceneAssetTraceabilityValid)throw new ArgumentException("Certification record is invalid.");
    }
    public static void ValidateResult(DocumentaryProductionCertificationResult result){ArgumentNullException.ThrowIfNull(result);if(!result.RejectionReasons.SequenceEqual(result.RejectionReasons.Distinct().OrderBy(x=>(int)x))||(result.IsCertified?(result.CertificationRecord is null||result.RejectionReasons.Count!=0):(result.CertificationRecord is not null||result.RejectionReasons.Count==0)))throw new ArgumentException("Certification result is invalid.");if(result.CertificationRecord is not null)ValidateRecord(result.CertificationRecord);}
    public static void ValidateSummary(DocumentaryProductionCertificationSummary summary){ArgumentNullException.ThrowIfNull(summary);if(!summary.IsCertified||summary.VariantCount!=4||summary.LongVariantCount!=2||summary.ShortVariantCount!=2||summary.EnglishVariantCount!=2||summary.HindiVariantCount!=2||summary.VerifiedOutputCount!=4||summary.TraceabilityLinkCount<1)throw new ArgumentException("Certification summary is invalid.");}
    static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> Ordered(IEnumerable<DocumentaryProductionCertificationRejectionReason> x)=>DocumentaryProductionCertificationInventory.Copy(x.Distinct().OrderBy(v=>(int)v));
    static IReadOnlyList<DocumentaryProductionCertificationRejectionReason> One(DocumentaryProductionCertificationRejectionReason x)=>Array.AsReadOnly(new[]{x});
}
