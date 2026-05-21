using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class DailySkyGuideAssetAwareCompositionPlannerTests
{
    [Fact]
    public async Task CompositionPlan_Returns_4_Ordered_Segments_With_Captured_Asset_Paths_And_Ready_True()
    {
        var planId = Guid.NewGuid();
        var context = BuildContext(planId, allExist: true);
        var planner = new DailySkyGuideAssetAwareCompositionPlanner(new FakeContextService(context));

        var plan = await planner.BuildAsync(planId, CancellationToken.None);

        Assert.Equal(4, plan.TotalSegments);
        Assert.True(plan.ReadyForComposition);
        Assert.Equal(new[] { 1, 2, 3, 4 }, plan.Segments.Select(x => x.SortOrder));
        Assert.Equal(new[] { "IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground" }, plan.Segments.Select(x => x.VisualRole));
        Assert.All(plan.Segments, x => Assert.True(x.ImageExists));
        Assert.Equal("D:/captures/02_DailySkyGuide_MoonFocus_ThumbnailCandidate.png", plan.Segments[1].ImagePath);
        Assert.Equal("FadeIn", plan.Segments[0].TransitionType);
        Assert.Equal("SlowZoom", plan.Segments[1].TransitionType);
        Assert.Equal("PanAndZoom", plan.Segments[2].TransitionType);
        Assert.Equal("CrossFade", plan.Segments[3].TransitionType);
        Assert.Equal([], plan.Warnings);
    }

    [Fact]
    public async Task CompositionPlan_Returns_Ready_False_And_Warnings_When_Assets_Missing()
    {
        var planId = Guid.NewGuid();
        var context = BuildContext(planId, allExist: false);
        var planner = new DailySkyGuideAssetAwareCompositionPlanner(new FakeContextService(context));

        var plan = await planner.BuildAsync(planId, CancellationToken.None);

        Assert.False(plan.ReadyForComposition);
        Assert.Contains(plan.Segments, x => x.VisualRole == "SupportingSkyMap" && !x.ImageExists);
        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, x => x.Contains("SupportingSkyMap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolver_Returns_DailySkyGuide_Planner()
    {
        var planner = new DailySkyGuideAssetAwareCompositionPlanner(new FakeContextService(BuildContext(Guid.NewGuid(), true)));
        var resolver = new AssetAwareCompositionPlannerResolver([planner]);

        var resolved = resolver.Resolve("DailySkyGuide");

        Assert.NotNull(resolved);
        Assert.Equal("DailySkyGuide", resolved!.ContentCategoryCode);
    }

    [Fact]
    public async Task Endpoint_Returns_Plan_Without_Pipeline_Run_Or_Db_Update()
    {
        var fake = new FakePlanner();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDailySkyGuideAssetAwareCompositionPlanner>(fake);
        var app = builder.Build();
        app.MapGet("/api/content-planning/plans/{id:guid}/daily-skyguide-composition-plan", async (Guid id, IDailySkyGuideAssetAwareCompositionPlanner planner, CancellationToken ct) => Results.Ok(await planner.BuildAsync(id, ct)));

        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync($"/api/content-planning/plans/{Guid.NewGuid()}/daily-skyguide-composition-plan");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(0, fake.FfmpegCalls);
        Assert.Equal(0, fake.PipelineRunCalls);
        Assert.Equal(0, fake.DbUpdateCalls);
        await app.StopAsync();
    }

    private static DailySkyGuideAssetAwareExecutionContext BuildContext(Guid planId, bool allExist)
        => new(
            planId,
            "DailySkyGuide",
            "IN-RJ-UDAIPUR",
            "Udaipur",
            new DateOnly(2026, 5, 21),
            "hi",
            "Tonight's Sky Guide",
            "Moon",
            "D:/captures/02_DailySkyGuide_MoonFocus_ThumbnailCandidate.png",
            [
                new DailySkyGuideVisualAsset("IntroBackground", "D:/captures/01_DailySkyGuide_IntroWideSky_IntroBackground.png", true, 1, null, null, null),
                new DailySkyGuideVisualAsset("ThumbnailCandidate", "D:/captures/02_DailySkyGuide_MoonFocus_ThumbnailCandidate.png", true, 2, null, null, null),
                new DailySkyGuideVisualAsset("SupportingSkyMap", "D:/captures/03_DailySkyGuide_StarMap_SupportingSkyMap.png", allExist, 3, null, null, null),
                new DailySkyGuideVisualAsset("OutroBackground", "D:/captures/04_DailySkyGuide_Outro_OutroBackground.png", true, 4, null, null, null)
            ],
            ["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"],
            []);

    private sealed class FakeContextService(DailySkyGuideAssetAwareExecutionContext context) : IDailySkyGuideAssetAwareContextService
    {
        public Task<DailySkyGuideAssetAwareExecutionContext> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(context);
    }

    private sealed class FakePlanner : IDailySkyGuideAssetAwareCompositionPlanner
    {
        public int Calls;
        public int FfmpegCalls;
        public int PipelineRunCalls;
        public int DbUpdateCalls;

        public Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AssetAwareVideoCompositionPlan(contentGenerationPlanId, "DailySkyGuide", "Udaipur", new DateOnly(2026, 5, 21), "hi", "t", 0, [], [], false));
        }
    }
}
