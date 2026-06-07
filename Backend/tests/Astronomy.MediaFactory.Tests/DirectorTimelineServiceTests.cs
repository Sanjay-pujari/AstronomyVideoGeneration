using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class DirectorTimelineServiceTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "director-timeline-tests", Guid.NewGuid().ToString("N"));
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateDirectorTimelines_UsesAudioManifestDurationsAndWritesSceneTimeline()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteTtsPackageAsync(plan.Id, "PlanetConjunction", "Short", [
            Segment(1, "Hook", "Watch Venus and Jupiter meet.", 10),
            Segment(2, "Explanation", "Here is how to find the pair.", 10)
        ]);
        var scene1Audio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "scene-01.wav");
        var scene2Audio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "scene-02.wav");
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1, 2, 3]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, scene1Audio, 4.2), (2, scene2Audio, 5.8)], totalDuration: 10.0);
        await SeedAssetAsync(db, plan.Id, 1, "StellariumScreenshot", "/visuals/scene-1.png");
        await SeedAssetAsync(db, plan.Id, 2, "SkyMapCard", "/visuals/scene-2.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, MaxPlans: 5, DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Equal(1, result.ReadyForRenderCount);
        var timeline = Assert.Single(result.Timelines);
        Assert.Equal(10.0, timeline.EstimatedDurationSeconds);
        Assert.Equal(10.0, timeline.Audio.TotalNarrationDurationSeconds);
        Assert.Collection(timeline.Scenes,
            scene =>
            {
                Assert.Equal(0, scene.StartSecond);
                Assert.Equal(4.2, scene.EndSecond);
                Assert.Equal(4.2, scene.DurationSeconds);
                Assert.Equal("StellariumScreenshot", scene.PrimaryAsset.AssetType);
            },
            scene =>
            {
                Assert.Equal(4.2, scene.StartSecond);
                Assert.Equal(10.0, scene.EndSecond);
                Assert.Equal(5.8, scene.DurationSeconds);
                Assert.Equal("SkyMapCard", scene.PrimaryAsset.AssetType);
            });
        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(Path.Combine(PlanRoot(plan.Id), "timeline", "director-timeline.json"), file);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task GenerateDirectorTimelines_AiPromptWithoutImageIsPlannedVisualAndReady()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteTtsPackageAsync(plan.Id, "RareEventAlert", "Short", [Segment(1, "Hook", "A rare meteor outburst may peak tonight.", 7)]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, Path.Combine(PlanRoot(plan.Id), "tts", "audio", "scene-01.wav"), 7.0)], totalDuration: 7.0);
        await SeedAssetAsync(db, plan.Id, 1, "AiHeroImage", Path.Combine(PlanRoot(plan.Id), "assets", "ai-hero-prompt.json"), prompt: "cinematic meteor shower over Udaipur");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var timeline = Assert.Single(result.Timelines);
        Assert.True(timeline.RenderReadiness.ReadyForRender);
        var scene = Assert.Single(timeline.Scenes);
        Assert.Equal("PlannedVisual", scene.PrimaryAsset.AssetType);
        Assert.Contains("AI image prompt exists but generated image is not available yet.", timeline.RenderReadiness.Warnings);
        Assert.Contains(result.Warnings, warning => warning.Contains("AI image prompt exists but generated image is not available yet."));
    }

    [Fact]
    public async Task GenerateDirectorTimelines_MissingRequiredAudioFailsRenderReadiness()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short");
        await WriteTtsPackageAsync(plan.Id, "PlanetGrouping", "Short", [Segment(1, "Guide", "Follow the planets across the sky.", 8)]);
        var expectedCombinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(expectedCombinedAudio)!);
        await File.WriteAllBytesAsync(expectedCombinedAudio, [1]);
        await WriteManifestAsync(plan.Id, Path.Combine(PlanRoot(plan.Id), "tts", "audio", "missing-combined.wav"), [(1, "scene-01.wav", 8.0)], totalDuration: 8.0);
        await SeedAssetAsync(db, plan.Id, 1, "SkyMapCard", "/visuals/grouping.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var timeline = Assert.Single(result.Timelines);
        Assert.False(timeline.RenderReadiness.ReadyForRender);
        Assert.Equal(1, result.NotReadyCount);
        Assert.Contains(timeline.RenderReadiness.MissingRequiredAssets, item => item.Contains("Missing required combined narration audio"));
    }

    [Fact]
    public async Task GenerateDirectorTimelines_FiltersHistoricalPlansWithoutProductionAudio()
    {
        await using var db = CreateDb();
        var historical = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        var production = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteTtsPackageAsync(historical.Id, "PlanetConjunction", "Short", [Segment(1, "Old", "Old plan without generated audio.", 4)]);
        await WriteTtsPackageAsync(production.Id, "PlanetConjunction", "Short", [Segment(1, "Ready", "Ready plan with generated audio.", 4)]);
        var combinedAudio = Path.Combine(PlanRoot(production.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(production.Id, combinedAudio, [(1, "scene-01.wav", 4.0)], totalDuration: 4.0);
        await SeedAssetAsync(db, production.Id, 1, "SkyMapCard", "/visuals/ready.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, MaxPlans: 20, DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        var timeline = Assert.Single(result.Timelines);
        Assert.Equal(production.Id.ToString("D"), timeline.ContentGenerationPlanId);
    }

    [Fact]
    public async Task GenerateDirectorTimelines_UsesCapturedPngPrimaryAndSscAsTechnicalReference()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteTtsPackageAsync(plan.Id, "PlanetConjunction", "Short", [Segment(1, "Hook", "Find the conjunction.", 5)]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 5.0)], totalDuration: 5.0);
        var sscPath = Path.Combine(PlanRoot(plan.Id), "stellarium", "scene-1.ssc");
        var pngPath = Path.Combine(PlanRoot(plan.Id), "stellarium", "scene-1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(sscPath)!);
        await File.WriteAllTextAsync(sscPath, "// script");
        await File.WriteAllBytesAsync(pngPath, [1]);
        await SeedAssetAsync(db, plan.Id, 1, "StellariumScreenshot", sscPath, metadata: JsonSerializer.Serialize(new { CapturePath = pngPath }));
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var scene = Assert.Single(Assert.Single(result.Timelines).Scenes);
        Assert.Equal(pngPath, scene.PrimaryAsset.Path);
        Assert.False(scene.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase));
        var technical = Assert.Single(scene.TechnicalReferences);
        Assert.Equal("StellariumScriptReference", technical.AssetType);
        Assert.Equal(sscPath, technical.Path);
    }

    [Fact]
    public async Task GenerateDirectorTimelines_ClosingSceneFallsBackToSamePlanVisual()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteTtsPackageAsync(plan.Id, "RareEventAlert", "Short", [
            Segment(1, "Hook", "A rare outburst is possible.", 4),
            Segment(2, "Close", "Step outside and look up.", 4)
        ]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 4.0), (2, "scene-02.wav", 4.0)], totalDuration: 8.0);
        await SeedAssetAsync(db, plan.Id, 1, "TextOverlayCard", "/visuals/opening-overlay.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var closing = Assert.Single(result.Timelines).Scenes.Last();
        Assert.Equal("/visuals/opening-overlay.json", closing.PrimaryAsset.Path);
        Assert.Contains("Fallback visual selected for closing scene.", closing.QualityNotes);
        Assert.Equal(1, result.ReadyForRenderCount);
    }


    [Fact]
    public async Task GenerateDirectorTimelines_SceneWithOnlySscRecoversSamePlanFallbackWithoutPrimarySsc()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteTtsPackageAsync(plan.Id, "PlanetConjunction", "Short", [
            Segment(1, "Opening", "Use the finder card first.", 4),
            Segment(2, "SSC only", "The Stellarium script is technical only.", 4)
        ]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 4.0), (2, "scene-02.wav", 4.0)], totalDuration: 8.0);
        await SeedAssetAsync(db, plan.Id, 1, "TextOverlayCard", "/visuals/opening-overlay.json");
        await SeedAssetAsync(db, plan.Id, 2, "StellariumScreenshot", Path.Combine(PlanRoot(plan.Id), "stellarium", "scene-2.ssc"));
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var timeline = Assert.Single(result.Timelines);
        var recovered = timeline.Scenes.Single(scene => scene.SceneNumber == 2);
        Assert.True(timeline.RenderReadiness.ReadyForRender);
        Assert.Equal(1, result.ReadyForRenderCount);
        Assert.Equal("/visuals/opening-overlay.json", recovered.PrimaryAsset.Path);
        Assert.False(recovered.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Recovered fallback visual for render readiness.", recovered.QualityNotes);
        Assert.Contains("Fallback reused from scene 1.", recovered.QualityNotes);
        Assert.All(timeline.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path)));
        Assert.All(timeline.Scenes, scene => Assert.False(scene.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GenerateDirectorTimelines_WeeklySkyForecastMissingScenesTwoAndFourRecoverUsableVisuals()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "WeeklySkyForecast", "Long");
        await WriteTtsPackageAsync(plan.Id, "WeeklySkyForecast", "Long", [
            Segment(1, "Opening", "The weekly overview starts here.", 3),
            Segment(2, "Missing two", "This night needs a recovered visual.", 3),
            Segment(3, "Middle", "A thumbnail concept is available.", 3),
            Segment(4, "Missing four", "The close also needs a recovered visual.", 3)
        ]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 3.0), (2, "scene-02.wav", 3.0), (3, "scene-03.wav", 3.0), (4, "scene-04.wav", 3.0)], totalDuration: 12.0);
        await SeedAssetAsync(db, plan.Id, 1, "SkyMapCard", "/visuals/week-overview.json");
        await SeedAssetAsync(db, plan.Id, 3, "ThumbnailConcept", "/visuals/week-thumbnail.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var timeline = Assert.Single(result.Timelines);
        Assert.True(timeline.RenderReadiness.ReadyForRender);
        Assert.Equal(1, result.ReadyForRenderCount);
        Assert.Equal(0, result.NotReadyCount);
        var scene2 = timeline.Scenes.Single(scene => scene.SceneNumber == 2);
        var scene4 = timeline.Scenes.Single(scene => scene.SceneNumber == 4);
        Assert.False(string.IsNullOrWhiteSpace(scene2.PrimaryAsset.Path));
        Assert.False(string.IsNullOrWhiteSpace(scene4.PrimaryAsset.Path));
        Assert.Contains("Recovered fallback visual for render readiness.", scene2.QualityNotes);
        Assert.Contains("Recovered fallback visual for render readiness.", scene4.QualityNotes);
        Assert.All(timeline.Scenes, scene => Assert.False(scene.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GenerateDirectorTimelines_PlanetGroupingSceneThreeRecoversAiPromptPlannedVisual()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short");
        await WriteTtsPackageAsync(plan.Id, "PlanetGrouping", "Short", [
            Segment(1, "Opening", "Start with the grouping card.", 3),
            Segment(2, "Finder", "Use the finder map.", 3),
            Segment(3, "Missing", "This scene should reuse an AI prompt.", 3)
        ]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 3.0), (2, "scene-02.wav", 3.0), (3, "scene-03.wav", 3.0)], totalDuration: 9.0);
        await SeedAssetAsync(db, plan.Id, 1, "AiImagePrompt", string.Empty, prompt: "wide field planet grouping over Udaipur");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var timeline = Assert.Single(result.Timelines);
        var scene3 = timeline.Scenes.Single(scene => scene.SceneNumber == 3);
        Assert.True(timeline.RenderReadiness.ReadyForRender);
        Assert.Equal("PlannedVisual", scene3.PrimaryAsset.AssetType);
        Assert.Equal("wide field planet grouping over Udaipur", scene3.PrimaryAsset.Path);
        Assert.Contains("Recovered fallback visual for render readiness.", scene3.QualityNotes);
        Assert.All(timeline.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path)));
        Assert.All(timeline.Scenes, scene => Assert.False(scene.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase)));
    }


    [Fact]
    public async Task GenerateDirectorTimelines_RecoversEightProductionTimelinesReadyForRender()
    {
        await using var db = CreateDb();
        var categories = new[]
        {
            "WeeklySkyForecast",
            "PlanetGrouping",
            "PlanetConjunction",
            "RareEventAlert",
            "WeeklySkyForecast",
            "PlanetGrouping",
            "PlanetConjunction",
            "RareEventAlert"
        };

        foreach (var category in categories)
        {
            var plan = await SeedPlanAsync(db, category, category == "WeeklySkyForecast" ? "Long" : "Short");
            await WriteTtsPackageAsync(plan.Id, category, plan.PlannedFormat!, [
                Segment(1, "Opening", "A production-ready visual is available.", 3),
                Segment(2, "Recovered", "This scene recovers a fallback visual.", 3)
            ]);
            var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
            await File.WriteAllBytesAsync(combinedAudio, [1]);
            await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 3.0), (2, "scene-02.wav", 3.0)], totalDuration: 6.0);
            await SeedAssetAsync(db, plan.Id, 1, "SkyMapCard", $"/visuals/{plan.Id:D}-scene-1.json");
            await SeedAssetAsync(db, plan.Id, 2, "StellariumScreenshot", Path.Combine(PlanRoot(plan.Id), "stellarium", "scene-2.ssc"));
        }

        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, MaxPlans: 20, DryRun: true), CancellationToken.None);

        Assert.Equal(8, result.PlanCount);
        Assert.Equal(8, result.GeneratedCount);
        Assert.Equal(8, result.ReadyForRenderCount);
        Assert.Equal(0, result.NotReadyCount);
        Assert.All(result.Timelines, timeline =>
        {
            Assert.True(timeline.RenderReadiness.ReadyForRender);
            Assert.All(timeline.Scenes, scene =>
            {
                Assert.False(string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType));
                Assert.False(string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path));
                Assert.False(scene.PrimaryAsset.Path.EndsWith(".ssc", StringComparison.OrdinalIgnoreCase));
            });
        });
    }

    [Fact]
    public async Task GenerateDirectorTimelines_DryRunDoesNotWriteOrRenderVideo()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "WeeklySkyForecast", "Long");
        await WriteTtsPackageAsync(plan.Id, "WeeklySkyForecast", "Long", [Segment(1, "Opening", "This week brings a parade of night sky sights.", 6)]);
        var combinedAudio = Path.Combine(PlanRoot(plan.Id), "tts", "audio", "narration-combined.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAudio)!);
        await File.WriteAllBytesAsync(combinedAudio, [1]);
        await WriteManifestAsync(plan.Id, combinedAudio, [(1, "scene-01.wav", 6.0)], totalDuration: 6.0);
        await SeedAssetAsync(db, plan.Id, 1, "TextOverlayCard", "/visuals/opening-overlay.json");
        var service = CreateService(db);

        var result = await service.GenerateDirectorTimelinesAsync(new DirectorTimelineRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        Assert.Empty(result.GeneratedFiles);
        Assert.False(File.Exists(Path.Combine(PlanRoot(plan.Id), "timeline", "director-timeline.json")));
        Assert.False(Directory.EnumerateFiles(_outputRoot, "*.mp4", SearchOption.AllDirectories).Any());
    }

    private MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private DirectorTimelineService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }), NullLogger<DirectorTimelineService>.Instance);

    private async Task<ContentGenerationPlan> SeedPlanAsync(MediaFactoryDbContext db, string category, string format)
    {
        var plan = new ContentGenerationPlan
        {
            ContentCategoryCode = category,
            RegionId = RegionId,
            PlannedFormat = format,
            Title = $"{category} title",
            ScheduledUtc = DateTimeOffset.UtcNow,
            AstronomyEventIntelligenceId = Guid.NewGuid()
        };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedAssetAsync(MediaFactoryDbContext db, Guid planId, int scene, string type, string path, string? prompt = null, string? metadata = null)
    {
        db.AstronomyAssetProductionJobs.Add(new AstronomyAssetProductionJob
        {
            ContentGenerationPlanId = planId,
            SceneNumber = scene,
            SceneName = $"Scene {scene}",
            AssetType = type,
            AssetPurpose = $"Use {type} for scene {scene}",
            PlannedProvider = "Test",
            Priority = 10,
            Status = AstronomyAssetProductionJobStatuses.Completed,
            OutputPath = path,
            PromptOrInstruction = prompt,
            MetadataJson = metadata
        });
        await db.SaveChangesAsync();
    }

    private async Task WriteTtsPackageAsync(Guid planId, string category, string format, IReadOnlyList<TtsPackageSegment> segments)
    {
        var package = new FinalTtsPackageDocument(
            planId.ToString("D"),
            RegionId,
            "en",
            category,
            format,
            $"{category} title",
            "AzureSpeech",
            new TtsVoiceProfile("test", "en-US-DavisNeural", "documentary", "neutral", "-3%", "medium"),
            new TtsMusicProfile("wonder", "low", "ambient"),
            segments,
            segments.Sum(s => s.EstimatedDurationSeconds),
            true,
            "Phase9B.3",
            DateTimeOffset.UtcNow,
            "Valid",
            DateTimeOffset.UtcNow,
            true,
            "AlreadyValid",
            DateTimeOffset.UtcNow,
            segments.Select(s => new TtsSegmentValidationResult(s.SceneNumber, true, [], [])).ToList());
        var path = Path.Combine(PlanRoot(planId), "tts", "tts-package-final.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private async Task WriteManifestAsync(Guid planId, string combinedAudioPath, IReadOnlyList<(int Scene, string AudioPath, double Duration)> scenes, double totalDuration)
    {
        var manifest = new TtsAudioManifest(
            planId.ToString("D"),
            RegionId,
            "en-US-DavisNeural",
            "AzureSpeech",
            scenes.Select(s => new TtsAudioManifestSegment(s.Scene, s.AudioPath, s.Duration, 100, "Completed")).ToList(),
            combinedAudioPath,
            totalDuration,
            DateTimeOffset.UtcNow);
        var path = Path.Combine(PlanRoot(planId), "tts", "audio", "tts-audio-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private TtsPackageSegment Segment(int scene, string name, string text, int estimatedSeconds)
        => new(scene, name, text, $"<speak>{text}</speak>", estimatedSeconds, [], [], null, Path.Combine(PlanRoot(Guid.Empty), "tts", "audio", $"scene-{scene:00}.wav"));

    private string PlanRoot(Guid planId)
        => Path.Combine(_outputRoot, "assets", RegionId, "plans", planId.ToString("D"));

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
            Directory.Delete(_outputRoot, recursive: true);
    }
}
