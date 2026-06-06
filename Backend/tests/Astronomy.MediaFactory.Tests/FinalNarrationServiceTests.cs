using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class FinalNarrationServiceTests : IDisposable
{
    private readonly string outputRoot = Path.Combine(Path.GetTempPath(), "phase9a2-final-narration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FinalNarration_RemovesTitleLanguage_AndDuplicateSceneNarration()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "PlanetGrouping", "Short", ["Moon", "Mars", "Regulus"]);
        var service = CreateService(db);

        var result = await service.GenerateFinalNarrationAsync(new FinalNarrationRequest(DryRun: true), CancellationToken.None);

        var final = Assert.Single(result.FinalNarrations);
        var allNarration = string.Join("\n", final.Segments.Select(s => s.FinalNarration));
        Assert.Equal("PremiumAstronomyDocumentary", final.ExecutiveProducerStyle);
        Assert.DoesNotContain(final.Title, allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Conjunction guide:", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rare sky alert:", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(final.Segments.Count, final.Segments.Select(s => s.FinalNarration).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(final.Segments.Count, final.Segments.Select(s => s.ScenePurpose).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(final.QualityChecklist.TitleNotRead);
        Assert.True(final.QualityChecklist.NoDuplicateNarration);
        Assert.True(final.QualityChecklist.UniqueScenePurpose);
    }

    [Fact]
    public async Task RareEventAlert_IsUrgentButCalm_AndReadyForTtsThresholdIsEnforced()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "RareEventAlert", "Short", ["meteor shower"]);
        var service = CreateService(db);

        var result = await service.GenerateFinalNarrationAsync(new FinalNarrationRequest(DryRun: true), CancellationToken.None);

        var final = Assert.Single(result.FinalNarrations);
        var allNarration = string.Join("\n", final.Segments.Select(s => s.FinalNarration));
        Assert.Contains("window may be brief", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch calmly", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("once in a lifetime", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a guarantee", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(final.Segments, s => s.VoiceDirection.Contains("Calm urgency", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(final.QualityScore >= 90, final.QualityChecklist.ReadyForTts);
        Assert.Equal(final.QualityChecklist.ReadyForTts ? 1 : 0, result.ReadyForTtsCount);
        Assert.Equal(final.QualityChecklist.ReadyForTts ? 0 : 1, result.NotReadyCount);
    }

    [Fact]
    public async Task PlanetConjunction_FinalNarrationSeparatesPerspectiveDistanceAndExperience()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "PlanetConjunction", "Long", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateFinalNarrationAsync(new FinalNarrationRequest(DryRun: true), CancellationToken.None);

        var final = Assert.Single(result.FinalNarrations);
        var allNarration = string.Join("\n", final.Segments.Select(s => s.FinalNarration));
        Assert.Contains("not close together in space", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line of sight", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("view", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physical", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.True(final.QualityChecklist.ScientificallySafe);
        Assert.Contains(final.Segments, s => s.VisualCue.Contains("line-of-sight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WeeklySkyForecast_FinalNarrationFeelsLikeEpisodeNotList()
    {
        await using var db = CreateDb();
        await SeedPlanAsync(db, "WeeklySkyForecast", "Long", ["Moon", "Saturn", "Venus"]);
        var service = CreateService(db);

        var result = await service.GenerateFinalNarrationAsync(new FinalNarrationRequest(DryRun: true), CancellationToken.None);

        var final = Assert.Single(result.FinalNarrations);
        var opening = final.Segments[0].FinalNarration;
        var allNarration = string.Join("\n", final.Segments.Select(s => s.FinalNarration));
        Assert.Contains("beginning, a middle", opening, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow its rhythm", opening, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Night by night", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Monday:", allNarration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(final.Segments, s => s.VisualCue.Contains("night-by-night montage", StringComparison.OrdinalIgnoreCase));
        Assert.True(final.QualityChecklist.StrongHook);
    }

    [Fact]
    public async Task DryRunFalse_WritesFinalNarrationJsonOnly_NoTtsAudioOrRendering()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short", ["Venus", "Jupiter"]);
        var service = CreateService(db);

        var result = await service.GenerateFinalNarrationAsync(new FinalNarrationRequest(DryRun: false), CancellationToken.None);

        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(ExpectedFinalPath(plan.Id), file);
        Assert.True(File.Exists(file));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal("PremiumAstronomyDocumentary", doc.RootElement.GetProperty("executiveProducerStyle").GetString());
        Assert.Equal("Phase9A.2", doc.RootElement.GetProperty("generationSource").GetString());
        Assert.True(doc.RootElement.GetProperty("segments")[0].TryGetProperty("finalNarration", out _));
        Assert.True(doc.RootElement.GetProperty("qualityChecklist").TryGetProperty("readyForTts", out _));

        var files = Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories);
        Assert.All(files, path => Assert.EndsWith(".json", path));
        Assert.DoesNotContain(files, path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }

    private FinalNarrationService CreateService(MediaFactoryDbContext db)
    {
        var narration = new NarrationPlanningService(db, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<NarrationPlanningService>.Instance);
        var director = new DirectorNarrationService(narration, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<DirectorNarrationService>.Instance);
        return new FinalNarrationService(director, Options.Create(new RenderingOptions { WorkingDirectory = outputRoot }), NullLogger<FinalNarrationService>.Instance);
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

    private string ExpectedFinalPath(Guid planId)
        => Path.Combine(outputRoot, "assets", "IN-RJ-UDAIPUR", "plans", planId.ToString("D"), "narration", "narration-final.json");

    public void Dispose()
    {
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
    }
}
