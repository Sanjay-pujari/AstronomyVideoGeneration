using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class RenderRecipeGeneratorTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "render-recipe-generator-tests", Guid.NewGuid().ToString("N"));
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateRenderRecipes_ReadsSceneAssemblyPlanAndCreatesRecipePerScene()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteAssemblyPlanAsync(plan, Assembly(plan, [
            Scene(1, "Opening", 0, 4.5, "/audio/scene-01.wav", [
                new SceneAssemblyLayer("BackgroundVisual", "StellariumScreenshot", "/visuals/scene-1.png", "image", "cover", 1.0, null, 0),
                new SceneAssemblyLayer("Overlay", "TextOverlayCard", "/visuals/title-card.json", "overlay_card", null, null, "center-safe", 10)
            ], "zoom_in_subtle"),
            Scene(2, "Finder", 4.5, 10, "/audio/scene-02.wav", [
                new SceneAssemblyLayer("BackgroundVisual", "SkyMapCard", "/visuals/finder.json", "json_visual_card", null, null, null, 0),
                new SceneAssemblyLayer("PlannedVisual", "PlannedVisual", "/visuals/planned-image-prompt.json", "planned_visual_prompt", null, null, null, 10)
            ], "zoom_in_focus")
        ]));
        var service = CreateService(db);

        var result = await service.GenerateRenderRecipesAsync(new RenderRecipeRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(2, result.RecipeCount);
        Assert.Equal(2, result.ReadyForExecutionCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.All(result.Recipes, recipe =>
        {
            Assert.Equal("ffmpeg", recipe.Renderer);
            Assert.Equal("Phase9E.1", recipe.GenerationSource);
            Assert.True(recipe.ExecutionReadiness.ReadyForRenderExecution);
            Assert.Contains(recipe.Inputs, input => input.InputType == "audio" && input.AssetType == "NarrationSegment" && input.Role == "scene_narration");
            Assert.Contains(recipe.Inputs, input => input.InputType is "visual" or "planned_visual");
        });
        Assert.Contains(result.Recipes[0].Inputs, input => input.InputType == "visual" && input.Role == "background" && input.RenderMode == "image");
        Assert.Contains(result.Recipes[0].Inputs, input => input.Role == "overlay" && input.RenderMode == "overlay_card");
        Assert.Equal("kenburns_zoom_in", result.Recipes[0].Motion.FilterHint);
        Assert.Contains(result.Recipes[1].ExecutionReadiness.Warnings, warning => warning == "PlannedVisual requires placeholder renderer or generated image before final production.");
        Assert.Contains(result.Recipes[1].ExecutionReadiness.Warnings, warning => warning == "JSON card renderer required.");
        Assert.False(Directory.Exists(Path.Combine(PlanRoot(plan.Id), "render-recipes")));
        Assert.False(Directory.EnumerateFiles(_outputRoot, "*.mp4", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task GenerateRenderRecipes_WritesRecipeFilesWithoutRenderingVideo()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteAssemblyPlanAsync(plan, Assembly(plan, [
            Scene(1, "Alert", 0, 6, "/audio/scene-01.wav", [new SceneAssemblyLayer("PlannedVisual", "PlannedVisual", "/visuals/meteor-prompt.json", "planned_visual_prompt", null, null, null, 0)], "hold")
        ]));
        var service = CreateService(db);

        var result = await service.GenerateRenderRecipesAsync(new RenderRecipeRequest(RegionId: RegionId, DryRun: false), CancellationToken.None);

        var file = Assert.Single(result.GeneratedFiles);
        Assert.Equal(Path.Combine(PlanRoot(plan.Id), "render-recipes", "scene-001.recipe.json"), file);
        Assert.True(File.Exists(file));
        var json = await File.ReadAllTextAsync(file);
        Assert.Contains("readyForRenderExecution", json);
        Assert.Contains("static_hold", json);
        Assert.False(Directory.EnumerateFiles(PlanRoot(plan.Id), "*.mp4", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task GenerateRenderRecipes_MissingSceneInputsAreNotReady()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short");
        await WriteAssemblyPlanAsync(plan, Assembly(plan, [
            Scene(1, "Broken", 0, 0, string.Empty, [], "hold", outputPath: string.Empty)
        ]));
        var service = CreateService(db);

        var result = await service.GenerateRenderRecipesAsync(new RenderRecipeRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        Assert.Equal(0, result.ReadyForExecutionCount);
        var recipe = Assert.Single(result.Recipes);
        Assert.False(recipe.ExecutionReadiness.ReadyForRenderExecution);
        Assert.Contains("missing scene audio", recipe.ExecutionReadiness.BlockingIssues);
        Assert.Contains("missing output path", recipe.ExecutionReadiness.BlockingIssues);
        Assert.Contains("no visual or planned visual input", recipe.ExecutionReadiness.BlockingIssues);
        Assert.Contains("duration <= 0", recipe.ExecutionReadiness.BlockingIssues);
        Assert.Contains(recipe.Inputs, input => input.InputType == "audio");
    }

    private MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private RenderRecipeGenerator CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }), NullLogger<RenderRecipeGenerator>.Instance);

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

    private SceneAssemblyScene Scene(
        int number,
        string name,
        double start,
        double end,
        string audioPath,
        IReadOnlyList<SceneAssemblyLayer> layers,
        string motionType,
        string? outputPath = null)
        => new(
            number,
            name,
            start,
            end,
            end - start,
            outputPath ?? Path.Combine(PlanRoot(Guid.Empty), "assembly", "scenes", $"scene-{number:000}.mp4"),
            audioPath,
            layers,
            new SceneAssemblyMotion(motionType, "subtle", "center_focus", 1.0, 1.08),
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
