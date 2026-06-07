using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class SceneAssemblyPlanServiceTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "scene-assembly-plan-tests", Guid.NewGuid().ToString("N"));
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateSceneAssemblyPlans_ReadsDirectorTimelineAndCreatesRendererReadyScenes()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        var timeline = Timeline(plan, [
            Scene(1, "Opening", 0, 4.5, "slow push-in", "/audio/scene-01.wav", new DirectorTimelineAsset("StellariumScreenshot", "/visuals/scene-1.png", "Primary sky capture"), [new DirectorTimelineAsset("TextOverlayCard", "/visuals/title-card.json", "Title overlay")], [new DirectorTimelineAsset("StellariumScriptReference", "/scripts/scene-1.ssc", "Regeneration script")], "/visuals/title-card.json"),
            Scene(2, "Finder", 4.5, 10, "slow zoom toward pairing", "/audio/scene-02.wav", new DirectorTimelineAsset("SkyMapCard", "/visuals/finder.json", "Finder card"), [new DirectorTimelineAsset("PlannedVisual", "/visuals/planned-image-prompt.json", "Needs generated image")], [], string.Empty)
        ]);
        await WriteTimelineAsync(plan.Id, timeline);
        var service = CreateService(db);

        var result = await service.GenerateSceneAssemblyPlansAsync(new SceneAssemblyPlanRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(1, result.GeneratedCount);
        Assert.Equal(1, result.ReadyForSceneRenderCount);
        Assert.Empty(result.GeneratedFiles);
        var assembly = Assert.Single(result.AssemblyPlans);
        Assert.True(assembly.RenderReadiness.ReadyForSceneRender);
        Assert.Equal("Phase9E.0", assembly.GenerationSource);
        Assert.Equal("16:9", assembly.OutputAspectRatio);
        Assert.Equal(1920, assembly.OutputResolution.Width);
        Assert.Equal(1080, assembly.OutputResolution.Height);
        Assert.Equal(30, assembly.FrameRate);
        Assert.Equal("/audio/combined.wav", assembly.Audio.CombinedNarrationPath);
        Assert.All(assembly.Scenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.OutputSceneVideoPath));
            Assert.EndsWith($"scene-{scene.SceneNumber:000}.mp4", scene.OutputSceneVideoPath);
            Assert.False(string.IsNullOrWhiteSpace(scene.AudioPath));
            Assert.NotEmpty(scene.Layers);
        });
        Assert.Contains(assembly.Scenes[0].Layers, layer => layer.LayerType == "BackgroundVisual" && layer.RenderMode == "image");
        Assert.Contains(assembly.Scenes[0].Layers, layer => layer.LayerType == "Overlay" && layer.RenderMode == "overlay_card");
        Assert.Contains(assembly.Scenes[0].RenderNotes, note => note.Contains("Technical reference", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("zoom_in_subtle", assembly.Scenes[0].Motion.Type);
        Assert.Equal("zoom_in_focus", assembly.Scenes[1].Motion.Type);
        Assert.Contains(assembly.RenderReadiness.Warnings, warning => warning.Contains("PlannedVisual requires actual image generation later", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.EnumerateFiles(_outputRoot, "*.mp4", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task GenerateSceneAssemblyPlans_WritesAssemblyPlanWithoutRenderingVideo()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteTimelineAsync(plan.Id, Timeline(plan, [
            Scene(1, "Alert", 0, 6, "closing hold", "/audio/scene-01.wav", new DirectorTimelineAsset("PlannedVisual", "/visuals/meteor-prompt.json", "AI prompt"), [], [], string.Empty)
        ]));
        var service = CreateService(db);

        var result = await service.GenerateSceneAssemblyPlansAsync(new SceneAssemblyPlanRequest(RegionId: RegionId, DryRun: false), CancellationToken.None);

        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(Path.Combine(PlanRoot(plan.Id), "assembly", "scene-assembly-plan.json"), file);
        Assert.True(File.Exists(file));
        Assert.False(Directory.EnumerateFiles(Path.Combine(PlanRoot(plan.Id), "assembly"), "*.mp4", SearchOption.AllDirectories).Any());
        var json = await File.ReadAllTextAsync(file);
        Assert.Contains("scene-001.mp4", json);
        Assert.Contains("readyForSceneRender", json);
    }

    [Fact]
    public async Task GenerateSceneAssemblyPlans_MissingSceneInputsAreNotReady()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short");
        await WriteTimelineAsync(plan.Id, Timeline(plan, [
            Scene(1, "Broken", 0, 0, "hold", string.Empty, new DirectorTimelineAsset(string.Empty, string.Empty, string.Empty), [], [], string.Empty)
        ]));
        var service = CreateService(db);

        var result = await service.GenerateSceneAssemblyPlansAsync(new SceneAssemblyPlanRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ReadyForSceneRenderCount);
        var assembly = Assert.Single(result.AssemblyPlans);
        Assert.False(assembly.RenderReadiness.ReadyForSceneRender);
        Assert.Contains(assembly.RenderReadiness.MissingInputs, missing => missing.Contains("audioPath", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assembly.RenderReadiness.MissingInputs, missing => missing.Contains("durationSeconds", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assembly.RenderReadiness.MissingInputs, missing => missing.Contains("BackgroundVisual", StringComparison.OrdinalIgnoreCase));
    }

    private MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private SceneAssemblyPlanService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }), NullLogger<SceneAssemblyPlanService>.Instance);

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

    private async Task WriteTimelineAsync(Guid planId, DirectorTimelineDocument timeline)
    {
        var path = Path.Combine(PlanRoot(planId), "timeline", "director-timeline.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(timeline, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static DirectorTimelineScene Scene(
        int number,
        string name,
        double start,
        double end,
        string motion,
        string audioPath,
        DirectorTimelineAsset primaryAsset,
        IReadOnlyList<DirectorTimelineAsset> secondaryAssets,
        IReadOnlyList<DirectorTimelineAsset> technicalReferences,
        string overlayPath)
        => new(
            number,
            name,
            start,
            end,
            end - start,
            $"Narration for {name}.",
            audioPath,
            primaryAsset,
            secondaryAssets,
            technicalReferences,
            new DirectorTimelineOverlayPlan(overlayPath, "center-safe", true),
            motion,
            number == 1 ? "fade from black" : "soft crossfade",
            "soft crossfade",
            "cinematic",
            "ambient",
            []);

    private static DirectorTimelineDocument Timeline(ContentGenerationPlan plan, IReadOnlyList<DirectorTimelineScene> scenes)
        => new(
            plan.Id.ToString("D"),
            RegionId,
            plan.ContentCategoryCode,
            plan.PlannedFormat ?? string.Empty,
            plan.Title ?? string.Empty,
            scenes.Sum(scene => scene.DurationSeconds),
            new DirectorTimelineAudio("/audio/combined.wav", scenes.Sum(scene => scene.DurationSeconds), "en-US-DavisNeural", "wonder", "low"),
            scenes,
            new DirectorTimelineRenderReadiness(true, [], []),
            "Phase9D",
            DateTimeOffset.UtcNow);

    private string PlanRoot(Guid planId)
        => Path.Combine(_outputRoot, "assets", RegionId, "plans", planId.ToString("D"));

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
            Directory.Delete(_outputRoot, recursive: true);
    }
}
