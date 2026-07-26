namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

/// <summary>Coordinates O2.17 instructions through logical media ports; it never performs physical I/O.</summary>
public sealed class DocumentaryMediaPipelineOrchestrator
{
    private readonly DocumentaryMediaProviderRegistry providers;
    private readonly DocumentaryMediaPipelinePlanner planner = new();

    public DocumentaryMediaPipelineOrchestrator(DocumentaryMediaProviderRegistry providers) =>
        this.providers = providers ?? throw new ArgumentNullException(nameof(providers));

    public DocumentaryMediaPipelineResult Execute(DocumentaryMediaPipelineRequest request)
    {
        DocumentaryMediaPipelineExecutionPlan plan;
        try { plan = planner.Plan(request); }
        catch (ArgumentException exception)
        {
            var reason = Enum.TryParse<DocumentaryMediaPipelineRejectionReason>(exception.Message, out var parsed)
                ? parsed : DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected;
            return new(DocumentaryMediaPipelineStatus.Rejected, [reason], null);
        }

        if (request.Policy.Mode == DocumentaryMediaPipelineMode.PlanOnly)
        {
            var planned = request.MediaProject.Variants.Select(variant => new DocumentaryMediaVariantExecutionRecord(
                $"{request.Metadata.ExecutionId}.{variant.VariantType}", variant.VariantId, variant.VariantType,
                DocumentaryMediaPipelineStatus.Planned, [], 0, 0, variant.PlannedDurationMilliseconds,
                variant.PlannedDurationMilliseconds, null, [], request.Metadata.CorrelationId)).ToArray();
            return Finish(request, plan, planned, DocumentaryMediaPipelineStatus.Planned, []);
        }

        if (!providers.IsComplete)
            return new(DocumentaryMediaPipelineStatus.Rejected, [DocumentaryMediaPipelineRejectionReason.ProviderUnavailable], null);

        return ExecuteProviders(request, plan);
    }

    private DocumentaryMediaPipelineResult ExecuteProviders(DocumentaryMediaPipelineRequest request, DocumentaryMediaPipelineExecutionPlan plan)
    {
        var records = new List<DocumentaryMediaVariantExecutionRecord>();
        foreach (var variant in request.MediaProject.Variants)
            records.Add(ExecuteVariant(request, plan.VariantPlans.Single(x => x.VariantId == variant.VariantId), variant));

        var completed = records.Count(x => x.Status == DocumentaryMediaPipelineStatus.Complete);
        var status = completed == 4 ? DocumentaryMediaPipelineStatus.Complete
            : completed > 0 && request.Policy.AllowPartialCompletion ? DocumentaryMediaPipelineStatus.PartiallyComplete
            : DocumentaryMediaPipelineStatus.Rejected;
        var reasons = records.SelectMany(x => x.RejectionReasons).Distinct().Order().ToArray();
        return Finish(request, plan, records, status, reasons);
    }

    private DocumentaryMediaVariantExecutionRecord ExecuteVariant(DocumentaryMediaPipelineRequest request, DocumentaryMediaVariantExecutionPlan plan, DocumentaryMediaVariant variant)
    {
        var results = new List<DocumentaryMediaAssetResult>();
        var reasons = new List<DocumentaryMediaPipelineRejectionReason>();
        var completedScenes = 0;
        long effectiveVariantDuration = 0;

        foreach (var scene in variant.Scenes)
        {
            var sceneFailed = false;
            var visualResults = new List<DocumentaryMediaAssetResult>();
            foreach (var prompt in scene.VisualPrompts)
            {
                var assetPlan = plan.SceneAssetPlans.Single(x => x.SourceInstructionId == prompt.VisualPromptId);
                DocumentaryVisualGenerationResult? response = null;
                for (var attempt = 1; attempt <= request.Policy.MaximumVisualAttempts; attempt++)
                {
                    response = providers.Visual!.Generate(new(assetPlan, prompt, assetPlan.ExpectedWidth, assetPlan.ExpectedHeight, assetPlan.AssetFormat, attempt, request.Metadata.CorrelationId));
                    if (response.Status != DocumentaryMediaAssetStatus.Failed) break;
                }
                var result = response!.AssetResult;
                results.Add(result); visualResults.Add(result);
                if (!ValidAsset(result, assetPlan, request.Metadata.CorrelationId)) { sceneFailed = true; reasons.Add(DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed); }
            }

            var narrationResults = new List<DocumentaryMediaAssetResult>();
            var measuredDuration = 0L;
            foreach (var block in scene.Narration)
            {
                var assetPlan = plan.NarrationAssetPlans.Single(x => x.SourceInstructionId == block.NarrationId);
                DocumentaryNarrationSynthesisResult? response = null;
                for (var attempt = 1; attempt <= request.Policy.MaximumNarrationAttempts; attempt++)
                {
                    response = providers.Narration!.Synthesize(new(assetPlan, block, variant.Language.ToString(), variant.Language,
                        assetPlan.AssetFormat, request.Policy.AudioSampleRate, request.Policy.AudioChannelCount, attempt, request.Metadata.CorrelationId));
                    if (response.Status != DocumentaryMediaAssetStatus.Failed) break;
                }
                var result = response!.AssetResult;
                results.Add(result); narrationResults.Add(result);
                if (!ValidAsset(result, assetPlan, request.Metadata.CorrelationId) || response.MeasuredDurationMilliseconds <= 0)
                { sceneFailed = true; reasons.Add(DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed); }
                else measuredDuration = Math.Max(measuredDuration, response.MeasuredDurationMilliseconds);
            }

            var effectiveDuration = Math.Max(scene.Timing.PlannedDurationMilliseconds,
                measuredDuration + scene.Timing.VisualHoldMilliseconds + scene.Timing.TransitionDurationMilliseconds);
            effectiveVariantDuration += effectiveDuration;

            DocumentaryMediaAssetResult? subtitleResult = null;
            if (narrationResults.Count > 0 && narrationResults.All(IsSuccessful))
            {
                var subtitlePlan = plan.SubtitleAssetPlans.Single(x => x.SceneId == scene.SceneId);
                var response = providers.Subtitle!.Generate(new(subtitlePlan, variant.VariantType, scene.SceneId, variant.Language,
                    scene.SubtitleCues, measuredDuration, subtitlePlan.AssetFormat, request.Metadata.CorrelationId));
                subtitleResult = response.AssetResult;
                results.Add(subtitleResult);
                if (!ValidAsset(subtitleResult, subtitlePlan, request.Metadata.CorrelationId) || response.CueCount != scene.SubtitleCues.Count || subtitleResult.DurationMilliseconds <= 0)
                { sceneFailed = true; reasons.Add(DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed); }
            }
            else sceneFailed = true;

            if (!sceneFailed && subtitleResult is not null)
            {
                var scenePlan = plan.SceneVideoAssetPlans.Single(x => x.SceneId == scene.SceneId);
                DocumentarySceneCompositionResult? response = null;
                for (var attempt = 1; attempt <= request.Policy.MaximumCompositionAttempts; attempt++)
                {
                    response = providers.Scene!.Compose(new(scenePlan, scene, visualResults, narrationResults[0], subtitleResult,
                        measuredDuration, scene.Timing.PlannedDurationMilliseconds, effectiveDuration, scene.Transition,
                        scenePlan.ExpectedWidth, scenePlan.ExpectedHeight, scenePlan.ExpectedFrameRate, request.Metadata.CorrelationId, attempt));
                    if (response.Status != DocumentaryMediaAssetStatus.Failed) break;
                }
                results.Add(response!.AssetResult);
                if (!ValidAsset(response.AssetResult, scenePlan, request.Metadata.CorrelationId) || response.EffectiveDurationMilliseconds != effectiveDuration)
                { sceneFailed = true; reasons.Add(DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed); }
                else completedScenes++;
            }
        }

        DocumentaryMediaAssetResult? output = null;
        var sceneResults = variant.Scenes.Select(scene => results.SingleOrDefault(x => x.AssetId == $"{scene.SceneId}.asset.video")).ToArray();
        if (sceneResults.All(x => x is not null && IsSuccessful(x)))
        {
            DocumentaryVariantCompositionResult? response = null;
            for (var attempt = 1; attempt <= request.Policy.MaximumCompositionAttempts; attempt++)
            {
                response = providers.Variant!.Compose(new(plan.VariantVideoAssetPlan, variant, sceneResults.Select(x=>x!).ToArray(), plan.VariantVideoAssetPlan.ExpectedWidth,
                    plan.VariantVideoAssetPlan.ExpectedHeight, plan.VariantVideoAssetPlan.ExpectedFrameRate, request.Policy.AudioSampleRate,
                    request.Policy.AudioChannelCount, request.Policy.VideoFormat, request.Metadata.CorrelationId, attempt));
                if (response.Status != DocumentaryMediaAssetStatus.Failed) break;
            }
            output = response!.AssetResult; results.Add(output);
            if (!ValidAsset(output, plan.VariantVideoAssetPlan, request.Metadata.CorrelationId) || response.SceneCount != variant.SceneCount ||
                response.EffectiveDurationMilliseconds != effectiveVariantDuration || string.IsNullOrWhiteSpace(output.Checksum))
                reasons.Add(DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed);
            else
            {
                var verification = providers.Verifier!.Verify(new(variant, output, variant.SceneCount, output.Width, output.Height, output.FrameRate,
                    output.SampleRate, output.ChannelCount, effectiveVariantDuration, effectiveVariantDuration, request.Metadata.CorrelationId));
                if (!ValidVerification(verification, variant.SceneCount, output, effectiveVariantDuration))
                    reasons.Add(DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed);
                else
                {
                    output = output with { Status = DocumentaryMediaAssetStatus.Verified };
                    results[^1] = output;
                }
            }
        }

        var complete = output is not null && output.Status == DocumentaryMediaAssetStatus.Verified && reasons.Count == 0;
        return new($"{request.Metadata.ExecutionId}.{variant.VariantType}", variant.VariantId, variant.VariantType,
            complete ? DocumentaryMediaPipelineStatus.Complete : DocumentaryMediaPipelineStatus.Rejected, results,
            completedScenes, variant.SceneCount - completedScenes, variant.PlannedDurationMilliseconds, effectiveVariantDuration,
            complete ? output!.AssetId : null, reasons.Distinct().Order().ToArray(), request.Metadata.CorrelationId);
    }

    private static bool IsSuccessful(DocumentaryMediaAssetResult result) => result.Status is DocumentaryMediaAssetStatus.Generated or DocumentaryMediaAssetStatus.Verified;
    private static bool ValidAsset(DocumentaryMediaAssetResult result, DocumentaryMediaAssetPlan plan, string correlation) =>
        result.AssetId == plan.AssetId && result.AssetType == plan.AssetType && result.AssetFormat == plan.AssetFormat &&
        result.CorrelationId == correlation && IsSuccessful(result) && result.AttemptCount > 0 &&
        result.DurationMilliseconds > 0 && !string.IsNullOrWhiteSpace(result.ContentIdentity) &&
        !string.IsNullOrWhiteSpace(result.Checksum) &&
        (plan.ExpectedWidth == 0 || result.Width == plan.ExpectedWidth) && (plan.ExpectedHeight == 0 || result.Height == plan.ExpectedHeight) &&
        (plan.ExpectedFrameRate == 0 || result.FrameRate == plan.ExpectedFrameRate) &&
        (plan.ExpectedSampleRate == 0 || result.SampleRate == plan.ExpectedSampleRate) &&
        (plan.ExpectedChannelCount == 0 || result.ChannelCount == plan.ExpectedChannelCount);

    private static bool ValidVerification(DocumentaryRenderVerificationResult value, int scenes, DocumentaryMediaAssetResult output, long duration) =>
        value.IsValid && value.ActualSceneCount == scenes && value.ActualWidth == output.Width && value.ActualHeight == output.Height &&
        value.ActualFrameRate == output.FrameRate && value.ActualAudioSampleRate == output.SampleRate &&
        value.ActualAudioChannelCount == output.ChannelCount && value.ActualDurationMilliseconds == duration && value.HasVideo &&
        value.HasAudio && value.HasSubtitleTrack && value.ChecksumValid && value.Failures.Count == 0;

    private static DocumentaryMediaPipelineResult Finish(DocumentaryMediaPipelineRequest request, DocumentaryMediaPipelineExecutionPlan plan,
        IReadOnlyList<DocumentaryMediaVariantExecutionRecord> variants, DocumentaryMediaPipelineStatus status,
        IReadOnlyList<DocumentaryMediaPipelineRejectionReason> reasons)
    {
        var assets = variants.SelectMany(x => x.AssetResults).ToArray();
        var completed = variants.Count(x => x.Status == DocumentaryMediaPipelineStatus.Complete);
        var manifest = new DocumentaryMediaOutputManifest($"{request.Metadata.ExecutionId}.manifest", request.Metadata.ExecutionId,
            request.MediaProject.MediaProjectId, request.MediaProject.TopicId, variants, assets,
            assets.Where(x => x.Status == DocumentaryMediaAssetStatus.Verified && x.Checksum is not null).Select(x => x.Checksum!).ToArray(), 4, assets.Length, completed,
            status == DocumentaryMediaPipelineStatus.Planned ? 0 : 4 - completed, "1.0", request.Metadata.CorrelationId);
        var record = new DocumentaryMediaPipelineExecutionRecord(request.Metadata.ExecutionId, request.MediaProject, request.Policy,
            request.Metadata, plan, variants, manifest, status, request.MediaProject.MediaProjectId, request.MediaProject.MaterializationId,
            request.MediaProject.TopicId, 4, completed, status == DocumentaryMediaPipelineStatus.Planned ? 0 : 4 - completed, assets.Length,
            assets.Count(x => x.Status == DocumentaryMediaAssetStatus.Generated), assets.Count(x => x.Status == DocumentaryMediaAssetStatus.Verified),
            assets.Count(x => x.Status == DocumentaryMediaAssetStatus.Failed), request.MediaProject.TotalPlannedDurationMilliseconds,
            variants.Sum(x => x.EffectiveDurationMilliseconds));
        DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record);
        return new(status, reasons, record);
    }
}
