using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.WeeklySkyForecast.AICinematicAssets;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionVisualComposerServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "production-visual-composer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateProductionVisualsAsync_DryRun_UsesLocalOverlayTimeCompleteTextAndDistinctScenePrompts()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db);
        var service = CreateService(db);

        var result = await service.GenerateProductionVisualsAsync(new ProductionVisualGenerationRequest(
            RegionId: "IN-RJ-UDAIPUR",
            MaxPlans: 1,
            DryRun: true), CancellationToken.None);

        Assert.Equal(4, result.SceneCount);
        Assert.Equal(4, result.PlannedVisuals.Count);
        Assert.Empty(result.Warnings);

        var overlays = result.PlannedVisuals.SelectMany(v => v.OverlayText).ToArray();
        Assert.All(overlays, overlay =>
        {
            Assert.DoesNotContain("UTC", overlay, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("…", overlay);
            Assert.DoesNotContain("...", overlay);
            Assert.DoesNotMatch(@"\b\d{4}-\d{2}[\u2026.]+", overlay);
            Assert.InRange(overlay.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 15);
        });

        Assert.Contains(result.PlannedVisuals.Single(v => v.SceneNumber == 1).OverlayText, text => text.Contains("7:23 PM IST", StringComparison.Ordinal));
        Assert.Contains(overlays, text => text.Contains("Jupiter and Venus will appear only 1.63° apart.", StringComparison.Ordinal));

        Assert.Contains("Rare celestial event", result.PlannedVisuals.Single(v => v.SceneNumber == 1).ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accurate sky map", result.PlannedVisuals.Single(v => v.SceneNumber == 2).ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("step-by-step observation guide", result.PlannedVisuals.Single(v => v.SceneNumber == 3).ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beautiful closing astronomy scene", result.PlannedVisuals.Single(v => v.SceneNumber == 4).ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, result.PlannedVisuals.Select(v => v.ImagePrompt).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory)) Directory.Delete(_workingDirectory, recursive: true);
    }

    private ProductionVisualComposerService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), new DisabledAICinematicImageGenerator(), NullLogger<ProductionVisualComposerService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task SeedPlanAsync(MediaFactoryDbContext db)
    {
        var eventIntelligence = new AstronomyEventIntelligence
        {
            EventCode = "VENUS-JUPITER-2026-UDAIPUR",
            EventType = "PlanetConjunction",
            Title = "Jupiter and Venus conjunction over Udaipur",
            Summary = "Jupiter and Venus conjunction over Udaipur with minimum separation 1.63° on 2026-06-07.",
            StartUtc = DateTimeOffset.Parse("2026-06-07T12:30:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T13:53:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-07T15:00:00Z"),
            RegionId = "IN-RJ-UDAIPUR",
            LocationName = "Udaipur",
            TimeZone = "Asia/Kolkata",
            RecommendedCategory = "RareEventAlert",
            Status = "Discovered",
            RawDataJson = JsonSerializer.Serialize(new { direction = "west", bestViewingTime = "around 13:53 UTC" })
        };
        eventIntelligence.Objects.Add(new AstronomyEventObject { ObjectName = "Venus", ObjectType = "Planet" });
        eventIntelligence.Objects.Add(new AstronomyEventObject { ObjectName = "Jupiter", ObjectType = "Planet" });

        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "RareEventAlert",
            Title = "Jupiter and Venus conjunction over Udaipur",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = "Short",
            PrimaryAstronomyEventTypeCode = "PlanetConjunction",
            AstronomyEventIntelligence = eventIntelligence,
            AstronomyEventIntelligenceId = eventIntelligence.Id,
            PriorityScore = 9m
        };

        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
    }
}
