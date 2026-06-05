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

    private static void SeedEvent(MediaFactoryDbContext db, string code, string eventType, string title, decimal score)
    {
        db.AstronomyEventIntelligences.Add(new AstronomyEventIntelligence
        {
            EventCode = code,
            EventType = eventType,
            Title = title,
            StartUtc = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
            RegionId = "test-region",
            RecommendedCategory = "Test",
            Status = "Candidate",
            ConfidenceScore = score,
            RarityScore = score,
            VisibilityScore = score,
            AudienceInterestScore = score,
            TimingUrgencyScore = score,
            ContentOpportunityScore = score
        });
    }

    private static AstronomyContentOpportunityService CreateService(MediaFactoryDbContext db) => new(db, NullLogger<AstronomyContentOpportunityService>.Instance);

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
