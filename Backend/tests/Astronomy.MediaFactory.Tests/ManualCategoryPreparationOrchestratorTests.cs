using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ManualCategoryPreparationOrchestratorTests
{
    [Fact]
    public async Task DailySkyGuide_FullPreparation_Succeeds()
    {
        var sut = Build();
        var response = await sut.RunAsync(Request(), CancellationToken.None);
        Assert.True(response.Success);
        Assert.Contains(response.Steps, s => s.StepName == "PipelineRequestPreview" && s.Status == "Completed");
        Assert.NotNull(response.RunPipelineRequest);
        Assert.False(response.RunPipelineRequest!.PublishToYouTube);
    }

    [Fact]
    public async Task Failed_StellariumCapture_Marks_Step_Failed()
    {
        var sut = Build(captureSuccess: false);
        var response = await sut.RunAsync(Request(), CancellationToken.None);
        Assert.Contains(response.Steps, s => s.StepName == "StellariumCapture" && s.Status == "Failed");
    }

    [Fact]
    public async Task PreviewVideo_Can_Be_Disabled()
    {
        var sut = Build();
        var response = await sut.RunAsync(Request() with { GeneratePreviewVideo = false }, CancellationToken.None);
        Assert.Contains(response.Steps, s => s.StepName == "PreviewVideoGeneration" && s.Status == "Skipped");
    }

    [Fact]
    public async Task Unsupported_Category_Steps_Are_Skipped()
    {
        var sut = Build(category: "TelescopeTargets");
        var response = await sut.RunAsync(Request() with { ContentCategoryCode = "TelescopeTargets" }, CancellationToken.None);
        Assert.Contains(response.Steps, s => s.StepName == "DailySkyContextPreview" && s.Status == "Skipped");
        Assert.Contains(response.Steps, s => s.StepName == "DailySkyGuideAssetContext" && s.Status == "Skipped");
    }

    private static ManualCategoryPreparationRequest Request() => new("DailySkyGuide", "hi", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-21T18:00:00Z"), "Moon", true, true, true, true);

    private static ManualCategoryPreparationOrchestrator Build(bool captureSuccess=true, string category="DailySkyGuide")
    {
        var plan = new ContentGenerationPlan { Id = Guid.NewGuid(), ContentCategoryCode = category, Status = "Planned", Language = "hi", RegionId = "IN-RJ-UDAIPUR", RegionName = "Udaipur", ScheduledUtc = DateTimeOffset.UtcNow };
        return new(new FakePlanning(plan), new FakeReq(), new FakeVisual(), new FakeScript(), new FakeCapture(captureSuccess), new FakePack(), new FakeManual(), new FakeCtx(), new FakeCompResolver(), new FakePreview(), NullLogger<ManualCategoryPreparationOrchestrator>.Instance);
    }

    private sealed class FakePlanning(ContentGenerationPlan plan) : IContentPlanningService { public Task<GenerateContentPlanResponse> GeneratePlanAsync(GenerateContentPlanRequest request, CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentGenerationPlan> GenerateDailyPlanAsync(string contentCategoryCode,string language,string regionId,DateTimeOffset scheduledUtc,string? primaryCelestialObjectCode,CancellationToken cancellationToken)=>Task.FromResult(plan); public Task<IReadOnlyCollection<ContentGenerationPlan>> GetPendingPlansAsync(string? status,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult<ContentGenerationPlan?>(plan); public Task<DailySkyGuideContext> BuildDailySkyGuideContextPreviewAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult(new DailySkyGuideContext(id,"r","l",0,0,"UTC",new DateOnly(2026,1,1),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,null,[],[],"a","b","c",null,0,[])); public Task<AstronomyVisibilityResult> BuildAstronomyVisibilityPreviewAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult(new AstronomyVisibilityResult("r","l",0,0,"UTC",new DateOnly(2026,1,1),DateTime.UtcNow,DateTime.UtcNow,DateTime.UtcNow,DateTime.UtcNow,"Full",100,[],[])); public Task<StellariumSceneCapturePlan> BuildStellariumScenePlanPreviewAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult(new StellariumSceneCapturePlan(id,plan.ContentCategoryCode,"r","l",0,0,"UTC",new DateOnly(2026,1,1),[],[])); public Task<PipelineBuildResult> BuildPipelineRequestPreviewAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult(new PipelineBuildResult(new RunPipelineRequest(new DateOnly(2026,5,21), ContentType.DailySkyGuide,"Udaipur","Asia/Kolkata",false,false,24.5,73.7,null,null,null,"IN-RJ-UDAIPUR","hi"),null,[])); public Task<PrepareManualRunResponse?> PrepareManualRunAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<bool> MarkPlanAsInProgressAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<bool> MarkPlanAsCompletedAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<bool> MarkPlanAsFailedAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid id,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentPipelineExecution?> CompleteExecutionAsync(Guid executionId,CompleteContentPlanningExecutionRequest request,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentPipelineExecution?> FailExecutionAsync(Guid executionId,FailContentPlanningExecutionRequest request,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<IReadOnlyCollection<ContentPipelineExecution>> GetExecutionsAsync(string? status,CancellationToken cancellationToken)=>throw new NotImplementedException(); public Task<ContentPipelineExecution?> GetExecutionByIdAsync(Guid executionId,CancellationToken cancellationToken)=>throw new NotImplementedException(); }
    private sealed class FakeReq : ICategoryRequirementResolver { public Task<CategoryPipelineRequirement> ResolveAsync(string c, CancellationToken t)=>Task.FromResult(new CategoryPipelineRequirement(c,true,true,true,false,false,false,false,false,"","","","",[],[],[])); }
    private sealed class FakeVisual : IVisualStrategyResolver { public Task<VisualStrategyPlan> ResolveAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)=>Task.FromResult(new VisualStrategyPlan(plan.Id,plan.ContentCategoryCode,"",true,true,false,false,false,false,false,[],[],[])); }
    private sealed class FakeScript : IStellariumScriptGenerator { public Task<StellariumScriptGenerationResult> GenerateAsync(StellariumSceneCapturePlan p, StellariumSceneCaptureItem s, CancellationToken c)=>Task.FromResult(new StellariumScriptGenerationResult(p.ContentGenerationPlanId,"a","b","c","d",true,null,[],null)); }
    private sealed class FakeCapture(bool success) : IStellariumImageCaptureExecutor { public Task<StellariumCaptureExecutionResponse> CaptureAsync(StellariumSceneCapturePlan p, StellariumCaptureExecutionRequest r, CancellationToken c)=>Task.FromResult(new StellariumCaptureExecutionResponse(p.ContentGenerationPlanId,success,0,0,null,[],[],success?null:"failed")); public Task<StellariumCaptureDiagnosticsResponse> GetDiagnosticsAsync(Guid id, CancellationToken c)=>throw new NotImplementedException(); }
    private sealed class FakePack : IDailySkyGuideVisualAssetPackager { public Task<DailySkyGuideVisualAssetPackageResponse> BuildPackageAsync(Guid id,CancellationToken c)=>Task.FromResult(new DailySkyGuideVisualAssetPackageResponse(id,true,"",[],[])); }
    private sealed class FakeManual : IAssetAwareManualRunPreparationService { public Task<AssetAwareManualRunPackage> PrepareAsync(Guid id,CancellationToken c)=>Task.FromResult(new AssetAwareManualRunPackage(id,"DailySkyGuide","Planned",null,null,true,true,[],[])); }
    private sealed class FakeCtx : IDailySkyGuideAssetAwareContextService { public Task<DailySkyGuideAssetAwareExecutionContext> BuildAsync(Guid id,CancellationToken c)=>Task.FromResult(new DailySkyGuideAssetAwareExecutionContext(id,"DailySkyGuide","r","l",new DateOnly(2026,1,1),"hi",null,null,null,[],[],[])); }
    private sealed class FakeCompResolver : IAssetAwareCompositionPlannerResolver { public IAssetAwareCompositionPlanner? Resolve(string c)=> new FakeComp(); }
    private sealed class FakeComp : IAssetAwareCompositionPlanner { public string ContentCategoryCode => "DailySkyGuide"; public Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid id,CancellationToken c)=>Task.FromResult(new AssetAwareVideoCompositionPlan(id,"DailySkyGuide","U",new DateOnly(2026,1,1),"hi",null,0,[],[],true)); }
    private sealed class FakePreview : IDailySkyGuidePreviewVideoGenerator { public Task<AssetAwarePreviewVideoResponse> GenerateAsync(Guid id,AssetAwarePreviewVideoRequest request,CancellationToken c)=>Task.FromResult(new AssetAwarePreviewVideoResponse(id,true,null,null,0,0,[],[],null)); public Task<AssetAwarePreviewVideoResponse> GetPreviewInfoAsync(Guid id,CancellationToken c)=>throw new NotImplementedException(); }
}
