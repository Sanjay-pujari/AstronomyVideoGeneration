using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class SkyMapCardExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "skymap-card-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecutePreferredAssetsAsync_SkyMapCard_GeneratesJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedSkyMapCardJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(generatedPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", job.ContentGenerationPlanId.ToString("D"), "sky-map-cards"), generatedPath);
        Assert.EndsWith($"scene-{job.SceneNumber}-skymap-{job.Id:D}.json", generatedPath);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(job.SceneNumber, document.RootElement.GetProperty("sceneNumber").GetInt32());
        Assert.Equal("SkyMapCard", document.RootElement.GetProperty("cardType").GetString());
        Assert.Equal("Phase8C.2A", document.RootElement.GetProperty("generationSource").GetString());
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal("Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("2026-06-07T11:00:00Z", document.RootElement.GetProperty("scheduledUtc").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("peakUtc").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("objectNames")[0].GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("viewingInstructions").EnumerateArray());
        Assert.Contains("Venus", document.RootElement.GetProperty("observationSummary").GetString());

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(generatedPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
    }

    [Fact]
    public async Task ExecutePreferredAssetsAsync_DryRun_ReturnsPreviewWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedSkyMapCardJobAsync(db);
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
        var job = await SeedSkyMapCardJobAsync(db);
        var service = CreateService(db);
        var first = await service.ExecutePreferredAssetsAsync(Request(dryRun: false), CancellationToken.None);
        var generatedPath = Assert.Single(first.GeneratedFiles);
        var originalJson = await File.ReadAllTextAsync(generatedPath);

        job.Status = AstronomyAssetProductionJobStatuses.Pending;
        job.MetadataJson = JsonSerializer.Serialize(new
        {
            instruction = "Show an updated internal sky map card.",
            regionId = "IN-RJ-UDAIPUR",
            locationName = "Updated Udaipur",
            scheduledUtc = "2026-06-07T12:00:00Z",
            peakUtc = "2026-06-07T12:30:00Z"
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
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, job.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50) => new(
        AssetTypes: ["SkyMapCard"],
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting);

    private SkyMapCardExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<SkyMapCardExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedSkyMapCardJobAsync(MediaFactoryDbContext db)
    {
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
            PriorityScore = 9m
        };

        var job = new AstronomyAssetProductionJob
        {
            ContentGenerationPlan = plan,
            ContentGenerationPlanId = plan.Id,
            SceneNumber = 2,
            SceneName = "Venus near Mars",
            AssetType = "SkyMapCard",
            AssetPurpose = "Produce SkyMapCard",
            PlannedProvider = "InternalTemplate",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Mars" }),
            PromptOrInstruction = "Create SkyMapCard.",
            ExpectedOutputType = "Json",
            Priority = 1,
            AssetPriority = AstronomyAssetClassificationRules.Preferred,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup("SkyMapCard"),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = JsonSerializer.Serialize(new
            {
                instruction = "Show an internal sky map card.",
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
