using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class RenderCapabilityMatrixService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<RenderCapabilityMatrixService> logger) : IRenderCapabilityMatrixService
{
    private const string GenerationSource = "Phase9E.2A";
    private const string RecipeDirectoryName = "render-recipes";
    private const string CapabilityDirectoryName = "render-capabilities";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RenderCapabilityMatrixResult> GenerateRenderCapabilitiesAsync(RenderCapabilityMatrixRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var candidates = await ResolveCandidatesAsync(request, root, cancellationToken);
        var capabilities = new List<RenderCapabilityDocument>();
        var generatedFiles = new List<string>();
        var warnings = new List<string>();
        var sceneCount = 0;

        foreach (var candidate in candidates)
        {
            var recipeDirectory = BuildRecipeDirectory(root, candidate.RegionId, candidate.Id);
            var recipeFiles = Directory.Exists(recipeDirectory)
                ? Directory.EnumerateFiles(recipeDirectory, "scene-*.recipe.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];

            if (recipeFiles.Length == 0)
            {
                warnings.Add($"No render recipes found for plan {candidate.Id}: {recipeDirectory}");
                continue;
            }

            foreach (var recipePath in recipeFiles)
            {
                try
                {
                    var recipe = await ReadJsonAsync<RenderRecipeDocument>(recipePath, cancellationToken);
                    if (recipe is null)
                    {
                        warnings.Add($"Missing or unreadable render recipe for plan {candidate.Id}: {recipePath}");
                        continue;
                    }

                    sceneCount++;
                    var capability = BuildCapability(recipe, recipePath);
                    capabilities.Add(capability);
                    warnings.AddRange(capability.ExecutionPlan.Warnings.Select(w => $"Plan {recipe.ContentGenerationPlanId} scene {recipe.SceneNumber}: {w}"));
                    warnings.AddRange(capability.ExecutionPlan.BlockingIssues.Select(i => $"Plan {recipe.ContentGenerationPlanId} scene {recipe.SceneNumber}: {i}"));

                    if (!request.DryRun)
                    {
                        var capabilityPath = BuildCapabilityPath(root, recipe.RegionId, recipe.ContentGenerationPlanId, recipe.SceneNumber);
                        if (File.Exists(capabilityPath) && !request.OverwriteExisting)
                        {
                            warnings.Add($"Skipped existing render capability for plan {recipe.ContentGenerationPlanId} scene {recipe.SceneNumber}. Set overwriteExisting=true to replace it.");
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(capabilityPath) ?? root);
                        await File.WriteAllTextAsync(capabilityPath, JsonSerializer.Serialize(capability, JsonOptions), cancellationToken);
                        generatedFiles.Add(capabilityPath);
                    }
                }
                catch (JsonException ex)
                {
                    warnings.Add($"Unreadable render recipe for plan {candidate.Id}: {recipePath}: {ex.Message}");
                    logger.LogWarning(ex, "Phase 9E.2A render capability matrix could not read recipe {RecipePath}", recipePath);
                }
            }
        }

        var canExecuteCount = capabilities.Count(capability => capability.ExecutionPlan.CanExecute);
        logger.LogInformation("Phase 9E.2A processed {PlanCount} content generation plan(s). Scenes={SceneCount} Capabilities={CapabilityCount} CanExecute={CanExecuteCount} DryRun={DryRun}", candidates.Count, sceneCount, capabilities.Count, canExecuteCount, request.DryRun);
        return new RenderCapabilityMatrixResult(candidates.Count, sceneCount, capabilities.Count, canExecuteCount, capabilities.Count - canExecuteCount, capabilities, generatedFiles, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolveCandidatesAsync(RenderCapabilityMatrixRequest request, string root, CancellationToken cancellationToken)
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
            .Where(p => Directory.Exists(BuildRecipeDirectory(root, p.RegionId, p.Id)))
            .Take(request.MaxPlans ?? int.MaxValue)
            .ToList();
    }

    private static RenderCapabilityDocument BuildCapability(RenderRecipeDocument recipe, string recipePath)
    {
        var warnings = new List<string>();
        var fallbacks = new List<string>();
        var blockingIssues = new List<string>();
        var requiredHandlers = recipe.Inputs
            .Where(input => input.InputType.Equals("visual", StringComparison.OrdinalIgnoreCase)
                || input.InputType.Equals("planned_visual", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(input.RenderMode))
            .Select(input => BuildVisualHandler(input, warnings, fallbacks, blockingIssues))
            .ToList();

        var hasAudioInput = recipe.Inputs.Any(input => input.InputType.Equals("audio", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(input.AssetPath));
        var audioHandler = new RenderCapabilityAudioHandler("SceneAudioMuxer", true, hasAudioInput);
        if (!audioHandler.Available)
            blockingIssues.Add("missing audio handler");

        var motionHandler = ResolveMotionHandler(recipe.Motion, warnings);
        var captionHandler = recipe.Captions.Enabled
            ? new RenderCapabilityCaptionHandler(true, "CinematicCaptionRenderer", true)
            : new RenderCapabilityCaptionHandler(false, string.Empty, true);
        var transitionHandler = new RenderCapabilityTransitionHandler(recipe.Transition.In, recipe.Transition.Out, "SceneTransitionPlanner", true);

        if (string.IsNullOrWhiteSpace(recipe.OutputVideoPath))
            blockingIssues.Add("missing output video path");
        if (requiredHandlers.Count == 0 || requiredHandlers.All(handler => !handler.Available))
            blockingIssues.Add("missing visual handler");

        blockingIssues.AddRange(recipe.ExecutionReadiness.BlockingIssues
            .Where(issue => issue.Contains("output", StringComparison.OrdinalIgnoreCase))
            .Select(_ => "missing output video path"));

        var distinctBlockingIssues = blockingIssues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctWarnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctFallbacks = fallbacks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new RenderCapabilityDocument(
            recipe.ContentGenerationPlanId,
            recipe.RegionId,
            recipe.SceneNumber,
            recipe.SceneName,
            recipePath,
            recipe.OutputVideoPath,
            recipe.Renderer,
            requiredHandlers,
            audioHandler,
            motionHandler,
            captionHandler,
            transitionHandler,
            new RenderCapabilityExecutionPlan(distinctBlockingIssues.Length == 0, distinctBlockingIssues, distinctWarnings, distinctFallbacks),
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static RenderCapabilityHandler BuildVisualHandler(RenderRecipeInput input, List<string> warnings, List<string> fallbacks, List<string> blockingIssues)
    {
        var renderMode = string.IsNullOrWhiteSpace(input.RenderMode) ? input.InputType : input.RenderMode.Trim();
        var handler = ResolveVisualHandler(renderMode);
        var notes = string.Empty;
        if (renderMode.Equals("planned_visual_prompt", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Planned visual will render as placeholder until generated image is available.");
            fallbacks.Add("planned_visual_prompt uses PlaceholderVisualRenderer");
            notes = "Placeholder renderer used until generated image is available.";
        }
        else if (renderMode.Equals("json_visual_card", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("JSON visual card renderer must create visual frame before FFmpeg execution.");
            notes = "JSON renderer must create visual frame before FFmpeg execution.";
        }
        else if (renderMode.Equals("metadata_visual_card", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Metadata visual card renderer must create visual frame before FFmpeg execution.");
            notes = "Metadata renderer must create visual frame before FFmpeg execution.";
        }

        if (handler.Length == 0)
            blockingIssues.Add($"unsupported renderMode with no fallback: {renderMode}");

        return new RenderCapabilityHandler(renderMode, handler, true, handler.Length > 0, notes);
    }

    private static string ResolveVisualHandler(string renderMode)
        => renderMode.Trim().ToLowerInvariant() switch
        {
            "overlay_card" => "JsonOverlayCardRenderer",
            "json_visual_card" => "JsonVisualCardRenderer",
            "metadata_visual_card" => "MetadataVisualCardRenderer",
            "planned_visual_prompt" => "PlaceholderVisualRenderer",
            "image" => "ImageVisualRenderer",
            "audio" => "SceneAudioMuxer",
            _ => string.Empty
        };

    private static RenderCapabilityMotionHandler ResolveMotionHandler(RenderRecipeMotion motion, List<string> warnings)
    {
        var filterHint = string.IsNullOrWhiteSpace(motion.FilterHint) ? motion.Type : motion.FilterHint.Trim();
        var handler = ResolveMotionHandlerName(filterHint);
        if (handler.Equals("DefaultSubtleMotionRenderer", StringComparison.Ordinal)
            && !string.Equals(motion.Type, filterHint, StringComparison.OrdinalIgnoreCase))
        {
            handler = ResolveMotionHandlerName(motion.Type);
        }

        if (handler.Equals("DefaultSubtleMotionRenderer", StringComparison.Ordinal))
            warnings.Add("Unknown motion filter hint mapped to default subtle motion.");

        return new RenderCapabilityMotionHandler(motion.Type, filterHint, handler, true);
    }

    private static string ResolveMotionHandlerName(string motionHint)
        => motionHint.Trim().ToLowerInvariant() switch
        {
            "kenburns_zoom_in" => "KenBurnsMotionRenderer",
            "kenburns_zoom_in_focus" => "KenBurnsMotionRenderer",
            "kenburns_zoom_out" => "KenBurnsMotionRenderer",
            "pan_hold" => "PanHoldMotionRenderer",
            "parallax_soft" => "ParallaxSoftMotionRenderer",
            "static_hold" => "StaticHoldMotionRenderer",
            "fade_hold" => "FadeHoldMotionRenderer",
            "guided_pan_across_group_with_object_sequence_emphasis" => "GroupedObjectPanRenderer",
            "pan_sequence" => "GroupedObjectPanRenderer",
            "guided_pan_across_group" => "GroupedObjectPanRenderer",
            "episode_montage_crossfade_with_night-by-night_progression" => "WeeklyMontageRenderer",
            "montage_crossfade" => "WeeklyMontageRenderer",
            "episode_montage" => "WeeklyMontageRenderer",
            "weekly_montage" => "WeeklyMontageRenderer",
            _ => "DefaultSubtleMotionRenderer"
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

    private static string BuildRecipeDirectory(string root, string regionId, Guid planId)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), RecipeDirectoryName);

    private static string BuildCapabilityPath(string root, string regionId, string planId, int sceneNumber)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), CapabilityDirectoryName, $"scene-{sceneNumber:000}.capability.json");
}
