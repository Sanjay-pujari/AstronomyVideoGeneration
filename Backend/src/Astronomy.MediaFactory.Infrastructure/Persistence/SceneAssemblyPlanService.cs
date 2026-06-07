using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class SceneAssemblyPlanService(
    MediaFactoryDbContext db,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<SceneAssemblyPlanService> logger) : ISceneAssemblyPlanService
{
    private const string GenerationSource = "Phase9E.0";
    private const string TimelineFileName = "director-timeline.json";
    private const string AssemblyFileName = "scene-assembly-plan.json";
    private const double TransitionDurationSeconds = 0.5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<SceneAssemblyPlanResult> GenerateSceneAssemblyPlansAsync(SceneAssemblyPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxPlans is < 1)
            throw new ArgumentException("MaxPlans must be greater than zero when provided.");

        var root = ResolveWorkingDirectoryRoot();
        var candidates = await ResolveCandidatesAsync(request, root, cancellationToken);
        var plans = new List<SceneAssemblyPlanDocument>();
        var generatedFiles = new List<string>();
        var warnings = new List<string>();

        foreach (var candidate in candidates)
        {
            try
            {
                var assemblyPath = BuildAssemblyPath(root, candidate.RegionId, candidate.Id);
                if (!request.DryRun && File.Exists(assemblyPath) && !request.OverwriteExisting)
                {
                    warnings.Add($"Skipped existing scene assembly plan for plan {candidate.Id}. Set overwriteExisting=true to replace it.");
                    continue;
                }

                var timelinePath = BuildTimelinePath(root, candidate.RegionId, candidate.Id);
                var timeline = await ReadJsonAsync<DirectorTimelineDocument>(timelinePath, cancellationToken);
                if (timeline is null)
                {
                    warnings.Add($"Missing or unreadable director timeline for plan {candidate.Id}: {timelinePath}");
                    continue;
                }

                var assembly = BuildAssemblyPlan(root, timeline);
                plans.Add(assembly);
                warnings.AddRange(assembly.RenderReadiness.Warnings.Select(w => $"Plan {candidate.Id}: {w}"));
                warnings.AddRange(assembly.RenderReadiness.MissingInputs.Select(m => $"Plan {candidate.Id}: {m}"));

                if (!request.DryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath) ?? root);
                    await File.WriteAllTextAsync(assemblyPath, JsonSerializer.Serialize(assembly, JsonOptions), cancellationToken);
                    generatedFiles.Add(assemblyPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Failed to generate scene assembly plan for plan {candidate.Id}: {ex.Message}");
                logger.LogWarning(ex, "Phase 9E.0 scene assembly plan generation failed for plan {PlanId}", candidate.Id);
            }
        }

        var readyCount = plans.Count(p => p.RenderReadiness.ReadyForSceneRender);
        return new SceneAssemblyPlanResult(candidates.Count, plans.Count, readyCount, plans.Count - readyCount, plans, generatedFiles, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<ContentGenerationPlan>> ResolveCandidatesAsync(SceneAssemblyPlanRequest request, string root, CancellationToken cancellationToken)
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
            .Where(p => File.Exists(BuildTimelinePath(root, p.RegionId, p.Id)))
            .Take(request.MaxPlans ?? int.MaxValue)
            .ToList();
    }

    private static SceneAssemblyPlanDocument BuildAssemblyPlan(string root, DirectorTimelineDocument timeline)
    {
        var missingInputs = new List<string>();
        var warnings = new List<string>();
        var scenes = timeline.Scenes
            .OrderBy(scene => scene.SceneNumber)
            .Select(scene => BuildScene(root, timeline, scene, missingInputs, warnings))
            .ToList();

        if (string.IsNullOrWhiteSpace(timeline.Audio.CombinedNarrationPath))
            missingInputs.Add("Combined narration path is missing.");
        if (string.IsNullOrWhiteSpace(timeline.Audio.MusicMood) || string.IsNullOrWhiteSpace(timeline.Audio.MusicIntensity))
            warnings.Add("music bed not selected yet.");

        var totalDuration = scenes.Count == 0 ? 0 : scenes.Max(scene => scene.EndSecond);
        if (totalDuration <= 0)
            missingInputs.Add("Assembly duration is invalid.");

        foreach (var timelineWarning in timeline.RenderReadiness.Warnings)
            warnings.Add(timelineWarning);

        var ready = missingInputs.Count == 0
            && scenes.Count > 0
            && scenes.All(scene => !string.IsNullOrWhiteSpace(scene.AudioPath)
                && scene.DurationSeconds > 0
                && !string.IsNullOrWhiteSpace(scene.OutputSceneVideoPath)
                && scene.Layers.Any(layer => IsVisualReadinessLayer(layer)));

        return new SceneAssemblyPlanDocument(
            timeline.ContentGenerationPlanId,
            timeline.RegionId,
            timeline.ContentCategory,
            timeline.PlannedFormat,
            timeline.Title,
            "16:9",
            new SceneAssemblyResolution(1920, 1080),
            30,
            Round(totalDuration),
            new SceneAssemblyAudio(timeline.Audio.CombinedNarrationPath, timeline.Audio.MusicMood, timeline.Audio.MusicIntensity, true),
            scenes,
            new SceneAssemblyRenderReadiness(ready, missingInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            GenerationSource,
            DateTimeOffset.UtcNow);
    }

    private static SceneAssemblyScene BuildScene(string root, DirectorTimelineDocument timeline, DirectorTimelineScene scene, List<string> missingInputs, List<string> warnings)
    {
        var layers = new List<SceneAssemblyLayer>();
        if (!string.IsNullOrWhiteSpace(scene.PrimaryAsset.AssetType) && !string.IsNullOrWhiteSpace(scene.PrimaryAsset.Path))
            layers.Add(ToLayer("BackgroundVisual", scene.PrimaryAsset, zIndex: 0));

        var zIndex = 10;
        foreach (var asset in scene.SecondaryAssets.Where(asset => !string.IsNullOrWhiteSpace(asset.AssetType) && !string.IsNullOrWhiteSpace(asset.Path)))
        {
            var layerType = MapSecondaryLayerType(asset.AssetType);
            if (layerType is null)
                continue;
            layers.Add(ToLayer(layerType, asset, zIndex++));
        }

        if (!string.IsNullOrWhiteSpace(scene.OverlayPlan.TextOverlayPath))
        {
            layers.Add(new SceneAssemblyLayer("Overlay", "TextOverlayCard", scene.OverlayPlan.TextOverlayPath, "overlay_card", null, null, "center-safe", zIndex++));
            warnings.Add("JSON visual package will need renderer implementation.");
        }

        foreach (var layer in layers)
        {
            if (layer.AssetType.Equals("PlannedVisual", StringComparison.OrdinalIgnoreCase))
                warnings.Add("PlannedVisual requires actual image generation later.");
            if (layer.RenderMode is "json_visual_card" or "metadata_visual_card" or "overlay_card" or "planned_visual_prompt")
                warnings.Add("JSON visual package will need renderer implementation.");
        }

        var renderNotes = new List<string>();
        renderNotes.AddRange(scene.QualityNotes);
        renderNotes.AddRange(scene.TechnicalReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Path) || !string.IsNullOrWhiteSpace(reference.AssetType))
            .Select(reference => $"Technical reference ({reference.AssetType}): {reference.Path} - {reference.Usage}"));

        var outputScenePath = BuildSceneOutputPath(root, timeline.RegionId, timeline.ContentGenerationPlanId, scene.SceneNumber);
        if (string.IsNullOrWhiteSpace(scene.AudioPath))
            missingInputs.Add($"Scene {scene.SceneNumber} has no audioPath.");
        if (scene.DurationSeconds <= 0)
            missingInputs.Add($"Scene {scene.SceneNumber} durationSeconds must be greater than zero.");
        if (string.IsNullOrWhiteSpace(outputScenePath))
            missingInputs.Add($"Scene {scene.SceneNumber} has no planned outputSceneVideoPath.");
        if (!layers.Any(IsVisualReadinessLayer))
            missingInputs.Add($"Scene {scene.SceneNumber} has no BackgroundVisual or PlannedVisual layer.");

        return new SceneAssemblyScene(
            scene.SceneNumber,
            scene.SceneName,
            scene.StartSecond,
            scene.EndSecond,
            scene.DurationSeconds,
            outputScenePath,
            scene.AudioPath,
            layers,
            MapMotion(scene.CameraMotion),
            new SceneAssemblyTransition(scene.TransitionIn, scene.TransitionOut, TransitionDurationSeconds),
            new SceneAssemblyCaptions(true, "narrationText", "lower-third-safe", "cinematic_subtitle"),
            renderNotes);
    }

    private static SceneAssemblyLayer ToLayer(string layerType, DirectorTimelineAsset asset, int zIndex)
    {
        var safeZone = layerType.Equals("Overlay", StringComparison.OrdinalIgnoreCase) ? "center-safe" : null;
        return layerType.Equals("BackgroundVisual", StringComparison.OrdinalIgnoreCase)
            ? new SceneAssemblyLayer(layerType, asset.AssetType, asset.Path, ResolveRenderMode(asset), "cover", 1.0, safeZone, zIndex)
            : new SceneAssemblyLayer(layerType, asset.AssetType, asset.Path, ResolveRenderMode(asset), null, null, safeZone, zIndex);
    }

    private static string? MapSecondaryLayerType(string assetType)
        => assetType.Trim() switch
        {
            "TextOverlayCard" => "Overlay",
            "SkyMapCard" => "SupportingVisual",
            "ConstellationGuide" => "SupportingVisual",
            "NasaAsset" => "SupportingVisual",
            "PlannedVisual" => "PlannedVisual",
            _ => null
        };

    private static string ResolveRenderMode(DirectorTimelineAsset asset)
    {
        var extension = Path.GetExtension(asset.Path);
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            return "audio";
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image";

        return asset.AssetType.Trim() switch
        {
            "TextOverlayCard" => "overlay_card",
            "SkyMapCard" => "json_visual_card",
            "ConstellationGuide" => "json_visual_card",
            "NasaAsset" => "metadata_visual_card",
            "PlannedVisual" => "planned_visual_prompt",
            _ => "image_or_json_visual"
        };
    }

    private static SceneAssemblyMotion MapMotion(string cameraMotion)
    {
        var normalized = cameraMotion.Trim().ToLowerInvariant();
        var type = normalized switch
        {
            "slow push-in" => "zoom_in_subtle",
            "slow zoom toward pairing" => "zoom_in_focus",
            "subtle pan" => "pan_hold",
            "hold" => "pan_hold",
            "subtle pan / hold" => "pan_hold",
            "gentle orbit" => "parallax_orbit_soft",
            "line-of-sight style" => "parallax_orbit_soft",
            "gentle orbit / line-of-sight style" => "parallax_orbit_soft",
            "guided pan across group" => "pan_sequence",
            "episode montage crossfade" => "montage_crossfade",
            "closing hold" => "hold",
            "slow fade out" => "fade_hold",
            "slow pull-back" => "zoom_out_subtle",
            _ => normalized.Replace(' ', '_').Replace("/", string.Empty)
        };

        return new SceneAssemblyMotion(type, "subtle", ResolveDirection(type), 1.0, type == "zoom_out_subtle" ? 0.94 : 1.08);
    }

    private static string ResolveDirection(string motionType)
        => motionType switch
        {
            "pan_hold" => "left_to_right_soft",
            "pan_sequence" => "guided_left_to_right",
            "parallax_orbit_soft" => "clockwise_soft",
            "zoom_out_subtle" => "pull_back_center",
            "fade_hold" or "hold" => "center_hold",
            _ => "center_focus"
        };

    private static bool IsVisualReadinessLayer(SceneAssemblyLayer layer)
        => layer.LayerType.Equals("BackgroundVisual", StringComparison.OrdinalIgnoreCase)
            || layer.LayerType.Equals("PlannedVisual", StringComparison.OrdinalIgnoreCase);

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

    private static string BuildTimelinePath(string root, string regionId, Guid planId)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), "timeline", TimelineFileName);

    private static string BuildAssemblyPath(string root, string regionId, Guid planId)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), "assembly", AssemblyFileName);

    private static string BuildSceneOutputPath(string root, string regionId, string planId, int sceneNumber)
        => Path.Combine(BuildPlanRoot(root, regionId, planId), "assembly", "scenes", $"scene-{sceneNumber:000}.mp4");

    private static double Round(double value)
        => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
