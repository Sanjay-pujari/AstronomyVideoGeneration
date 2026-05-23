using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    public async Task Orchestrator_ProcessesAllRequests_AndGeneratesSscOnlyForStellarium()
    {
        var orchestrator = new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceService(), NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>.Instance);
        var result = await orchestrator.RunAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow), null, CancellationToken.None);

        Assert.True(result.SceneRenderingValidation.IsValid);
        Assert.True(result.SceneRenderingValidation.ReadyForTimelineComposition);
        Assert.False(result.SceneRenderingValidation.ReadyForPublishing);
        Assert.Equal(2, result.SceneRenderResults.Count);
        Assert.Single(result.StellariumRenderResults);
        Assert.DoesNotContain(result.StellariumRenderResults, x => x.SceneCode == "moon_jupiter_hero_scene");
        Assert.All(result.SceneRenderResults, x => Assert.False(string.IsNullOrWhiteSpace(x.OutputPath)));
    }

    private sealed class FakeOrchestrator : IWeeklySkyForecastSceneRenderingOrchestrator
    {
        public Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(new SceneRenderingPackage([], [], [], [], [], null, new SceneRenderingValidation(true, true, true, true, true, true, true, true, false, [], []), new SceneRenderingFreezeStatus(true, [], [], [])));
    }

    private sealed class FakeIntelligenceService : IWeeklySkyForecastV2IntelligenceService
    {
        public Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2IntelligenceRequest request, CancellationToken cancellationToken)
        {
            var root = Path.Combine(Path.GetTempPath(), "weekly-v2-render-tests", Guid.NewGuid().ToString("N"));
            var prep = new RenderPreparationPackage("prep", new RenderWorkingDirectoryPlan(root, root, root, root, root, root, root, root, root, root, root, "v1", "test"),
            [
                new SceneRenderRequest("req-stel","hero_scene","Stellarium","Stellarium",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,8,[],[],[],[],null,[],[],"fallback",Path.Combine(root,"hero.mp4"),Path.Combine(root,"hero.meta.json"),Path.Combine(root,"hero.debug.json"),1,false,true),
                new SceneRenderRequest("req-asset","moon_jupiter_hero_scene","CelestialAsset","Asset",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,8,[],[],[],["moon.png"],null,[],[],"fallback",Path.Combine(root,"moon-jupiter.mp4"),Path.Combine(root,"moon-jupiter.meta.json"),Path.Combine(root,"moon-jupiter.debug.json"),1,false,true)
            ],
            new AssetResolutionPlan([]),
            new StellariumRenderPlan([new StellariumRenderJob("job1","hero_scene","req-stel",DateOnly.FromDateTime(DateTime.UtcNow),DateTime.UtcNow,"us",0,0,"UTC",[],"intent","1920x1080",Path.Combine(root,"hero.ssc"),Path.Combine(root,"hero_capture.png"),8,[],"still",1,"planned")]),
            new OverlayRenderPlan([new OverlayRenderJob("ov1","hero_scene","text labels","Hero",0,3,1,"none","safe","body",Path.Combine(root,"ov1.png"),"png",1,"planned")]),
            new TimelineRenderPlan(20,2,1,0,100,[]),
            new ThumbnailRenderPlan("thumb-1","Thumbnail","Hybrid",[],[],"focus","left","wow","safe","mobile","crop",[],Path.Combine(root,"thumb.png"),Path.Combine(root,"thumb.meta.json"),Path.Combine(root,"thumb.debug.json"),"planned"),
            new RenderPreparationValidation(true,true,true,true,true,true,true,true,true,[] ,[]),
            new RenderPreparationFreezeStatus(true,true,[],[],[]));

            return Task.FromResult(new WeeklySkyForecastV2IntelligenceResponse(null,"WeeklySkyForecast",true,DateOnly.FromDateTime(DateTime.UtcNow),DateOnly.FromDateTime(DateTime.UtcNow),"US",new WeeklySkyForecastV2SkyfieldSummary(0,0,0,0,null,null,null),[],new WeeklyStoryArc("h","s","t","o",[],"c",[],[],[]),new WeeklyEditorialStoryPackage(new WeeklyHeroEvent("e","t","t","d",DateOnly.FromDateTime(DateTime.UtcNow),null,[],[],0,0,0,"v","w"),[],"h","s","o","t",[],[],new WeeklyThumbnailDirection([],[],[],"e","v","c","b","o"),[],"",[]),null,null,null,null,null,null,null,null,null,prep,null,null,null,null,true,true,false,true,[],[],[]));
        }
    }
}
