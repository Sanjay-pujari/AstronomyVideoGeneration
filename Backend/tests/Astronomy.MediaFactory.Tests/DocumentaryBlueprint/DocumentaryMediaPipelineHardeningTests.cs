using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryMediaPipelineHardeningTests
{
    [Fact]
    public void PlanOnly_builds_complete_graph_without_executing_providers()
    {
        var fake=new DocumentaryMediaPipelineFakeProviders();
        var result=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion(),DocumentaryMediaPipelineFixture.PlanOnly()));
        Assert.Equal(DocumentaryMediaPipelineStatus.Planned,result.Status); Assert.NotNull(result.ExecutionRecord);
        Assert.Equal(4,result.ExecutionRecord.ExecutionPlan.VariantPlans.Count); Assert.Empty(result.ExecutionRecord.OutputManifest.Assets);
        Assert.Equal(result.ExecutionRecord.ExecutionPlan.AssetDependencies.Count,result.ExecutionRecord.ExecutionPlan.AssetPlans.Sum(x=>x.Dependencies.Count));
        Assert.Empty(fake.VisualRequests); Assert.Empty(fake.NarrationRequests); Assert.Empty(fake.SubtitleRequests); Assert.Empty(fake.SceneRequests); Assert.Empty(fake.VariantRequests); Assert.Empty(fake.VerificationRequests);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Execute_completes_all_canonical_variants(Func<DocumentaryMediaProject> projectFactory)
    {
        var fake=new DocumentaryMediaPipelineFakeProviders(); var result=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(projectFactory()));
        Assert.Equal(DocumentaryMediaPipelineStatus.Complete,result.Status); Assert.Equal(4,result.ExecutionRecord!.CompletedVariantCount);
        Assert.All(result.ExecutionRecord.VariantRecords,x=>{Assert.Equal(DocumentaryMediaPipelineStatus.Complete,x.Status);Assert.NotNull(x.OutputAssetId);});
        Assert.NotEmpty(fake.VisualRequests); Assert.NotEmpty(fake.NarrationRequests); Assert.NotEmpty(fake.SubtitleRequests); Assert.NotEmpty(fake.SceneRequests); Assert.Equal(4,fake.VariantRequests.Count); Assert.Equal(4,fake.VerificationRequests.Count);
        var summary=new DocumentaryMediaPipelineSummarizer().Summarize(result.ExecutionRecord); Assert.True(summary.IsComplete); Assert.Equal(result.ExecutionRecord.AssetCount,summary.AssetCount);
    }

    public static IEnumerable<object[]> Projects()=>[[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Orion)],[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Leo)],[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Conjunction)]];

    [Fact]
    public void Measured_narration_expands_subtitles_scenes_and_variant()
    {
        var fake=new DocumentaryMediaPipelineFakeProviders{NarrationExtensionMilliseconds=5000}; var request=DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion());
        var result=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(request); Assert.Equal(DocumentaryMediaPipelineStatus.Complete,result.Status);
        Assert.All(fake.SubtitleRequests,x=>Assert.True(x.MeasuredNarrationDurationMilliseconds>x.AssetPlan.ExpectedDurationMilliseconds));
        Assert.All(fake.SceneRequests,x=>Assert.True(x.EffectiveSceneDurationMilliseconds>=x.MeasuredNarrationDurationMilliseconds+x.MediaScene.Timing.VisualHoldMilliseconds+x.MediaScene.Timing.TransitionDurationMilliseconds));
        Assert.Equal(fake.SceneRequests.Sum(x=>x.EffectiveSceneDurationMilliseconds),fake.VariantRequests.Sum(x=>x.SceneAssets.Sum(a=>a.DurationMilliseconds)));
    }

    [Fact]
    public void A_failed_short_hindi_branch_does_not_suppress_other_variants()
    {
        var project=DocumentaryMediaPipelineFixture.Orion(); var plan=DocumentaryMediaPipelineFixture.Plan(project); var fake=new DocumentaryMediaPipelineFakeProviders();
        fake.FailedAssetIds.Add(plan.VariantPlans.Single(x=>x.VariantType==DocumentaryMediaVariantType.ShortHindi).SceneAssetPlans[0].AssetId);
        var result=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(project));
        Assert.Equal(DocumentaryMediaPipelineStatus.PartiallyComplete,result.Status); Assert.Equal(3,result.ExecutionRecord!.CompletedVariantCount);
        Assert.Equal(DocumentaryMediaPipelineStatus.Rejected,result.ExecutionRecord.VariantRecords.Single(x=>x.VariantType==DocumentaryMediaVariantType.ShortHindi).Status);
        Assert.Equal(3,fake.VariantRequests.Count); Assert.Equal(3,fake.VerificationRequests.Count);
    }

    [Fact]
    public void Retries_keep_logical_asset_identity_and_attempt_count()
    {
        var project=DocumentaryMediaPipelineFixture.Orion(); var plan=DocumentaryMediaPipelineFixture.Plan(project); var vp=plan.VariantPlans[0]; var fake=new DocumentaryMediaPipelineFakeProviders();
        foreach(var id in new[]{vp.SceneAssetPlans[0].AssetId,vp.NarrationAssetPlans[0].AssetId,vp.SceneVideoAssetPlans[0].AssetId,vp.VariantVideoAssetPlan.AssetId}) fake.FailFirstAssetIds.Add(id);
        var result=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(project)); Assert.Equal(DocumentaryMediaPipelineStatus.Complete,result.Status);
        AssertRetries(fake.VisualRequests.Where(x=>x.AssetPlan.AssetId==vp.SceneAssetPlans[0].AssetId).Select(x=>(x.AssetPlan.AssetId,x.Attempt)));
        AssertRetries(fake.NarrationRequests.Where(x=>x.AssetPlan.AssetId==vp.NarrationAssetPlans[0].AssetId).Select(x=>(x.AssetPlan.AssetId,x.Attempt)));
        AssertRetries(fake.SceneRequests.Where(x=>x.AssetPlan.AssetId==vp.SceneVideoAssetPlans[0].AssetId).Select(x=>(x.AssetPlan.AssetId,x.Attempt)));
        AssertRetries(fake.VariantRequests.Where(x=>x.AssetPlan.AssetId==vp.VariantVideoAssetPlan.AssetId).Select(x=>(x.AssetPlan.AssetId,x.Attempt)));
        Assert.Equal(2,result.ExecutionRecord!.OutputManifest.Assets.Single(x=>x.AssetId==vp.VariantVideoAssetPlan.AssetId).AttemptCount);
    }

    [Fact]
    public void Invalid_render_and_unavailable_registry_are_rejected_without_false_completion()
    {
        var fake=new DocumentaryMediaPipelineFakeProviders(); fake.InvalidVariants.Add(DocumentaryMediaVariantType.ShortHindi);
        var partial=new DocumentaryMediaPipelineOrchestrator(fake.Registry).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()));
        Assert.Equal(DocumentaryMediaPipelineStatus.PartiallyComplete,partial.Status); Assert.Contains(DocumentaryMediaPipelineRejectionReason.RenderVerificationFailed,partial.RejectionReasons);
        var unavailable=new DocumentaryMediaPipelineOrchestrator(new()).Execute(DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()));
        Assert.Equal(DocumentaryMediaPipelineStatus.Rejected,unavailable.Status); Assert.Null(unavailable.ExecutionRecord); Assert.Contains(DocumentaryMediaPipelineRejectionReason.ProviderUnavailable,unavailable.RejectionReasons);
    }

    [Fact]
    public void Planning_execution_and_summary_are_non_mutating_and_deterministic()
    {
        var one=DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()); var before=Json(one);
        var a=new DocumentaryMediaPipelineOrchestrator(new DocumentaryMediaPipelineFakeProviders().Registry).Execute(one); var summaryA=new DocumentaryMediaPipelineSummarizer().Summarize(a.ExecutionRecord!);
        Assert.Equal(before,Json(one));
        var two=DocumentaryMediaPipelineFixture.Request(DocumentaryMediaPipelineFixture.Orion()); var b=new DocumentaryMediaPipelineOrchestrator(new DocumentaryMediaPipelineFakeProviders().Registry).Execute(two); var summaryB=new DocumentaryMediaPipelineSummarizer().Summarize(b.ExecutionRecord!);
        Assert.Equal(Json(a),Json(b)); Assert.Equal(Json(summaryA),Json(summaryB));
    }

    private static void AssertRetries(IEnumerable<(string Id,int Attempt)> attempts){var values=attempts.ToArray();Assert.Equal(2,values.Length);Assert.Equal(values[0].Id,values[1].Id);Assert.Equal([1,2],values.Select(x=>x.Attempt));}
    private static string Json<T>(T value)=>JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
