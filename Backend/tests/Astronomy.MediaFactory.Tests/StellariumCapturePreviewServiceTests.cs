using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class StellariumCapturePreviewServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "stellarium-capture-preview-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewCaptureAsync_CompletedStellariumScreenshot_BuildsValidCapturePreviewWithoutGeneratingPng()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory);
        var beforeFiles = Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
        var service = CreateService(db);

        var result = await service.PreviewCaptureAsync(new StellariumCapturePreviewRequest("IN-RJ-UDAIPUR", [], 50), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.Valid);
        Assert.Equal(0, result.Invalid);
        Assert.Equal(0, result.Warnings);
        var preview = Assert.Single(result.CapturePreviews);
        Assert.Equal(job.Id, preview.JobId);
        Assert.Equal(job.ContentGenerationPlanId, preview.ContentGenerationPlanId);
        Assert.Equal(job.AstronomyEventIntelligenceId, preview.AstronomyEventIntelligenceId);
        Assert.Equal(job.OutputPath, preview.SscFile);
        Assert.True(File.Exists(preview.SscFile));
        Assert.True(File.Exists(preview.MetadataFile));
        Assert.Contains("Venus", preview.TargetObjects);
        Assert.Contains("Jupiter", preview.TargetObjects);
        Assert.Equal("2026-06-07T11:00:00Z", preview.ScheduledUtc);
        Assert.Equal("2026-06-07T11:30:00Z", preview.PeakUtc);
        Assert.Equal("Western horizon landscape after sunset", preview.Orientation);
        Assert.True(preview.RequiresLabels);
        Assert.True(preview.RequiresConstellationLines);
        Assert.True(preview.RequiresLandscape);
        Assert.Equal("Valid", preview.ValidationStatus);
        Assert.EndsWith(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "stellarium-captures", $"capture-scene-{job.SceneNumber}-{job.Id:D}.png"), preview.ExpectedCapturePath);
        Assert.Contains("Stellarium.exe", preview.CaptureCommandPreview);
        Assert.Contains($"--script {Path.GetFileName(job.OutputPath)}", preview.CaptureCommandPreview);
        Assert.Contains($"--capture capture-scene-{job.SceneNumber}-{job.Id:D}.png", preview.CaptureCommandPreview);
        Assert.False(File.Exists(preview.ExpectedCapturePath));
        Assert.Equal(beforeFiles, Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray());
    }

    [Fact]
    public async Task PreviewCaptureAsync_MissingMetadata_ReturnsInvalidPreviewWarningWithoutLaunchingOrGeneratingFiles()
    {
        await using var db = CreateDb();
        var job = await SeedCompletedStellariumScreenshotJobAsync(db, _workingDirectory, writeMetadata: false);
        var beforeFiles = Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
        var service = CreateService(db);

        var result = await service.PreviewCaptureAsync(new StellariumCapturePreviewRequest("IN-RJ-UDAIPUR", [job.Id], 50), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.Valid);
        Assert.Equal(1, result.Invalid);
        Assert.Equal(1, result.Warnings);
        var preview = Assert.Single(result.CapturePreviews);
        Assert.Equal("Invalid", preview.ValidationStatus);
        Assert.Contains(preview.Warnings, warning => warning.Contains("Metadata file does not exist", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith($"capture-scene-{job.SceneNumber}-{job.Id:D}.png", preview.ExpectedCapturePath);
        Assert.Contains("Stellarium.exe", preview.CaptureCommandPreview);
        Assert.False(File.Exists(preview.ExpectedCapturePath));
        Assert.Equal(beforeFiles, Directory.GetFiles(_workingDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private StellariumCapturePreviewService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }));

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedCompletedStellariumScreenshotJobAsync(MediaFactoryDbContext db, string workingDirectory, bool writeMetadata = true)
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8D",
            Title = "Phase 8D capture preview plan",
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
            Status = AstronomyAssetProductionJobStatuses.Completed,
            MetadataJson = JsonSerializer.Serialize(new
            {
                regionId = "IN-RJ-UDAIPUR",
                objectNames = new[] { "Venus", "Jupiter" },
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                orientation = "Western horizon landscape after sunset",
                requiresConstellationLines = true,
                requiresLabels = true,
                requiresLandscape = true
            })
        };

        var sscDirectory = Path.Combine(workingDirectory, "assets", "IN-RJ-UDAIPUR", "events", eventIntelligenceId.ToString("D"), "stellarium-scripts");
        Directory.CreateDirectory(sscDirectory);
        var sscPath = Path.Combine(sscDirectory, $"scene-{job.SceneNumber}-stellarium-{job.Id:D}.ssc");
        var metadataPath = Path.Combine(sscDirectory, $"scene-{job.SceneNumber}-stellarium-{job.Id:D}.metadata.json");
        await File.WriteAllTextAsync(sscPath, "// reusable SSC preview\ncore.clear(\"natural\");\n");
        if (writeMetadata)
        {
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(new
            {
                assetType = "StellariumScreenshot",
                objectNames = new[] { "Venus", "Jupiter" },
                regionId = "IN-RJ-UDAIPUR",
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                orientation = "Western horizon landscape after sunset",
                requiresConstellationLines = true,
                requiresLabels = true,
                requiresLandscape = true,
                sscFile = sscPath,
                captureExecuted = false
            }));
        }

        job.OutputPath = sscPath;
        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
