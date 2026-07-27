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
        if (request.MediaProject.MaterializationId != request.MediaProject.MaterializationRecord.MaterializationId)
            Reject(DocumentaryMediaPipelineRejectionReason.MaterializationIdentityMismatch);
        if (request.MediaProject.TopicId != request.MediaProject.TopicProfile.TopicId)
            Reject(DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch);
        if (request.Metadata.ExecutionId != $"{request.MediaProject.MediaProjectId}.execution.1")
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch);
        var canonical = Enum.GetValues<DocumentaryMediaVariantType>();
        if (canonical.Any(type => request.MediaProject.Variants.All(x => x.VariantType != type)))
            Reject(DocumentaryMediaPipelineRejectionReason.RequiredVariantMissing);
        if (request.MediaProject.Variants.Count != canonical.Length)
            Reject(DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch);
        if (!request.MediaProject.Variants.Select(x => x.VariantType).SequenceEqual(canonical))
            Reject(DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch);
        foreach (var variant in request.MediaProject.Variants)
        {
            if (variant.VariantId != $"{request.MediaProject.MediaProjectId}.{variant.VariantType}")
                Reject(DocumentaryMediaPipelineRejectionReason.VariantIdentityMismatch);
            if (!variant.Scenes.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, variant.Scenes.Count)))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch);
            if (variant.Scenes.Any(x => x.Narration.Count == 0 || x.SubtitleCues.Count == 0 || x.VisualPrompts.Count == 0 ||
                x.Timing.CorrelationId != request.Metadata.CorrelationId || x.KnowledgeReferences.Count == 0))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch);
            foreach (var scene in variant.Scenes)
            {
                if (scene.SceneId != $"{variant.VariantId}.scene.{scene.Sequence}" || scene.VariantType != variant.VariantType)
                    Reject(DocumentaryMediaPipelineRejectionReason.SceneIdentityMismatch);
                if (!scene.Narration.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.Narration.Count)) ||
                    scene.Narration.Any(x => x.Language != variant.Language || x.CorrelationId != request.Metadata.CorrelationId ||
                        x.NarrationId != $"{scene.SceneId}.narration.{x.Sequence}" || x.KnowledgeReferences.Count == 0))
                    Reject(DocumentaryMediaPipelineRejectionReason.NarrationPlanRejected);
                if (!scene.SubtitleCues.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.SubtitleCues.Count)) ||
                    scene.SubtitleCues.Any(x => x.Language != variant.Language || x.CorrelationId != request.Metadata.CorrelationId ||
                        !scene.Narration.Any(n => n.NarrationId == x.NarrationId) || x.EndOffsetMilliseconds <= x.StartOffsetMilliseconds || x.KnowledgeReferences.Count == 0))
                    Reject(DocumentaryMediaPipelineRejectionReason.SubtitlePlanRejected);
                if (!scene.VisualPrompts.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.VisualPrompts.Count)) ||
                    scene.VisualPrompts.Any(x => x.VisualPromptId != $"{scene.SceneId}.visual.{x.Sequence}" || x.AspectRatio != variant.AspectRatio ||
                        x.SubjectIds.Count == 0 || x.KnowledgeReferences.Count == 0))
                    Reject(DocumentaryMediaPipelineRejectionReason.VisualPlanRejected);
                if (scene.Timing.PlannedEndMilliseconds - scene.Timing.PlannedStartMilliseconds != scene.Timing.PlannedDurationMilliseconds ||
                    scene.Timing.NarrationDurationMilliseconds <= 0 || scene.Timing.NarrationDurationMilliseconds > scene.Timing.PlannedDurationMilliseconds)
                    Reject(DocumentaryMediaPipelineRejectionReason.TimingPlanRejected);
                if (!Enum.IsDefined(scene.Transition)) Reject(DocumentaryMediaPipelineRejectionReason.TransitionPlanRejected);
            }
            if (variant.PlannedDurationMilliseconds != variant.Scenes.Sum(x => x.Timing.PlannedDurationMilliseconds))
                Reject(DocumentaryMediaPipelineRejectionReason.TimingPlanRejected);
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
        if (plan.AssetPlans.Any(x => !Enum.IsDefined(x.AssetType) || !Enum.IsDefined(x.AssetFormat) || !SupportedMapping(x.AssetType, x.AssetFormat)))
            Reject(DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType);
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

    private static bool SupportedMapping(DocumentaryMediaAssetType type, DocumentaryMediaAssetFormat format) => type switch
    {
        DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.SkySimulationImage or DocumentaryMediaAssetType.StarChartImage or
            DocumentaryMediaAssetType.TelescopeViewImage or DocumentaryMediaAssetType.ScientificDiagramImage or DocumentaryMediaAssetType.HistoricalIllustrationImage
                => format is DocumentaryMediaAssetFormat.Png or DocumentaryMediaAssetFormat.Jpeg or DocumentaryMediaAssetFormat.WebP,
        DocumentaryMediaAssetType.NarrationAudio => format is DocumentaryMediaAssetFormat.Wav or DocumentaryMediaAssetFormat.Mp3 or DocumentaryMediaAssetFormat.Aac,
        DocumentaryMediaAssetType.SubtitleDocument => format is DocumentaryMediaAssetFormat.Srt or DocumentaryMediaAssetFormat.Vtt,
        DocumentaryMediaAssetType.SceneVideo or DocumentaryMediaAssetType.VariantVideo => format == DocumentaryMediaAssetFormat.Mp4,
        _ => false
    };

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
