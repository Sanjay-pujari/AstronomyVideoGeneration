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
        Assert.Equal("PlanetGrouping", pipelineRequest.Category);
        Assert.Equal("PLANET_GROUPING", plan.PrimaryAstronomyEventTypeCode);
        Assert.Equal("planet-grouping-udaipur-2026", plan.SourceExternalEventId);
        Assert.Equal(40, plan.Priority);
        Assert.Equal(evt.ContentOpportunityScore, plan.PriorityScore);
        Assert.False(plan.GeneratedByAi);
        Assert.True(plan.ManualValidation);
        Assert.Empty(response.Warnings);
        Assert.False(response.ManualReviewOverrideApplied);
        Assert.Equal("Verified", response.VerificationStatus);
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

    [Fact]
    public async Task CreatePlanFromEventAsync_NeedsManualReviewWithExplicitManualValidation_CreatesDraftPlanWithWarning()
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db, "NeedsManualReview");
        var service = CreateService(db);

        var response = await service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"],
            ManualValidation: true,
            Reason: "Astronomy V1.2 Solar Eclipse validation"), CancellationToken.None);

        var plan = await db.ContentGenerationPlans.SingleAsync();
        var reloadedEvent = await db.AstronomyEventIntelligences.Include(e => e.Objects).SingleAsync();

        Assert.True(response.Success);
        Assert.True(response.ManualValidation);
        Assert.True(response.ManualReviewOverrideApplied);
        Assert.Equal("NeedsManualReview", response.VerificationStatus);
        Assert.Equal(["Created manual validation plan for NeedsManualReview event. This does not enable automatic generation."], response.Warnings);
        Assert.Equal("Draft", plan.Status);
        Assert.Equal("Draft", plan.PlanStatus);
        Assert.False(plan.GeneratedByAi);
        Assert.True(plan.ManualValidation);
        Assert.Equal("Astronomy V1.2 Solar Eclipse validation", plan.PlanningReason);
        Assert.Equal(["ShortVideo", "LongVideo", "HeroAsset", "Thumbnail"], ReadStringArray(plan.RequestedOutputTypesJson));
        Assert.False(reloadedEvent.AutoGenerateAllowed);
        Assert.Equal("NeedsManualReview", reloadedEvent.VerificationStatus);
        Assert.Equal("ManualValidationCandidate", reloadedEvent.ContentStrategy);
        Assert.Equal("{\"rule\":\"do-not-overwrite\"}", reloadedEvent.RulesAppliedJson);
    }

    [Fact]
    public async Task CreatePlanFromEventAsync_NeedsManualReviewWithoutManualValidation_RejectsRequest()
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db, "NeedsManualReview");
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo"],
            ManualValidation: false,
            Reason: "Invalid status validation"), CancellationToken.None));

        Assert.Equal("NeedsManualReview events cannot be converted into content plans by this endpoint.", exception.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidNeedsManualReviewOverrideFields))]
    public async Task CreatePlanFromEventAsync_NeedsManualReviewMissingRequiredOverrideFields_RejectsRequest(string? reason, string[] requestedOutputs)
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db, "NeedsManualReview");
        var service = CreateService(db);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: requestedOutputs,
            ManualValidation: true,
            Reason: reason), CancellationToken.None));

        Assert.Equal("NeedsManualReview events cannot be converted into content plans by this endpoint.", exception.Message);
    }

    [Theory]
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


    [Fact]
    public async Task CreatePlanFromEventAsync_VerifiedConstellation_CreatesConstellationPlanLinkedToIntelligence()
    {
        await using var db = CreateDb();
        var evt = SeedOrionConstellationEvent(db);
        var service = CreateService(db);

        var response = await service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "US",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo", "Thumbnail"],
            ManualValidation: true,
            Reason: "Orion evergreen constellation validation"), CancellationToken.None);

        var plan = await db.ContentGenerationPlans.SingleAsync();

        Assert.True(response.Success);
        Assert.Equal(evt.Id, plan.AstronomyEventIntelligenceId);
        Assert.Equal("CONSTELLATION", plan.PrimaryAstronomyEventTypeCode);
        Assert.Equal("AstronomyEducation", plan.ContentCategoryCode);
        Assert.Equal("US", plan.RegionId);
        Assert.Equal("en", plan.Language);
        Assert.Equal("Draft", plan.Status);
        Assert.Equal("Draft", plan.PlanStatus);
        Assert.Equal(["ShortVideo", "Thumbnail"], ReadStringArray(plan.RequestedOutputTypesJson));
        Assert.Contains("Orion", ReadStringArray(plan.PlannedObjectNamesJson));
        Assert.Equal(1, await db.AstronomyEventIntelligences.CountAsync(e => e.EventType == "CONSTELLATION"));
    }

    [Fact]
    public async Task CreatePlanFromEventAsync_ExistingPlanetGroupingBehavior_RemainsUnchanged()
    {
        await using var db = CreateDb();
        var evt = SeedPlanetGroupingEvent(db);
        var service = CreateService(db);

        var response = await service.CreatePlanFromEventAsync(new CreatePlanFromEventRequest(
            AstronomyEventIntelligenceId: evt.Id,
            RegionId: "IN-RJ-UDAIPUR",
            Language: "en",
            PlannedFormat: "ShortVideo",
            RequestedOutputs: ["ShortVideo"],
            ManualValidation: true,
            Reason: "Regression check"), CancellationToken.None);

        var plan = await db.ContentGenerationPlans.SingleAsync();
        Assert.True(response.Success);
        Assert.Equal("PLANET_GROUPING", plan.PrimaryAstronomyEventTypeCode);
        Assert.Equal("CosmicStoryShort", plan.ContentCategoryCode);
        Assert.Equal(evt.Id, plan.AstronomyEventIntelligenceId);
    }


    public static TheoryData<string?, string[]> InvalidNeedsManualReviewOverrideFields => new()
    {
        { null, ["ShortVideo"] },
        { "", ["ShortVideo"] },
        { "Needs outputs", [] }
    };

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


    private static AstronomyEventIntelligence SeedOrionConstellationEvent(MediaFactoryDbContext db)
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = "CONSTELLATION-ORION-EVERGREEN-2026",
            ExternalEventId = "constellation-orion-evergreen-v1",
            Year = 2026,
            Language = "en",
            VerificationStatus = "Verified",
            AutoGenerateAllowed = true,
            ContentStrategy = "EvergreenConstellationEducation",
            EventType = "CONSTELLATION",
            Title = "Orion constellation guide",
            StartUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            RegionId = "US",
            RecommendedCategory = "AstronomyEducation",
            Status = "Verified",
            ContentOpportunityScore = 8m,
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Orion", ObjectType = "Constellation", ObjectRole = "Primary", CatalogId = "IAU:ORI" }
            ]
        };
        db.AstronomyEventIntelligences.Add(evt);
        db.SaveChanges();
        return evt;
    }

    private static string[] ReadStringArray(string? json)
        => JsonSerializer.Deserialize<string[]>(json ?? "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
}
