using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class DirectorNarrationServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9a1-director-narration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RareEventAlert_HookIsImproved_WithTrustworthyUrgency()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "RareEventAlert", "Short", ["meteor shower"]);
        var service = CreateService(db);

        var result = await service.GenerateDirectorNarrationAsync(new DirectorNarrationRequest(DryRun: true), CancellationToken.None);

        var directorCut = Assert.Single(result.DirectorNarrations);
        var opening = Assert.Single(directorCut.Segments.Where(s => s.SceneNumber == 1));
        Assert.Equal("ProfessionalAstronomyDocumentary", directorCut.DirectorStyle);
        Assert.Contains("window may be brief", opening.DirectorNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not because it is guaranteed", opening.DirectorNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trustworthy urgency", opening.RetentionPurpose, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(opening.PauseHints);
        Assert.NotEmpty(opening.AssetSynchronizationHints);
        Assert.DoesNotContain("once in a lifetime", opening.DirectorNarration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanetConjunction_ExplainsAppearancePerspectiveAndSignificance()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateDirectorNarrationAsync(new DirectorNarrationRequest(DryRun: true), CancellationToken.None);

        var directorCut = Assert.Single(result.DirectorNarrations);
        var allNarration = string.Join("\n", directorCut.Segments.Select(s => s.DirectorNarration));
        Assert.Contains("appear", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("perspective", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line of sight", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("geometry", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(directorCut.Segments, s => s.AssetSynchronizationHints.Any(h => h.Contains("line-of-sight graphic", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task WeeklySkyForecast_BecomesStoryDrivenJourney()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "WeeklySkyForecast", "Long", ["Moon", "Saturn", "Venus"]);
        var service = CreateService(db);

        var result = await service.GenerateDirectorNarrationAsync(new DirectorNarrationRequest(DryRun: true), CancellationToken.None);

        var directorCut = Assert.Single(result.DirectorNarrations);
        var opening = directorCut.Segments[0].DirectorNarration;
        var allNarration = string.Join("\n", directorCut.Segments.Select(s => s.DirectorNarration));
        Assert.Contains("not unfold as a list", opening, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("journey across the night sky", opening, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Night by night", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(directorCut.Segments, s => s.AssetSynchronizationHints.Any(h => h.Contains("night-by-night montage", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain("Monday:", allNarration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRunFalse_WritesDirectorCutJsonOnly_NoTtsAudioOrRendering()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short", ["Moon", "Mars", "Regulus"]);
        var service = CreateService(db);

        var result = await service.GenerateDirectorNarrationAsync(new DirectorNarrationRequest(DryRun: false), CancellationToken.None);

        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(ExpectedPath(plan.Id), file);
        Assert.True(File.Exists(file));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal("ProfessionalAstronomyDocumentary", doc.RootElement.GetProperty("directorStyle").GetString());
        Assert.True(doc.RootElement.GetProperty("segments")[0].TryGetProperty("pauseHints", out _));
        Assert.True(doc.RootElement.GetProperty("segments")[0].TryGetProperty("assetSynchronizationHints", out _));

        var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        Assert.All(files, path => Assert.EndsWith(".json", path));
        Assert.DoesNotContain(files, path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }

    private DirectorNarrationService CreateService(MediaFactoryDbContext db)
    {
        var narration = new NarrationPlanningService(db, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<NarrationPlanningService>.Instance);
        return new DirectorNarrationService(narration, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<DirectorNarrationService>.Instance);
    }

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
        => Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "narration", "narration-director-cut.json");

    public void Dispose()
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }
}
