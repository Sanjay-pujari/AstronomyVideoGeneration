using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ContentPlanningGeneratePlanTests
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
