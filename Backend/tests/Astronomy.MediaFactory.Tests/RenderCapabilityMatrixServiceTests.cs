using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class RenderCapabilityMatrixServiceTests : IDisposable
{
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), "render-capability-matrix-tests", Guid.NewGuid().ToString("N"));
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateRenderCapabilities_ReadsAllRecipesAndWritesCapabilityPerRecipeWithoutRendering()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetConjunction", "Short");
        await WriteRecipeAsync(plan.Id, Recipe(plan, 1, "Opening", [Input("visual", "TextOverlayCard", "/visuals/overlay.json", "overlay_card", "overlay")], "kenburns_zoom_in"));
        await WriteRecipeAsync(plan.Id, Recipe(plan, 2, "Finder", [
            Input("visual", "SkyMapCard", "/visuals/finder.json", "json_visual_card", "background"),
            Input("planned_visual", "PlannedVisual", "/visuals/prompt.json", "planned_visual_prompt", "planned_or_placeholder")
        ], "kenburns_zoom_in_focus"));
        var service = CreateService(db);

        var result = await service.GenerateRenderCapabilitiesAsync(new RenderCapabilityMatrixRequest(RegionId: RegionId, DryRun: false), CancellationToken.None);

        Assert.Equal(1, result.PlanCount);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(2, result.CapabilityCount);
        Assert.Equal(2, result.CanExecuteCount);
        Assert.Equal(0, result.BlockedCount);
        Assert.Equal(2, result.GeneratedFiles.Count);
        Assert.All(result.GeneratedFiles, file => Assert.True(File.Exists(file)));
        Assert.Contains(result.Capabilities[0].RequiredHandlers, handler => handler.RenderMode == "overlay_card" && handler.Handler == "JsonOverlayCardRenderer" && handler.Available);
        Assert.Contains(result.Capabilities[1].RequiredHandlers, handler => handler.RenderMode == "json_visual_card" && handler.Handler == "JsonVisualCardRenderer" && handler.Available);
        Assert.Contains(result.Capabilities[1].RequiredHandlers, handler => handler.RenderMode == "planned_visual_prompt" && handler.Handler == "PlaceholderVisualRenderer" && handler.Available);
        Assert.Contains(result.Capabilities[1].ExecutionPlan.Warnings, warning => warning == "Planned visual will render as placeholder until generated image is available.");
        Assert.Contains(result.Capabilities[1].ExecutionPlan.Warnings, warning => warning == "JSON visual card renderer must create visual frame before FFmpeg execution.");
        Assert.False(Directory.EnumerateFiles(PlanRoot(plan.Id), "*.mp4", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task GenerateRenderCapabilities_DryRunReturnsPreviewsWithoutWritingFiles()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "NasaAssetStory", "Short");
        await WriteRecipeAsync(plan.Id, Recipe(plan, 1, "Image", [
            Input("visual", "Image", "/visuals/scene.png", "image", "background"),
            Input("visual", "NasaAsset", "/visuals/nasa.json", "metadata_visual_card", "supporting")
        ], "parallax_soft"));
        var service = CreateService(db);

        var result = await service.GenerateRenderCapabilitiesAsync(new RenderCapabilityMatrixRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var capability = Assert.Single(result.Capabilities);
        Assert.Empty(result.GeneratedFiles);
        Assert.False(Directory.Exists(Path.Combine(PlanRoot(plan.Id), "render-capabilities")));
        Assert.True(capability.ExecutionPlan.CanExecute);
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "image" && handler.Handler == "ImageVisualRenderer");
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "metadata_visual_card" && handler.Handler == "MetadataVisualCardRenderer");
        Assert.Contains(capability.ExecutionPlan.Warnings, warning => warning == "Metadata visual card renderer must create visual frame before FFmpeg execution.");
        Assert.Equal("ParallaxSoftMotionRenderer", capability.MotionHandler.Handler);
    }

    [Fact]
    public async Task GenerateRenderCapabilities_AllKnownRenderModesMapToHandlersWithoutBlocking()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "PlanetGrouping", "Short");
        await WriteRecipeAsync(plan.Id, Recipe(plan, 1, "Mappings", [
            Input("visual", "TextOverlayCard", "/visuals/overlay.json", "overlay_card", "overlay"),
            Input("visual", "SkyMapCard", "/visuals/card.json", "json_visual_card", "supporting"),
            Input("visual", "NasaAsset", "/visuals/metadata.json", "metadata_visual_card", "supporting"),
            Input("planned_visual", "PlannedVisual", "/visuals/prompt.json", "planned_visual_prompt", "planned_or_placeholder"),
            Input("visual", "Image", "/visuals/scene.jpg", "image", "background")
        ], "static_hold"));
        var service = CreateService(db);

        var result = await service.GenerateRenderCapabilitiesAsync(new RenderCapabilityMatrixRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var capability = Assert.Single(result.Capabilities);
        Assert.True(capability.ExecutionPlan.CanExecute);
        Assert.Equal(0, result.BlockedCount);
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "overlay_card" && handler.Handler == "JsonOverlayCardRenderer");
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "json_visual_card" && handler.Handler == "JsonVisualCardRenderer");
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "metadata_visual_card" && handler.Handler == "MetadataVisualCardRenderer");
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "planned_visual_prompt" && handler.Handler == "PlaceholderVisualRenderer");
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "image" && handler.Handler == "ImageVisualRenderer");
        Assert.Equal("SceneAudioMuxer", capability.AudioHandler.Handler);
        Assert.True(capability.AudioHandler.Available);
        Assert.Equal("CinematicCaptionRenderer", capability.CaptionHandler.Handler);
        Assert.Equal("SceneTransitionPlanner", capability.TransitionHandler.Handler);
    }

    [Fact]
    public async Task GenerateRenderCapabilities_UnknownMotionUsesDefaultHandlerAndWarning()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteRecipeAsync(plan.Id, Recipe(plan, 1, "Unknown motion", [Input("visual", "Image", "/visuals/scene.png", "image", "background")], "orbit_spin"));
        var service = CreateService(db);

        var result = await service.GenerateRenderCapabilitiesAsync(new RenderCapabilityMatrixRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var capability = Assert.Single(result.Capabilities);
        Assert.True(capability.ExecutionPlan.CanExecute);
        Assert.Equal("DefaultSubtleMotionRenderer", capability.MotionHandler.Handler);
        Assert.Contains("Unknown motion filter hint mapped to default subtle motion.", capability.ExecutionPlan.Warnings);
    }

    [Fact]
    public async Task GenerateRenderCapabilities_UnsupportedRenderModeBlocksExecution()
    {
        await using var db = CreateDb();
        var plan = await SeedPlanAsync(db, "RareEventAlert", "Short");
        await WriteRecipeAsync(plan.Id, Recipe(plan, 1, "Unsupported", [Input("visual", "Mystery", "/visuals/mystery.bin", "unsupported_mode", "background")], "static_hold"));
        var service = CreateService(db);

        var result = await service.GenerateRenderCapabilitiesAsync(new RenderCapabilityMatrixRequest(RegionId: RegionId, DryRun: true), CancellationToken.None);

        var capability = Assert.Single(result.Capabilities);
        Assert.False(capability.ExecutionPlan.CanExecute);
        Assert.Equal(1, result.BlockedCount);
        Assert.Contains(capability.RequiredHandlers, handler => handler.RenderMode == "unsupported_mode" && !handler.Available && handler.Handler == string.Empty);
        Assert.Contains("unsupported renderMode with no fallback: unsupported_mode", capability.ExecutionPlan.BlockingIssues);
    }

    private MediaFactoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaFactoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new MediaFactoryDbContext(options);
    }

    private RenderCapabilityMatrixService CreateService(MediaFactoryDbContext db)
        => new(db, Options.Create(new RenderingOptions { WorkingDirectory = _outputRoot }), NullLogger<RenderCapabilityMatrixService>.Instance);

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

    private async Task WriteRecipeAsync(Guid planId, RenderRecipeDocument recipe)
    {
        var path = Path.Combine(PlanRoot(planId), "render-recipes", $"scene-{recipe.SceneNumber:000}.recipe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(recipe, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private RenderRecipeDocument Recipe(ContentGenerationPlan plan, int sceneNumber, string sceneName, IReadOnlyList<RenderRecipeInput> visualInputs, string motionFilterHint)
    {
        var inputs = visualInputs.Concat([new RenderRecipeInput("audio", "NarrationSegment", $"/audio/scene-{sceneNumber:000}.wav", null, null, "scene_narration")]).ToArray();
        return new RenderRecipeDocument(
            plan.Id.ToString("D"),
            RegionId,
            plan.ContentCategoryCode,
            plan.PlannedFormat ?? string.Empty,
            sceneNumber,
            sceneName,
            "ffmpeg",
            6,
            30,
            new RenderRecipeResolution(1920, 1080),
            Path.Combine(PlanRoot(plan.Id), "assembly", "scenes", $"scene-{sceneNumber:000}.mp4"),
            inputs,
            new RenderRecipeMotion(motionFilterHint, 1.0, 1.08, "center_focus", motionFilterHint),
            new RenderRecipeCaptions(true, "narrationText", "lower-third-safe", "cinematic_subtitle"),
            new RenderRecipeTransition(sceneNumber == 1 ? "fade from black" : "soft crossfade", "soft crossfade", 0.5),
            [new RenderRecipeFilter("kenburns", true)],
            new RenderRecipeExecutionReadiness(true, [], []),
            "Phase9E.1",
            DateTimeOffset.UtcNow);
    }

    private static RenderRecipeInput Input(string inputType, string assetType, string assetPath, string renderMode, string role)
        => new(inputType, assetType, assetPath, renderMode, 0, role);

    private string PlanRoot(Guid planId)
        => Path.Combine(_outputRoot, "assets", RegionId, "plans", planId.ToString("D"));

    public void Dispose()
    {
        if (Directory.Exists(_outputRoot))
            Directory.Delete(_outputRoot, recursive: true);
    }
}
