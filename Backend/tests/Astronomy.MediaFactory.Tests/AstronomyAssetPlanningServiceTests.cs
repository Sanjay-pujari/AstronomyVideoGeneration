using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyAssetPlanningServiceTests
{
    [Fact]
    public async Task GenerateAssetPlansAsync_DryRun_ReturnsPlansWithoutSaving()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "RareEventAlert", "Short");
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            ContentCategories: ["RareEventAlert"],
            PlannedFormats: ["Short"],
            MinPriorityScore: 7.5m,
            MaxPlans: 10,
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(0, result.SavedCount);
        Assert.False(string.IsNullOrWhiteSpace(result.AssetPlans.Single().AssetRequirements.First().PromptOrInstruction));
        Assert.Null(plan.AssetPlanJson);
        Assert.Equal("Planned", plan.AssetPlanStatus);
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_IncludesPlannedObjectNames()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetGrouping", "Long", ["Venus", "Mars", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: true), CancellationToken.None);

        var assetPlan = Assert.Single(result.AssetPlans);
        Assert.Equal(["Venus", "Mars", "Jupiter"], assetPlan.ObjectNames);
        Assert.All(assetPlan.AssetRequirements, r => Assert.Contains("Venus", r.ObjectNames));
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_RareEventAlertShort_ProducesFiveSceneAssetGroups()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "RareEventAlert", "Short");
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            ContentCategories: ["RareEventAlert"],
            PlannedFormats: ["Short"],
            DryRun: true), CancellationToken.None);

        var assetPlan = Assert.Single(result.AssetPlans);
        Assert.Equal(5, assetPlan.SceneGroupCount);
        Assert.Equal(5, assetPlan.SceneAssetGroups.Count);
        Assert.Equal(["Hook", "What is happening", "When and where to look", "Why it matters", "CTA / reminder"], assetPlan.SceneAssetGroups.Select(g => g.SceneName).ToArray());
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_PlanetConjunctionLong_IncludesStellariumAndConstellationRequirements()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            ContentCategories: ["PlanetConjunction"],
            PlannedFormats: ["Long"],
            DryRun: true), CancellationToken.None);

        var requirements = Assert.Single(result.AssetPlans).AssetRequirements;
        Assert.Contains(requirements, r => r.AssetType == "StellariumScreenshot" && r.PlannedProvider == "Stellarium");
        Assert.Contains(requirements, r => r.AssetType == "ConstellationGuide" && r.PlannedProvider == "InternalTemplate");
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_DryRunFalse_SavesWhenAssetPlanColumnsExist()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>().UseSqlite(connection).Options;
        await using var db = new MediaFactoryDbContext(options);
        await db.Database.EnsureCreatedAsync();
        SeedPlan(db, "RareEventAlert", "Short");
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.SavedCount);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("asset_plan_json", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result.AssetPlans);
        var saved = await db.ContentGenerationPlans.Select(p => new { p.AssetPlanJson, p.AssetPlanStatus, p.UpdatedUtc }).SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(saved.AssetPlanJson));
        Assert.Equal("Planned", saved.AssetPlanStatus);
        Assert.Null(saved.UpdatedUtc);
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_DuplicateSkipWorksWhenAssetPlanExistsAndOverwriteFalse()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "RareEventAlert", "Short");
        plan.AssetPlanJson = "{\"existing\":true}";
        plan.AssetPlanStatus = "Planned";
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false, OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.Empty(result.AssetPlans);
        Assert.Equal("{\"existing\":true}", plan.AssetPlanJson);
    }


    [Fact]
    public async Task GenerateAssetPlansAsync_OverwriteTrue_ReplacesExistingAssetPlan()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "RareEventAlert", "Short");
        plan.AssetPlanJson = "{\"existing\":true}";
        plan.AssetPlanStatus = "Planned";
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false, OverwriteExisting: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.NotEqual("{\"existing\":true}", plan.AssetPlanJson);
        Assert.Contains("sceneAssetGroups", plan.AssetPlanJson);
        Assert.Equal("Planned", plan.AssetPlanStatus);
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_ClassifiesAssetPriorityAndExecutionGroup()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: true), CancellationToken.None);

        var requirements = Assert.Single(result.AssetPlans).AssetRequirements;
        Assert.Contains(requirements, r => r.AssetType == "TextOverlayCard" && r.AssetPriority == "Required" && r.AssetExecutionGroup == "Core");
        Assert.Contains(requirements, r => r.AssetType == "StellariumScreenshot" && r.AssetPriority == "Preferred" && r.AssetExecutionGroup == "AstronomyVisualization");
        Assert.Contains(requirements, r => r.AssetType == "ConstellationGuide" && r.AssetPriority == "Preferred" && r.AssetExecutionGroup == "Educational");
        Assert.Contains(requirements, r => r.AssetType == "AiCinematicImage" && r.AssetPriority == "Optional" && r.AssetExecutionGroup == "Cinematic");
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRun_ConvertsSavedAssetPlanRequirements()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var planning = CreateService(db);
        await planning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: true), CancellationToken.None);

        Assert.Equal(9, result.JobCount);
        Assert.Equal(3, result.RequiredJobs);
        Assert.Equal(3, result.PreferredJobs);
        Assert.Equal(3, result.OptionalJobs);
        Assert.All(result.Jobs, job => Assert.True(job.DryRun));
        Assert.Contains(result.Jobs, j => j.AssetType == "StellariumScreenshot" && j.AssetPriority == "Preferred" && j.AssetExecutionGroup == "AstronomyVisualization");
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRunFalse_IsRejected()
    {
        await using var db = CreateInMemoryDb();
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: false), CancellationToken.None));
    }

    private static AstronomyAssetPlanningService CreateService(MediaFactoryDbContext db)
        => new(db, NullLogger<AstronomyAssetPlanningService>.Instance);

    private static MediaFactoryDbContext CreateInMemoryDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static ContentGenerationPlan SeedPlan(MediaFactoryDbContext db, string category, string format, IReadOnlyList<string>? objects = null)
    {
        objects ??= ["Venus", "Jupiter"];
        var evt = new AstronomyEventIntelligence
        {
            EventCode = $"{category}_{format}",
            EventType = category,
            Title = $"{category} event",
            StartUtc = DateTimeOffset.Parse("2026-06-07T00:00:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T12:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            RegionId = "IN-RJ-UDAIPUR",
            LocationName = "Udaipur",
            RecommendedCategory = category,
            Status = "Candidate",
            ConfidenceScore = 8m,
            RarityScore = 8m,
            VisibilityScore = 8m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 8m,
            ContentOpportunityScore = 8m
        };
        var opportunity = new AstronomyContentOpportunity
        {
            AstronomyEventIntelligence = evt,
            ContentCategory = category,
            Title = $"{category} opportunity",
            PriorityScore = 9.1m,
            Status = "Planned"
        };
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = category,
            Title = $"{category} plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
            Status = "Planned",
            AstronomyContentOpportunity = opportunity,
            AstronomyEventIntelligence = evt,
            SourceEventObjectIdsJson = "[]",
            PlannedObjectNamesJson = System.Text.Json.JsonSerializer.Serialize(objects),
            PlanStatus = "Planned",
            PlannedFormat = format,
            PriorityScore = 9.1m,
            PlanningReason = BuildPlanningReason(category, format)
        };
        db.ContentGenerationPlans.Add(plan);
        db.SaveChanges();
        return plan;
    }

    private static string BuildPlanningReason(string category, string format)
    {
        var scenes = category switch
        {
            "RareEventAlert" => new[] { "Hook", "What is happening", "When and where to look", "Why it matters", "CTA / reminder" },
            "PlanetConjunction" when format == "Long" => ["Hook with object pair", "Sky map / where to look", "Closest approach / peak timing", "Why conjunction happens", "Viewing tips", "Myth/story/cultural context optional", "Recap"],
            _ => ["Hook", "Main explanation", "Recap"]
        };
        return System.Text.Json.JsonSerializer.Serialize(new { visualStrategy = new { sceneStrategy = scenes } });
    }
}
