namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Coordinates O2.17 instructions through logical media ports; it never performs physical I/O.</summary>
public sealed class DocumentaryMediaPipelineOrchestrator
{
    readonly DocumentaryMediaProviderRegistry providers; readonly DocumentaryMediaPipelinePlanner planner=new();
    public DocumentaryMediaPipelineOrchestrator(DocumentaryMediaProviderRegistry providers)=>this.providers=providers??throw new ArgumentNullException(nameof(providers));
    public DocumentaryMediaPipelineResult Execute(DocumentaryMediaPipelineRequest request)
    {
        DocumentaryMediaPipelineExecutionPlan plan;
        try { plan=planner.Plan(request); }
        catch(ArgumentException e) { var reason=Enum.TryParse<DocumentaryMediaPipelineRejectionReason>(e.Message,out var parsed)?parsed:DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected;return new(DocumentaryMediaPipelineStatus.Rejected,new[]{reason},null); }
        if(request.Policy.Mode==DocumentaryMediaPipelineMode.Execute&&!providers.IsComplete)return new(DocumentaryMediaPipelineStatus.Rejected,new[]{DocumentaryMediaPipelineRejectionReason.ProviderUnavailable},null);
        if(request.Policy.Mode==DocumentaryMediaPipelineMode.Execute) return ExecuteProviders(request,plan);
        var records=request.MediaProject.Variants.Select(v=>new DocumentaryMediaVariantExecutionRecord(request.Metadata.ExecutionId+"."+v.VariantType,v.VariantId,v.VariantType,DocumentaryMediaPipelineStatus.Complete,Array.Empty<DocumentaryMediaAssetResult>(),0,0,v.PlannedDurationMilliseconds,v.PlannedDurationMilliseconds,null,Array.Empty<DocumentaryMediaPipelineRejectionReason>(),request.Metadata.CorrelationId)).ToArray();
        return Finish(request,plan,records,DocumentaryMediaPipelineStatus.Complete,Array.Empty<DocumentaryMediaPipelineRejectionReason>());
    }
    DocumentaryMediaPipelineResult ExecuteProviders(DocumentaryMediaPipelineRequest request,DocumentaryMediaPipelineExecutionPlan plan)
    {
        // Providers own bytes; orchestration retains deterministic logical identity and isolates each variant.
        var records=new List<DocumentaryMediaVariantExecutionRecord>();
        foreach(var variant in request.MediaProject.Variants)
        {
            var vp=plan.VariantPlans.Single(x=>x.VariantId==variant.VariantId);var results=new List<DocumentaryMediaAssetResult>();var reasons=new List<DocumentaryMediaPipelineRejectionReason>();
            foreach(var scene in variant.Scenes)
            {
                foreach(var prompt in scene.VisualPrompts){var ap=vp.SceneAssetPlans.Single(x=>x.SourceInstructionId==prompt.VisualPromptId);DocumentaryVisualGenerationResult? result=null;for(var attempt=1;attempt<=request.Policy.MaximumVisualAttempts;attempt++){result=providers.Visual!.Generate(new(ap,prompt,ap.ExpectedWidth,ap.ExpectedHeight,ap.AssetFormat,attempt,request.Metadata.CorrelationId));if(result.Status!=DocumentaryMediaAssetStatus.Failed)break;}results.Add(result!.AssetResult);if(result.Status==DocumentaryMediaAssetStatus.Failed)reasons.Add(DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed);}
                if(reasons.Count>0)continue;
                foreach(var block in scene.Narration){var ap=vp.NarrationAssetPlans.Single(x=>x.SourceInstructionId==block.NarrationId);var n=providers.Narration!.Synthesize(new(ap,block,variant.Language==DocumentaryMediaLanguage.English?"English":"Hindi",variant.Language,ap.AssetFormat,request.Policy.AudioSampleRate,request.Policy.AudioChannelCount,1,request.Metadata.CorrelationId));results.Add(n.AssetResult);if(n.Status==DocumentaryMediaAssetStatus.Failed)reasons.Add(DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed);}
            }
            records.Add(new(request.Metadata.ExecutionId+"."+variant.VariantType,variant.VariantId,variant.VariantType,reasons.Count==0?DocumentaryMediaPipelineStatus.Complete:DocumentaryMediaPipelineStatus.Rejected,results,reasons.Count==0?variant.SceneCount:0,reasons.Count==0?0:variant.SceneCount,variant.PlannedDurationMilliseconds,variant.PlannedDurationMilliseconds,null,reasons.Distinct().Order().ToArray(),request.Metadata.CorrelationId));
        }
        var complete=records.Count(x=>x.Status==DocumentaryMediaPipelineStatus.Complete);var status=complete==4?DocumentaryMediaPipelineStatus.Complete:complete>0&&request.Policy.AllowPartialCompletion?DocumentaryMediaPipelineStatus.PartiallyComplete:DocumentaryMediaPipelineStatus.Rejected;var reasons=records.SelectMany(x=>x.RejectionReasons).Distinct().Order().ToArray();return Finish(request,plan,records,status,reasons);
    }
    static DocumentaryMediaPipelineResult Finish(DocumentaryMediaPipelineRequest q,DocumentaryMediaPipelineExecutionPlan p,IReadOnlyList<DocumentaryMediaVariantExecutionRecord> variants,DocumentaryMediaPipelineStatus status,IReadOnlyList<DocumentaryMediaPipelineRejectionReason> reasons)
    {var assets=variants.SelectMany(x=>x.AssetResults).ToArray();var completed=variants.Count(x=>x.Status==DocumentaryMediaPipelineStatus.Complete);var manifest=new DocumentaryMediaOutputManifest(q.Metadata.ExecutionId+".manifest",q.Metadata.ExecutionId,q.MediaProject.MediaProjectId,q.MediaProject.TopicId,variants,assets,assets.Where(x=>x.Checksum is not null).Select(x=>x.Checksum!).ToArray(),4,assets.Length,completed,4-completed,"1.0",q.Metadata.CorrelationId);var record=new DocumentaryMediaPipelineExecutionRecord(q.Metadata.ExecutionId,q.MediaProject,q.Policy,q.Metadata,p,variants,manifest,status,q.MediaProject.MediaProjectId,q.MediaProject.MaterializationId,q.MediaProject.TopicId,4,completed,4-completed,assets.Length,assets.Count(x=>x.Status==DocumentaryMediaAssetStatus.Generated),assets.Count(x=>x.Status==DocumentaryMediaAssetStatus.Verified),assets.Count(x=>x.Status==DocumentaryMediaAssetStatus.Failed),q.MediaProject.TotalPlannedDurationMilliseconds,variants.Sum(x=>x.EffectiveDurationMilliseconds));return new(status,reasons,record);}
}
