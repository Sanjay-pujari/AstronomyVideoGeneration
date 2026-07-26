namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryMediaProjectionValidator
{
    public static bool PolicyValid(DocumentaryMediaProjectionPolicy? p)=>p is not null&&p.RequireCompleteMaterialization&&p.RequireFourCanonicalVariants&&p.RequireCanonicalVariantOrdering&&p.RequireCanonicalSceneOrdering&&p.RequireExactCorrelation&&p.RequireDeterministicIdentity&&p.RequireNarrativeTraceability&&p.RequireSubtitleTraceability&&p.RequireVisualTraceability&&p.RequireTimingConsistency&&p.RequiredVariantTypes.SequenceEqual(DocumentaryMediaProjectionInventory.Variants)&&p.SupportedLanguages.SequenceEqual(DocumentaryMediaProjectionInventory.Languages)&&p.SupportedFormats.SequenceEqual(DocumentaryMediaProjectionInventory.Formats)&&p.SupportedTopicFamilies.SequenceEqual(DocumentaryMediaProjectionInventory.TopicFamilies)&&p.LongMinimumSceneCount>=1&&p.LongMaximumSceneCount>=p.LongMinimumSceneCount&&p.ShortMinimumSceneCount>=1&&p.ShortMaximumSceneCount>=p.ShortMinimumSceneCount&&p.LongMinimumDurationSeconds>=1&&p.LongMaximumDurationSeconds>=p.LongMinimumDurationSeconds&&p.ShortMinimumDurationSeconds>=1&&p.ShortMaximumDurationSeconds>=p.ShortMinimumDurationSeconds&&p.MaximumNarrationCharactersPerScene>=1&&p.MaximumSubtitleCharactersPerLine>=1&&p.MaximumSubtitleLines is 1 or 2&&p.PolicySchemaVersion=="1.0";

    internal static IReadOnlyList<DocumentaryMediaProjectionRejectionReason> ValidateRequest(DocumentaryMediaProjectionRequest request)
    {
        var r=request.MaterializationRecord;var reasons=new List<DocumentaryMediaProjectionRejectionReason>();
        if(!r.IsComplete)reasons.Add(DocumentaryMediaProjectionRejectionReason.MaterializationRecordNotComplete);
        if(r.MaterializationId!=$"{r.ExportSpecificationId}.materialization")reasons.Add(DocumentaryMediaProjectionRejectionReason.MaterializationIdentityMismatch);
        if(r.ExportSpecificationId!=r.ExportSpecification.ExportSpecificationId)reasons.Add(DocumentaryMediaProjectionRejectionReason.ExportSpecificationIdentityMismatch);
        if(r.CertificationId!=r.CertificationRecord.CertificationId)reasons.Add(DocumentaryMediaProjectionRejectionReason.CertificationIdentityMismatch);
        if(r.ProvenanceId!=r.ProvenanceRecord.ProvenanceId)reasons.Add(DocumentaryMediaProjectionRejectionReason.ProvenanceIdentityMismatch);
        if(r.PackageId!=r.ProductionPackage.PackageId)reasons.Add(DocumentaryMediaProjectionRejectionReason.PackageIdentityMismatch);
        var c=request.Metadata.CorrelationId;var correlations=new[]{request.TopicProfile.CorrelationId,r.Metadata.CorrelationId,r.Manifest.CorrelationId}.Concat(r.Payloads.Select(x=>x.CorrelationId)).Concat(r.Payloads.SelectMany(x=>x.Dependencies).Select(x=>x.CorrelationId));
        if(correlations.Any(x=>!DocumentaryMediaProjectionInventory.Eq(x,c)))reasons.Add(DocumentaryMediaProjectionRejectionReason.CorrelationMismatch);
        if(!PolicyValid(request.Policy))reasons.Add(DocumentaryMediaProjectionRejectionReason.ProjectionPolicyRejected);
        if(!Enum.IsDefined(request.TopicProfile.TopicFamily)||!request.Policy.SupportedTopicFamilies.Contains(request.TopicProfile.TopicFamily))reasons.Add(DocumentaryMediaProjectionRejectionReason.TopicProfileRejected);
        return reasons.Distinct().OrderBy(x=>(int)x).ToArray();
    }

    internal static IReadOnlyList<DocumentaryMediaProjectionRejectionReason> ValidateVariants(DocumentaryMediaProjectionRequest request,IReadOnlyList<DocumentaryMediaVariant> variants)
    {
        var reasons=new List<DocumentaryMediaProjectionRejectionReason>();var expected=DocumentaryMediaProjectionInventory.Variants;
        if(expected.Any(x=>variants.All(v=>v.VariantType!=x)))reasons.Add(DocumentaryMediaProjectionRejectionReason.RequiredVariantMissing);
        if(variants.Count!=expected.Length)reasons.Add(DocumentaryMediaProjectionRejectionReason.VariantInventoryMismatch);
        if(variants.Any(v=>!Enum.IsDefined(v.VariantType)))reasons.Add(DocumentaryMediaProjectionRejectionReason.UnsupportedVariantPresent);
        if(!variants.Select(v=>v.VariantType).SequenceEqual(expected))reasons.Add(DocumentaryMediaProjectionRejectionReason.VariantOrderMismatch);
        var projectId=$"{request.MaterializationRecord.MaterializationId}.media-project";
        foreach(var v in variants.Where(v=>Enum.IsDefined(v.VariantType))){if(v.VariantId!=$"{projectId}.{v.VariantType}")reasons.Add(DocumentaryMediaProjectionRejectionReason.VariantIdentityMismatch);reasons.AddRange(ValidateScenes(v,v.Scenes,request.Policy));}
        return reasons.Distinct().OrderBy(x=>(int)x).ToArray();
    }

    internal static IReadOnlyList<DocumentaryMediaProjectionRejectionReason> ValidateScenes(DocumentaryMediaVariant variant,IReadOnlyList<DocumentaryMediaScene> scenes,DocumentaryMediaProjectionPolicy policy)
    {
        var r=new List<DocumentaryMediaProjectionRejectionReason>();var map=DocumentaryMediaProjectionInventory.Mapping(variant.VariantType);var min=map.Format==DocumentaryVideoFormat.Long?policy.LongMinimumSceneCount:policy.ShortMinimumSceneCount;var max=map.Format==DocumentaryVideoFormat.Long?policy.LongMaximumSceneCount:policy.ShortMaximumSceneCount;
        if(scenes.Count<min||scenes.Count>max||variant.SceneCount!=scenes.Count)r.Add(DocumentaryMediaProjectionRejectionReason.SceneInventoryMismatch);
        if(!scenes.Select(s=>s.Sequence).SequenceEqual(Enumerable.Range(0,scenes.Count)))r.Add(DocumentaryMediaProjectionRejectionReason.SceneOrderMismatch);
        long start=0;for(var i=0;i<scenes.Count;i++){var s=scenes[i];if(s.SceneId!=$"{variant.VariantId}.scene.{i}")r.Add(DocumentaryMediaProjectionRejectionReason.SceneIdentityMismatch);if(s.VariantType!=variant.VariantType||s.Narration.Count==0||s.Narration.Any(n=>n.NarrationId!=$"{s.SceneId}.narration.{n.Sequence}"||n.Language!=variant.Language||n.Text.Length>policy.MaximumNarrationCharactersPerScene||n.KnowledgeReferences.Count==0))r.Add(DocumentaryMediaProjectionRejectionReason.NarrativeMappingMismatch);if(s.SubtitleCues.Count==0||s.SubtitleCues.Any(c=>c.SubtitleCueId!=$"{s.SceneId}.subtitle.{c.Sequence}"||c.Language!=variant.Language||!s.Narration.Any(n=>n.NarrationId==c.NarrationId)||c.Line1.Length>policy.MaximumSubtitleCharactersPerLine||(c.Line2?.Length??0)>policy.MaximumSubtitleCharactersPerLine))r.Add(DocumentaryMediaProjectionRejectionReason.SubtitleMappingMismatch);if(s.VisualPrompts.Count==0||s.VisualPrompts.Any(v=>v.VisualPromptId!=$"{s.SceneId}.visual.{v.Sequence}"||v.AspectRatio!=variant.AspectRatio||v.KnowledgeReferences.Count==0))r.Add(DocumentaryMediaProjectionRejectionReason.VisualPromptMappingMismatch);if(s.Timing.TimingId!=$"{s.SceneId}.timing"||s.Timing.PlannedStartMilliseconds!=start||s.Timing.PlannedEndMilliseconds-s.Timing.PlannedStartMilliseconds!=s.Timing.PlannedDurationMilliseconds)r.Add(DocumentaryMediaProjectionRejectionReason.TimingPlanMismatch);start=s.Timing.PlannedEndMilliseconds;}
        var minMs=(map.Format==DocumentaryVideoFormat.Long?policy.LongMinimumDurationSeconds:policy.ShortMinimumDurationSeconds)*1000L;var maxMs=(map.Format==DocumentaryVideoFormat.Long?policy.LongMaximumDurationSeconds:policy.ShortMaximumDurationSeconds)*1000L;if(start<minMs||start>maxMs||variant.PlannedDurationMilliseconds!=start)r.Add(DocumentaryMediaProjectionRejectionReason.TimingPlanMismatch);
        return r.Distinct().OrderBy(x=>(int)x).ToArray();
    }
    public static bool ProjectValid(DocumentaryMediaProject? p)=>p is not null&&p.IsComplete&&PolicyValid(p.Policy)&&ValidateVariants(new(p.MaterializationRecord,p.Policy,p.Metadata,p.TopicProfile),p.Variants).Count==0;
}
