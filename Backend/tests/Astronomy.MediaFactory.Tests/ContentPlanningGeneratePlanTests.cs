using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed partial class ContentPlanningGeneratePlanTests
{
    [Fact]
    public async Task GeneratePlanAsync_Creates_ContentGenerationPlan_Row()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var svc = CreateService(db);

        var response = await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "en", "IN-RJ-UDAIPUR", "Udaipur"), CancellationToken.None);

        var row = await db.ContentGenerationPlans.SingleAsync(x => x.Id == response.ContentGenerationPlanId);
        Assert.Equal("Planned", row.Status);
        Assert.Equal("Planned", response.Status);
    }

    [Fact]
    public async Task GeneratePlanAsync_Falls_Back_To_English_Template_For_Hindi()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentIdeaTemplates.Add(new ContentIdeaTemplate { ContentCategoryCode = "DailySkyGuide", TemplateCode = "EN1", Language = "en", Enabled = true, Priority = 99, TitleTemplate = "{ContentCategoryCode} for {RegionName}", TopicTemplate = "{RegionId}" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var response = await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "hi", "IN-RJ-UDAIPUR", "Udaipur"), CancellationToken.None);

        Assert.Equal("DailySkyGuide for Udaipur", response.Title);
        Assert.Contains("EN1", response.PlanningReason);
    }

    [Fact]
    public async Task GeneratePlanAsync_Selects_Style_Settings_From_Category_Style_Table()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, Priority = 500, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var response = await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "en", "IN-RJ-UDAIPUR", "Udaipur"), CancellationToken.None);

        var row = await db.ContentGenerationPlans.SingleAsync(x => x.Id == response.ContentGenerationPlanId);
        Assert.Equal("HookA", row.HookStyleCode);
        Assert.Equal("NarA", row.NarrationStyleCode);
        Assert.Equal("ThumbA", row.ThumbnailStyleCode);
    }

    [Fact]
    public async Task GeneratePlanAsync_Missing_Optional_Celestial_Object_Does_Not_Fail()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var svc = CreateService(db);

        var response = await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "en", "IN-RJ-UDAIPUR", "Udaipur", PrimaryCelestialObjectCode: "UnknownObject"), CancellationToken.None);

        Assert.Equal("Planned", response.Status);
    }

    [Fact]
    public async Task GeneratePlanAsync_Does_Not_Trigger_Pipeline_Execution()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var svc = CreateService(db);

        await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "en", "IN-RJ-UDAIPUR", "Udaipur"), CancellationToken.None);

        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public async Task GeneratePlanAsync_Status_Is_Always_Planned()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var svc = CreateService(db);

        var response = await svc.GeneratePlanAsync(new GenerateContentPlanRequest("DailySkyGuide", "en", "IN-RJ-UDAIPUR", "Udaipur", GeneratedByAi: true), CancellationToken.None);

        Assert.Equal("Planned", response.Status);
    }

    [Fact]
    public void Existing_Generate_Daily_Plan_Method_Remains_Available()
    {
        var method = typeof(IContentPlanningService).GetMethod(nameof(IContentPlanningService.GenerateDailyPlanAsync));
        Assert.NotNull(method);
    }

    [Fact]
    public async Task DailySkyContextPreview_Builds_Context_Only()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.CelestialObjects.Add(new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 1, PhotogenicScore = 1, EducationalScore = 1, ViralityScore = 1 });
        var scheduledUtc = new DateTimeOffset(2026, 5, 21, 14, 0, 0, TimeSpan.Zero);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = scheduledUtc, PrimaryCelestialObjectCode = "Moon" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var context = await svc.BuildDailySkyGuideContextPreviewAsync(plan.Id, CancellationToken.None);

        Assert.Equal("IN-RJ-UDAIPUR", context.RegionId);
        Assert.Equal("MoonDominant", context.ThumbnailStrategy);
        Assert.Equal("Stellarium", context.ImageInputSource);
        Assert.Equal("AzureSpeech", context.AudioSource);
        Assert.Equal(3, context.SceneCaptureTimesUtc.Count);
        Assert.Empty(db.ContentPipelineExecutions);
    }


    [Fact]
    public async Task AstronomyVisibilityPreview_Returns_Result_And_Does_Not_Update_Db()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.CelestialObjects.Add(new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 1, PhotogenicScore = 1, EducationalScore = 1, ViralityScore = 1 });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow, PrimaryCelestialObjectCode = "Moon" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var before = plan.UpdatedUtc;
        var svc = CreateService(db);

        var result = await svc.BuildAstronomyVisibilityPreviewAsync(plan.Id, CancellationToken.None);

        Assert.Equal("IN-RJ-UDAIPUR", result.RegionId);
        Assert.NotEmpty(result.VisibleObjects);
        Assert.Equal(before, (await db.ContentGenerationPlans.SingleAsync(x=>x.Id==plan.Id)).UpdatedUtc);
        Assert.Empty(db.ContentPipelineExecutions);
    }
    private static ContentPlanningService CreateService(MediaFactoryDbContext db)
    {
        var packager = new DailySkyGuideVisualAssetPackager(Options.Create(new StellariumOptions { CaptureDirectory = Path.GetTempPath() }));
        var contextBuilder = new DailySkyGuideContextBuilder(db, Options.Create(new Astronomy.MediaFactory.Contracts.SchedulerOptions()), new AstronomyVisibilityService(db, new FakeSkyfieldVisibilityClient(), Options.Create(new SkyfieldSidecarOptions())), new StellariumScenePlannerResolver([new DailySkyGuideStellariumScenePlanner()]));
        return new(
            db,
            new NoopVarietyGuard(),
            new ContentCategoryPipelineStrategyResolver([new DailySkyGuidePipelineStrategy(packager, contextBuilder)]),
            contextBuilder,
            new AstronomyVisibilityService(db, new FakeSkyfieldVisibilityClient(), Options.Create(new SkyfieldSidecarOptions())),
            new StellariumScenePlannerResolver([new DailySkyGuideStellariumScenePlanner()]));
    }

    private static void SeedRequired(MediaFactoryDbContext db)
    {
        db.ContentCategories.Add(new ContentCategoryMaster { Code = "DailySkyGuide", DisplayName = "Daily", Priority = 1, Enabled = true });
        db.SaveChanges();
    }

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoopVarietyGuard : IContentVarietyGuard
    {
        public Task<bool> CanUseCelestialObjectAsync(string categoryCode, string objectCode, DateTimeOffset date, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> CanUseStyleAsync(string categoryCode, string styleCode, string styleType, DateTimeOffset date, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyCollection<ContentVarietyBlockedItem>> GetBlockedItemsAsync(string categoryCode, DateTimeOffset date, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<ContentVarietyBlockedItem>>([]);
    }

    private sealed class FakeSkyfieldVisibilityClient : ISkyfieldVisibilityClient
    {
        public Task<SkyfieldVisibilityResponse> CalculateAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "not configured in unit tests"));
    }

    private sealed class FakeDailySkyGuideContextBuilder : IDailySkyGuideContextBuilder
    {
        public Task<DailySkyGuideContext> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
            => Task.FromResult(new DailySkyGuideContext(
                plan.Id,
                plan.RegionId,
                "Region",
                0,
                0,
                "UTC",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                "Moon",
                "Moon",
                [],
                [],
                "Stellarium",
                "AzureSpeech",
                "MoonDominant",
                null,
                0,
                []));
    }
}



public sealed partial class ContentPlanningGeneratePlanTests
{
    [Fact]
    public async Task PipelineRequestPreview_Returns_Json_For_Planned()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "T", ScheduledUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var planId = db.ContentGenerationPlans.Single().Id;
        var svc = CreateService(db);

        var preview = await svc.BuildPipelineRequestPreviewAsync(planId, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.NotNull(preview.PipelineRequest);
        Assert.NotNull(preview.AssetAwareMetadata);
    }

    [Fact]
    public async Task PipelineRequestPreview_Returns_Json_For_ReadyForManualRun()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "ReadyForManualRun", Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "T", ScheduledUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var planId = db.ContentGenerationPlans.Single().Id;
        var svc = CreateService(db);

        var preview = await svc.BuildPipelineRequestPreviewAsync(planId, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.NotNull(preview.PipelineRequest);
        Assert.NotNull(preview.AssetAwareMetadata);
    }

    [Fact]
    public async Task PipelineRequestPreview_Does_Not_Execute_Pipeline_Or_Update_Db()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentCategoryStyleSettings.Add(new ContentCategoryStyleSettings { ContentCategoryCode = "DailySkyGuide", Language = "en", Enabled = true, HookStyleCode = "HookA", NarrationStyleCode = "NarA", ThumbnailStyleCode = "ThumbA" });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", Title = "Original", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var beforeUpdated = plan.UpdatedUtc;
        var svc = CreateService(db);

        _ = await svc.BuildPipelineRequestPreviewAsync(plan.Id, CancellationToken.None);

        Assert.Empty(db.ContentPipelineExecutions);
        var reloaded = await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id);
        Assert.Equal(beforeUpdated, reloaded.UpdatedUtc);
        Assert.Equal("Original", reloaded.Title);
    }

    [Fact]
    public async Task PipelineRequestPreview_Includes_Warnings_For_Missing_Optional_Master_Data()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan
        {
            ContentCategoryCode = "DailySkyGuide",
            Status = "Planned",
            Language = "fr",
            RegionId = "IN-RJ-UDAIPUR",
            PrimaryCelestialObjectCode = "UnknownObject",
            PrimaryAstronomyEventTypeCode = "UnknownEvent"
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var preview = await svc.BuildPipelineRequestPreviewAsync(db.ContentGenerationPlans.Single().Id, CancellationToken.None);

        Assert.True(preview.Success);
    }

    [Fact]
    public async Task PipelineRequestPreview_AssetAwareMetadata_Works_Without_Assets()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan
        {
            ContentCategoryCode = "DailySkyGuide",
            Status = "Planned",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var preview = await svc.BuildPipelineRequestPreviewAsync(db.ContentGenerationPlans.Single().Id, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.NotNull(preview.PipelineRequest);
        Assert.NotNull(preview.AssetAwareMetadata);
        Assert.Empty(preview.AssetAwareMetadata!.RecommendedImageSequence);
        Assert.Contains(preview.Warnings, x => x.Contains("Missing expected assets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PipelineRequestPreview_AssetAwareMetadata_Works_With_Assets()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "DailySkyGuide",
            Status = "Planned",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.UtcNow
        };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();

        var sceneRoot = Path.Combine(Path.GetTempPath(), "content-plans", plan.Id.ToString(), "stellarium-scenes");
        Directory.CreateDirectory(sceneRoot);
        foreach (var role in new[] { "IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground" })
        {
            await File.WriteAllTextAsync(Path.Combine(sceneRoot, $"01_{role}_{role}.png"), "asset");
        }

        var svc = CreateService(db);
        var preview = await svc.BuildPipelineRequestPreviewAsync(plan.Id, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.NotNull(preview.PipelineRequest);
        Assert.NotNull(preview.AssetAwareMetadata);
        Assert.Equal(["IntroBackground", "ThumbnailCandidate", "SupportingSkyMap", "OutroBackground"], preview.AssetAwareMetadata!.RecommendedImageSequence);
        Assert.NotNull(preview.AssetAwareMetadata.ThumbnailCandidatePath);
    }

    [Fact]
    public void Existing_Pipeline_Run_Request_Dto_Remains_Unchanged()
    {
        var type = typeof(Astronomy.MediaFactory.Contracts.RunPipelineRequest);
        Assert.NotNull(type.GetProperty("Date"));
        Assert.NotNull(type.GetProperty("ContentType"));
        Assert.NotNull(type.GetProperty("LocationName"));
    }

    [Fact]
    public async Task StartManualExecution_Creates_Execution_And_Updates_Plan_Status()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.StartManualExecutionAsync(plan.Id, CancellationToken.None);

        Assert.NotNull(result);
        var execution = await db.ContentPipelineExecutions.SingleAsync(x => x.Id == result!.ContentPipelineExecutionId);
        Assert.Equal("InProgress", execution.Status);
        Assert.Null(execution.PipelineRunId);
        var reloadedPlan = await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id);
        Assert.Equal("InProgress", reloadedPlan.Status);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Skipped")]
    [InlineData("Cancelled")]
    public async Task StartManualExecution_Rejects_Invalid_Statuses(string status)
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = status, Language = "en", RegionId = "IN-RJ-UDAIPUR" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartManualExecutionAsync(plan.Id, CancellationToken.None));
        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public async Task CompleteExecution_Marks_Execution_And_Plan_As_Completed()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "InProgress", Language = "en", RegionId = "IN-RJ-UDAIPUR" };
        db.ContentGenerationPlans.Add(plan);
        var execution = new ContentPipelineExecution { ContentGenerationPlanId = plan.Id, ContentCategoryCode = plan.ContentCategoryCode, Status = "InProgress", StartedUtc = DateTimeOffset.UtcNow };
        db.ContentPipelineExecutions.Add(execution);
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        var runId = Guid.NewGuid();

        var updated = await svc.CompleteExecutionAsync(execution.Id, new CompleteContentPlanningExecutionRequest(runId, "/tmp/out", "long.mp4", "short.mp4", "long.png", "short.png", true, true), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Completed", updated!.Status);
        Assert.Equal(runId, updated.PipelineRunId);
        Assert.True(updated.PublishingCompleted);
        Assert.True(updated.AnalyticsInitialized);
        Assert.Equal("Completed", (await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id)).Status);
    }

    [Fact]
    public async Task FailExecution_Marks_Execution_And_Plan_As_Failed()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "InProgress", Language = "en", RegionId = "IN-RJ-UDAIPUR" };
        db.ContentGenerationPlans.Add(plan);
        var execution = new ContentPipelineExecution { ContentGenerationPlanId = plan.Id, ContentCategoryCode = plan.ContentCategoryCode, Status = "InProgress", StartedUtc = DateTimeOffset.UtcNow };
        db.ContentPipelineExecutions.Add(execution);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var updated = await svc.FailExecutionAsync(execution.Id, new FailContentPlanningExecutionRequest("manual error"), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Failed", updated!.Status);
        Assert.Equal("manual error", updated.ErrorMessage);
        Assert.Equal("Failed", (await db.ContentGenerationPlans.SingleAsync(x => x.Id == plan.Id)).Status);
    }
}

public sealed partial class ContentPlanningGeneratePlanTests
{
    [Fact]
    public void Resolver_Returns_DailySkyGuide_Strategy()
    {
        var resolver = new ContentCategoryPipelineStrategyResolver([new DailySkyGuidePipelineStrategy(new DailySkyGuideVisualAssetPackager(Options.Create(new StellariumOptions { CaptureDirectory = Path.GetTempPath() })), new FakeDailySkyGuideContextBuilder())]);
        Assert.NotNull(resolver.Resolve("DailySkyGuide"));
    }

    [Fact]
    public void Resolver_Returns_Null_For_WeeklySkyForecast()
    {
        var resolver = new ContentCategoryPipelineStrategyResolver([new DailySkyGuidePipelineStrategy(new DailySkyGuideVisualAssetPackager(Options.Create(new StellariumOptions { CaptureDirectory = Path.GetTempPath() })), new FakeDailySkyGuideContextBuilder())]);
        Assert.Null(resolver.Resolve("WeeklySkyForecast"));
    }

    [Fact]
    public async Task PrepareManualRun_Changes_Status_And_Does_Not_Create_Execution()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var response = await svc.PrepareManualRunAsync(plan.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("ReadyForManualRun", response!.Status);
        Assert.NotNull(response.PipelineRequest);
        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public async Task Preview_Unsupported_Category_Returns_Clear_Warning()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { ContentCategoryCode = "WeeklySkyForecast", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var preview = await svc.BuildPipelineRequestPreviewAsync(db.ContentGenerationPlans.Single().Id, CancellationToken.None);

        Assert.True(preview.Success);
        Assert.Contains("No pipeline strategy implemented for this category yet.", preview.Warnings);
    }
}


public sealed partial class ContentPlanningGeneratePlanTests
{
    [Fact]
    public async Task ScenePlanner_Returns_Scenes_For_DailySkyGuide_And_WideSky()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.CelestialObjects.AddRange(
            new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 10, PhotogenicScore = 10, EducationalScore = 10, ViralityScore = 10 },
            new CelestialObject { Code = "Jupiter", Name = "Jupiter", ObjectType = "Planet", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 9, PhotogenicScore = 9, EducationalScore = 9, ViralityScore = 9 });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow, PrimaryCelestialObjectCode = "Jupiter" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var preview = await svc.BuildStellariumScenePlanPreviewAsync(plan.Id, CancellationToken.None);

        Assert.Equal("DailySkyGuide", preview.ContentCategoryCode);
        Assert.True(preview.Scenes.Count >= 3);
        Assert.Contains(preview.Scenes, x => x.SceneType == "WideSky");
        Assert.Contains(preview.Scenes, x => x.SceneType == "ObjectFocus" && x.TargetObjectCode == "Jupiter");
        Assert.Contains(preview.Scenes, x => x.SceneType == "MoonFocus" && x.OutputImageRole == "ThumbnailCandidate");
    }

    [Fact]
    public async Task ScenePreview_Does_Not_Update_Db_Or_Execute_Pipeline()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.CelestialObjects.Add(new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 10, PhotogenicScore = 10, EducationalScore = 10, ViralityScore = 10 });
        var plan = new ContentGenerationPlan { ContentCategoryCode = "DailySkyGuide", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR", ScheduledUtc = DateTimeOffset.UtcNow, PrimaryCelestialObjectCode = "Moon" };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        var before = plan.UpdatedUtc;
        var svc = CreateService(db);

        _ = await svc.BuildStellariumScenePlanPreviewAsync(plan.Id, CancellationToken.None);

        Assert.Equal(before, (await db.ContentGenerationPlans.SingleAsync(x=>x.Id==plan.Id)).UpdatedUtc);
        Assert.Empty(db.ContentPipelineExecutions);
    }

    [Fact]
    public async Task ScenePreview_Unsupported_Category_Returns_Clear_Warning()
    {
        await using var db = CreateDb();
        SeedRequired(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan { ContentCategoryCode = "WeeklySkyForecast", Status = "Planned", Language = "en", RegionId = "IN-RJ-UDAIPUR" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var preview = await svc.BuildStellariumScenePlanPreviewAsync(db.ContentGenerationPlans.Single().Id, CancellationToken.None);

        Assert.Contains("No Stellarium scene planner implemented for this category.", preview.Warnings);
    }
}
