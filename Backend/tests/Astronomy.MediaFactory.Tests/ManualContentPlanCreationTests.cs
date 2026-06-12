using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Tests;

public sealed class ManualContentPlanCreationTests
{
    [Fact]
    public async Task CreatePlanFromEventAsync_VerifiedPlanetGrouping_CreatesDraftManualPlanWithoutMutatingEvent()
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db);
        var service = CreateService(db);

        var response = await service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"],
            ManualValidation: true,
            Reason: "Astronomy V1.2 Planet Grouping validation"), CancellationToken.None);

        var plan = await db.ContentGenerationPlans.SingleAsync();
        var reloadedEvent = await db.AstronomyEventIntelligences.Include(e => e.Objects).SingleAsync();
        var pipelineRequest = new ContentPlanProductionRequestMapper().Map(plan, reloadedEvent);

        Assert.True(response.Success);
        Assert.Equal(plan.Id, response.ContentGenerationPlanId);
        Assert.Equal("PLANET_GROUPING", response.EventType);
        Assert.Equal("Planet grouping window over Udaipur, Rajasthan, India", response.Title);
        Assert.Equal(evt.Id, plan.AstronomyEventIntelligenceId);
        Assert.Equal("Draft", plan.Status);
        Assert.Equal("Draft", plan.PlanStatus);
        Assert.Equal("CosmicStoryShort", plan.ContentCategoryCode);
        Assert.Equal("PLANET_GROUPING", plan.PrimaryAstronomyEventTypeCode);
        Assert.Equal("planet-grouping-udaipur-2026", plan.SourceExternalEventId);
        Assert.Equal(40, plan.Priority);
        Assert.Equal(evt.ContentOpportunityScore, plan.PriorityScore);
        Assert.False(plan.GeneratedByAi);
        Assert.Equal("manual validation: Astronomy V1.2 Planet Grouping validation", plan.PlanningReason);
        Assert.Equal(["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"], ReadStringArray(plan.RequestedOutputTypesJson));
        Assert.Equal(["Saturn", "Mars", "Jupiter", "Venus"], ReadStringArray(plan.PlannedObjectNamesJson));
        Assert.Equal(["Saturn"], pipelineRequest.PrimaryObjects);
        Assert.Equal(["Mars", "Jupiter", "Venus"], pipelineRequest.SecondaryObjects);
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal("Verified", reloadedEvent.VerificationStatus);
        Assert.Equal("ManualValidationCandidate", reloadedEvent.ContentStrategy);
        Assert.Equal("{\"rule\":\"do-not-overwrite\"}", reloadedEvent.RulesAppliedJson);
    }

    [Fact]
    public async Task CreatePlanFromEventAsync_DuplicateActivePlan_RejectsRequest()
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db);
        db.ContentGenerationPlans.Add(new ContentGenerationPlan
        {
            AstronomyEventIntelligenceId = evt.Id,
            RegionId = "IN-RJ-UDAIPUR",
            Language = "en",
            PlannedFormat = "ShortVideo",
            Status = "Draft",
            PlanStatus = "Draft",
            ContentCategoryCode = "CosmicStoryShort",
            Title = evt.Title
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo"],
            ManualValidation: true,
            Reason: "Duplicate validation"), CancellationToken.None));
    }

    [Theory]
    [InlineData("NeedsManualReview")]
    [InlineData("Rejected")]
    public async Task CreatePlanFromEventAsync_NotVerified_RejectsRequest(string verificationStatus)
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db, verificationStatus);
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo"],
            ManualValidation: true,
            Reason: "Invalid status validation"), CancellationToken.None));
    }

    private static ContentPlanningService CreateService(MediaFactoryDbContext db)
        => new(db, null!, null!, null!, null!, null!);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static AstronomyEventIntelligence SeedPlanetGroupingEvent(MediaFactoryDbContext db, string verificationStatus = "Verified")
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = "PLANET-GROUPING-UDAIPUR-2026",
            ExternalEventId = "planet-grouping-udaipur-2026",
            Year = 2026,
            Language = "en",
            VerificationStatus = verificationStatus,
            AutoGenerateAllowed = false,
            ContentStrategy = "ManualValidationCandidate",
            EventType = "PLANET_GROUPING",
            Title = "Planet grouping window over Udaipur, Rajasthan, India",
            StartUtc = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero),
            PeakUtc = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero),
            RegionId = "IN-RJ-UDAIPUR",
            RecommendedCategory = "",
            ContentOpportunityScore = 6.75m,
            RulesAppliedJson = "{\"rule\":\"do-not-overwrite\"}",
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Saturn", ObjectType = "Planet", ObjectRole = "Primary" },
                new AstronomyEventObject { ObjectName = "Mars", ObjectType = "Planet", ObjectRole = "Companion" },
                new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet", ObjectRole = "Companion" },
                new AstronomyEventObject { ObjectName = "Venus", ObjectType = "Planet", ObjectRole = "Companion" }
            ]
        };
        db.AstronomyEventIntelligences.Add(evt);
        db.SaveChanges();
        return evt;
    }

    private static string[] ReadStringArray(string? json)
        => JsonSerializer.Deserialize<string[]>(json ?? "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
}
