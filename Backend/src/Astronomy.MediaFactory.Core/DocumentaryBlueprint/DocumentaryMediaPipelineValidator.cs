namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public static class DocumentaryMediaPipelineValidator
{
    public static void ValidateRequest(DocumentaryMediaPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Policy is null || request.Metadata is null || request.MediaProject is null)
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);

        var project = request.MediaProject;

        // Stage 1: classify top-level O2.18 ownership before invoking the broad O2.17 validator.
        // This keeps the detailed O2.18 rejection inventory executable and auditable.
        if (!project.IsComplete)
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete);

        if (request.Metadata.PipelineSchemaVersion != "1.0" || request.Policy.PipelineSchemaVersion != "1.0" ||
            request.Policy.MaximumVisualAttempts < 1 || request.Policy.MaximumNarrationAttempts < 1 ||
            request.Policy.MaximumCompositionAttempts < 1 || !Enum.IsDefined(request.Policy.Mode))
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);

        if (project.MediaProjectId != $"{project.MaterializationId}.media-project" ||
            request.Metadata.ExecutionId != $"{project.MediaProjectId}.execution.1")
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch);

        if (project.MaterializationId != project.MaterializationRecord.MaterializationId ||
            project.ExportSpecificationId != project.MaterializationRecord.ExportSpecificationId ||
            project.CertificationId != project.MaterializationRecord.CertificationId ||
            project.ProvenanceId != project.MaterializationRecord.ProvenanceId ||
            project.PackageId != project.MaterializationRecord.PackageId ||
            project.ReleaseCandidateId != project.MaterializationRecord.ReleaseCandidateId ||
            project.ConvergenceId != project.MaterializationRecord.ConvergenceId)
            Reject(DocumentaryMediaPipelineRejectionReason.MaterializationIdentityMismatch);

        if (project.TopicId != project.TopicProfile.TopicId)
            Reject(DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch);

        if (request.Metadata.CorrelationId != project.Metadata.CorrelationId ||
            project.TopicProfile.CorrelationId != request.Metadata.CorrelationId)
            Reject(DocumentaryMediaPipelineRejectionReason.CorrelationMismatch);

        var canonical = Enum.GetValues<DocumentaryMediaVariantType>();
        if (canonical.Any(type => project.Variants.All(x => x.VariantType != type)))
            Reject(DocumentaryMediaPipelineRejectionReason.RequiredVariantMissing);
        if (project.Variants.Count != canonical.Length || project.VariantCount != project.Variants.Count)
            Reject(DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch);
        if (!project.Variants.Select(x => x.VariantType).SequenceEqual(canonical))
            Reject(DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch);

        foreach (var variant in project.Variants)
        {
            if (!Enum.IsDefined(variant.VariantType))
                Reject(DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch);

            var mapping = DocumentaryMediaProjectionInventory.Mapping(variant.VariantType);
            if (variant.VariantId != $"{project.MediaProjectId}.{variant.VariantType}" ||
                variant.Format != mapping.Format || variant.Language != mapping.Language ||
                variant.CorrelationId != request.Metadata.CorrelationId)
                Reject(DocumentaryMediaPipelineRejectionReason.VariantIdentityMismatch);

            if (variant.Scenes.Count == 0 || variant.SceneCount != variant.Scenes.Count ||
                variant.Scenes.Any(x => x.Narration.Count == 0 || x.SubtitleCues.Count == 0 ||
                    x.VisualPrompts.Count == 0 || x.KnowledgeReferences.Count == 0))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch);

            if (!variant.Scenes.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, variant.Scenes.Count)))
                Reject(DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch);

            long expectedStart = 0;
            foreach (var scene in variant.Scenes)
            {
                if (scene.SceneId != $"{variant.VariantId}.scene.{scene.Sequence}" ||
                    scene.VariantType != variant.VariantType || scene.CorrelationId != request.Metadata.CorrelationId)
                    Reject(DocumentaryMediaPipelineRejectionReason.SceneIdentityMismatch);

                if (!scene.Narration.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.Narration.Count)) ||
                    scene.Narration.Any(x => x.Language != variant.Language ||
                        x.CorrelationId != request.Metadata.CorrelationId ||
                        x.NarrationId != $"{scene.SceneId}.narration.{x.Sequence}" ||
                        x.KnowledgeReferences.Count == 0))
                    Reject(DocumentaryMediaPipelineRejectionReason.NarrationPlanRejected);

                if (!scene.SubtitleCues.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.SubtitleCues.Count)) ||
                    scene.SubtitleCues.Any(x => x.Language != variant.Language ||
                        x.CorrelationId != request.Metadata.CorrelationId ||
                        !scene.Narration.Any(n => n.NarrationId == x.NarrationId) ||
                        x.EndOffsetMilliseconds <= x.StartOffsetMilliseconds ||
                        x.KnowledgeReferences.Count == 0))
                    Reject(DocumentaryMediaPipelineRejectionReason.SubtitlePlanRejected);

                if (!scene.VisualPrompts.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, scene.VisualPrompts.Count)) ||
                    scene.VisualPrompts.Any(x => x.VisualPromptId != $"{scene.SceneId}.visual.{x.Sequence}" ||
                        x.AspectRatio != variant.AspectRatio || x.SubjectIds.Count == 0 ||
                        x.KnowledgeReferences.Count == 0 || x.CorrelationId != request.Metadata.CorrelationId))
                    Reject(DocumentaryMediaPipelineRejectionReason.VisualPlanRejected);

                if (scene.Timing.TimingId != $"{scene.SceneId}.timing" ||
                    scene.Timing.PlannedStartMilliseconds != expectedStart ||
                    scene.Timing.PlannedEndMilliseconds - scene.Timing.PlannedStartMilliseconds != scene.Timing.PlannedDurationMilliseconds ||
                    scene.Timing.NarrationDurationMilliseconds <= 0 ||
                    scene.Timing.NarrationDurationMilliseconds > scene.Timing.PlannedDurationMilliseconds ||
                    scene.Timing.CorrelationId != request.Metadata.CorrelationId)
                    Reject(DocumentaryMediaPipelineRejectionReason.TimingPlanRejected);

                if (!Enum.IsDefined(scene.Transition))
                    Reject(DocumentaryMediaPipelineRejectionReason.TransitionPlanRejected);

                expectedStart = scene.Timing.PlannedEndMilliseconds;
            }

            if (variant.PlannedDurationMilliseconds != expectedStart)
                Reject(DocumentaryMediaPipelineRejectionReason.TimingPlanRejected);
        }

        if (request.Policy.VideoFormat != DocumentaryMediaAssetFormat.Mp4 ||
            request.Policy.SubtitleFormat != DocumentaryMediaAssetFormat.Srt ||
            request.Policy.AudioSampleRate <= 0 || request.Policy.AudioChannelCount <= 0 ||
            request.Policy.LongWidth <= 0 || request.Policy.LongHeight <= 0 ||
            request.Policy.ShortWidth <= 0 || request.Policy.ShortHeight <= 0 ||
            request.Policy.LongFrameRate <= 0 || request.Policy.ShortFrameRate <= 0)
            Reject(DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected);

        // Stage 2: after O2.18-specific classifications are exhausted, retain O2.17 certification.
        // Any residual upstream corruption is correctly classified as an incomplete media project.
        if (!DocumentaryMediaProjectionValidator.ProjectValid(project))
            Reject(DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete);
    }

    public static void ValidateExecutionPlan(DocumentaryMediaPipelineExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var canonical = Enum.GetValues<DocumentaryMediaVariantType>();

        if (plan.AssetPlans.Any(x => !Enum.IsDefined(x.AssetType) || !Enum.IsDefined(x.AssetFormat) ||
            !SupportedMapping(x.AssetType, x.AssetFormat)))
            Reject(DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType);

        if (!plan.IsComplete || plan.VariantCount != canonical.Length || plan.VariantPlans.Count != canonical.Length ||
            !plan.VariantPlans.Select(x => x.VariantType).SequenceEqual(canonical) ||
            plan.AssetCount != plan.AssetPlans.Count || plan.DependencyCount != plan.AssetDependencies.Count ||
            plan.AssetPlans.Select(x => x.AssetId).Distinct(StringComparer.Ordinal).Count() != plan.AssetCount ||
            plan.AssetDependencies.Select(x => x.DependencyId).Distinct(StringComparer.Ordinal).Count() != plan.DependencyCount ||
            !plan.AssetDependencies.Select(x => x.Sequence).SequenceEqual(Enumerable.Range(0, plan.DependencyCount)) ||
            !plan.AssetPlans.SelectMany(x => x.Dependencies).Select(x => x.DependencyId).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(plan.AssetDependencies.Select(x => x.DependencyId).OrderBy(x => x, StringComparer.Ordinal)))
            Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);

        var ids = plan.AssetPlans.Select(x => x.AssetId).ToHashSet(StringComparer.Ordinal);
        if (plan.AssetDependencies.Any(x => !ids.Contains(x.SourceAssetId) || !ids.Contains(x.TargetAssetId) ||
                x.SourceAssetId == x.TargetAssetId ||
                x.DependencyId != $"{x.SourceAssetId}.depends-on.{x.TargetAssetId}" ||
                x.CorrelationId != plan.CorrelationId) || HasCycle(plan))
            Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);

        foreach (var variant in plan.VariantPlans)
        {
            if (variant.SubtitleAssetPlans.Count != variant.SceneVideoAssetPlans.Count ||
                variant.VariantVideoAssetPlan.Dependencies.Count != variant.SceneVideoAssetPlans.Count ||
                variant.AssetCount != variant.SceneAssetPlans.Count + variant.NarrationAssetPlans.Count +
                    variant.SubtitleAssetPlans.Count + variant.SceneVideoAssetPlans.Count + 1)
                Reject(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch);
        }
    }

    private static bool SupportedMapping(DocumentaryMediaAssetType type, DocumentaryMediaAssetFormat format) => type switch
    {
        DocumentaryMediaAssetType.VisualImage or DocumentaryMediaAssetType.SkySimulationImage or
            DocumentaryMediaAssetType.StarChartImage or DocumentaryMediaAssetType.TelescopeViewImage or
            DocumentaryMediaAssetType.ScientificDiagramImage or DocumentaryMediaAssetType.HistoricalIllustrationImage
                => format is DocumentaryMediaAssetFormat.Png or DocumentaryMediaAssetFormat.Jpeg or DocumentaryMediaAssetFormat.WebP,
        DocumentaryMediaAssetType.NarrationAudio
                => format is DocumentaryMediaAssetFormat.Wav or DocumentaryMediaAssetFormat.Mp3 or DocumentaryMediaAssetFormat.Aac,
        DocumentaryMediaAssetType.SubtitleDocument
                => format is DocumentaryMediaAssetFormat.Srt or DocumentaryMediaAssetFormat.Vtt,
        DocumentaryMediaAssetType.SceneVideo or DocumentaryMediaAssetType.VariantVideo
                => format == DocumentaryMediaAssetFormat.Mp4,
        _ => false
    };

    public static void ValidateExecutionRecord(DocumentaryMediaPipelineExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateExecutionPlan(record.ExecutionPlan);

        var manifest = record.OutputManifest;
        if (record.ExecutionId != record.Metadata.ExecutionId ||
            record.MediaProjectId != record.MediaProject.MediaProjectId ||
            record.MaterializationId != record.MediaProject.MaterializationId ||
            record.TopicId != record.MediaProject.TopicId ||
            manifest.ManifestId != $"{record.ExecutionId}.manifest" ||
            manifest.ExecutionId != record.ExecutionId || manifest.MediaProjectId != record.MediaProjectId ||
            manifest.TopicId != record.TopicId || manifest.ManifestSchemaVersion != "1.0" ||
            record.VariantCount != 4 || record.VariantRecords.Count != 4 ||
            manifest.VariantCount != record.VariantCount ||
            record.AssetCount != manifest.Assets.Count || manifest.AssetCount != record.AssetCount ||
            manifest.CompletedVariantCount != record.CompletedVariantCount ||
            manifest.FailedVariantCount != record.FailedVariantCount ||
            manifest.CorrelationId != record.Metadata.CorrelationId ||
            !manifest.VariantRecords.SequenceEqual(record.VariantRecords))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);

        if (record.Status == DocumentaryMediaPipelineStatus.Planned &&
            (record.AssetCount != 0 || record.CompletedVariantCount != 0 || record.FailedVariantCount != 0 ||
             record.VariantRecords.Any(x => x.Status != DocumentaryMediaPipelineStatus.Planned || x.OutputAssetId is not null)))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);

        if (record.Status == DocumentaryMediaPipelineStatus.Complete &&
            (record.CompletedVariantCount != 4 || record.FailedVariantCount != 0 || record.FailedAssetCount != 0 ||
             record.VariantRecords.Any(x => x.Status != DocumentaryMediaPipelineStatus.Complete || x.OutputAssetId is null)))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);

        if (record.Status == DocumentaryMediaPipelineStatus.PartiallyComplete &&
            (record.CompletedVariantCount is < 1 or > 3 || record.FailedVariantCount != 4 - record.CompletedVariantCount ||
             record.VariantRecords.Count(x => x.Status == DocumentaryMediaPipelineStatus.Complete) != record.CompletedVariantCount))
            Reject(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch);
    }

    private static bool HasCycle(DocumentaryMediaPipelineExecutionPlan plan)
    {
        var incoming = plan.AssetPlans.ToDictionary(x => x.AssetId, _ => 0, StringComparer.Ordinal);
        var outgoing = plan.AssetPlans.ToDictionary(x => x.AssetId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var dependency in plan.AssetDependencies)
        {
            incoming[dependency.SourceAssetId]++;
            outgoing[dependency.TargetAssetId].Add(dependency.SourceAssetId);
        }

        var queue = new Queue<string>(incoming.Where(x => x.Value == 0).Select(x => x.Key));
        var visited = 0;
        while (queue.TryDequeue(out var id))
        {
            visited++;
            foreach (var target in outgoing[id])
                if (--incoming[target] == 0) queue.Enqueue(target);
        }
        return visited != incoming.Count;
    }

    private static void Reject(DocumentaryMediaPipelineRejectionReason reason) =>
        throw new ArgumentException(reason.ToString());
}
