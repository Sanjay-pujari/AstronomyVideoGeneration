using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyVideoPlanningServiceTests
{
    [Fact]
    public async Task GenerateVideoPlansAsync_DryRun_ReturnsPlansWithoutSaving()
    {
        await using var db = CreateDb();
        SeedMasterData(db);
        var opportunity = SeedOpportunity(db, "PLANET_CONJUNCTION", "PlanetConjunction", 9.2m);

        var service = CreateService(db);
        var result = await service.GenerateVideoPlansAsync(new AstronomyVideoPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            StartUtc: DateTimeOffset.Parse("2026-06-05T00:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-06-13T23:59:59Z"),
            ContentCategories: ["PlanetConjunction"],
            MinPriorityScore: 7.5m,
            MaxPlans: 10,
            DryRun: true), CancellationToken.None);

        Assert.Equal(2, result.PlanCount);
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(0, await db.ContentGenerationPlans.CountAsync());
        Assert.All(result.GeneratedPlans, p =>
        {
            Assert.Equal(opportunity.Id, p.OpportunityId);
            Assert.Equal("PlanetConjunction", p.ContentCategory);
            Assert.Equal("en", p.Language);
            Assert.Equal("IN-RJ-UDAIPUR", p.RegionId);
            Assert.Equal("Planned", p.Status);
            Assert.False(string.IsNullOrWhiteSpace(p.SourceEventObjectIdsJson));
            Assert.False(string.IsNullOrWhiteSpace(p.PlannedObjectNamesJson));
        });
        Assert.Contains(result.GeneratedPlans, p => p.PlannedFormat == "Short" && p.SceneCount == 5);
        Assert.Contains(result.GeneratedPlans, p => p.PlannedFormat == "Long" && p.SceneCount == 7);
    }

    [Fact]
    public async Task GenerateVideoPlansAsync_Save_SkipsDuplicateOpportunityCategoryFormat()
    {
        await using var db = CreateDb();
        SeedMasterData(db);
        var opportunity = SeedOpportunity(db, "MOON_SPECIAL", "MoonSpecials", 8.5m);

        var service = CreateService(db);
        var request = new AstronomyVideoPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            StartUtc: DateTimeOffset.Parse("2026-06-05T00:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-06-13T23:59:59Z"),
            ContentCategories: ["MoonSpecials"],
            MinPriorityScore: 7.5m,
            MaxPlans: 10,
            DryRun: false);

        var first = await service.GenerateVideoPlansAsync(request, CancellationToken.None);
        var second = await service.GenerateVideoPlansAsync(request, CancellationToken.None);

        Assert.Equal(1, first.SavedCount);
        Assert.Equal(1, await db.ContentGenerationPlans.CountAsync());
        var saved = await db.ContentGenerationPlans.SingleAsync();
        Assert.Equal(opportunity.Id, saved.AstronomyContentOpportunityId);
        Assert.Equal(opportunity.AstronomyEventIntelligenceId, saved.AstronomyEventIntelligenceId);
        Assert.False(string.IsNullOrWhiteSpace(saved.SourceEventObjectIdsJson));
        Assert.False(string.IsNullOrWhiteSpace(saved.PlannedObjectNamesJson));
        Assert.Equal("Planned", saved.PlanStatus);
        Assert.Equal("Short", saved.PlannedFormat);
        Assert.Equal(opportunity.PriorityScore, saved.PriorityScore);
        Assert.Contains(opportunity.Id.ToString("N"), saved.PlanningReason);
        Assert.Equal(0, second.SavedCount);
        Assert.Equal(1, second.SkippedDuplicates);
        Assert.True(second.GeneratedPlans.Single().DuplicateSkipped);
        Assert.Equal(1, await db.ContentGenerationPlans.CountAsync());
    }

    [Fact]
    public async Task GenerateVideoPlansAsync_Save_DetectsDuplicateUsingTrackingColumns()
    {
        await using var db = CreateDb();
        SeedMasterData(db);
        var opportunity = SeedOpportunity(db, "TRACKING_DUPLICATE", "MoonSpecials", 8.5m);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan
        {
            ContentCategoryCode = "MoonSpecials",
            Title = "Existing tracked plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            Status = "Planned",
            AstronomyContentOpportunityId = opportunity.Id,
            AstronomyEventIntelligenceId = opportunity.AstronomyEventIntelligenceId,
            PlannedFormat = "Short",
            PlanStatus = "Planned",
            PriorityScore = opportunity.PriorityScore
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.GenerateVideoPlansAsync(new AstronomyVideoPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            StartUtc: DateTimeOffset.Parse("2026-06-05T00:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-06-13T23:59:59Z"),
            ContentCategories: ["MoonSpecials"],
            MinPriorityScore: 7.5m,
            MaxPlans: 10,
            DryRun: false), CancellationToken.None);

        Assert.Equal(0, result.SavedCount);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.True(result.GeneratedPlans.Single().DuplicateSkipped);
        Assert.Equal(1, await db.ContentGenerationPlans.CountAsync());
    }


    [Fact]
    public async Task GenerateVideoPlansAsync_FiltersStatusRegionDateCategoryAndScore()
    {
        await using var db = CreateDb();
        SeedMasterData(db);
        var selected = SeedOpportunity(db, "RARE_EVENT", "RareEventAlert", 9.0m);
        SeedOpportunity(db, "LOW_SCORE", "RareEventAlert", 5.0m);
        SeedOpportunity(db, "OTHER_CATEGORY", "MoonSpecials", 9.0m);
        SeedOpportunity(db, "OTHER_REGION", "RareEventAlert", 9.0m, regionId: "US-CA-SF");
        SeedOpportunity(db, "DRAFT", "RareEventAlert", 9.0m, status: "Draft");

        var service = CreateService(db);
        var result = await service.GenerateVideoPlansAsync(new AstronomyVideoPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            StartUtc: DateTimeOffset.Parse("2026-06-05T00:00:00Z"),
            EndUtc: DateTimeOffset.Parse("2026-06-13T23:59:59Z"),
            ContentCategories: ["RareEventAlert"],
            MinPriorityScore: 7.5m,
            MaxPlans: 10,
            DryRun: true), CancellationToken.None);

        var plan = Assert.Single(result.GeneratedPlans);
        Assert.Equal(selected.Id, plan.OpportunityId);
        Assert.Equal("Short", plan.PlannedFormat);
        Assert.Equal(5, plan.SceneCount);
    }

    private static AstronomyVideoPlanningService CreateService(MediaFactoryDbContext db)
        => new(db, NullLogger<AstronomyVideoPlanningService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static void SeedMasterData(MediaFactoryDbContext db)
    {
        db.ContentCategories.AddRange(
            new ContentCategoryMaster { Code = "RareEventAlert", DisplayName = "Rare Event Alert", Enabled = true },
            new ContentCategoryMaster { Code = "PlanetConjunction", DisplayName = "Planet Conjunction", Enabled = true },
            new ContentCategoryMaster { Code = "MoonSpecials", DisplayName = "Moon Specials", Enabled = true });
        db.NarrationStyles.Add(new NarrationStyle { Code = "documentary", DisplayName = "Documentary", Enabled = true, Priority = 1 });
        db.ThumbnailStyles.Add(new ThumbnailStyle { Code = "bold", DisplayName = "Bold", Enabled = true, Priority = 1 });
        db.SaveChanges();
    }

    private static AstronomyContentOpportunity SeedOpportunity(
        MediaFactoryDbContext db,
        string eventCode,
        string category,
        decimal priorityScore,
        string regionId = "IN-RJ-UDAIPUR",
        string status = "Proposed")
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = eventCode,
            EventType = eventCode,
            Title = $"{eventCode} title",
            StartUtc = DateTimeOffset.Parse("2026-06-07T00:00:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T12:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
            RegionId = regionId,
            LocationName = "Udaipur",
            RecommendedCategory = category,
            Status = "Candidate",
            ConfidenceScore = 8m,
            RarityScore = 8m,
            VisibilityScore = 8m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 8m,
            ContentOpportunityScore = 8m,
            Objects = [new AstronomyEventObject { ObjectName = "Venus", ObjectType = "Planet" }]
        };
        var opportunity = new AstronomyContentOpportunity
        {
            AstronomyEventIntelligence = evt,
            ContentCategory = category,
            Title = $"{category}: {eventCode}",
            PriorityScore = priorityScore,
            Status = status,
            VisualStrategyJson = "{\"requiresStellarium\":true}",
            NarrationStrategyJson = "{\"tone\":\"excited\"}"
        };
        db.AstronomyContentOpportunities.Add(opportunity);
        db.SaveChanges();
        return opportunity;
    }
}
