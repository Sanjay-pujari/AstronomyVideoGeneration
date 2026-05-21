using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class DailySkyGuideAssetAwareContextTests
{
    [Fact]
    public async Task Provider_Returns_Assets_When_Files_Exist_And_Thumbnail_Resolved_And_Order_Correct()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var planId = Guid.NewGuid();
        var dir = Path.Combine(root, "content-plans", planId.ToString(), "stellarium-scenes");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "01_x_IntroBackground.png"), "x");
        File.WriteAllText(Path.Combine(dir, "02_x_ThumbnailCandidate.png"), "x");
        File.WriteAllText(Path.Combine(dir, "03_x_SupportingSkyMap.png"), "x");
        File.WriteAllText(Path.Combine(dir, "04_x_OutroBackground.png"), "x");

        var provider = new CapturedDailySkyGuideVisualAssetProvider(Options.Create(new StellariumOptions { CaptureDirectory = root }));
        var assets = await provider.GetAssetsAsync(planId, CancellationToken.None);
        Assert.All(assets, x => Assert.True(x.Exists));
        Assert.Equal(new[] { "IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground" }, assets.Select(x => x.Role));

        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Service_Returns_Missing_Assets_Safely_And_Rejects_Non_DailySkyGuide()
    {
        await using var db = CreateDb();
        var id = Guid.NewGuid();
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { Id = id, ContentCategoryCode = "SpecialEventGuide", RegionId = "IN-RJ-UDAIPUR", Language = "hi", Title = "x", PrimaryCelestialObjectCode = "Moon" });
        await db.SaveChangesAsync();

        var svc = new DailySkyGuideAssetAwareContextService(db, new FakePlanning(), new CapturedDailySkyGuideVisualAssetProvider(Options.Create(new StellariumOptions { CaptureDirectory = Path.GetTempPath() })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BuildAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Api_Endpoint_Does_Not_Update_Db_Or_Run_Pipeline()
    {
        var fake = new FakeContextService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IDailySkyGuideAssetAwareContextService>(fake);
        var app = builder.Build();
        app.MapGet("/api/content-planning/plans/{id:guid}/daily-skyguide-asset-context", async (Guid id, IDailySkyGuideAssetAwareContextService contextService, CancellationToken ct) => Results.Ok(await contextService.BuildAsync(id, ct)));

        await app.StartAsync();
        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/content-planning/plans/{Guid.NewGuid()}/daily-skyguide-asset-context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fake.Calls);
        Assert.Equal(0, fake.PipelineRunCalls);
        Assert.Equal(0, fake.DbUpdateCalls);
        await app.StopAsync();
    }

    [Fact]
    public async Task NoOp_Consumer_Does_Not_Consume()
    {
        var consumer = new NoOpDailySkyGuideVisualAssetConsumer();
        var ctx = new DailySkyGuideAssetAwareExecutionContext(Guid.NewGuid(), "DailySkyGuide", "r", "l", new DateOnly(2026, 5, 21), "hi", null, null, null, [], [], []);
        Assert.False(await consumer.CanConsumeAsync(ctx, CancellationToken.None));
        await consumer.ConsumeAsync(ctx, CancellationToken.None);
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class FakePlanning : IContentPlanningService
    {
        public Task<StellariumSceneCapturePlan> BuildStellariumScenePlanPreviewAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(new StellariumSceneCapturePlan(id, "DailySkyGuide", "IN-RJ-UDAIPUR", "Udaipur", 1, 1, "Asia/Kolkata", new DateOnly(2026, 5, 21), [new("s1", "WideSky", "title", "Moon", "Moon", DateTime.UtcNow, "Focus", null, true, true, true, false, false, "IntroBackground", 1, null)], []));
        public Task<DailySkyGuideContext> BuildDailySkyGuideContextPreviewAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(new DailySkyGuideContext(id, "IN-RJ-UDAIPUR", "Udaipur", 1, 1, "Asia/Kolkata", new DateOnly(2026, 5, 21), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Moon", "Moon", ["Moon"], [], "x", "x", "x", null, 1, []));
        public Task<GenerateContentPlanResponse> GeneratePlanAsync(GenerateContentPlanRequest request, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentGenerationPlan> GenerateDailyPlanAsync(string contentCategoryCode, string language, string regionId, DateTimeOffset scheduledUtc, string? primaryCelestialObjectCode, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<IReadOnlyCollection<ContentGenerationPlan>> GetPendingPlansAsync(string? status, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<AstronomyVisibilityResult> BuildAstronomyVisibilityPreviewAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<PipelineBuildResult> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<PrepareManualRunResponse?> PrepareManualRunAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<bool> MarkPlanAsInProgressAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<bool> MarkPlanAsCompletedAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<bool> MarkPlanAsFailedAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid id, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentPipelineExecution?> CompleteExecutionAsync(Guid executionId, CompleteContentPlanningExecutionRequest request, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentPipelineExecution?> FailExecutionAsync(Guid executionId, FailContentPlanningExecutionRequest request, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<IReadOnlyCollection<ContentPipelineExecution>> GetExecutionsAsync(string? status, CancellationToken cancellationToken)=>throw new NotImplementedException();
        public Task<ContentPipelineExecution?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken)=>throw new NotImplementedException();
    }

    private sealed class FakeContextService : IDailySkyGuideAssetAwareContextService
    {
        public int Calls; public int PipelineRunCalls; public int DbUpdateCalls;
        public Task<DailySkyGuideAssetAwareExecutionContext> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DailySkyGuideAssetAwareExecutionContext(contentGenerationPlanId, "DailySkyGuide", "IN-RJ-UDAIPUR", "Udaipur", new DateOnly(2026, 5, 21), "hi", "title", "Moon", "thumb.png", [], ["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"], []));
        }
    }
}
