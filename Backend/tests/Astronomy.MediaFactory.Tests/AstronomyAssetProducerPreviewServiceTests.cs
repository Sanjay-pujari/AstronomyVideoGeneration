using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyAssetProducerPreviewServiceTests
{
    [Fact]
    public async Task RequiredJobsPreview_ReturnsTextOverlayAndThumbnailConceptProducers()
    {
        await using var db = CreateDb();
        await SeedCurrentPhase8BaselineAsync(db);
        var service = CreateService(db);

        var result = await service.PreviewAssetProductionAsync(new AstronomyAssetProducerPreviewRequest(
            RegionId: "IN-RJ-UDAIPUR",
            AssetPriorities: [AstronomyAssetClassificationRules.Required],
            Status: [AstronomyAssetProductionJobStatuses.Pending],
            MaxJobs: 100), CancellationToken.None);

        Assert.Equal(17, result.JobCount);
        Assert.Equal(17, result.ValidJobs);
        Assert.Equal(0, result.InvalidJobs);
        Assert.Contains(result.Previews, p => p.AssetType == "TextOverlayCard" && p.ProducerName == nameof(TextOverlayAssetProducer));
        Assert.Contains(result.Previews, p => p.AssetType == "ThumbnailConcept" && p.ProducerName == nameof(ThumbnailConceptAssetProducer));
        Assert.All(result.Previews, p => Assert.False(p.WillExecute));
    }

    [Fact]
    public async Task StellariumScreenshotPreview_CreatesRequestPreviewWithoutExecutingStellarium()
    {
        await using var db = CreateDb();
        await SeedCurrentPhase8BaselineAsync(db);
        var service = CreateService(db);

        var result = await service.PreviewAssetProductionAsync(new AstronomyAssetProducerPreviewRequest(
            AssetTypes: ["StellariumScreenshot"],
            Status: [AstronomyAssetProductionJobStatuses.Pending],
            MaxJobs: 20), CancellationToken.None);

        var preview = Assert.Single(result.Previews);
        Assert.Equal(nameof(StellariumScreenshotAssetProducer), preview.ProducerName);
        Assert.Equal("FutureStellariumSscCapturePreview", preview.ProductionRequestPreview?.RequestType);
        Assert.False(preview.ProductionRequestPreview?.WillExecute ?? true);
        Assert.Contains(preview.ProductionRequestPreview!.SafetyNotes, n => n.Contains("Stellarium is not executed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AiImagePreview_CreatesRequestPreviewWithoutCallingAi()
    {
        await using var db = CreateDb();
        await SeedCurrentPhase8BaselineAsync(db);
        var service = CreateService(db);

        var result = await service.PreviewAssetProductionAsync(new AstronomyAssetProducerPreviewRequest(
            AssetTypes: ["AiHeroImage"],
            Status: [AstronomyAssetProductionJobStatuses.Pending],
            MaxJobs: 1), CancellationToken.None);

        var preview = Assert.Single(result.Previews);
        Assert.Equal(nameof(AiImageAssetProducer), preview.ProducerName);
        Assert.Equal("AiImageGenerationPreview", preview.ProductionRequestPreview?.RequestType);
        Assert.Contains(preview.ProductionRequestPreview!.SafetyNotes, n => n.Contains("AI image generation is not called", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NasaPreview_CreatesRequestPreviewWithoutCallingNasa()
    {
        await using var db = CreateDb();
        await SeedCurrentPhase8BaselineAsync(db);
        var service = CreateService(db);

        var result = await service.PreviewAssetProductionAsync(new AstronomyAssetProducerPreviewRequest(
            AssetTypes: ["NasaAsset"],
            Status: [AstronomyAssetProductionJobStatuses.Pending],
            MaxJobs: 1), CancellationToken.None);

        var preview = Assert.Single(result.Previews);
        Assert.Equal(nameof(NasaAssetProducer), preview.ProducerName);
        Assert.Equal("NasaSearchPreview", preview.ProductionRequestPreview?.RequestType);
        Assert.Contains(preview.ProductionRequestPreview!.SafetyNotes, n => n.Contains("NASA APIs are not called", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AllCurrentJobs_HaveProducerCoverage_AndNoDbMutationOccurs()
    {
        await using var db = CreateDb();
        await SeedCurrentPhase8BaselineAsync(db);
        var before = await SnapshotAsync(db);
        var service = CreateService(db);

        var result = await service.PreviewAssetProductionAsync(new AstronomyAssetProducerPreviewRequest(
            RegionId: "IN-RJ-UDAIPUR",
            Status: [AstronomyAssetProductionJobStatuses.Pending],
            MaxJobs: 100), CancellationToken.None);

        Assert.Equal(59, result.JobCount);
        Assert.Equal(59, result.ValidJobs);
        Assert.Equal(0, result.InvalidJobs);
        Assert.All(result.ProducerCoverage, c => Assert.True(c.Covered));
        Assert.Equal(7, result.ProducerCoverage.Count);
        Assert.Empty(result.Warnings);
        Assert.Equal(before, await SnapshotAsync(db));
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    private static AstronomyAssetProducerPreviewService CreateService(MediaFactoryDbContext db)
        => new(db, Producers(), NullLogger<AstronomyAssetProducerPreviewService>.Instance);

    private static IAstronomyAssetProducer[] Producers() =>
    [
        new TextOverlayAssetProducer(),
        new ThumbnailConceptAssetProducer(),
        new StellariumScreenshotAssetProducer(),
        new ConstellationGuideAssetProducer(),
        new SkyMapCardAssetProducer(),
        new NasaAssetProducer(),
        new AiImageAssetProducer()
    ];

    private static MediaFactoryDbContext CreateDb()
        => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<string> SnapshotAsync(MediaFactoryDbContext db)
    {
        var rows = await db.AstronomyAssetProductionJobs
            .AsNoTracking()
            .OrderBy(j => j.Id)
            .Select(j => new { j.Id, j.Status, j.OutputPath, j.StartedUtc, j.CompletedUtc, j.FailureReason })
            .ToListAsync();
        return JsonSerializer.Serialize(rows);
    }

    private static async Task SeedCurrentPhase8BaselineAsync(MediaFactoryDbContext db)
    {
        var plans = Enumerable.Range(1, 8).Select(i => new ContentGenerationPlan
        {
            ContentCategoryCode = "Phase8B",
            Title = $"Phase 8B plan {i}",
            Language = "en",
            RegionId = "IN-RJ-UDAIPUR",
            ScheduledUtc = DateTimeOffset.Parse("2026-06-07T11:00:00Z").AddHours(i),
            Status = "Planned",
            PlanStatus = "Planned",
            PlannedFormat = i % 2 == 0 ? "Long" : "Short",
            PriorityScore = 9m,
            PlannedObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" })
        }).ToList();
        db.ContentGenerationPlans.AddRange(plans);
        await db.SaveChangesAsync();

        var counts = new (string AssetType, int Count)[]
        {
            ("TextOverlayCard", 16),
            ("ThumbnailConcept", 1),
            ("StellariumScreenshot", 1),
            ("ConstellationGuide", 8),
            ("SkyMapCard", 8),
            ("NasaAsset", 8),
            ("AiHeroImage", 8),
            ("AiCinematicImage", 9)
        };

        var jobs = new List<AstronomyAssetProductionJob>();
        var scene = 1;
        foreach (var (assetType, count) in counts)
        {
            for (var i = 0; i < count; i++)
            {
                var plan = plans[(jobs.Count + i) % plans.Count];
                jobs.Add(new AstronomyAssetProductionJob
                {
                    ContentGenerationPlanId = plan.Id,
                    SceneNumber = scene++,
                    SceneName = $"Scene {scene}",
                    AssetType = assetType,
                    AssetPurpose = $"Preview {assetType}",
                    PlannedProvider = ProviderFor(assetType),
                    ObjectNamesJson = JsonSerializer.Serialize(new[] { "Venus", "Jupiter" }),
                    PromptOrInstruction = $"Create preview request for {assetType}.",
                    ExpectedOutputType = "PreviewOnly",
                    Priority = scene,
                    AssetPriority = assetType is "TextOverlayCard" or "ThumbnailConcept" ? AstronomyAssetClassificationRules.Required : AstronomyAssetClassificationRules.Optional,
                    AssetExecutionGroup = AstronomyAssetClassificationRules.ResolveExecutionGroup(assetType),
                    Status = AstronomyAssetProductionJobStatuses.Pending,
                    MetadataJson = JsonSerializer.Serialize(MetadataFor(assetType, plan))
                });
            }
        }

        db.AstronomyAssetProductionJobs.AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private static string ProviderFor(string assetType) => assetType switch
    {
        "StellariumScreenshot" => "Stellarium",
        "AiHeroImage" or "AiCinematicImage" => "AzureOpenAIImage",
        "NasaAsset" => "NASA",
        _ => "InternalTemplate"
    };

    private static object MetadataFor(string assetType, ContentGenerationPlan plan) => assetType switch
    {
        "TextOverlayCard" => new { titleText = "Tonight", subtitleText = "Look west", dataPoints = new[] { "Venus", "Jupiter" } },
        "ThumbnailConcept" => new { thumbnailText = "Don't miss this", keyObjects = new[] { "Venus", "Jupiter" }, composition = "Large readable text with planets." },
        "StellariumScreenshot" => new { targetObjects = new[] { "Venus", "Jupiter" }, regionId = plan.RegionId, locationName = "Udaipur", scheduledUtc = plan.ScheduledUtc, suggestedOrientation = "landscape-16:9" },
        "ConstellationGuide" => new { instruction = "Show the constellation context." },
        "SkyMapCard" => new { instruction = "Show an internal sky map card." },
        "NasaAsset" => new { searchTerms = new[] { "Venus", "Jupiter" }, fallbackToAiImage = true },
        "AiHeroImage" or "AiCinematicImage" => new { imagePrompt = "Cinematic planets in evening sky", aspectRatio = "16:9", style = "cinematic astronomy" },
        _ => new { instruction = "Preview only." }
    };
}
