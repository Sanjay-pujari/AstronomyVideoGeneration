using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    private static ContentPlanningService CreateService(MediaFactoryDbContext db) => new(db, new NoopVarietyGuard());

    private static void SeedRequired(MediaFactoryDbContext db)
    {
        db.ContentCategories.Add(new ContentCategory { Code = "DailySkyGuide", Name = "Daily", Priority = 1, Enabled = true });
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

        Assert.Equal("Planned", preview.Status);
        Assert.NotNull(preview.PipelineRequest);
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

        Assert.Equal("ReadyForManualRun", preview.Status);
        Assert.NotNull(preview.PipelineRequest);
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

        Assert.Contains("missing style setting", preview.Warnings);
        Assert.Contains("missing celestial object", preview.Warnings);
        Assert.Contains("missing astronomy event type", preview.Warnings);
        Assert.Contains("missing scheduledUtc", preview.Warnings);
    }

    [Fact]
    public void Existing_Pipeline_Run_Request_Dto_Remains_Unchanged()
    {
        var type = typeof(Astronomy.MediaFactory.Contracts.RunPipelineRequest);
        Assert.NotNull(type.GetProperty("Date"));
        Assert.NotNull(type.GetProperty("ContentType"));
        Assert.NotNull(type.GetProperty("LocationName"));
    }
}
