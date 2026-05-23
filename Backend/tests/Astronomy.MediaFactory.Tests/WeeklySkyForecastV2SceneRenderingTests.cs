using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2SceneRenderingTests
{
    [Fact]
    public async Task RenderScenesEndpoint_Exists_AndReturnsPackage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IWeeklySkyForecastSceneRenderingOrchestrator, FakeOrchestrator>();
        var app = builder.Build();
        app.MapPost("/api/content-planning/weekly-skyforecast-v2/render-scenes", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastSceneRenderingOrchestrator orchestrator, CancellationToken ct) =>
        {
            var ireq = new WeeklySkyForecastV2IntelligenceRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, request.WeekStartDate, request.Diagnostics);
            return Results.Ok(await orchestrator.RunAsync(ireq, request.ContentGenerationPlanId, ct));
        });
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync("/api/content-planning/weekly-skyforecast-v2/render-scenes", new WeeklySkyForecastV2RenderScenesRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Orchestrator_DispatchesPhase6BRenderers_WithoutPlaceholderFallback()
    {
        var orchestrator = new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceService(), NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>.Instance);
        var result = await orchestrator.RunAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow), null, CancellationToken.None);

        Assert.True(result.SceneRenderingValidation.IsValid);
        Assert.True(result.SceneRenderingValidation.ReadyForTimelineComposition);
        Assert.False(result.SceneRenderingValidation.ReadyForPublishing);
        Assert.Equal(4, result.SceneRenderResults.Count);
        Assert.All(result.SceneRenderResults, x => Assert.DoesNotContain("Unsupported rendererType", x.Warnings));
        Assert.Single(result.StellariumRenderResults);
        Assert.Single(result.CelestialAssetRenderResults);
        Assert.Single(result.HybridCompositeResults);
        Assert.Single(result.OverlayRenderResults);
        Assert.NotNull(result.ThumbnailRenderResult);
        Assert.Equal("Rendered", result.ThumbnailRenderResult!.Status);
        Assert.Matches(@".+\.(png|jpg)$", result.ThumbnailRenderResult.OutputPath);
        Assert.All(result.SceneRenderResults, x => Assert.False(string.IsNullOrWhiteSpace(x.OutputPath)));
    }

    [Fact]
    public async Task Orchestrator_UnknownRendererType_ReturnsValidationError()
    {
        var orchestrator = new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceService(includeUnsupportedRenderer: true), NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>.Instance);
        var result = await orchestrator.RunAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow), null, CancellationToken.None);

        Assert.False(result.SceneRenderingValidation.IsValid);
        Assert.Contains(result.SceneRenderingValidation.BlockingIssues, x => x.Contains("Unsupported rendererType 'UnknownRendererX'"));
    }

    private sealed class FakeOrchestrator : IWeeklySkyForecastSceneRenderingOrchestrator
    {
        public Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(new SceneRenderingPackage([], [], [], [], [], null, new SceneRenderingValidation(true, true, true, true, true, true, true, true, false, [], []), new SceneRenderingFreezeStatus(true, [], [], [])));
    }

    private sealed class FakeIntelligenceService(bool includeUnsupportedRenderer = false) : IWeeklySkyForecastV2IntelligenceService
    {
        public Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2IntelligenceRequest request, CancellationToken cancellationToken)
        {
            var root = Path.Combine(Path.GetTempPath(), "weekly-v2-render-tests", Guid.NewGuid().ToString("N"));
            var sceneRequests = new List<SceneRenderRequest>
            {
                new("req-stel","hero_scene","StellariumSceneRenderer","Stellarium",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,8,[],[],[],[],null,[],[],"fallback",Path.Combine(root,"hero.mp4"),Path.Combine(root,"hero.meta.json"),Path.Combine(root,"hero.debug.json"),1,false,true),
                new("req-asset","asset_scene","CelestialAssetCompositor","Asset",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,8,[],[],[],["moon_hero_image"],null,[],[],"fallback",Path.Combine(root,"asset.mp4"),Path.Combine(root,"asset.meta.json"),Path.Combine(root,"asset.debug.json"),1,false,true),
                new("req-hybrid","hybrid_scene","HybridCompositor","Hybrid",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,8,[],[],[],["jupiter_hero_image"],new MotionExecutionDirective("drift","none","none",1,1,"none",false,"intent"),[new OverlayExecutionDirective("hero_scene","title","Hero",0,2,1,"fade","safe","heading",1)],[],"fallback",Path.Combine(root,"hybrid.mp4"),Path.Combine(root,"hybrid.meta.json"),Path.Combine(root,"hybrid.debug.json"),1,false,true),
                new("req-thumb","thumbnail_story_scene","ThumbnailCompositor","Thumbnail",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,3,[],[],[],["thumb_asset"],null,[],[],"fallback",Path.Combine(root,"scene-thumb.mp4"),Path.Combine(root,"scene-thumb.meta.json"),Path.Combine(root,"scene-thumb.debug.json"),1,true,true)
            };
            if (includeUnsupportedRenderer)
            {
                sceneRequests.Add(new SceneRenderRequest("req-unknown","unknown_scene","UnknownRendererX","Unknown",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,3,[],[],[],[],null,[],[],"fallback",Path.Combine(root,"unknown.mp4"),Path.Combine(root,"unknown.meta.json"),Path.Combine(root,"unknown.debug.json"),1,false,true));
            }

            var prep = new RenderPreparationPackage("prep", new RenderWorkingDirectoryPlan(root, root, root, root, root, root, root, root, root, root, root, "v1", "test"),
            sceneRequests,
            new AssetResolutionPlan([
                new AssetResolutionItem("moon_hero_image","MOON","hero","image",["asset_scene"],["/assets/moon.png"],"fallback",true,"resolved",1,["CelestialAssetCompositor"]),
                new AssetResolutionItem("jupiter_hero_image","JUPITER","hero","image",["hybrid_scene"],["/assets/jupiter.png"],"fallback",true,"resolved",1,["HybridCompositor"])
            ]),
            new StellariumRenderPlan([
                new StellariumRenderJob("job1","hero_scene","req-stel",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,"us",0,0,"UTC",[],"intent","1920x1080",Path.Combine(root,"hero.ssc"),Path.Combine(root,"hero_capture.png"),8,[],"still",1,"planned"),
                new StellariumRenderJob("job2","hybrid_scene","req-hybrid",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,"us",0,0,"UTC",[],"intent","1920x1080",Path.Combine(root,"hybrid.ssc"),Path.Combine(root,"hybrid_capture.png"),8,[],"still",1,"planned")
            ]),
            new OverlayRenderPlan([new OverlayRenderJob("ov1","hero_scene","text labels","Hero",0,3,1,"none","safe","body",Path.Combine(root,"ov1.png"),"png",1,"planned")]),
            new TimelineRenderPlan(20,2,1,0,100,[]),
            new ThumbnailRenderPlan("thumb-1","ThumbnailCompositor","Hybrid",[],[],"focus","left","wow","safe","mobile","crop",[],Path.Combine(root,"thumbnails","thumb.png"),Path.Combine(root,"thumb.meta.json"),Path.Combine(root,"thumb.debug.json"),"planned"),
            new RenderPreparationValidation(true,true,true,true,true,true,true,true,true,true,[] ,[]),
            new RenderPreparationFreezeStatus(true,true,[],[],[]));

            return Task.FromResult(new WeeklySkyForecastV2IntelligenceResponse(
                ContentGenerationPlanId: null,
                Category: "WeeklySkyForecast",
                Success: true,
                WeekStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
                WeekEndDate: DateOnly.FromDateTime(DateTime.UtcNow),
                Region: "US",
                SkyfieldSummary: new WeeklySkyForecastV2SkyfieldSummary(0,0,0,0,null,null,null),
                EventIntelligence: [],
                WeeklyStoryArc: new WeeklyStoryArc("h","s","t","o",[],"c",[],[],[]),
                EditorialStoryPackage: new WeeklyEditorialStoryPackage(new WeeklyHeroEvent("e","t","t","d",DateOnly.FromDateTime(DateTime.UtcNow),null,[],[],0,0,0,"v","w"),[],"h","s","o","t",[],[],new WeeklyThumbnailDirection([],[],[],"e","v","c","b","o"),[],"",[]),
                CinematicStoryBlueprint: null,
                NarrativeAbstractionPackage: null,
                NarrationPlan: null,
                GeneratedNarrationPackage: null,
                NarrationQuality: null,
                VisualRequirementPackage: null,
                HybridScenePlanPackage: null,
                NormalizedEditorialPackage: null,
                SceneChoreographyPackage: null,
                CinematicChoreographyPackage: null,
                RenderExecutionPackage: null,
                RenderPreparationPackage: prep,
                ExecutionValidation: null,
                PreviewStability: null,
                Phase5FoundationStatus: null,
                RenderPreparationFreezeStatus: null,
                ReadyForRenderPreparation: true,
                ReadyForSceneRendering: true,
                ReadyForRendering: false,
                LegacyEditorialPackageDeprecated: true,
                RecommendedVisualStrategies: [],
                Warnings: [],
                StepResults: []));
        }
    }
}
