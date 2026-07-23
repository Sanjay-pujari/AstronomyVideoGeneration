using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyContentOpportunityServiceTests
{
    [Fact]
    public async Task GenerateAsync_DryRun_AppliesCategoryWeightsAndSortsByFinalPriority()
    {
        await using var db = CreateDb();
        SeedEvent(db, "bright-planet", "BRIGHT_PLANET_VISIBILITY", "Bright planet visibility", 7.69m);
        SeedEvent(db, "planet-grouping", "PLANET_GROUPING", "Planet grouping", 7.89m);
        SeedEvent(db, "planet-conjunction", "PLANET_CONJUNCTION", "Planet conjunction", 7.18m);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(DryRun: true), CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(0, await db.AstronomyContentOpportunities.CountAsync());
        Assert.Equal(result.GeneratedOpportunities.OrderByDescending(o => o.PriorityScore).Select(o => o.ContentCategory), result.GeneratedOpportunities.Select(o => o.ContentCategory));

        var rareAlert = Assert.Single(result.GeneratedOpportunities, o => o.ContentCategory == "RareEventAlert");
        Assert.Equal(7.18m, rareAlert.BasePriorityScore);
        Assert.Equal(2.00m, rareAlert.CategoryWeight);
        Assert.Equal(9.18m, rareAlert.PriorityScore);

        var conjunction = Assert.Single(result.GeneratedOpportunities, o => o.ContentCategory == "PlanetConjunction");
        Assert.Equal(7.18m, conjunction.BasePriorityScore);
        Assert.Equal(1.75m, conjunction.CategoryWeight);
        Assert.Equal(8.93m, conjunction.PriorityScore);
        Assert.Contains("generic visibility", conjunction.ScoringReason, StringComparison.OrdinalIgnoreCase);

        var grouping = Assert.Single(result.GeneratedOpportunities, o => o.ContentCategory == "PlanetGrouping");
        Assert.Equal(8.89m, grouping.PriorityScore);

        var planetVisibility = Assert.Single(result.GeneratedOpportunities, o => o.ContentCategory == "PlanetVisibilityGuide");
        Assert.Equal(0.00m, planetVisibility.CategoryWeight);
        Assert.Equal(7.69m, planetVisibility.PriorityScore);

        Assert.True(conjunction.PriorityScore > planetVisibility.PriorityScore);
        Assert.True(rareAlert.PriorityScore > grouping.PriorityScore);
    }

    [Fact]
    public async Task GenerateAsync_CapsFinalPriorityAtTenAndPersistsNarrationScoringMetadata()
    {
        await using var db = CreateDb();
        SeedEvent(db, "rare-cap", "PLANET_CONJUNCTION", "Very strong conjunction", 9.50m);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(DryRun: false), CancellationToken.None);

        var rareAlert = Assert.Single(result.GeneratedOpportunities, o => o.ContentCategory == "RareEventAlert");
        Assert.Equal(10.00m, rareAlert.PriorityScore);

        var saved = await db.AstronomyContentOpportunities.SingleAsync(o => o.ContentCategory == "RareEventAlert");
        using var doc = JsonDocument.Parse(saved.NarrationStrategyJson!);
        var scoring = doc.RootElement.GetProperty("scoring");
        Assert.Equal(9.50m, scoring.GetProperty("basePriorityScore").GetDecimal());
        Assert.Equal(2.00m, scoring.GetProperty("categoryWeight").GetDecimal());
        Assert.Equal(10.00m, scoring.GetProperty("finalPriorityScore").GetDecimal());
        Assert.Contains("urgency", scoring.GetProperty("scoringReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task GenerateAsync_PersistsSelectedEventObjectTrackingJson()
    {
        await using var db = CreateDb();
        SeedEvent(db, "tracked-conjunction", "PLANET_CONJUNCTION", "Tracked conjunction", 7.18m, "Venus", "Jupiter");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.GenerateAsync(new AstronomyContentOpportunityRequest(DryRun: false), CancellationToken.None);

        var saved = await db.AstronomyContentOpportunities.SingleAsync(o => o.ContentCategory == "PlanetConjunction");
        using var idDoc = JsonDocument.Parse(saved.SelectedEventObjectIdsJson!);
        using var nameDoc = JsonDocument.Parse(saved.SelectedObjectNamesJson!);
        Assert.Equal(2, idDoc.RootElement.GetArrayLength());
        Assert.Equal(["Venus", "Jupiter"], nameDoc.RootElement.EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task GenerateAsync_ConstellationEvergreen_CreatesAstronomyEducationWithoutSkyfieldAndSelectsOrion()
    {
        await using var db = CreateDb();
        var evt = SeedOrionConstellation(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(
            RegionId: "US",
            EventTypes: ["CONSTELLATION"],
            DryRun: false,
            MaxOpportunities: 1), CancellationToken.None);

        var opportunity = Assert.Single(result.GeneratedOpportunities);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(evt.Id, opportunity.AstronomyEventIntelligenceId);
        Assert.Equal("AstronomyEducation", opportunity.ContentCategory);
        Assert.False(opportunity.RequiresSkyfield);
        Assert.True(opportunity.RequiresConstellationGuide);
        Assert.Contains("Orion", opportunity.SelectedObjectNames);

        var saved = await db.AstronomyContentOpportunities.SingleAsync();
        using var visual = JsonDocument.Parse(saved.VisualStrategyJson!);
        Assert.False(visual.RootElement.GetProperty("requiresSkyfield").GetBoolean());
        Assert.Equal("constellation-outline-object-callouts-and-cultural-context", visual.RootElement.GetProperty("visualStyle").GetString());
    }

    [Fact]
    public async Task GenerateAsync_TransientCandidateDateFilteringRemainsUnchanged()
    {
        await using var db = CreateDb();
        SeedEvent(db, "inside", "PLANET_CONJUNCTION", "Inside", 7m);
        SeedEvent(db, "outside", "PLANET_CONJUNCTION", "Outside", 7m, status: "Candidate", startUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        SeedEvent(db, "verified-transient", "PLANET_CONJUNCTION", "Verified transient", 9m, status: "Verified", startUtc: new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(
            StartUtc: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            EndUtc: new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
            EventTypes: ["PLANET_CONJUNCTION"],
            DryRun: true), CancellationToken.None);

        Assert.All(result.GeneratedOpportunities, o => Assert.Equal("inside", o.EventCode));
        Assert.All(result.GeneratedOpportunities, o => Assert.True(o.RequiresSkyfield));
    }

    [Fact]
    public async Task GenerateAsync_UnverifiedOrInvalidConstellation_IsRejected()
    {
        await using var db = CreateDb();
        SeedOrionConstellation(db, code: "orion-rejected", verificationStatus: "Rejected");
        SeedOrionConstellation(db, code: "orion-no-region", regionId: "");
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(EventTypes: ["CONSTELLATION"], DryRun: true), CancellationToken.None);

        Assert.Empty(result.GeneratedOpportunities);
    }

    [Fact]
    public async Task GenerateAsync_DuplicateOrionOpportunity_IsSkipped()
    {
        await using var db = CreateDb();
        var evt = SeedOrionConstellation(db);
        db.AstronomyContentOpportunities.Add(new AstronomyContentOpportunity
        {
            AstronomyEventIntelligence = evt,
            ContentCategory = "AstronomyEducation",
            Title = "Existing Orion education opportunity"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.GenerateAsync(new AstronomyContentOpportunityRequest(EventTypes: ["CONSTELLATION"], DryRun: false), CancellationToken.None);

        var opportunity = Assert.Single(result.GeneratedOpportunities);
        Assert.True(opportunity.DuplicateSkipped);
        Assert.Equal(0, result.SavedCount);
        Assert.Equal(1, result.SkippedDuplicates);
        Assert.Equal(1, await db.AstronomyContentOpportunities.CountAsync());
    }

    private static void SeedEvent(MediaFactoryDbContext db, string code, string eventType, string title, decimal score, params string[] objectNames)
        => SeedEvent(db, code, eventType, title, score, "Candidate", new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero), objectNames);

    private static void SeedEvent(MediaFactoryDbContext db, string code, string eventType, string title, decimal score, string status, DateTimeOffset startUtc, params string[] objectNames)
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = code,
            EventType = eventType,
            Title = title,
            StartUtc = startUtc,
            RegionId = "test-region",
            RecommendedCategory = "Test",
            Status = status,
            ConfidenceScore = score,
            RarityScore = score,
            VisibilityScore = score,
            AudienceInterestScore = score,
            TimingUrgencyScore = score,
            ContentOpportunityScore = score
        };

        foreach (var objectName in objectNames)
        {
            evt.Objects.Add(new AstronomyEventObject
            {
                ObjectName = objectName,
                ObjectType = "Planet",
                ObjectRole = "Primary"
            });
        }

        db.AstronomyEventIntelligences.Add(evt);
    }


    private static AstronomyEventIntelligence SeedOrionConstellation(MediaFactoryDbContext db, string code = "orion-evergreen", string verificationStatus = "Verified", string regionId = "US")
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = code,
            ExternalEventId = "constellation-orion-evergreen-v1",
            Year = 2026,
            Language = "en",
            VerificationStatus = verificationStatus,
            AutoGenerateAllowed = true,
            ContentStrategy = "EvergreenConstellationEducation",
            EventType = "CONSTELLATION",
            Title = "Orion constellation guide",
            Summary = "Evergreen Orion constellation education subject.",
            Description = "Orion is used as an editorial constellation guide without a transient peak claim.",
            StartUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            RegionId = regionId,
            RecommendedCategory = "AstronomyEducation",
            Status = "Verified",
            ConfidenceScore = 8m,
            RarityScore = 4m,
            VisibilityScore = 7m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 1m,
            ContentOpportunityScore = 8m,
            MetadataJson = "{\"provenance\":\"test fixture\",\"evergreen\":true}",
            Objects =
            [
                new AstronomyEventObject { ObjectName = "Orion", ObjectType = "Constellation", ObjectRole = "Primary", CatalogId = "IAU:ORI" }
            ]
        };
        db.AstronomyEventIntelligences.Add(evt);
        return evt;
    }

    private static AstronomyContentOpportunityService CreateService(MediaFactoryDbContext db) => new(db, NullLogger<AstronomyContentOpportunityService>.Instance);

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
