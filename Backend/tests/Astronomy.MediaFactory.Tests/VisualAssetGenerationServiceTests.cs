using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class VisualAssetGenerationServiceTests : IDisposable
{
    private const string RegionId = "IN-RJ-UDAIPUR";
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "visual-asset-generation-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateVisualAssets_DryRun_UsesAstronomyBackgroundsBeforeTextOverlaysForRareEventPilot()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteAssemblyPlanAsync(plan, Assembly(plan, [
            Scene(1, "Immediate hook", [
                new SceneAssemblyLayer("Overlay", "TextOverlayCard", "text-cards/scene-001-text.json", "overlay_card", null, null, "center-safe", 10)
            ]),
            Scene(2, "What to watch", [
                new SceneAssemblyLayer("Overlay", "TextOverlayCard", "text-cards/scene-002-text.json", "overlay_card", null, null, "center-safe", 10)
            ]),
            Scene(3, "Viewing guidance", [
                new SceneAssemblyLayer("Overlay", "TextOverlayCard", "text-cards/scene-003-text.json", "overlay_card", null, null, "center-safe", 10)
            ]),
            Scene(4, "Close", [])
        ]));
        await WriteJsonAsync(plan.Id, "ai-image-prompts/scene-001-cinematic-prompt.json", new { title = "Immediate hook", subtitle = "Cinematic sky event hero", keyMessage = "Look west after sunset." });
        await WriteJsonAsync(plan.Id, "ai-image-prompts/scene-004-cinematic-prompt.json", new { title = "Close", subtitle = "Cinematic closing sky", keyMessage = "Share the viewing window." });
        await WriteJsonAsync(plan.Id, "sky-map-cards/scene-002-sky-map.json", new { title = "What to watch", subtitle = "Clean western horizon map", keyMessage = "Track the event near Venus." });
        await WriteJsonAsync(plan.Id, "constellation-guides/scene-003-guide.json", new { title = "Viewing guidance", subtitle = "Guide stars frame the view", keyMessage = "Use nearby bright stars." });
        await WriteJsonAsync(plan.Id, "text-cards/scene-001-text.json", new { title = "Tonight", subtitle = "Do not miss it", keyMessage = "Set a reminder." });
        await WriteJsonAsync(plan.Id, "text-cards/scene-002-text.json", new { title = "Map note", subtitle = "Use binoculars", keyMessage = "Keep the horizon clear." });
        await WriteJsonAsync(plan.Id, "text-cards/scene-003-text.json", new { title = "Viewing tip", subtitle = "Find a dark site", keyMessage = "Avoid buildings." });
        var service = CreateService(db);

        var result = await service.GenerateVisualAssetsAsync(new VisualAssetGenerationRequest(RegionId, [plan.Id], MaxPlans: 1, DryRun: true, OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(4, result.SceneCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(Directory.Exists(Path.Combine(PlanRoot(plan.Id), "visual-assets")));
        Assert.Collection(result.PlannedVisualOutputs.OrderBy(x => x.SceneNumber),
            scene =>
            {
                Assert.Equal(1, scene.SceneNumber);
                Assert.Equal("AiPromptVisual", scene.VisualSourceType);
                Assert.EndsWith("scene-001-cinematic-prompt.json", scene.SourcePath);
                Assert.NotEmpty(scene.OverlayPath);
                Assert.Empty(scene.Issues);
            },
            scene =>
            {
                Assert.Equal(2, scene.SceneNumber);
                Assert.Equal("SkyMapVisual", scene.VisualSourceType);
                Assert.EndsWith("scene-002-sky-map.json", scene.SourcePath);
                Assert.Empty(scene.Issues);
            },
            scene =>
            {
                Assert.Equal(3, scene.SceneNumber);
                Assert.Equal("SkyMapVisual", scene.VisualSourceType);
                Assert.EndsWith("scene-002-sky-map.json", scene.SourcePath);
                Assert.EndsWith("scene-003-overlay.png", scene.OverlayPath);
                Assert.Empty(scene.Issues);
            },
            scene =>
            {
                Assert.Equal(4, scene.SceneNumber);
                Assert.Equal("AiPromptVisual", scene.VisualSourceType);
                Assert.EndsWith("scene-004-cinematic-prompt.json", scene.SourcePath);
                Assert.Empty(scene.Issues);
            });
    }

    [Fact]
    public async Task GenerateVisualAssets_DryRun_FailsAiPromptApprovalWhenPromptJsonIsMissing()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteAssemblyPlanAsync(plan, Assembly(plan, [Scene(1, "Immediate hook", [])]));
        var service = CreateService(db);

        var result = await service.GenerateVisualAssetsAsync(new VisualAssetGenerationRequest(RegionId, [plan.Id], MaxPlans: 1, DryRun: true, OverwriteExisting: false), CancellationToken.None);

        var scene = Assert.Single(result.PlannedVisualOutputs);
        Assert.Equal("AiPromptVisual", scene.VisualSourceType);
        Assert.Equal(string.Empty, scene.SourcePath);
        Assert.Contains(scene.Issues, issue => issue.Contains("sourcePath is required", StringComparison.OrdinalIgnoreCase));
    }

    private MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private VisualAssetGenerationService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }), NullLogger<VisualAssetGenerationService>.Instance);

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

    private async Task WriteAssemblyPlanAsync(ContentGenerationPlan plan, SceneAssemblyPlanDocument assembly)
    {
        var path = Path.Combine(PlanRoot(plan.Id), "assembly", "scene-assembly-plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(assembly, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private async Task WriteJsonAsync(Guid planId, string relativePath, object value)
    {
        var path = Path.Combine(PlanRoot(planId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private SceneAssemblyScene Scene(int number, string name, IReadOnlyList<SceneAssemblyLayer> layers)
        => new(
            number,
            name,
            (number - 1) * 4,
            number * 4,
            4,
            Path.Combine(PlanRoot(Guid.Empty), "assembly", "scenes", $"scene-{number:000}.mp4"),
            $"/audio/scene-{number:000}.wav",
            layers,
            new SceneAssemblyMotion("hold", "subtle", "center_focus", 1.0, 1.08),
            new SceneAssemblyTransition(number == 1 ? "fade from black" : "soft crossfade", "soft crossfade", 0.5),
            new SceneAssemblyCaptions(true, "narrationText", "lower-third-safe", "cinematic_subtitle"),
            []);

    private static SceneAssemblyPlanDocument Assembly(ContentGenerationPlan plan, IReadOnlyList<SceneAssemblyScene> scenes)
        => new(
            plan.Id.ToString("D"),
            RegionId,
            plan.ContentCategoryCode,
            plan.PlannedFormat ?? string.Empty,
            plan.Title ?? string.Empty,
            "16:9",
            new SceneAssemblyResolution(1920, 1080),
            30,
            scenes.Sum(scene => scene.DurationSeconds),
            new SceneAssemblyAudio("/audio/combined.wav", "wonder", "low", true),
            scenes,
            new SceneAssemblyRenderReadiness(true, [], []),
            "Phase9E.0",
            DateTimeOffset.UtcNow);

    private string PlanRoot(Guid planId)
        => Path.Combine(_outputRoot, "assets", RegionId, "plans", planId.ToString("D"));

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
            Directory.Delete(_outputRoot, recursive: true);
    }
}
