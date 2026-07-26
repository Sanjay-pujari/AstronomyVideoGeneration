namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public static class DocumentaryMediaPipelineValidator
{
    public static void ValidateRequest(DocumentaryMediaPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); if(!request.MediaProject.IsComplete)throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete));
        if(request.MediaProject.Variants.Count!=4||!request.MediaProject.Variants.Select(x=>x.VariantType).SequenceEqual(Enum.GetValues<DocumentaryMediaVariantType>()))throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch));
        if(request.Metadata.PipelineSchemaVersion!="1.0"||request.Policy.PipelineSchemaVersion!="1.0")throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected));
        if(request.Metadata.CorrelationId!=request.MediaProject.Metadata.CorrelationId||request.MediaProject.TopicId!=request.MediaProject.TopicProfile.TopicId)throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.CorrelationMismatch));
        if(request.Metadata.ExecutionId!=request.MediaProject.MediaProjectId+".execution."+request.Metadata.ExecutionId.Split('.').Last())throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch));
        if(request.MediaProject.Variants.Any(v=>!v.Scenes.Select(x=>x.Sequence).SequenceEqual(Enumerable.Range(0,v.Scenes.Count))))throw new ArgumentException(nameof(DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch));
    }
    public static void ValidateExecutionPlan(DocumentaryMediaPipelineExecutionPlan plan){if(!plan.IsComplete||plan.VariantCount!=4||plan.AssetCount!=plan.AssetPlans.Count||plan.DependencyCount!=plan.AssetDependencies.Count)throw new ArgumentException("Invalid execution plan.");}
}
