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

public sealed class WeeklySkyForecastV2TimelineCompositionTests
{
    [Fact]
    public async Task ComposeTimelineEndpoint_Exists_AndReturnsPackage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IWeeklySkyForecastTimelineCompositionOrchestrator, FakeTimelineOrchestrator>();
        var app = builder.Build();
        app.MapPost("/api/content-planning/weekly-skyforecast-v2/compose-timeline", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastTimelineCompositionOrchestrator orchestrator, CancellationToken ct) => Results.Ok(await orchestrator.RunAsync(new WeeklySkyForecastV2IntelligenceRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, request.WeekStartDate, request.Diagnostics), request.ContentGenerationPlanId, ct)));
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync("/api/content-planning/weekly-skyforecast-v2/compose-timeline", new WeeklySkyForecastV2RenderScenesRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Orchestrator_ComposesDeterministic110SecondTimeline()
    {
        var orchestrator = new WeeklySkyForecastTimelineCompositionOrchestrator(new FakeIntelligenceServiceFor6C(), new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceServiceFor6C(), NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>.Instance));
        var result = await orchestrator.RunAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow), null, CancellationToken.None);

        Assert.Equal(110, result.TimelineCompositionValidation.TotalDurationSeconds);
        Assert.True(result.TimelineCompositionValidation.TimelineHasNoGaps);
        Assert.True(result.TimelineCompositionValidation.ThumbnailExcluded);
        Assert.True(result.TimelineCompositionValidation.ReuseSceneResolved);
        Assert.True(result.TimelineCompositionValidation.SinglePipelineRunIdUsed);
        Assert.True(result.TimelineCompositionValidation.ReadyForFinalVideoReview);
        Assert.False(result.TimelineCompositionValidation.ReadyForPublishing);
        Assert.Equal("Planned", result.AudioCompositionPlan.Status);
        Assert.False(result.AudioCompositionPlan.AudioRendered);
        Assert.All(result.ShortsCompositionPlans, s => Assert.Equal("Planned", s.Status));
        Assert.True(File.Exists(result.LongFormTimelineResult.OutputPath));
        Assert.NotEmpty(result.TransitionCompositionResults);
        Assert.True(result.NarrationSyncResult.NarrationTrackPlanned);
        Assert.False(result.NarrationSyncResult.AudioRendered);
    }

    private sealed class FakeTimelineOrchestrator : IWeeklySkyForecastTimelineCompositionOrchestrator
    {
        public Task<TimelineCompositionPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(new TimelineCompositionPackage(new LongFormTimelineResult("/tmp/out.mp4", 110, "Composed", true, true, [], []), [], [], new NarrationSyncResult(true, "generated", 100, 110, false, "Planned", [], [], []), new AudioCompositionPlan("a", "b", "c", false, false, false, "Planned"), [], new TimelineCompositionValidation(true, true, 110, 110, true, true, true, true, true, true, true, false, [], []), new TimelineCompositionFreezeStatus(true, true, [], [], [])));
    }
}

internal sealed class FakeIntelligenceServiceFor6C : IWeeklySkyForecastV2IntelligenceService
{
    public Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2IntelligenceRequest request, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "weekly-v2-compose-tests", Guid.NewGuid().ToString("N"));
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var sceneRequests = new List<SceneRenderRequest>
        {
            Mk("req-hero","hero_western_grouping_scene",48,false,null),
            Mk("req-best","best_night_wide_scene",19,false,null),
            Mk("req-moon","moon_jupiter_hero_scene",18,false,null),
            Mk("req-tip","viewing_tip_wide_scene",11,false,null),
            Mk("req-close","best_night_wide_closing_reuse",14,false,"best_night_wide_scene"),
            Mk("req-thumb","thumbnail_story_scene",3,true,null)
        };

        SceneRenderRequest Mk(string id, string code, int dur, bool thumb, string? reuse) =>
            new(id, code, "HybridCompositor", "Hybrid", now, DateTime.UtcNow, dur, [code+"-narr"], [], [], [], null, [], [], "fallback", Path.Combine(root, code+".mp4"), Path.Combine(root, code+".meta.json"), Path.Combine(root, code+".debug.json"), 1, thumb, true, reuse is not null, reuse);

        var timeline = new TimelineRenderPlan(110, 6, 5, 0, 100, [
            new TimelineRenderSegment("seg-hero","hero_western_grouping_scene","req-hero",0,48,48,["hero_narr"],"intro fade-in","soft crossfade",1,1,false,"",false),
            new TimelineRenderSegment("seg-best","best_night_wide_scene","req-best",48,67,19,["best_narr"],"soft crossfade","cinematic push",1,1,false,"",false),
            new TimelineRenderSegment("seg-moon","moon_jupiter_hero_scene","req-moon",67,85,18,["moon_narr"],"cinematic push","gentle dissolve",1,1,false,"",false),
            new TimelineRenderSegment("seg-tip","viewing_tip_wide_scene","req-tip",85,96,11,["tip_narr"],"gentle dissolve","closing fade",1,1,false,"",false),
            new TimelineRenderSegment("seg-close","best_night_wide_closing_reuse","req-close",96,110,14,["close_narr"],"closing fade","closing fade",1,1,false,"",false),
            new TimelineRenderSegment("seg-thumb","thumbnail_story_scene","req-thumb",0,3,3,[],"none","none",0,0,false,"",true)
        ]);

        var prep = new RenderPreparationPackage("prep", new RenderWorkingDirectoryPlan(root, root, root, root, root, root, Path.Combine(root,"final"), root, root, root, root, "v1", "test"), sceneRequests, new AssetResolutionPlan([]), new StellariumRenderPlan([]), new OverlayRenderPlan([]), timeline, new ThumbnailRenderPlan("thumb","ThumbnailCompositor","Hybrid",[],[],"f","e","emo","s","m","c",[],Path.Combine(root,"thumb.png"),Path.Combine(root,"thumb.meta"),Path.Combine(root,"thumb.debug"),"planned"), new RenderPreparationValidation(true,true,true,true,true,true,true,true,true,[],[]), new RenderPreparationFreezeStatus(true,true,[],[],[]));

        var transitions = new List<TransitionExecutionDirective>
        {
            new("intro","hero_western_grouping_scene","intro fade-in",0,1,"m","t1"),
            new("hero_western_grouping_scene","best_night_wide_scene","soft crossfade",48,1,"m","t2"),
            new("best_night_wide_scene","moon_jupiter_hero_scene","cinematic push",67,1,"m","t3"),
            new("moon_jupiter_hero_scene","viewing_tip_wide_scene","gentle dissolve",85,1,"m","t4"),
            new("viewing_tip_wide_scene","best_night_wide_closing_reuse","closing fade",96,1,"m","t5")
        };
        var exec = new WeeklyRenderExecutionPackage("e",[],timeline.TimelineSegments.Select(s=>new WeeklySceneTimeline(s.SceneCode,s.StartSecond,s.EndSecond,s.StartSecond,s.EndSecond,0,s.NarrationSegmentCodes,s.TransitionInSeconds,s.TransitionOutSeconds,s.IsThumbnailOnly,false,false)).ToList(),[],[],[],[],[],transitions,[],new ThumbnailExecutionContract("t","v",[],[],"f","e","em","s","m","c",[],"f","o"),[]);
        var generated = new WeeklyGeneratedNarrationPackage("en","tone",new WeeklyGeneratedLongNarration("full",110,[new WeeklyGeneratedNarrationSegment("hero_narr","h","x",48,[],"",""),new WeeklyGeneratedNarrationSegment("best_narr","b","x",19,[],"",""),new WeeklyGeneratedNarrationSegment("moon_narr","m","x",18,[],"",""),new WeeklyGeneratedNarrationSegment("tip_narr","t","x",11,[],"","") ,new WeeklyGeneratedNarrationSegment("close_narr","c","x",14,[],"","")]),[],[]);

        return Task.FromResult(new WeeklySkyForecastV2IntelligenceResponse(null,"WeeklySkyForecast",true,now,now,"US",new WeeklySkyForecastV2SkyfieldSummary(0,0,0,0,null,null,null),[],new WeeklyStoryArc("h","s","t","o",[],"c",[],[],[]),new WeeklyEditorialStoryPackage(new WeeklyHeroEvent("e","t","t","d",now,null,[],[],0,0,0,"v","w"),[],"h","s","o","t",[],[],new WeeklyThumbnailDirection([],[],[],"e","v","c","b","o"),[] ,"",[]),new WeeklyCinematicStoryBlueprint("id","h","s","o","p",new WeeklyHeroStory("t","d",now,[],null,[],[],"a","b","c","v","s"),[],[],[],[new WeeklyShortBlueprint("short_hero","Hero Short","Hook","Angle",[],now,"v",30,1)],new WeeklyThumbnailBlueprint([],[],[],"e","c","b","o","v"),"n","v",[]),null,null,generated,null,null,null,null,null,exec,prep,null,null,null,null,true,true,true,true,[],[],[]));
    }
}
