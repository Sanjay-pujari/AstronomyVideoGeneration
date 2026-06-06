using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumScreenshotExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "stellarium-screenshot-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecutePreferredAssetsAsync_StellariumScreenshot_GeneratesSscAndMetadataAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedStellariumScreenshotJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, result.GeneratedFiles.Count);
        var sscPath = result.GeneratedFiles.Single(path => path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase));
        var metadataPath = result.GeneratedFiles.Single(path => path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(sscPath));
        Assert.True(File.Exists(metadataPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "stellarium-scripts"), sscPath);
        Assert.EndsWith($"scene-{job.SceneNumber}-stellarium-{job.Id:D}.ssc", sscPath);
        Assert.EndsWith($"scene-{job.SceneNumber}-stellarium-{job.Id:D}.metadata.json", metadataPath);

        var ssc = await File.ReadAllTextAsync(sscPath);
        Assert.Contains("core.setObserverLocation(73.7125, 24.5854", ssc);
        Assert.Contains("core.setDate(\"2026-06-07T11:30:00\", \"utc\")", ssc);
        Assert.Contains("core.selectObjectByName(\"Venus\", true)", ssc);
        Assert.Contains("ConstellationMgr.setFlagLines(true)", ssc);
        Assert.Contains("ConstellationMgr.setFlagLabels(true)", ssc);
        Assert.Contains("Orientation hint: Western horizon after sunset", ssc);
        Assert.Contains("Framing instructions:", ssc);
        Assert.DoesNotContain("core.screenshot", ssc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".png", ssc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.quitStellarium", ssc, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        Assert.Equal("StellariumScreenshot", document.RootElement.GetProperty("assetType").GetString());
        Assert.Equal("planet-conjunction", document.RootElement.GetProperty("eventCode").GetString());
        Assert.Equal("PlanetConjunction", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("objectNames")[0].GetString());
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal("Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("2026-06-07T11:00:00Z", document.RootElement.GetProperty("scheduledUtc").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("peakUtc").GetString());
        Assert.Equal("Western horizon after sunset", document.RootElement.GetProperty("orientation").GetString());
        Assert.Equal(sscPath, document.RootElement.GetProperty("sscFile").GetString());
        Assert.False(document.RootElement.GetProperty("captureExecuted").GetBoolean());

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(sscPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ExecutePreferredAssetsAsync_DryRun_ReturnsSscPreviewPathWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedStellariumScreenshotJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecutePreferredAssetsAsync(Request(dryRun: true), CancellationToken.None);

        var previewPath = Assert.Single(result.GeneratedFiles);
        Assert.EndsWith($"scene-{job.SceneNumber}-stellarium-{job.Id:D}.ssc", previewPath);
        Assert.False(File.Exists(previewPath));
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);
        Assert.Null(job.OutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50) => new(
        AssetTypes: ["StellariumScreenshot"],
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting);

    private StellariumScreenshotExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<StellariumScreenshotExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedStellariumScreenshotJobAsync(MediaFactoryDbContext db)
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8D",
            Title = "Phase 8D SSC plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = "Short",
            PrimaryAstronomyEventTypeCode = "PlanetConjunction",
            AstronomyEventIntelligenceId = eventIntelligenceId,
            PriorityScore = 9m
        };

        var job = new AstronomyAssetProductionJob
        {
            ContentGenerationPlan = plan,
            ContentGenerationPlanId = plan.Id,
            AstronomyEventIntelligenceId = eventIntelligenceId,
            SceneNumber = 3,
            SceneName = "Venus near Jupiter Stellarium frame",
            AssetType = "StellariumScreenshot",
            AssetPurpose = "Produce reusable Stellarium SSC script",
            PlannedProvider = "StellariumScreenshotProducer",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
            PromptOrInstruction = "Create reusable SSC only.",
            ExpectedOutputType = "SscScript",
            Priority = 1,
            AssetPriority = AstronomyAssetClassificationRules.Preferred,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup("StellariumScreenshot"),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = JsonSerializer.Serialize(new
            {
                regionId = "IN-RJ-UDAIPUR",
                locationName = "Udaipur",
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                suggestedOrientation = "Western horizon after sunset",
                requiresConstellationLines = true,
                requiresLabels = true,
                eventCode = "planet-conjunction",
                eventType = "PlanetConjunction",
                latitude = 24.5854,
                longitude = 73.7125
            })
        };

        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
