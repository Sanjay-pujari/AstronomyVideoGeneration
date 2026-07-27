using System.Reflection;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

/// <summary>Executes every O2.18 rejection through the production component that owns it.</summary>
public sealed class DocumentaryMediaPipelineRejectionPathTests
{
    private static readonly DocumentaryMediaPipelineRejectionReason[] RequestReasons =
    [
        DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete,
        DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch,
        DocumentaryMediaPipelineRejectionReason.MaterializationIdentityMismatch,
        DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch,
        DocumentaryMediaPipelineRejectionReason.CorrelationMismatch,
        DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected,
        DocumentaryMediaPipelineRejectionReason.RequiredVariantMissing,
        DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch,
        DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch,
        DocumentaryMediaPipelineRejectionReason.VariantIdentityMismatch,
        DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch,
        DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch,
        DocumentaryMediaPipelineRejectionReason.SceneIdentityMismatch,
        DocumentaryMediaPipelineRejectionReason.NarrationPlanRejected,
        DocumentaryMediaPipelineRejectionReason.SubtitlePlanRejected,
        DocumentaryMediaPipelineRejectionReason.VisualPlanRejected,
        DocumentaryMediaPipelineRejectionReason.TimingPlanRejected,
        DocumentaryMediaPipelineRejectionReason.TransitionPlanRejected
    ];

    [Fact]
    public void Every_request_rejection_is_executed_through_the_orchestrator_validation_path()
    {
        foreach (var reason in RequestReasons)
        {
            var first = ExecuteRequestScenario(reason);
            var second = ExecuteRequestScenario(reason);
            Assert.Equal(DocumentaryMediaPipelineStatus.Rejected, first.Status);
            AssertExact(reason, first);
            Assert.Equal(first.RejectionReasons, second.RejectionReasons);
        }
    }

    [Fact]
    public void Every_non_request_rejection_is_executed_through_its_real_owner()
    {
        var unavailable = new DocumentaryMediaPipelineOrchestrator(new()).Execute(ValidRequest());
        Assert.Equal(DocumentaryMediaPipelineStatus.Rejected, unavailable.Status);
        AssertExact(DocumentaryMediaPipelineRejectionReason.ProviderUnavailable, unavailable);

        foreach (var pair in new[]
        {
            (DocumentaryMediaAssetType.VisualImage, DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed),
            (DocumentaryMediaAssetType.NarrationAudio, DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed),
            (DocumentaryMediaAssetType.SubtitleDocument, DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed),
            (DocumentaryMediaAssetType.SceneVideo, DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed),
            (DocumentaryMediaAssetType.VariantVideo, DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed)
        })
        {
            var project = DocumentaryMediaPipelineFixture.Orion();
            var fake = new DocumentaryMediaPipelineFakeProviders();
            fake.FailedAssetIds.Add(DocumentaryMediaPipelineFixture.Plan(project).AssetPlans.First(x => x.AssetType == pair.Item1).AssetId);
            AssertExact(pair.Item2, DocumentaryMediaPipelineFixture.Run(project, providers: fake));
        }

        var verifier = new DocumentaryMediaPipelineFakeProviders();
        verifier.InvalidVariants.Add(DocumentaryMediaVariantType.LongEnglish);
        AssertExact(DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed,
            DocumentaryMediaPipelineFixture.Run(DocumentaryMediaPipelineFixture.Orion(), providers: verifier));

        var plan = DocumentaryMediaPipelineFixture.Plan(DocumentaryMediaPipelineFixture.Orion());
        var unsupportedAssets = plan.AssetPlans.ToArray();
        unsupportedAssets[0] = unsupportedAssets[0] with { AssetFormat = DocumentaryMediaAssetFormat.Mp4 };
        AssertValidatorReason(DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType,
            () => DocumentaryMediaPipelineValidator.ValidateExecutionPlan(plan with { AssetPlans = unsupportedAssets }));

        AssertValidatorReason(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch,
            () => DocumentaryMediaPipelineValidator.ValidateExecutionPlan(plan with { DependencyCount = -1 }));

        var record = DocumentaryMediaPipelineFixture.Complete(DocumentaryMediaPipelineFixture.Orion());
        AssertValidatorReason(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch,
            () => DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record with { AssetCount = -1 }));
    }

    [Fact]
    public void Every_rejection_reason_has_exactly_one_executable_owner_category()
    {
        var executed = RequestReasons.Concat(
        [
            DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch,
            DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType,
            DocumentaryMediaPipelineRejectionReason.ProviderUnavailable,
            DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed,
            DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed,
            DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed,
            DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed,
            DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed,
            DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed,
            DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch
        ]).ToArray();

        Assert.Equal(Enum.GetValues<DocumentaryMediaPipelineRejectionReason>(), executed.OrderBy(x => (int)x));
        Assert.Equal(executed.Length, executed.Distinct().Count());
    }

    private static DocumentaryMediaPipelineResult ExecuteRequestScenario(DocumentaryMediaPipelineRejectionReason reason)
    {
        var request = ValidRequest();
        var project = request.MediaProject;
        var variant = project.Variants[0];
        var scene = variant.Scenes[0];

        request = reason switch
        {
            DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete =>
                request with { MediaProject = Set(project, nameof(project.IsComplete), false) },

            DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch =>
                request with { MediaProject = Set(project, nameof(project.MediaProjectId), "wrong.media-project") },

            DocumentaryMediaPipelineRejectionReason.MaterializationIdentityMismatch =>
                MaterializationMismatch(request, project),

            DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch =>
                request with { MediaProject = Set(project, nameof(project.TopicId), "wrong.topic") },

            DocumentaryMediaPipelineRejectionReason.CorrelationMismatch =>
                request with { Metadata = request.Metadata with { CorrelationId = "wrong-correlation" } },

            DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected =>
                request with { Metadata = request.Metadata with { PipelineSchemaVersion = "9.9" } },

            DocumentaryMediaPipelineRejectionReason.RequiredVariantMissing =>
                request with { MediaProject = ReplaceVariants(project, project.Variants.Take(3).ToArray(), 3) },

            DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch =>
                request with { MediaProject = ReplaceVariants(project, project.Variants.Concat([project.Variants[0]]).ToArray(), 5) },

            DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch =>
                request with
                {
                    MediaProject = ReplaceVariants(project,
                    [project.Variants[1], project.Variants[0], project.Variants[2], project.Variants[3]], 4)
                },

            DocumentaryMediaPipelineRejectionReason.VariantIdentityMismatch =>
                request with { MediaProject = ReplaceVariant(project, 0, Set(variant, nameof(variant.VariantId), "wrong.variant")) },

            DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch =>
                request with { MediaProject = ReplaceScene(project, 0, 0, Set(scene, nameof(scene.Narration), Array.Empty<DocumentaryNarrationBlock>())) },

            DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch =>
                request with { MediaProject = ReplaceScene(project, 0, 0, Set(scene, nameof(scene.Sequence), 1)) },

            DocumentaryMediaPipelineRejectionReason.SceneIdentityMismatch =>
                request with { MediaProject = ReplaceScene(project, 0, 0, Set(scene, nameof(scene.SceneId), "wrong.scene")) },

            DocumentaryMediaPipelineRejectionReason.NarrationPlanRejected =>
                request with
                {
                    MediaProject = ReplaceNarration(project, 0, 0, 0,
                    Set(scene.Narration[0], nameof(DocumentaryNarrationBlock.NarrationId), "wrong.narration"))
                },

            DocumentaryMediaPipelineRejectionReason.SubtitlePlanRejected =>
                request with
                {
                    MediaProject = ReplaceSubtitle(project, 0, 0, 0,
                    Set(scene.SubtitleCues[0], nameof(DocumentarySubtitleCue.NarrationId), "wrong.narration-link"))
                },

            DocumentaryMediaPipelineRejectionReason.VisualPlanRejected =>
                request with
                {
                    MediaProject = ReplaceVisual(project, 0, 0, 0,
                    Set(scene.VisualPrompts[0], nameof(DocumentaryVisualPrompt.VisualPromptId), "wrong.visual"))
                },

            DocumentaryMediaPipelineRejectionReason.TimingPlanRejected =>
                request with
                {
                    MediaProject = ReplaceScene(project, 0, 0,
                    Set(scene, nameof(scene.Timing), Set(scene.Timing, nameof(DocumentarySceneTiming.PlannedEndMilliseconds),
                        scene.Timing.PlannedEndMilliseconds + 1)))
                },

            DocumentaryMediaPipelineRejectionReason.TransitionPlanRejected =>
                request with
                {
                    MediaProject = ReplaceScene(project, 0, 0,
                    Set(scene, nameof(scene.Transition), (DocumentarySceneTransition)int.MaxValue))
                },

            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

        return new DocumentaryMediaPipelineOrchestrator(new DocumentaryMediaPipelineFakeProviders().Registry).Execute(request);
    }

    private static DocumentaryMediaPipelineRequest MaterializationMismatch(DocumentaryMediaPipelineRequest request, DocumentaryMediaProject project)
    {
        const string materializationId = "wrong.materialization";
        var changed = Set(project, nameof(project.MaterializationId), materializationId);
        changed = Set(changed, nameof(project.MediaProjectId), $"{materializationId}.media-project");
        return request with
        {
            MediaProject = changed,
            Metadata = request.Metadata with { ExecutionId = $"{changed.MediaProjectId}.execution.1" }
        };
    }

    private static DocumentaryMediaPipelineRequest ValidRequest() =>
        DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion());

    private static DocumentaryMediaProject ReplaceVariants(DocumentaryMediaProject project,
        IReadOnlyList<DocumentaryMediaVariant> variants, int variantCount) =>
        Set(Set(project, nameof(project.Variants), variants), nameof(project.VariantCount), variantCount);

    private static DocumentaryMediaProject ReplaceVariant(DocumentaryMediaProject project, int index, DocumentaryMediaVariant replacement)
    {
        var variants = project.Variants.ToArray();
        variants[index] = replacement;
        return Set(project, nameof(project.Variants), variants);
    }

    private static DocumentaryMediaProject ReplaceScene(DocumentaryMediaProject project, int variantIndex, int sceneIndex,
        DocumentaryMediaScene replacement)
    {
        var variant = project.Variants[variantIndex];
        var scenes = variant.Scenes.ToArray();
        scenes[sceneIndex] = replacement;
        return ReplaceVariant(project, variantIndex, Set(variant, nameof(variant.Scenes), scenes));
    }

    private static DocumentaryMediaProject ReplaceNarration(DocumentaryMediaProject project, int variantIndex, int sceneIndex,
        int narrationIndex, DocumentaryNarrationBlock replacement)
    {
        var scene = project.Variants[variantIndex].Scenes[sceneIndex];
        var values = scene.Narration.ToArray();
        values[narrationIndex] = replacement;
        return ReplaceScene(project, variantIndex, sceneIndex, Set(scene, nameof(scene.Narration), values));
    }

    private static DocumentaryMediaProject ReplaceSubtitle(DocumentaryMediaProject project, int variantIndex, int sceneIndex,
        int subtitleIndex, DocumentarySubtitleCue replacement)
    {
        var scene = project.Variants[variantIndex].Scenes[sceneIndex];
        var values = scene.SubtitleCues.ToArray();
        values[subtitleIndex] = replacement;
        return ReplaceScene(project, variantIndex, sceneIndex, Set(scene, nameof(scene.SubtitleCues), values));
    }

    private static DocumentaryMediaProject ReplaceVisual(DocumentaryMediaProject project, int variantIndex, int sceneIndex,
        int visualIndex, DocumentaryVisualPrompt replacement)
    {
        var scene = project.Variants[variantIndex].Scenes[sceneIndex];
        var values = scene.VisualPrompts.ToArray();
        values[visualIndex] = replacement;
        return ReplaceScene(project, variantIndex, sceneIndex, Set(scene, nameof(scene.VisualPrompts), values));
    }

    private static T Set<T>(T source, string propertyName, object? value) where T : class
    {
        var clone = (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, null)!;
        var field = typeof(T).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Backing field for {typeof(T).Name}.{propertyName} was not found.");
        field.SetValue(clone, value);
        return clone;
    }

    private static void AssertExact(DocumentaryMediaPipelineRejectionReason expected, DocumentaryMediaPipelineResult result)
    {
        Assert.NotEqual(DocumentaryMediaPipelineStatus.Complete, result.Status);
        Assert.NotEqual(DocumentaryMediaPipelineStatus.Planned, result.Status);
        Assert.Equal([expected], result.RejectionReasons);
        Assert.All(result.RejectionReasons, x => Assert.True(Enum.IsDefined(x)));
        Assert.Equal(result.RejectionReasons.Count, result.RejectionReasons.Distinct().Count());
        Assert.Equal(result.RejectionReasons.OrderBy(x => (int)x), result.RejectionReasons);
    }

    private static void AssertValidatorReason(DocumentaryMediaPipelineRejectionReason expected, Action validate)
    {
        var exception = Assert.Throws<ArgumentException>(validate);
        Assert.Equal(expected.ToString(), exception.Message);
    }
}
