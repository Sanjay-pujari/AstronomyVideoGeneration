using System.Text.Json;
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
    public async Task GenerateAssetPlansAsync_ReplacesImportedJsonWithoutAssetRequirementsWhenOverwriteFalse()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "RareEventAlert", "Short");
        plan.RequestedOutputTypesJson = JsonSerializer.Serialize(new[] { "HeroAsset", "Thumbnail", "ShortVideo", "LongVideo" });
        plan.AssetPlanJson = "{\"existing\":true}";
        plan.AssetPlanStatus = "Imported";
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false, OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.Single(result.AssetPlans);
        Assert.NotEqual("{\"existing\":true}", plan.AssetPlanJson);
        Assert.Contains("assetRequirements", plan.AssetPlanJson);
        Assert.Contains("hero_landscape", plan.AssetPlanJson);
        Assert.Contains("thumbnail_landscape", plan.AssetPlanJson);
        Assert.Contains("thumbnail_portrait", plan.AssetPlanJson);
        Assert.Contains("short_scene_portrait", plan.AssetPlanJson);
        Assert.Contains("long_scene_landscape", plan.AssetPlanJson);
    }


    [Fact]
    public async Task GenerateAssetPlansAsync_SelectedPlanIds_LoadsExactImportedDraftPlanAndSavesDefaultRequirements()
    {
        await using var db = CreateInMemoryDb();
        var selectedPlan = SeedPlan(db, "RareEventAlert", "Short", ["Geminids", "Meteor shower"]);
        selectedPlan.Status = "Draft";
        selectedPlan.PlanStatus = "Draft";
        selectedPlan.SourceExternalEventId = "geminids-2026-peak";
        selectedPlan.RequestedOutputTypesJson = JsonSerializer.Serialize(new[] { "ShortVideo", "LongVideo", "HeroAsset", "Thumbnail" });
        selectedPlan.AssetPlanJson = JsonSerializer.Serialize(new { imported = true, assetRequirements = Array.Empty<object>() });
        selectedPlan.AstronomyEventIntelligence!.EventType = "MeteorShower";
        selectedPlan.AstronomyEventIntelligence.Title = "Geminids Meteor Shower Peak";
        selectedPlan.AstronomyEventIntelligence.Summary = "Geminids peak";
        selectedPlan.AstronomyEventIntelligence.MetadataJson = JsonSerializer.Serialize(new { showerCode = "GEM" });
        selectedPlan.AstronomyEventIntelligence.RawDataJson = JsonSerializer.Serialize(new { zhr = 120 });
        selectedPlan.AstronomyEventIntelligence.Objects.Add(new AstronomyEventObject { ObjectName = "Geminids", ObjectType = "MeteorShower", ObjectRole = "Primary" });
        selectedPlan.AstronomyEventIntelligence.Objects.Add(new AstronomyEventObject { ObjectName = "Castor", ObjectType = "Star", ObjectRole = "Secondary" });
        var unselectedPlan = SeedPlan(db, "RareEventAlert", "Short", ["Unselected"]);
        unselectedPlan.RequestedOutputTypesJson = selectedPlan.RequestedOutputTypesJson;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            PlanIds: [selectedPlan.Id],
            MaxPlans: 1,
            DryRun: false,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.True(result.AssetRequirementCount > 0);
        var assetPlan = Assert.Single(result.AssetPlans);
        Assert.Contains(assetPlan.AssetRequirements, r => r.SceneName == "hero_landscape");
        Assert.Contains(assetPlan.AssetRequirements, r => r.SceneName == "thumbnail_landscape");
        Assert.Contains(assetPlan.AssetRequirements, r => r.SceneName == "thumbnail_portrait");
        Assert.Contains(assetPlan.AssetRequirements, r => r.SceneName == "short_scene_portrait");
        Assert.Contains(assetPlan.AssetRequirements, r => r.SceneName == "long_scene_landscape");
        Assert.Contains("Geminids", assetPlan.ObjectNames);
        Assert.Contains("Castor", assetPlan.ObjectNames);
        Assert.Contains("sourceExternalEventId", selectedPlan.AssetPlanJson);
        Assert.Contains("geminids-2026-peak", selectedPlan.AssetPlanJson);
        Assert.Contains("MeteorShower", selectedPlan.AssetPlanJson);
        Assert.Contains("primaryObjects", selectedPlan.AssetPlanJson);
        Assert.Null(unselectedPlan.AssetPlanJson);
    }

    [Fact]
    public async Task GenerateAssetPlansAsync_SelectedPlanIds_WarnsAndSkipsMissingEventOrRequestedOutputs()
    {
        await using var db = CreateInMemoryDb();
        var missingEventPlan = SeedPlan(db, "RareEventAlert", "Short");
        missingEventPlan.RequestedOutputTypesJson = JsonSerializer.Serialize(new[] { "HeroAsset" });
        missingEventPlan.AstronomyEventIntelligence = null;
        missingEventPlan.AstronomyEventIntelligenceId = null;
        var missingOutputsPlan = SeedPlan(db, "RareEventAlert", "Short");
        missingOutputsPlan.RequestedOutputTypesJson = null;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            PlanIds: [missingEventPlan.Id, missingOutputsPlan.Id],
            DryRun: false), CancellationToken.None);

        Assert.Equal(2, result.PlanCount);
        Assert.Equal(0, result.AssetRequirementCount);
        Assert.Equal(0, result.SavedCount);
        Assert.Contains(result.Warnings, w => w.Contains("missing linked AstronomyEventIntelligence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("missing RequestedOutputTypesJson", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task GenerateAssetPlansAsync_SkipsValidExistingAssetPlanWhenOverwriteFalse()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "RareEventAlert", "Short");
        var service = CreateService(db);
        await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);
        var existing = plan.AssetPlanJson;

        var result = await service.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false, OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.Empty(result.AssetPlans);
        Assert.Equal(existing, plan.AssetPlanJson);
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
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.Empty(db.AstronomyAssetProductionJobs);
        Assert.All(result.Jobs, job => Assert.True(job.DryRun));
        Assert.All(result.Jobs, job => Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status));
        Assert.Contains(result.Jobs, j => j.AssetType == "StellariumScreenshot" && j.AssetPriority == "Preferred" && j.AssetExecutionGroup == "AstronomyVisualization");
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRun_SkipsInvalidAssetPlanJsonWithWarning()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var invalidPlan = SeedPlan(db, "RareEventAlert", "Short", ["Moon"]);
        invalidPlan.AssetPlanJson = "{not-json";
        await db.SaveChangesAsync();

        var planning = CreateService(db);
        await planning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
            DryRun: false,
            ContentCategories: ["PlanetConjunction"]), CancellationToken.None);
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: true), CancellationToken.None);

        Assert.Equal(9, result.JobCount);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains(invalidPlan.Id.ToString(), warning);
        Assert.Contains("invalid AssetPlanJson", warning);
        Assert.All(result.Jobs, job => Assert.NotEqual(invalidPlan.Id, job.ContentGenerationPlanId));
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }


    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRun_RecoversRequirementsFromSceneGroupsWhenTopLevelRequirementsMissing()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "PlanetConjunction", "Long", ["Venus"]);
        plan.AssetPlanJson = JsonSerializer.Serialize(new
        {
            contentGenerationPlanId = plan.Id,
            astronomyContentOpportunityId = plan.AstronomyContentOpportunityId,
            astronomyEventIntelligenceId = plan.AstronomyEventIntelligenceId,
            contentCategory = plan.ContentCategoryCode,
            plannedFormat = plan.PlannedFormat,
            regionId = plan.RegionId,
            locationName = "Udaipur",
            planStatus = plan.PlanStatus,
            assetPlanStatus = "AssetPlanned",
            sceneGroupCount = 1,
            assetRequirementCount = 1,
            objectNames = new[] { "Venus" },
            sceneAssetGroups = new[]
            {
                new
                {
                    sceneNumber = 1,
                    sceneName = "Opening sky map",
                    assetRequirements = new[]
                    {
                        new
                        {
                            sceneNumber = 1,
                            sceneName = "Opening sky map",
                            assetType = "StellariumScreenshot",
                            assetPurpose = "Show the conjunction location",
                            objectNames = new[] { "Venus" },
                            plannedProvider = "Stellarium",
                            promptOrInstruction = "Capture Venus before dawn.",
                            expectedOutputType = "png",
                            priority = 10,
                            status = "Planned",
                            dependsOn = Array.Empty<string>(),
                            assetPriority = "Preferred",
                            assetExecutionGroup = "AstronomyVisualization",
                            metadataJson = new { recovered = true }
                        }
                    }
                }
            },
            metadataJson = new { legacy = true }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: true), CancellationToken.None);

        var job = Assert.Single(result.Jobs);
        Assert.Equal("StellariumScreenshot", job.AssetType);
        Assert.Equal("Stellarium", job.PlannedProvider);
        Assert.Contains("missing top-level assetRequirements", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRun_SkipsAssetPlanWithNoRequirements()
    {
        await using var db = CreateInMemoryDb();
        var plan = SeedPlan(db, "PlanetConjunction", "Long", ["Venus"]);
        plan.AssetPlanJson = JsonSerializer.Serialize(new
        {
            contentGenerationPlanId = plan.Id,
            contentCategory = plan.ContentCategoryCode,
            plannedFormat = plan.PlannedFormat,
            regionId = plan.RegionId,
            planStatus = plan.PlanStatus,
            assetPlanStatus = "AssetPlanned",
            sceneGroupCount = 0,
            assetRequirementCount = 0,
            objectNames = new[] { "Venus" },
            metadataJson = new { empty = true }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.JobCount);
        Assert.Contains("has no asset requirements", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DryRunFalse_SavesJobs()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var planning = CreateService(db);
        await planning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: false), CancellationToken.None);

        Assert.Equal(9, result.JobCount);
        Assert.Equal(9, result.SavedCount);
        Assert.Equal(0, result.SkippedDuplicates);
        Assert.Equal(9, await db.AstronomyAssetProductionJobs.CountAsync());
        Assert.All(await db.AstronomyAssetProductionJobs.ToListAsync(), job => Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status));
        Assert.Contains(await db.AstronomyAssetProductionJobs.ToListAsync(), j => j.ObjectNamesJson != null && j.ObjectNamesJson.Contains("Venus"));
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_DuplicateRetry_DoesNotCreateAdditionalJobs()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var planning = CreateService(db);
        await planning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);

        var first = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: false), CancellationToken.None);
        var second = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: false), CancellationToken.None);

        Assert.Equal(9, first.SavedCount);
        Assert.Equal(0, second.JobCount);
        Assert.Equal(0, second.SavedCount);
        Assert.Equal(9, second.SkippedDuplicates);
        Assert.Equal(9, await db.AstronomyAssetProductionJobs.CountAsync());
    }

    [Fact]
    public async Task CreateAssetProductionJobsAsync_CompletedJobsArePreserved()
    {
        await using var db = CreateInMemoryDb();
        SeedPlan(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var planning = CreateService(db);
        var planResult = await planning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(DryRun: false), CancellationToken.None);
        var service = new AstronomyAssetProductionJobService(db, NullLogger<AstronomyAssetProductionJobService>.Instance);
        var firstRequirement = Assert.Single(planResult.AssetPlans).AssetRequirements.First();
        var completed = new AstronomyAssetProductionJob
        {
            ContentGenerationPlanId = planResult.AssetPlans.Single().ContentGenerationPlanId,
            SceneNumber = firstRequirement.SceneNumber,
            SceneName = firstRequirement.SceneName,
            AssetType = firstRequirement.AssetType,
            AssetPurpose = firstRequirement.AssetPurpose,
            PlannedProvider = firstRequirement.PlannedProvider,
            PromptOrInstruction = firstRequirement.PromptOrInstruction,
            ExpectedOutputType = firstRequirement.ExpectedOutputType,
            Priority = firstRequirement.Priority,
            AssetPriority = firstRequirement.AssetPriority,
            AssetExecutionGroup = firstRequirement.AssetExecutionGroup,
            Status = AstronomyAssetProductionJobStatuses.Completed,
            OutputPath = "/assets/completed.png",
            CompletedUtc = DateTimeOffset.Parse("2026-06-07T13:00:00Z")
        };
        db.AstronomyAssetProductionJobs.Add(completed);
        await db.SaveChangesAsync();

        var result = await service.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(DryRun: false), CancellationToken.None);

        Assert.Equal(8, result.SavedCount);
        Assert.Equal(1, result.SkippedDuplicates);
        var preserved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == completed.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, preserved.Status);
        Assert.Equal("/assets/completed.png", preserved.OutputPath);
        Assert.Equal(9, await db.AstronomyAssetProductionJobs.CountAsync());
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
