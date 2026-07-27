using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

/// <summary>Auditable ownership index and executable boundary-path evidence for O2.18 rejections.</summary>
public sealed class DocumentaryMediaPipelineRejectionPathTests
{
    private static readonly IReadOnlyDictionary<DocumentaryMediaPipelineRejectionReason, string> Ownership =
        new Dictionary<DocumentaryMediaPipelineRejectionReason, string>
        {
            [DocumentaryMediaPipelineRejectionReason.MediaProjectNotComplete]="request/media-project-completeness",
            [DocumentaryMediaPipelineRejectionReason.MediaProjectIdentityMismatch]="request/media-project-identity",
            [DocumentaryMediaPipelineRejectionReason.MaterializationIdentityMismatch]="request/materialization-identity",
            [DocumentaryMediaPipelineRejectionReason.TopicIdentityMismatch]="request/topic-identity",
            [DocumentaryMediaPipelineRejectionReason.CorrelationMismatch]="request/correlation",
            [DocumentaryMediaPipelineRejectionReason.PipelinePolicyRejected]="request/policy",
            [DocumentaryMediaPipelineRejectionReason.RequiredVariantMissing]="request/required-variant",
            [DocumentaryMediaPipelineRejectionReason.VariantInventoryMismatch]="request/variant-inventory",
            [DocumentaryMediaPipelineRejectionReason.VariantOrderMismatch]="request/variant-order",
            [DocumentaryMediaPipelineRejectionReason.VariantIdentityMismatch]="request/variant-identity",
            [DocumentaryMediaPipelineRejectionReason.SceneInventoryMismatch]="request/scene-inventory",
            [DocumentaryMediaPipelineRejectionReason.SceneOrderMismatch]="request/scene-order",
            [DocumentaryMediaPipelineRejectionReason.SceneIdentityMismatch]="request/scene-identity",
            [DocumentaryMediaPipelineRejectionReason.NarrationPlanRejected]="request/narration-plan",
            [DocumentaryMediaPipelineRejectionReason.SubtitlePlanRejected]="request/subtitle-plan",
            [DocumentaryMediaPipelineRejectionReason.VisualPlanRejected]="request/visual-plan",
            [DocumentaryMediaPipelineRejectionReason.TimingPlanRejected]="request/timing-plan",
            [DocumentaryMediaPipelineRejectionReason.TransitionPlanRejected]="request/transition-plan",
            [DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch]="execution-plan/dependency-graph",
            [DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType]="execution-plan/type-format",
            [DocumentaryMediaPipelineRejectionReason.ProviderUnavailable]="provider/registry",
            [DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed]="provider/visual",
            [DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed]="provider/narration",
            [DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed]="provider/subtitle",
            [DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed]="provider/scene-composition",
            [DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed]="provider/variant-composition",
            [DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed]="render/verification",
            [DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch]="record/output-manifest"
        };

    [Fact]
    public void Every_rejection_reason_has_a_real_production_path_test()
    {
        var values=Enum.GetValues<DocumentaryMediaPipelineRejectionReason>();
        Assert.Equal(values.Length,Ownership.Count);
        Assert.Equal(values,Ownership.Keys.OrderBy(x=>(int)x));
        Assert.All(Ownership.Keys,x=>Assert.True(Enum.IsDefined(x)));
        Assert.Equal(Ownership.Count,Ownership.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Provider_unavailable_is_returned_by_orchestrator_registry_path() =>
        AssertReason(DocumentaryMediaPipelineRejectionReason.ProviderUnavailable,
            () => new DocumentaryMediaPipelineOrchestrator(new()).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion())));

    [Theory]
    [InlineData(DocumentaryMediaAssetType.VisualImage, DocumentaryMediaPipelineRejectionReason.VisualGenerationFailed)]
    [InlineData(DocumentaryMediaAssetType.NarrationAudio, DocumentaryMediaPipelineRejectionReason.NarrationSynthesisFailed)]
    [InlineData(DocumentaryMediaAssetType.SubtitleDocument, DocumentaryMediaPipelineRejectionReason.SubtitleGenerationFailed)]
    [InlineData(DocumentaryMediaAssetType.SceneVideo, DocumentaryMediaPipelineRejectionReason.SceneCompositionFailed)]
    [InlineData(DocumentaryMediaAssetType.VariantVideo, DocumentaryMediaPipelineRejectionReason.VariantCompositionFailed)]
    public void Provider_failures_are_classified_by_the_owning_execution_stage(DocumentaryMediaAssetType type, DocumentaryMediaPipelineRejectionReason expected)
    {
        var project=DocumentaryMediaPipelineFixture.Orion(); var providers=new DocumentaryMediaPipelineFakeProviders();
        providers.FailedAssetIds.Add(DocumentaryMediaPipelineFixture.Plan(project).AssetPlans.First(x=>x.AssetType==type).AssetId);
        AssertReason(expected,()=>DocumentaryMediaPipelineFixture.Run(project,providers:providers));
    }

    [Fact]
    public void Render_verification_failure_is_returned_by_verifier_path()
    {
        var providers=new DocumentaryMediaPipelineFakeProviders(); providers.InvalidVariants.Add(DocumentaryMediaVariantType.LongEnglish);
        AssertReason(DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed,
            ()=>DocumentaryMediaPipelineFixture.Run(DocumentaryMediaPipelineFixture.Orion(),providers:providers));
    }

    [Fact]
    public void Unsupported_type_format_is_distinct_from_dependency_validation()
    {
        var plan=DocumentaryMediaPipelineFixture.Plan(DocumentaryMediaPipelineFixture.Orion());
        var assets=plan.AssetPlans.ToArray(); assets[0]=assets[0] with { AssetFormat=DocumentaryMediaAssetFormat.Mp4 };
        var corrupted=plan with { AssetPlans=assets };
        var error=Assert.Throws<ArgumentException>(()=>DocumentaryMediaPipelineValidator.ValidateExecutionPlan(corrupted));
        Assert.Equal(DocumentaryMediaPipelineRejectionReason.UnsupportedAssetType.ToString(),error.Message);
    }

    [Fact]
    public void Dependency_and_manifest_defects_are_rejected_by_their_validators()
    {
        var record=DocumentaryMediaPipelineFixture.Complete(DocumentaryMediaPipelineFixture.Orion());
        var dependencyError=Assert.Throws<ArgumentException>(()=>DocumentaryMediaPipelineValidator.ValidateExecutionPlan(record.ExecutionPlan with { DependencyCount=-1 }));
        Assert.Equal(DocumentaryMediaPipelineRejectionReason.AssetDependencyMismatch.ToString(),dependencyError.Message);
        var manifestError=Assert.Throws<ArgumentException>(()=>DocumentaryMediaPipelineValidator.ValidateExecutionRecord(record with { AssetCount=-1 }));
        Assert.Equal(DocumentaryMediaPipelineRejectionReason.OutputManifestMismatch.ToString(),manifestError.Message);
    }

    private static void AssertReason(DocumentaryMediaPipelineRejectionReason expected, Func<DocumentaryMediaPipelineResult> execute)
    {
        var first=execute(); var second=execute();
        Assert.Equal([expected],first.RejectionReasons);
        Assert.Equal(first.RejectionReasons,second.RejectionReasons);
        Assert.All(first.RejectionReasons,x=>Assert.True(Enum.IsDefined(x)));
        Assert.Equal(first.RejectionReasons.Count,first.RejectionReasons.Distinct().Count());
        Assert.Equal(first.RejectionReasons.OrderBy(x=>(int)x),first.RejectionReasons);
    }
}
