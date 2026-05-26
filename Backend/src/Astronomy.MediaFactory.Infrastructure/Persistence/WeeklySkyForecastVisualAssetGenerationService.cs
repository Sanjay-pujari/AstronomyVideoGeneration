using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastVisualAssetGenerationService(
    IContentPlanningService planning,
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastSegmentPlanner segmentPlanner,
    IWeeklySkyForecastSscScenePlanner scenePlanner,
    ICategoryOutputPathResolver pathResolver,
    IStellariumScriptGenerator scriptGenerator,
    IStellariumImageCaptureExecutor captureExecutor,
    IOptions<StellariumOptions> stellariumOptions) : IWeeklySkyForecastVisualAssetGenerationService
{
    public async Task<WeeklySkyForecastVisualAssetsResponse> GenerateAsync(Guid contentGenerationPlanId, WeeklySkyForecastVisualAssetsGenerateRequest request, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var steps = new List<CategoryProductionStepResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var plan = await planning.GetPlanByIdAsync(contentGenerationPlanId, cancellationToken)
            ?? throw new KeyNotFoundException($"Content generation plan '{contentGenerationPlanId}' was not found.");
        if (!string.Equals(plan.ContentCategoryCode, "WeeklySkyForecast", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This endpoint only supports WeeklySkyForecast plans.");
        }

        var weekStartDate = DateOnly.FromDateTime((plan.ScheduledUtc ?? DateTimeOffset.UtcNow).UtcDateTime);
        var weeklyRequest = new WeeklySkyForecastProductionRequest(
            plan.ContentCategoryCode,
            plan.Language,
            plan.RegionId,
            plan.RegionId,
            plan.ScheduledUtc ?? DateTimeOffset.UtcNow,
            weekStartDate,
            weekStartDate.AddDays(6),
            false,
            false,
            false,
            true);

        var context = await contextBuilder.BuildAsync(weeklyRequest, cancellationToken);
        var segmentPlan = await segmentPlanner.BuildAsync(context, cancellationToken);
        var weeklyScenePlan = await scenePlanner.BuildAsync(context, segmentPlan, cancellationToken);
        if (!request.AllowExtraScenes && weeklyScenePlan.Scenes.Count > 5)
            throw new InvalidOperationException($"WeeklySkyForecast visual planning generated {weeklyScenePlan.Scenes.Count} scenes. Maximum allowed is 5 unless allowExtraScenes=true.");
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", context.WeekStartDate, context.RegionId, contentGenerationPlanId);
        var canonicalSscScriptsDirectory = Path.Combine(stellariumOptions.Value.ScriptsDirectory, "content-plans", contentGenerationPlanId.ToString());
        var canonicalStellariumCapturesDirectory = Path.Combine(stellariumOptions.Value.CaptureDirectory, "content-plans", contentGenerationPlanId.ToString(), "stellarium-scenes");
        Directory.CreateDirectory(canonicalSscScriptsDirectory);
        Directory.CreateDirectory(canonicalStellariumCapturesDirectory);
        Directory.CreateDirectory(outputPaths.ManifestsDirectory);
        Directory.CreateDirectory(outputPaths.NarrationDirectory);
        steps.Add(Step("BuildWeeklySceneInputs", sw.ElapsedMilliseconds));

        sw.Restart();
        var capturePlan = new StellariumSceneCapturePlan(contentGenerationPlanId, "WeeklySkyForecast", context.RegionId, context.LocationName, context.Latitude, context.Longitude, context.Timezone, context.WeekStartDate, [], []);
        var sceneByCode = weeklyScenePlan.Scenes.ToDictionary(x => x.SceneCode, StringComparer.OrdinalIgnoreCase);
        foreach (var s in weeklyScenePlan.Scenes.OrderBy(x => x.SceneCode))
        {
            var normalizedTarget = string.IsNullOrWhiteSpace(s.TargetObjectCode) ? null : WeeklySkyForecastObjectCodeResolver.NormalizeObjectCode(s.TargetObjectCode);
            capturePlan.Scenes.Add(new StellariumSceneCaptureItem(s.SceneCode, s.SceneType, s.SceneCode, normalizedTarget, normalizedTarget, s.CaptureTimeUtc, "Focus", s.FieldOfViewDegrees, true, true, true, false, false, s.OutputRole, capturePlan.Scenes.Count + 1,
                new Dictionary<string, string> { ["linkedSegmentCode"] = s.LinkedSegmentCode }));
        }

        var scriptResults = new List<WeeklySkyForecastVisualAssetScriptResult>();
        foreach (var scene in capturePlan.Scenes)
        {
            var generated = await scriptGenerator.GenerateAsync(capturePlan, scene, cancellationToken);
            var destinationScriptPath = Path.Combine(canonicalSscScriptsDirectory, $"{scene.SceneCode}.ssc");
            File.Copy(generated.ScriptPath, destinationScriptPath, true);
            var expectedImagePath = Path.Combine(canonicalStellariumCapturesDirectory, $"{scene.SceneCode}_{scene.OutputImageRole}.png");
            scriptResults.Add(new WeeklySkyForecastVisualAssetScriptResult(scene.SceneCode, destinationScriptPath, expectedImagePath, generated.Success, generated.ErrorMessage));
            if (string.IsNullOrWhiteSpace(scene.TargetObjectCode) is false && scene.TargetObjectCode != scene.TargetObjectCode.ToUpperInvariant())
                errors.Add($"targetObjectCode must be uppercase for scene {scene.SceneCode}.");
            if (scene.CaptureTimeUtc == default)
                errors.Add($"captureTimeUtc missing for scene {scene.SceneCode}.");
        }
        steps.Add(Step("GenerateSscScripts", sw.ElapsedMilliseconds));

        sw.Restart();
        StellariumCaptureExecutionResponse? captureResponse = null;
        if (!request.DryRun && request.CaptureStellariumScenes)
        {
            captureResponse = await captureExecutor.CaptureAsync(capturePlan, new StellariumCaptureExecutionRequest(contentGenerationPlanId, false, request.OverwriteExisting, request.Diagnostics), cancellationToken);
            warnings.AddRange(captureResponse.Warnings);
        }

        var images = new List<WeeklySkyForecastVisualAssetImageResult>();
        foreach (var script in scriptResults)
        {
            var scene = sceneByCode[script.SceneCode];
            var exists = request.DryRun ? false : (File.Exists(script.ExpectedImagePath) && new FileInfo(script.ExpectedImagePath).Length > 0);
            if (!request.DryRun && !exists)
                errors.Add($"Missing image for scene {script.SceneCode}.");
            images.Add(new WeeklySkyForecastVisualAssetImageResult(script.SceneCode, script.ExpectedImagePath, exists, scene.OutputRole, scene.LinkedSegmentCode, scene.TargetObjectCode));
        }
        steps.Add(Step("CaptureStellariumScenes", sw.ElapsedMilliseconds));

        var narrationManifestPath = Path.Combine(outputPaths.ManifestsDirectory, "NarrationManifest.json");
        WeeklyNarrationManifest? narrationManifest = null;
        if (File.Exists(narrationManifestPath))
            narrationManifest = JsonSerializer.Deserialize<WeeklyNarrationManifest>(await File.ReadAllTextAsync(narrationManifestPath, cancellationToken));
        var narrationBySegment = narrationManifest?.Segments.ToDictionary(x => x.SegmentCode, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, WeeklyNarrationAudioSegment>(StringComparer.OrdinalIgnoreCase);

        var segmentVisualMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WeeklyIntro"] = "WeeklyIntroWideSky",
            ["MoonPhaseForecast"] = "BestMoonNight",
            ["BestPlanets"] = "BestPlanetOfWeek",
            ["RecommendedNights"] = "BestObservationNightWide",
            ["WeeklyHighlights"] = "BestObservationNightWide",
            ["AstroPhotographyTip"] = "BestObservationNightWide",
            ["WeeklyOutro"] = "WeeklyIntroWideSky"
        };
        var allSegments = segmentPlan.LongSegments.Concat(segmentPlan.ShortSegments).ToList();
        var visualAssetManifest = new List<WeeklySkyForecastVisualAssetManifestItem>();
        foreach (var segment in allSegments)
        {
            if (!segmentVisualMap.TryGetValue(segment.SegmentCode, out var sceneCode))
                sceneCode = segment.SuggestedSceneType;
            var scene = weeklyScenePlan.Scenes.FirstOrDefault(x => x.SceneCode.Equals(sceneCode, StringComparison.OrdinalIgnoreCase));
            if (scene is null)
            {
                warnings.Add($"Segment '{segment.SegmentCode}' has no visual mapping.");
                continue;
            }

            var audioPath = narrationBySegment.TryGetValue(segment.SegmentCode, out var narration)
                ? Path.Combine(outputPaths.NarrationDirectory, narration.OutputFileName)
                : string.Empty;
            var script = scriptResults.First(x => x.SceneCode.Equals(scene.SceneCode, StringComparison.OrdinalIgnoreCase));
            var image = images.First(x => x.SceneCode.Equals(scene.SceneCode, StringComparison.OrdinalIgnoreCase));
            var reuseAllowed = !scene.IsThumbnailCandidate;
            visualAssetManifest.Add(new(segment.SegmentCode, audioPath, scene.SceneCode, script.ScriptPath, image.ImagePath, reuseAllowed, segment.NarrationPurpose, scene.OutputRole, scene.TargetObjectCode, scene.CaptureTimeUtc));
        }

        foreach (var group in weeklyScenePlan.Scenes.GroupBy(x => new { x.TargetObjectCode, x.CaptureTimeUtc, x.FieldOfViewDegrees }))
        {
            if (group.Count() < 2)
                continue;
            warnings.Add($"Duplicate scene signature: target={group.Key.TargetObjectCode ?? "NONE"}, captureTimeUtc={group.Key.CaptureTimeUtc:O}, fov={group.Key.FieldOfViewDegrees}.");
        }

        var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "weekly-visual-assets-manifest.json");
        var manifest = new { contentGenerationPlanId, canonicalSscScriptsDirectory, canonicalStellariumCapturesDirectory, visualAssetManifestPath = manifestPath, contextSummary = context, sscScenes = weeklyScenePlan.Scenes, scripts = scriptResults, images, visualAssetManifest, capture = captureResponse, warnings, errors };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var success = errors.Count == 0 && scriptResults.Count == weeklyScenePlan.Scenes.Count;
        return new WeeklySkyForecastVisualAssetsResponse(contentGenerationPlanId, success, scriptResults.Count, images.Count(x => x.Exists), canonicalSscScriptsDirectory, canonicalStellariumCapturesDirectory, manifestPath, scriptResults, images, visualAssetManifest, warnings, errors, steps);
    }

    private static CategoryProductionStepResult Step(string name, long durationMs)
    {
        var started = DateTime.UtcNow.AddMilliseconds(-Math.Max(1, durationMs));
        var ended = DateTime.UtcNow;
        return new(name, "Completed", started, ended, Math.Max(1, durationMs), null, null, []);
    }
}
