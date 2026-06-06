using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationPlanningServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9a-narration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DryRunTrue_ReturnsPreviews_AndWritesNothing()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short", ["Mercury"]);
        var service = CreateService(db);

        var result = await service.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(ExpectedPath(plan.Id)));
        Assert.False(Directory.Exists(outputRoot));
    }

    [Fact]
    public async Task DryRunFalse_WritesNarrationJson()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(
            RegionId: "IN-RJ-UDAIPUR",
            DryRun: false), CancellationToken.None);

        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(ExpectedPath(plan.Id), file);
        Assert.True(File.Exists(file));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal(plan.Id.ToString(), doc.RootElement.GetProperty("contentGenerationPlanId").GetString());
        Assert.Equal("ProfessionalCinematic", doc.RootElement.GetProperty("narrationStyle").GetString());
        Assert.True(doc.RootElement.GetProperty("segments").GetArrayLength() >= 5);
    }

    [Fact]
    public async Task RareEventAlertShort_HasConciseUrgentTrustworthyNarration()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "RareEventAlert", "Short", ["Meteor shower"]);
        var service = CreateService(db);

        var result = await service.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(DryRun: true), CancellationToken.None);

        var script = Assert.Single(result.NarrationScripts);
        Assert.InRange(script.EstimatedDurationSeconds, 30, 60);
        Assert.Contains(script.Segments, s => s.VoiceTone.Contains("urgent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(script.Segments, s => s.Script.Contains("not a guarantee", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(script.Segments, s => s.Script.Contains("once in a lifetime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WeeklySkyForecastLong_HasFlowingStoryNarration_NotBulletListing()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "WeeklySkyForecast", "Long", ["Moon", "Saturn", "Venus"]);
        var service = CreateService(db);

        var result = await service.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(DryRun: true), CancellationToken.None);

        var script = Assert.Single(result.NarrationScripts);
        Assert.True(script.EstimatedDurationSeconds >= 90);
        Assert.Contains(script.Segments, s => s.Script.Contains("does not arrive as a checklist", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(script.Segments, s => s.Script.Contains("night by night", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(script.Segments, s => s.Script.StartsWith("Monday:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generation_DoesNotCreateAudioOrRenderOutputs()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "RareEventAlert", "Short", ["Mars"]);
        var service = CreateService(db);

        await service.GenerateNarrationScriptsAsync(new NarrationPlanningRequest(DryRun: false), CancellationToken.None);

        var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        Assert.All(files, path => Assert.EndsWith(".json", path));
        Assert.DoesNotContain(files, path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }

    private NarrationPlanningService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<NarrationPlanningService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<ContentGenerationPlan> SeedPlanAsync(MediaFactoryDbContext db, string category, string format, IReadOnlyList<string> objects)
    {
        var intelligence = new AstronomyEventIntelligence
        {
            EventCode = $"{category}-20260606",
            EventType = category,
            Title = $"{category} over Udaipur",
            StartUtc = DateTimeOffset.Parse("2026-06-07T12:00:00Z"),
            PeakUtc = DateTimeOffset.Parse("2026-06-07T15:30:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-06-07T18:00:00Z"),
            RegionId = "IN-RJ-UDAIPUR",
            LocationName = "Udaipur",
            RecommendedCategory = category,
            ConfidenceScore = 0.9m,
            RarityScore = 0.8m,
            VisibilityScore = 0.7m,
            AudienceInterestScore = 0.8m,
            TimingUrgencyScore = 0.6m,
            ContentOpportunityScore = 0.9m
        };
        db.AstronomyEventIntelligences.Add(intelligence);
        await db.SaveChangesAsync();

        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = category,
            Title = $"{category} narration plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T15:00:00Z"),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = format,
            AstronomyEventIntelligenceId = intelligence.Id,
            PlannedObjectNamesJson = JsonSerializer.Serialize(objects),
            PriorityScore = 9m
        };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private string ExpectedPath(Guid planId)
        => Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "narration", $"narration-script-{planId:D}.json");

    public void Dispose()
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }
}
