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
            ScheduledUtc = DateTimeOffset.UtcNow
        };
        db.ContentGenerationPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private async Task SeedAssetAsync(MediaFactoryDbContext db, Guid planId, int scene, string type, string path, string? prompt = null)
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
            PromptOrInstruction = prompt
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
