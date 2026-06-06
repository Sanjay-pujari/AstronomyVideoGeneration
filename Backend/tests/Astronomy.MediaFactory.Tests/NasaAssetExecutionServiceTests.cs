using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class NasaAssetExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "nasa-asset-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_DryRun_ReturnsPreviewsWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedNasaAssetJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: true), CancellationToken.None);

        var previewPath = Assert.Single(result.GeneratedFiles);
        Assert.False(File.Exists(previewPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "nasa-assets"), previewPath);
        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);
        Assert.Null(job.OutputPath);
        Assert.Null(job.CompletedUtc);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_WritesEventLevelNasaMetadataJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedNasaAssetJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(generatedPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "nasa-assets"), generatedPath);
        Assert.EndsWith($"nasa-asset-scene-{job.SceneNumber}-{job.Id:D}.json", generatedPath);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(job.SceneNumber, document.RootElement.GetProperty("sceneNumber").GetInt32());
        Assert.Equal("NASA background plate", document.RootElement.GetProperty("sceneName").GetString());
        Assert.Equal("VENUS-JUPITER-2026", document.RootElement.GetProperty("eventCode").GetString());
        Assert.Equal("PlanetConjunction", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal("Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("2026-06-07T11:00:00Z", document.RootElement.GetProperty("scheduledUtc").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("peakUtc").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("objectNames")[0].GetString());
        Assert.Equal("NasaAsset", document.RootElement.GetProperty("assetType").GetString());
        Assert.Equal("Venus Jupiter conjunction", document.RootElement.GetProperty("searchTerms")[0].GetString());
        Assert.Equal("NASA planetary imagery", document.RootElement.GetProperty("searchTerms")[1].GetString());
        Assert.Equal(0, document.RootElement.GetProperty("selectedAssets").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("fallbackToAiImage").GetBoolean());
        Assert.Equal("Optional cinematic reference imagery", document.RootElement.GetProperty("assetUsagePurpose").GetString());
        Assert.Equal("Phase8E.1", document.RootElement.GetProperty("generationSource").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("generatedUtc").GetString()));

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(generatedPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_EnableExternalLookupFalseDoesNotWarnOrCallExternalLookup()
    {
        await using var db = CreateDb();
        await SeedNasaAssetJobAsync(db);
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false, enableExternalLookup: false), CancellationToken.None);

        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("External NASA lookup", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.CompletedCount);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_UsesFallbackMetadataWhenNasaLookupDisabled()
    {
        await using var db = CreateDb();
        var job = await SeedNasaAssetJobAsync(db, metadataJson: "{ invalid json");
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.CompletedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal("NasaAsset", document.RootElement.GetProperty("assetType").GetString());
        Assert.True(document.RootElement.GetProperty("fallbackToAiImage").GetBoolean());
        Assert.Equal("Venus", document.RootElement.GetProperty("searchTerms")[0].GetString());
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, job.Status);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_FivePendingNasaAssetJobsGenerateFiveFiles()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 5; i++)
            await SeedNasaAssetJobAsync(db, sceneNumber: i);
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false, maxJobs: 50), CancellationToken.None);

        Assert.Equal(5, result.JobCount);
        Assert.Equal(5, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.All(result.GeneratedFiles, path =>
        {
            Assert.True(File.Exists(path));
            Assert.Contains(Path.Combine("events"), path);
            Assert.Contains(Path.Combine("nasa-assets"), path);
        });
        Assert.Equal(5, await db.AstronomyAssetProductionJobs.CountAsync(j => j.Status == AstronomyAssetProductionJobStatuses.Completed));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50, bool enableExternalLookup = false) => new(
        AssetTypes: ["NasaAsset"],
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting,
        EnableExternalLookup: enableExternalLookup);

    private NasaAssetExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<NasaAssetExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedNasaAssetJobAsync(MediaFactoryDbContext db, int sceneNumber = 3, string? metadataJson = null)
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8E",
            Title = "Phase 8E optional asset plan",
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
            SceneNumber = sceneNumber,
            SceneName = "NASA background plate",
            AssetType = "NasaAsset",
            AssetPurpose = "Produce NasaAsset",
            PlannedProvider = "NasaMetadataOnly",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
            PromptOrInstruction = "Create NASA asset search metadata.",
            ExpectedOutputType = "Json",
            Priority = sceneNumber,
            AssetPriority = AstronomyAssetClassificationRules.Optional,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup("NasaAsset"),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = metadataJson ?? JsonSerializer.Serialize(new
            {
                regionId = "IN-RJ-UDAIPUR",
                locationName = "Udaipur",
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                eventCode = "VENUS-JUPITER-2026",
                eventType = "PlanetConjunction",
                searchTerms = new[] { "Venus Jupiter conjunction", "NASA planetary imagery" },
                fallbackToAiImage = true,
                assetUsagePurpose = "Optional cinematic reference imagery"
            })
        };

        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
