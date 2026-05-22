using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class WeeklySkyForecastVisualAssetGenerationService(
    IContentPlanningService planning,
    IWeeklySkyForecastContextBuilder contextBuilder,
    IWeeklySkyForecastSegmentPlanner segmentPlanner,
    IWeeklySkyForecastSscScenePlanner scenePlanner,
    ICategoryOutputPathResolver pathResolver,
    IStellariumScriptGenerator scriptGenerator,
    IStellariumImageCaptureExecutor captureExecutor) : IWeeklySkyForecastVisualAssetGenerationService
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

        var weeklyRequest = new WeeklySkyForecastProductionRequest(
            plan.ContentCategoryCode,
            plan.Language,
            plan.RegionId,
            plan.RegionId,
            plan.ScheduledUtc ?? DateTimeOffset.UtcNow,
            false,
            false,
            false,
            true);

        var context = await contextBuilder.BuildAsync(weeklyRequest, cancellationToken);
        var segmentPlan = await segmentPlanner.BuildAsync(context, cancellationToken);
        var weeklyScenePlan = await scenePlanner.BuildAsync(context, segmentPlan, cancellationToken);
        var outputPaths = pathResolver.Resolve("WeeklySkyForecast", context.WeekStartDate, context.RegionId, contentGenerationPlanId);
        Directory.CreateDirectory(outputPaths.StellariumScriptsDirectory);
        Directory.CreateDirectory(outputPaths.StellariumScenesDirectory);
        Directory.CreateDirectory(outputPaths.ManifestsDirectory);
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
            var destinationScriptPath = Path.Combine(outputPaths.StellariumScriptsDirectory, $"{scene.SceneCode}.ssc");
            File.Copy(generated.ScriptPath, destinationScriptPath, true);
            var expectedImagePath = Path.Combine(outputPaths.StellariumScenesDirectory, $"{scene.SceneCode}_{scene.OutputImageRole}.png");
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

        var manifestPath = Path.Combine(outputPaths.ManifestsDirectory, "weekly-visual-assets-manifest.json");
        var manifest = new { contentGenerationPlanId, contextSummary = context, sscScenes = weeklyScenePlan.Scenes, scripts = scriptResults, images, capture = captureResponse, warnings, errors };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        var success = errors.Count == 0 && scriptResults.Count == weeklyScenePlan.Scenes.Count;
        return new WeeklySkyForecastVisualAssetsResponse(contentGenerationPlanId, success, scriptResults.Count, images.Count(x => x.Exists), scriptResults, images, warnings, errors, steps);
    }

    private static CategoryProductionStepResult Step(string name, long durationMs)
    {
        var started = DateTime.UtcNow.AddMilliseconds(-Math.Max(1, durationMs));
        var ended = DateTime.UtcNow;
        return new(name, "Completed", started, ended, Math.Max(1, durationMs), null, null, []);
    }
}
