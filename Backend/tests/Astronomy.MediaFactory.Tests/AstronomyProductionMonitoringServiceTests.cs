using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyProductionMonitoringServiceTests
{
    [Fact]
    public async Task GetProductionSummaryAsync_ReturnsCurrentTotalsAndCompletionPercent()
    {
        await using var db = CreateDb();
        SeedProductionState(db);
        var service = CreateService(db);

        var summary = await service.GetProductionSummaryAsync(new AstronomyProductionMonitoringRequest(), CancellationToken.None);

        Assert.Equal(4, summary.Events.Total);
        Assert.Equal(2, summary.Events.Candidate);
        Assert.Equal(1, summary.Events.Planned);
        Assert.Equal(1, summary.Events.Completed);
        Assert.Equal(3, summary.Opportunities.Total);
        Assert.Equal(1, summary.Opportunities.Proposed);
        Assert.Equal(1, summary.Opportunities.Planned);
        Assert.Equal(1, summary.Opportunities.Completed);
        Assert.Equal(3, summary.VideoPlans.Total);
        Assert.Equal(1, summary.VideoPlans.Planned);
        Assert.Equal(1, summary.VideoPlans.Completed);
        Assert.Equal(1, summary.VideoPlans.Failed);
        Assert.Equal(4, summary.AssetJobs.Total);
        Assert.Equal(2, summary.AssetJobs.Pending);
        Assert.Equal(2, summary.AssetJobs.Completed);
        Assert.Equal(0, summary.AssetJobs.Failed);
        Assert.Equal(0, summary.AssetJobs.InProgress);
        Assert.Equal(50m, summary.CompletionPercent);
    }

    [Fact]
    public async Task GetProductionSummaryAsync_CountsMatchDatabaseBreakdowns()
    {
        await using var db = CreateDb();
        SeedProductionState(db);
        var service = CreateService(db);

        var summary = await service.GetProductionSummaryAsync(new AstronomyProductionMonitoringRequest(), CancellationToken.None);

        Assert.Equal(await db.AstronomyEventIntelligences.CountAsync(), summary.Events.Total);
        Assert.Equal(await db.AstronomyContentOpportunities.CountAsync(), summary.Opportunities.Total);
        Assert.Equal(await db.ContentGenerationPlans.CountAsync(), summary.VideoPlans.Total);
        Assert.Equal(await db.AstronomyAssetProductionJobs.CountAsync(), summary.AssetJobs.Total);

        var textOverlay = Assert.Single(summary.AssetTypeBreakdown, item => item.AssetType == "TextOverlayCard");
        Assert.Equal(2, textOverlay.Total);
        Assert.Equal(1, textOverlay.Completed);
        Assert.Equal(1, textOverlay.Pending);

        Assert.Equal(2, summary.PriorityBreakdown.Required);
        Assert.Equal(1, summary.PriorityBreakdown.Preferred);
        Assert.Equal(1, summary.PriorityBreakdown.Optional);
        Assert.Contains(summary.ProducerCoverage, item => item.AssetType == "TextOverlayCard" && item.Producer == nameof(TextOverlayAssetProducer) && item.Covered);
        Assert.Equal("TextOverlayCard", summary.TopPendingAssets.First().AssetType);
    }

    [Fact]
    public async Task GetProductionSummaryAsync_RegionFilteringWorks()
    {
        await using var db = CreateDb();
        SeedProductionState(db);
        var service = CreateService(db);

        var summary = await service.GetProductionSummaryAsync(new AstronomyProductionMonitoringRequest(RegionId: "region-a"), CancellationToken.None);

        Assert.Equal("region-a", summary.RegionId);
        Assert.Equal(3, summary.Events.Total);
        Assert.Equal(2, summary.Opportunities.Total);
        Assert.Equal(2, summary.VideoPlans.Total);
        Assert.Equal(3, summary.AssetJobs.Total);
        Assert.Equal(2, summary.AssetJobs.Completed);
        Assert.Equal(Math.Round(2m * 100m / 3m, 2, MidpointRounding.AwayFromZero), summary.CompletionPercent);
    }

    [Fact]
    public async Task GetProductionSummaryAsync_DoesNotTrackOrWriteEntities()
    {
        await using var db = CreateDb();
        SeedProductionState(db);
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        await service.GetProductionSummaryAsync(new AstronomyProductionMonitoringRequest(), CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static AstronomyProductionMonitoringService CreateService(MediaFactoryDbContext db)
        => new(db, [new TextOverlayAssetProducer(), new ThumbnailConceptAssetProducer(), new StellariumScreenshotAssetProducer()]);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static void SeedProductionState(MediaFactoryDbContext db)
    {
        var eventA1 = SeedEvent(db, "event-a-1", "region-a", "Candidate", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var eventA2 = SeedEvent(db, "event-a-2", "region-a", "Planned", new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var eventA3 = SeedEvent(db, "event-a-3", "region-a", "Completed", new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero));
        var eventB1 = SeedEvent(db, "event-b-1", "region-b", "Candidate", new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero));

        var opportunityA1 = SeedOpportunity(db, eventA1, "Proposed");
        var opportunityA2 = SeedOpportunity(db, eventA2, "Planned");
        var opportunityB1 = SeedOpportunity(db, eventB1, "Completed");

        var planA1 = SeedPlan(db, eventA1, opportunityA1, "region-a", "Planned", new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var planA2 = SeedPlan(db, eventA2, opportunityA2, "region-a", "Completed", new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var planB1 = SeedPlan(db, eventB1, opportunityB1, "region-b", "Failed", new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero));

        SeedJob(db, planA1, opportunityA1, eventA1, "TextOverlayCard", AstronomyAssetClassificationRules.Required, AstronomyAssetProductionJobStatuses.Pending, 1);
        SeedJob(db, planA1, opportunityA1, eventA1, "StellariumScreenshot", AstronomyAssetClassificationRules.Preferred, AstronomyAssetProductionJobStatuses.Completed, 5);
        SeedJob(db, planA2, opportunityA2, eventA2, "TextOverlayCard", AstronomyAssetClassificationRules.Required, AstronomyAssetProductionJobStatuses.Completed, 2);
        SeedJob(db, planB1, opportunityB1, eventB1, "ThumbnailConcept", AstronomyAssetClassificationRules.Optional, AstronomyAssetProductionJobStatuses.Pending, 10);

        db.SaveChanges();
    }

    private static AstronomyEventIntelligence SeedEvent(MediaFactoryDbContext db, string code, string regionId, string status, DateTimeOffset startUtc)
    {
        var evt = new AstronomyEventIntelligence
        {
            EventCode = code,
            EventType = "TEST_EVENT",
            Title = code,
            StartUtc = startUtc,
            RegionId = regionId,
            RecommendedCategory = "TestCategory",
            Status = status,
            ConfidenceScore = 8m,
            RarityScore = 8m,
            VisibilityScore = 8m,
            AudienceInterestScore = 8m,
            TimingUrgencyScore = 8m,
            ContentOpportunityScore = 8m
        };
        db.AstronomyEventIntelligences.Add(evt);
        return evt;
    }

    private static AstronomyContentOpportunity SeedOpportunity(MediaFactoryDbContext db, AstronomyEventIntelligence evt, string status)
    {
        var opportunity = new AstronomyContentOpportunity
        {
            AstronomyEventIntelligence = evt,
            ContentCategory = "TestCategory",
            Title = $"Opportunity {evt.EventCode}",
            PriorityScore = 8m,
            Status = status
        };
        db.AstronomyContentOpportunities.Add(opportunity);
        return opportunity;
    }

    private static ContentGenerationPlan SeedPlan(MediaFactoryDbContext db, AstronomyEventIntelligence evt, AstronomyContentOpportunity opportunity, string regionId, string status, DateTimeOffset scheduledUtc)
    {
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "TestCategory",
            Title = $"Plan {evt.EventCode}",
            Language = "en",
            RegionId = regionId,
            ScheduledUtc = scheduledUtc,
            Status = status,
            PlanStatus = status,
            AstronomyEventIntelligence = evt,
            AstronomyContentOpportunity = opportunity,
            PlannedFormat = "Short",
            PriorityScore = 8m
        };
        db.ContentGenerationPlans.Add(plan);
        return plan;
    }

    private static void SeedJob(
        MediaFactoryDbContext db,
        ContentGenerationPlan plan,
        AstronomyContentOpportunity opportunity,
        AstronomyEventIntelligence evt,
        string assetType,
        string assetPriority,
        string status,
        int priority)
    {
        db.AstronomyAssetProductionJobs.Add(new AstronomyAssetProductionJob
        {
            ContentGenerationPlan = plan,
            AstronomyContentOpportunity = opportunity,
            AstronomyEventIntelligence = evt,
            SceneNumber = 1,
            SceneName = "Scene",
            AssetType = assetType,
            AssetPurpose = "Monitoring test asset",
            PlannedProvider = "TestProvider",
            Priority = priority,
            AssetPriority = assetPriority,
            AssetExecutionGroup = AstronomyAssetClassificationRules.Core,
            Status = status
        });
    }
}
