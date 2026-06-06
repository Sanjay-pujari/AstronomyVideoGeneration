using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AssetExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "asset-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteRequiredAssetsAsync_TextOverlayCard_GeneratesJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedJobAsync(db, "TextOverlayCard", new { titleText = "Tonight", subtitleText = "Look west", dataPoints = new[] { "Venus", "Jupiter" } });
        var service = CreateService(db);

        var result = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(generatedPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal("Tonight", document.RootElement.GetProperty("titleText").GetString());
        Assert.Equal("Look west", document.RootElement.GetProperty("subtitleText").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("dataPoints")[0].GetString());

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(generatedPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
    }

    [Fact]
    public async Task ExecuteRequiredAssetsAsync_ThumbnailConcept_GeneratesJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        await SeedJobAsync(db, "ThumbnailConcept", new { thumbnailText = "Don't miss this", emotion = "awe", composition = "Planets above horizon", keyObjects = new[] { "Mars" } });
        var service = CreateService(db);

        var result = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        var generatedPath = Assert.Single(result.GeneratedFiles);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal("Don't miss this", document.RootElement.GetProperty("thumbnailText").GetString());
        Assert.Equal("awe", document.RootElement.GetProperty("emotion").GetString());
        Assert.Equal("Planets above horizon", document.RootElement.GetProperty("composition").GetString());
        Assert.Equal("Mars", document.RootElement.GetProperty("keyObjects")[0].GetString());
        Assert.Equal(1, await db.AstronomyAssetProductionJobs.CountAsync(j => j.Status == AstronomyAssetProductionJobStatuses.Completed));
    }

    [Fact]
    public async Task ExecuteRequiredAssetsAsync_SkipsDuplicateUnlessOverwriteExistingTrue()
    {
        await using var db = CreateDb();
        var job = await SeedJobAsync(db, "TextOverlayCard", new { titleText = "Old", subtitleText = "Original", dataPoints = new[] { "Moon" } });
        var service = CreateService(db);
        var first = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false), CancellationToken.None);
        var generatedPath = Assert.Single(first.GeneratedFiles);
        var originalJson = await File.ReadAllTextAsync(generatedPath);

        job.Status = AstronomyAssetProductionJobStatuses.Pending;
        job.MetadataJson = JsonSerializer.Serialize(new { titleText = "New", subtitleText = "Updated", dataPoints = new[] { "Saturn" } });
        await db.SaveChangesAsync();

        var skipped = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, skipped.SkippedCount);
        Assert.Equal(0, skipped.CompletedCount);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);

        var overwritten = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false, overwriteExisting: true), CancellationToken.None);

        Assert.Equal(1, overwritten.CompletedCount);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal("New", document.RootElement.GetProperty("titleText").GetString());
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, job.Status);
    }

    [Fact]
    public async Task ExecuteRequiredAssetsAsync_DoesNotExecuteUnsupportedOrNonRequiredJobs()
    {
        await using var db = CreateDb();
        await SeedJobAsync(db, "TextOverlayCard", new { titleText = "Required", subtitleText = "Safe", dataPoints = new[] { "Moon" } });
        await SeedJobAsync(db, "StellariumScreenshot", new { targetObjects = new[] { "Moon" }, regionId = "IN-RJ-UDAIPUR", locationName = "Udaipur", suggestedOrientation = "landscape" });
        await SeedJobAsync(db, "AiCinematicImage", new { imagePrompt = "Do not generate" }, priority: AstronomyAssetClassificationRules.Required);
        await SeedJobAsync(db, "NasaAsset", new { searchTerms = new[] { "Moon" } }, priority: AstronomyAssetClassificationRules.Preferred);
        var service = CreateService(db);

        var result = await service.ExecuteRequiredAssetsAsync(Request(dryRun: false, maxJobs: 10), CancellationToken.None);

        Assert.Equal(3, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Warnings, w => w.Contains("StellariumScreenshot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("AiCinematicImage", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, (await db.AstronomyAssetProductionJobs.SingleAsync(j => j.AssetType == "StellariumScreenshot")).Status);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, (await db.AstronomyAssetProductionJobs.SingleAsync(j => j.AssetType == "AiCinematicImage")).Status);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, (await db.AstronomyAssetProductionJobs.SingleAsync(j => j.AssetType == "NasaAsset")).Status);
        Assert.DoesNotContain(Directory.EnumerateFiles(_workingDirectory, "*.json", SearchOption.AllDirectories), path => path.Contains("stellarium", StringComparison.OrdinalIgnoreCase) || path.Contains("aicinematicimage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteRequiredAssetsAsync_DryRun_ReturnsPreviewWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedJobAsync(db, "ThumbnailConcept", new { thumbnailText = "Preview", composition = "No file", keyObjects = new[] { "Moon" } });
        var service = CreateService(db);

        var result = await service.ExecuteRequiredAssetsAsync(Request(dryRun: true), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.CompletedCount);
        var previewPath = Assert.Single(result.GeneratedFiles);
        Assert.False(File.Exists(previewPath));
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);
        Assert.Null(job.OutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50) => new(
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting);

    private AssetExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<AssetExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedJobAsync(MediaFactoryDbContext db, string assetType, object metadata, string priority = AstronomyAssetClassificationRules.Required)
    {
        var plan = db.ContentGenerationPlans.Local.FirstOrDefault()
            ?? new ContentGenerationPlan
            {
                ContentCategoryCode = "Phase8C",
                Title = "Phase 8C required asset plan",
                Language = "en",
                RegionId = "IN-RJ-UDAIPUR",
                ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
                Status = "Planned",
                PlanStatus = "Planned",
                PlannedFormat = "Short",
                PriorityScore = 9m
            };

        if (plan.Id == Guid.Empty || !db.ContentGenerationPlans.Local.Contains(plan))
            db.ContentGenerationPlans.Add(plan);

        var job = new AstronomyAssetProductionJob
        {
            ContentGenerationPlan = plan,
            ContentGenerationPlanId = plan.Id,
            SceneNumber = db.AstronomyAssetProductionJobs.Local.Count + 1,
            SceneName = $"Scene {db.AstronomyAssetProductionJobs.Local.Count + 1}",
            AssetType = assetType,
            AssetPurpose = $"Produce {assetType}",
            PlannedProvider = assetType == "StellariumScreenshot" ? "Stellarium" : assetType.StartsWith("Ai", StringComparison.OrdinalIgnoreCase) ? "AzureOpenAIImage" : assetType == "NasaAsset" ? "NASA" : "InternalTemplate",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Moon", "Mars" }),
            PromptOrInstruction = $"Create {assetType}.",
            ExpectedOutputType = "Json",
            Priority = db.AstronomyAssetProductionJobs.Local.Count + 1,
            AssetPriority = priority,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup(assetType),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = JsonSerializer.Serialize(metadata)
        };

        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
