using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class ConstellationGuideExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "constellation-guide-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecutePreferredAssetsAsync_ConstellationGuide_GeneratesEventLevelJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedConstellationGuideJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(generatedPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "constellation-guides"), generatedPath);
        Assert.EndsWith($"constellation-guide-scene-{job.SceneNumber}-{job.Id:D}.json", generatedPath);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(job.SceneNumber, document.RootElement.GetProperty("sceneNumber").GetInt32());
        Assert.Equal("ConstellationGuide", document.RootElement.GetProperty("guideType").GetString());
        Assert.Equal("Phase8C.2B", document.RootElement.GetProperty("generationSource").GetString());
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal("Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("2026-06-07T11:00:00Z", document.RootElement.GetProperty("scheduledUtc").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("peakUtc").GetString());
        Assert.Equal("Western sky after sunset", document.RootElement.GetProperty("viewingDirection").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("recommendedObservationTime").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("objectNames")[0].GetString());
        Assert.True(document.RootElement.GetProperty("starHopInstructions")[0].GetString()!.Contains("locate Venus first", StringComparison.OrdinalIgnoreCase));
        Assert.True(document.RootElement.GetProperty("orientationTips")[2].GetString()!.Contains("binoculars", StringComparison.OrdinalIgnoreCase));
        Assert.True(document.RootElement.GetProperty("labelingRequirements").GetProperty("showConstellationLines").GetBoolean());
        Assert.True(document.RootElement.GetProperty("labelingRequirements").GetProperty("showHorizon").GetBoolean());

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(generatedPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ExecutePreferredAssetsAsync_DryRun_ReturnsPreviewWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedConstellationGuideJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecutePreferredAssetsAsync(Request(dryRun: true), CancellationToken.None);

        var previewPath = Assert.Single(result.GeneratedFiles);
        Assert.False(File.Exists(previewPath));
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);
        Assert.Null(job.OutputPath);
    }

    [Fact]
    public async Task ExecutePreferredAssetsAsync_DuplicateSkippedUnlessOverwriteExistingTrue()
    {
        await using var db = CreateDb();
        var job = await SeedConstellationGuideJobAsync(db);
        var service = CreateService(db);
        var first = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);
        var generatedPath = Assert.Single(first.GeneratedFiles);
        var originalJson = await File.ReadAllTextAsync(generatedPath);

        job.Status = AstronomyAssetProductionJobStatuses.Pending;
        job.MetadataJson = JsonSerializer.Serialize(new
        {
            regionId = "IN-RJ-UDAIPUR",
            locationName = "Updated Udaipur",
            scheduledUtc = "2026-06-07T12:00:00Z",
            peakUtc = "2026-06-07T12:30:00Z",
            viewingDirection = "Southwestern sky after sunset",
            eventType = "PlanetGrouping"
        });
        await db.SaveChangesAsync();

        var skipped = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, skipped.SkippedCount);
        Assert.Equal(0, skipped.CompletedCount);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);

        var overwritten = await service.ExecutePreferredAssetsAsync(Request(dryRun: false, overwriteExisting: true), CancellationToken.None);

        Assert.Equal(1, overwritten.CompletedCount);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal("Updated Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("Southwestern sky after sunset", document.RootElement.GetProperty("viewingDirection").GetString());
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, job.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50) => new(
        AssetTypes: ["ConstellationGuide"],
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting);

    private ConstellationGuideExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<ConstellationGuideExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedConstellationGuideJobAsync(MediaFactoryDbContext db)
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8C",
            Title = "Phase 8C preferred asset plan",
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
            SceneNumber = 2,
            SceneName = "Venus near Jupiter",
            AssetType = "ConstellationGuide",
            AssetPurpose = "Produce ConstellationGuide",
            PlannedProvider = "InternalTemplate",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
            PromptOrInstruction = "Create ConstellationGuide.",
            ExpectedOutputType = "Json",
            Priority = 1,
            AssetPriority = AstronomyAssetClassificationRules.Preferred,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup("ConstellationGuide"),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = JsonSerializer.Serialize(new
            {
                instruction = "Show constellation context.",
                regionId = "IN-RJ-UDAIPUR",
                locationName = "Udaipur",
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                eventType = "PlanetConjunction"
            })
        };

        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
