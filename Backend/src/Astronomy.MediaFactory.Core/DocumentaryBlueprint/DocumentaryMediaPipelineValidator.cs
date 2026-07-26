namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryMediaPipelineValidator
{
    public static void ValidateRequest(DocumentaryMediaPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Policy is null || request.Metadata is null || request.MediaProject is null)
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);
        if (!DocumentaryMediaProjectionValidator.ProjectValid(request.MediaProject) || !request.MediaProject.IsComplete)
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete);
        if (request.Metadata.PipelineSchemaVersion != "1.0" || request.Policy.PipelineSchemaVersion != "1.0" ||
            request.Policy.MaximumVisualAttempts < 1 || request.Policy.MaximumNarrationAttempts < 1 || request.Policy.MaximumCompositionAttempts < 1)
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);
        if (request.Metadata.CorrelationId != request.MediaProject.Metadata.CorrelationId)
            Reject(DocumentaryMediaPipelineRejectionReason.CorrelationMismatch);
        if (request.MediaProject.TopicId != request.MediaProject.TopicProfile.TopicId)
            Reject(DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch);
        if (request.Metadata.ExecutionId != $"{request.MediaProject.MediaProjectId}.execution.1")
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch);
        var canonical = Enum.GetValues<DocumentaryMediaVariantType>();
        if (request.MediaProject.Variants.Count != canonical.Length)
            Reject(DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch);
        if (!request.MediaProject.Variants.Select(x => x.VariantType).SequenceEqual(canonical))
            Reject(DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch);
        foreach (var variant in request.MediaProject.Variants)
        {
            if (!variant.Scenes.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, variant.Scenes.Count)))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch);
            if (variant.Scenes.Any(x => x.Narration.Count == 0 || x.SubtitleCues.Count == 0 || x.VisualPrompts.Count == 0 ||
                x.Timing.CorrelationId != request.Metadata.CorrelationId || x.KnowledgeReferences.Count == 0))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch);
        }
        if (request.Policy.VideoFormat != DocumentaryMediaAssetFormat.Mp4 || request.Policy.SubtitleFormat != DocumentaryMediaAssetFormat.Srt ||
            request.Policy.AudioSampleRate <= 0 || request.Policy.AudioChannelCount <= 0 || request.Policy.LongWidth <= 0 || request.Policy.ShortWidth <= 0 ||
            request.Policy.LongFrameRate <= 0 || request.Policy.ShortFrameRate <= 0)
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);
    }

    public static void ValidateExecutionPlan(DocumentaryMediaPipelineExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var canonical = Enum.GetValues<DocumentaryMediaVariantType>();
        if (!plan.IsComplete || plan.VariantCount != canonical.Length || plan.VariantPlans.Count != canonical.Length ||
            !plan.VariantPlans.Select(x => x.VariantType).SequenceEqual(canonical) || plan.AssetCount != plan.AssetPlans.Count ||
            plan.DependencyCount != plan.AssetDependencies.Count || plan.AssetPlans.Select(x => x.AssetId).Distinct().Count() != plan.AssetCount ||
            plan.AssetDependencies.Select(x => x.DependencyId).Distinct().Count() != plan.DependencyCount ||
            !plan.AssetDependencies.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, plan.DependencyCount)) ||
            plan.AssetPlans.SelectMany(x => x.Dependencies).Select(x => x.DependencyId).Order().SequenceEqual(plan.AssetDependencies.Select(x => x.DependencyId).Order()) == false)
            Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);
        var ids = plan.AssetPlans.Select(x => x.AssetId).ToHashSet(StringComparer.Ordinal);
        if (plan.AssetDependencies.Any(x => !ids.Contains(x.SourceAssetId) || !ids.Contains(x.TargetAssetId) || x.SourceAssetId == x.TargetAssetId) || HasCycle(plan))
            Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);
        foreach (var variant in plan.VariantPlans)
            if (variant.SubtitleAssetPlans.Count != variant.SceneVideoAssetPlans.Count || variant.VariantVideoAssetPlan.Dependencies.Count != variant.SceneVideoAssetPlans.Count ||
                variant.AssetCount != variant.SceneAssetPlans.Count + variant.NarrationAssetPlans.Count + variant.SubtitleAssetPlans.Count + variant.SceneVideoAssetPlans.Count + 1)
                Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);
    }

    public static void ValidateExecutionRecord(DocumentaryMediaPipelineExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record); ValidateExecutionPlan(record.ExecutionPlan);
        if (record.VariantCount != 4 || record.VariantRecords.Count != 4 || record.AssetCount != record.OutputManifest.Assets.Count ||
            record.OutputManifest.AssetCount != record.AssetCount || record.OutputManifest.CompletedVariantCount != record.CompletedVariantCount ||
            record.OutputManifest.CorrelationId != record.Metadata.CorrelationId)
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);
        if (record.Status == DocumentaryMediaPipelineStatus.Planned && (record.AssetCount != 0 || record.CompletedVariantCount != 0 || record.VariantRecords.Any(x => x.Status != DocumentaryMediaPipelineStatus.Planned)))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);
        if (record.Status == DocumentaryMediaPipelineStatus.Complete && (record.CompletedVariantCount != 4 || record.FailedAssetCount != 0 ||
            record.VariantRecords.Any(x => x.Status != DocumentaryMediaPipelineStatus.Complete || x.OutputAssetId is null)))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);
        if (record.Status == DocumentaryMediaPipelineStatus.PartiallyComplete && (record.CompletedVariantCount is < 1 or > 3 || record.FailedVariantCount < 1))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);
    }

    private static bool HasCycle(DocumentaryMediaPipelineExecutionPlan plan)
    {
        var incoming = plan.AssetPlans.ToDictionary(x => x.AssetId, _ => 0);
        var outgoing = plan.AssetPlans.ToDictionary(x => x.AssetId, _ => new List<string>());
        foreach (var dependency in plan.AssetDependencies) { incoming[dependency.SourceAssetId]++; outgoing[dependency.TargetAssetId].Add(dependency.SourceAssetId); }
        var queue = new Queue<string>(incoming.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; foreach (var target in outgoing[id]) if (--incoming[target] == 0) queue.Enqueue(target); }
        return visited != incoming.Count;
    }

    private static void Reject(DocumentaryMediaPipelineRejectionReason reason) => throw new ArgumentException(reason.ToString());
}
