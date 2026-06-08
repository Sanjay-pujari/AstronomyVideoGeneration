using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class AiImagePromptExecutionServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "ai-image-prompt-execution-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_DryRun_ReturnsPreviewsWithoutWritingOrUpdating()
    {
        await using var db = CreateDb();
        var job = await SeedAiImageJobAsync(db, "AiHeroImage", plannedFormat: "Short");
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: true), CancellationToken.None);

        var previewPath = Assert.Single(result.GeneratedFiles);
        Assert.False(File.Exists(previewPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "ai-image-prompts"), previewPath);
        Assert.Equal(1, result.JobCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Pending, job.Status);
        Assert.Null(job.OutputPath);
        Assert.Null(job.CompletedUtc);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_WritesEventLevelPromptJsonAndCompletesJob()
    {
        await using var db = CreateDb();
        var job = await SeedAiImageJobAsync(db, "AiHeroImage", plannedFormat: "Short");
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false), CancellationToken.None);

        Assert.Equal(1, result.JobCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        var generatedPath = Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(generatedPath));
        Assert.Contains(Path.Combine("assets", "IN-RJ-UDAIPUR", "events", job.AstronomyEventIntelligenceId!.Value.ToString("D"), "ai-image-prompts"), generatedPath);
        Assert.EndsWith($"ai-image-prompt-scene-{job.SceneNumber}-AiHeroImage-{job.Id:D}.json", generatedPath);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(generatedPath));
        Assert.Equal(job.SceneNumber, document.RootElement.GetProperty("sceneNumber").GetInt32());
        Assert.Equal("Opening hook", document.RootElement.GetProperty("sceneName").GetString());
        Assert.Equal("VENUS-JUPITER-2026", document.RootElement.GetProperty("eventCode").GetString());
        Assert.Equal("PlanetConjunction", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("IN-RJ-UDAIPUR", document.RootElement.GetProperty("regionId").GetString());
        Assert.Equal("Udaipur", document.RootElement.GetProperty("locationName").GetString());
        Assert.Equal("2026-06-07T11:00:00Z", document.RootElement.GetProperty("scheduledUtc").GetString());
        Assert.Equal("2026-06-07T11:30:00Z", document.RootElement.GetProperty("peakUtc").GetString());
        Assert.Equal("AiHeroImage", document.RootElement.GetProperty("assetType").GetString());
        Assert.Equal("Venus", document.RootElement.GetProperty("objectNames")[0].GetString());
        Assert.Equal("9:16", document.RootElement.GetProperty("aspectRatio").GetString());
        Assert.Equal("premium cinematic astronomy hero", document.RootElement.GetProperty("style").GetString());
        Assert.Contains("foreground", document.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean upper and lower caption-safe zones", document.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fake UI", document.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("smooth continuous sky gradient", document.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visible horizontal bands", document.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blurry", document.RootElement.GetProperty("negativePrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wrong number of planets", document.RootElement.GetProperty("negativePrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visible horizontal banding", document.RootElement.GetProperty("negativePrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visibleHorizontalBanding=false", document.RootElement.GetProperty("lightingGuide").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("naturalSkyGradient=true", document.RootElement.GetProperty("qualityChecklist")[7].GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high-retention", document.RootElement.GetProperty("qualityChecklist")[5].GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Phase8F.1", document.RootElement.GetProperty("generationSource").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("generatedUtc").GetString()));

        var saved = await db.AstronomyAssetProductionJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(AstronomyAssetProductionJobStatuses.Completed, saved.Status);
        Assert.Equal(generatedPath, saved.OutputPath);
        Assert.NotNull(saved.CompletedUtc);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_EnableExternalGenerationFalseDoesNotWarnOrCallExternalAi()
    {
        await using var db = CreateDb();
        await SeedAiImageJobAsync(db, "AiHeroImage");
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false, enableExternalGeneration: false), CancellationToken.None);

        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("External AI image generation", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.CompletedCount);
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_SupportsHeroAndCinematicImages()
    {
        await using var db = CreateDb();
        await SeedAiImageJobAsync(db, "AiHeroImage", sceneNumber: 0, plannedFormat: "Short");
        await SeedAiImageJobAsync(db, "AiCinematicImage", sceneNumber: 1, plannedFormat: "Long");
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false, maxJobs: 50), CancellationToken.None);

        Assert.Equal(2, result.JobCount);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.GeneratedFiles, path => path.Contains("AiHeroImage", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedFiles, path => path.Contains("AiCinematicImage", StringComparison.Ordinal));
        Assert.All(result.GeneratedFiles, path =>
        {
            Assert.True(File.Exists(path));
            Assert.Contains(Path.Combine("events"), path);
            Assert.Contains(Path.Combine("ai-image-prompts"), path);
        });

        using var cinematicDocument = JsonDocument.Parse(await File.ReadAllTextAsync(result.GeneratedFiles.Single(path => path.Contains("AiCinematicImage", StringComparison.Ordinal))));
        Assert.Equal("16:9", cinematicDocument.RootElement.GetProperty("aspectRatio").GetString());
        Assert.Contains("educational cinematic illustration", cinematicDocument.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("documentary", cinematicDocument.RootElement.GetProperty("professionalPrompt").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await db.AstronomyAssetProductionJobs.CountAsync(j => j.Status == AstronomyAssetProductionJobStatuses.Completed));
    }

    [Fact]
    public async Task ExecuteOptionalAssetsAsync_TwelvePendingAiImageJobsGenerateTwelveFiles()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 12; i++)
            await SeedAiImageJobAsync(db, i % 2 == 0 ? "AiHeroImage" : "AiCinematicImage", sceneNumber: i);
        var service = CreateService(db);

        var result = await service.ExecuteOptionalAssetsAsync(Request(dryRun: false, maxJobs: 50), CancellationToken.None);

        Assert.Equal(12, result.JobCount);
        Assert.Equal(12, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(12, result.GeneratedFiles.Count);
        Assert.All(result.GeneratedFiles, path => Assert.True(File.Exists(path)));
        Assert.Equal(12, await db.AstronomyAssetProductionJobs.CountAsync(j => j.Status == AstronomyAssetProductionJobStatuses.Completed));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }

    private AssetExecutionRequest Request(bool dryRun, bool overwriteExisting = false, int maxJobs = 50, bool enableExternalGeneration = false) => new(
        AssetTypes: ["AiHeroImage", "AiCinematicImage"],
        RegionId: "IN-RJ-UDAIPUR",
        MaxJobs: maxJobs,
        DryRun: dryRun,
        OverwriteExisting: overwriteExisting,
        EnableExternalGeneration: enableExternalGeneration);

    private AiImagePromptExecutionService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _workingDirectory }), NullLogger<AiImagePromptExecutionService>.Instance);

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<AstronomyAssetProductionJob> SeedAiImageJobAsync(MediaFactoryDbContext db, string assetType, int sceneNumber = 0, string plannedFormat = "Short")
    {
        var eventIntelligenceId = Guid.NewGuid();
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8F",
            Title = "Phase 8F AI image prompt plan",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z"),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = plannedFormat,
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
            SceneName = assetType == "AiHeroImage" ? "Opening hook" : "Why conjunction happens",
            AssetType = assetType,
            AssetPurpose = $"Produce {assetType} prompt package",
            PlannedProvider = "AiPromptOnly",
            ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
            PromptOrInstruction = "Create a cinematic no-generation AI image prompt.",
            ExpectedOutputType = "Json",
            Priority = sceneNumber,
            AssetPriority = AstronomyAssetClassificationRules.Optional,
            AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup(assetType),
            Status = AstronomyAssetProductionJobStatuses.Pending,
            MetadataJson = JsonSerializer.Serialize(new
            {
                regionId = "IN-RJ-UDAIPUR",
                locationName = "Udaipur",
                scheduledUtc = "2026-06-07T11:00:00Z",
                peakUtc = "2026-06-07T11:30:00Z",
                eventCode = "VENUS-JUPITER-2026",
                eventType = "PlanetConjunction",
                imagePrompt = "Venus and Jupiter glowing above a desert lake at blue hour",
                aspectRatio = plannedFormat.Equals("Short", StringComparison.OrdinalIgnoreCase) ? "9:16" : "16:9",
                style = assetType == "AiHeroImage" ? "premium cinematic astronomy hero" : "documentary educational astronomy illustration"
            })
        };

        db.ContentGenerationPlans.Add(plan);
        db.AstronomyAssetProductionJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }
}
