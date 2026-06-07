using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class RenderRecipeGenerator(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<RenderRecipeGenerator> logger) : IRenderRecipeGenerator
{
    private const string GenerationSource = "Phase9E.1";
    private const string AssemblyFileName = "scene-assembly-plan.json";
    private const string RecipeDirectoryName = "render-recipes";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RenderRecipeResult> GenerateRenderRecipesAsync(RenderRecipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var candidates = await ResolveCandidatesAsync(request, root, cancellationToken);
        var recipes = new List<RenderRecipeDocument>();
        var generatedFiles = new List<string>();
        var warnings = new List<string>();
        var sceneCount = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                var assemblyPath = BuildAssemblyPath(root, candidate.RegionId, candidate.Id);
                var assembly = await ReadJsonAsync<SceneAssemblyPlanDocument>(assemblyPath, cancellationToken);
                if (assembly is null)
                {
                    warnings.Add($"Missing or unreadable scene assembly plan for plan {candidate.Id}: {assemblyPath}");
                    continue;
                }

                foreach (var scene in assembly.Scenes.OrderBy(scene => scene.SceneNumber))
                {
                    sceneCount++;
                    var recipe = BuildRecipe(assembly, scene);
                    recipes.Add(recipe);
                    warnings.AddRange(recipe.ExecutionReadiness.Warnings.Select(w => $"Plan {assembly.ContentGenerationPlanId} scene {scene.SceneNumber}: {w}"));
                    warnings.AddRange(recipe.ExecutionReadiness.BlockingIssues.Select(i => $"Plan {assembly.ContentGenerationPlanId} scene {scene.SceneNumber}: {i}"));

                    if (!request.DryRun)
                    {
                        var recipePath = BuildRecipePath(root, assembly.RegionId, assembly.ContentGenerationPlanId, scene.SceneNumber);
                        if (File.Exists(recipePath) && !request.OverwriteExisting)
                        {
                            warnings.Add($"Skipped existing render recipe for plan {assembly.ContentGenerationPlanId} scene {scene.SceneNumber}. Set overwriteExisting=true to replace it.");
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(recipePath) ?? root);
                        await File.WriteAllTextAsync(recipePath, JsonSerializer.Serialize(recipe, JsonOptions), cancellationToken);
                        generatedFiles.Add(recipePath);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to generate render recipes for plan {candidate.Id}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9E.1 render recipe generation failed for plan {PlanId}", candidate.Id);
            }
        }

        var readyCount = recipes.Count(recipe => recipe.ExecutionReadiness.ReadyForRenderExecution);
        logger.LogInformation("Phase 9E.1 processed {PlanCount} content generation plan(s). Scenes={SceneCount} Recipes={RecipeCount} ReadyForExecution={ReadyForExecutionCount} DryRun={DryRun}", candidates.Count, sceneCount, recipes.Count, readyCount, request.DryRun);
        return new RenderRecipeResult(candidates.Count, sceneCount, recipes.Count, readyCount, recipes.Count - readyCount, recipes, generatedFiles, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolveCandidatesAsync(RenderRecipeRequest request, string root, CancellationToken cancellationToken)
    {
        var requestedCategories = ToSet(request.ContentCategories);
        var requestedFormats = ToSet(request.PlannedFormats);
        var query = db.ContentGenerationPlans.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.RegionId))
        {
            var region = request.RegionId.Trim();
            query = query.Where(p => p.RegionId == region);
        }

        if (request.PlanIds is { Count: > 0 })
        {
            var ids = request.PlanIds.ToHashSet();
            query = query.Where(p => ids.Contains(p.Id));
        }

        if (requestedCategories is not null)
            query = query.Where(p => requestedCategories.Contains(p.ContentCategoryCode));
        if (requestedFormats is not null)
            query = query.Where(p => p.PlannedFormat != null && requestedFormats.Contains(p.PlannedFormat));

        query = query.Where(p => p.AstronomyContentOpportunityId != null || p.AstronomyEventIntelligenceId != null);

        var plans = await query
            .OrderByDescending(p => p.ScheduledUtc ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return plans
            .Where(p => File.Exists(BuildAssemblyPath(root, p.RegionId, p.Id)))
            .Take(request.MaxPlans ?? int.MaxValue)
            .ToList();
    }

    private static RenderRecipeDocument BuildRecipe(SceneAssemblyPlanDocument assembly, SceneAssemblyScene scene)
    {
        var warnings = new List<string>();
        var inputs = scene.Layers
            .OrderBy(layer => layer.ZIndex)
            .Select(layer => ToInput(layer, warnings))
            .Where(input => input is not null)
            .Cast<RenderRecipeInput>()
            .ToList();

        inputs.Add(new RenderRecipeInput(
            "audio",
            "NarrationSegment",
            scene.AudioPath,
            null,
            null,
            "scene_narration"));

        var blockingIssues = new List<string>();
        if (string.IsNullOrWhiteSpace(scene.AudioPath))
            blockingIssues.Add("missing scene audio");
        if (string.IsNullOrWhiteSpace(scene.OutputSceneVideoPath))
            blockingIssues.Add("missing output path");
        if (!inputs.Any(input => input.InputType is "visual" or "planned_visual"))
            blockingIssues.Add("no visual or planned visual input");
        if (scene.DurationSeconds <= 0)
            blockingIssues.Add("duration <= 0");

        var ready = blockingIssues.Count == 0;
        var motionFilterHint = ResolveMotionFilterHint(scene.Motion.Type);

        return new RenderRecipeDocument(
            assembly.ContentGenerationPlanId,
            assembly.RegionId,
            assembly.ContentCategory,
            assembly.PlannedFormat,
            scene.SceneNumber,
            scene.SceneName,
            "ffmpeg",
            Round(scene.DurationSeconds),
            assembly.FrameRate,
            new RenderRecipeResolution(assembly.OutputResolution.Width, assembly.OutputResolution.Height),
            scene.OutputSceneVideoPath,
            inputs,
            new RenderRecipeMotion(scene.Motion.Type, scene.Motion.StartScale, scene.Motion.EndScale, scene.Motion.Direction, motionFilterHint),
            new RenderRecipeCaptions(scene.Captions.Enabled, scene.Captions.Source, scene.Captions.SafeZone, scene.Captions.Style),
            new RenderRecipeTransition(scene.Transition.In, scene.Transition.Out, scene.Transition.DurationSeconds),
            [new RenderRecipeFilter("kenburns", true)],
            new RenderRecipeExecutionReadiness(ready, blockingIssues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static RenderRecipeInput? ToInput(SceneAssemblyLayer layer, List<string> warnings)
    {
        var inputType = ResolveInputType(layer.LayerType);
        if (inputType is null)
            return null;

        var renderMode = ResolveRenderMode(layer);
        if (layer.AssetType.Equals("PlannedVisual", StringComparison.OrdinalIgnoreCase))
            warnings.Add("PlannedVisual requires placeholder renderer or generated image before final production.");
        if (renderMode.Equals("json_visual_card", StringComparison.OrdinalIgnoreCase))
            warnings.Add("JSON card renderer required.");
        if (renderMode.Equals("metadata_visual_card", StringComparison.OrdinalIgnoreCase))
            warnings.Add("metadata visual renderer required.");

        return new RenderRecipeInput(
            inputType,
            layer.AssetType,
            layer.AssetPath,
            renderMode,
            layer.ZIndex,
            ResolveRole(layer.LayerType));
    }

    private static string? ResolveInputType(string layerType)
        => layerType.Trim() switch
        {
            "BackgroundVisual" => "visual",
            "SupportingVisual" => "visual",
            "Overlay" => "visual",
            "PlannedVisual" => "planned_visual",
            _ => null
        };

    private static string ResolveRole(string layerType)
        => layerType.Trim() switch
        {
            "BackgroundVisual" => "background",
            "SupportingVisual" => "supporting",
            "Overlay" => "overlay",
            "PlannedVisual" => "planned_or_placeholder",
            _ => "supporting"
        };

    private static string ResolveRenderMode(SceneAssemblyLayer layer)
    {
        var extension = Path.GetExtension(layer.AssetPath);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image";

        return layer.AssetType.Trim() switch
        {
            "TextOverlayCard" => "overlay_card",
            "SkyMapCard" => "json_visual_card",
            "ConstellationGuide" => "json_visual_card",
            "NasaAsset" => "metadata_visual_card",
            _ => string.IsNullOrWhiteSpace(layer.RenderMode) ? "image_or_json_visual" : layer.RenderMode
        };
    }

    private static string ResolveMotionFilterHint(string motionType)
        => motionType.Trim() switch
        {
            "zoom_in_subtle" => "kenburns_zoom_in",
            "zoom_in_focus" => "kenburns_zoom_in_focus",
            "pan_hold" => "pan_hold",
            "parallax_orbit_soft" => "parallax_soft",
            "zoom_out_subtle" => "kenburns_zoom_out",
            "hold" => "static_hold",
            "fade_hold" => "fade_hold",
            "montage_crossfade" => "montage_crossfade",
            _ => motionType
        };

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

    private string ResolveWorkingDirectoryRoot()
        => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "media-output")
            : renderingOptions.Value.WorkingDirectory;

    private static string BuildPlanRoot(string root, string regionId, Guid planId)
        => Path.Combine(root, "assets", regionId, "plans", planId.ToString("D"));

    private static string BuildPlanRoot(string root, string regionId, string planId)
        => Path.Combine(root, "assets", regionId, "plans", planId);

    private static string BuildAssemblyPath(string root, string regionId, Guid planId)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), "assembly", AssemblyFileName);

    private static string BuildRecipePath(string root, string regionId, string planId, int sceneNumber)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), RecipeDirectoryName, $"scene-{sceneNumber:000}.recipe.json");

    private static double Round(double value)
        => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
