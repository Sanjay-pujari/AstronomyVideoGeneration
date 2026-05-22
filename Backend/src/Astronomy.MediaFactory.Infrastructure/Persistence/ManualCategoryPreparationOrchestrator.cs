using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ManualCategoryPreparationOrchestrator(
    IContentPlanningService planning,
    ICategoryRequirementResolver requirementResolver,
    IVisualStrategyResolver visualStrategyResolver,
    IStellariumScriptGenerator scriptGenerator,
    IStellariumImageCaptureExecutor stellariumCaptureExecutor,
    IDailySkyGuideVisualAssetPackager visualAssetPackager,
    IAssetAwareManualRunPreparationService manualRunPreparationService,
    IDailySkyGuideAssetAwareContextService dailySkyGuideAssetContextService,
    IAssetAwareCompositionPlannerResolver compositionPlannerResolver,
    IDailySkyGuidePreviewVideoGenerator previewVideoGenerator,
    ILogger<ManualCategoryPreparationOrchestrator> logger) : IManualCategoryPreparationOrchestrator
{
    public async Task<ManualCategoryPreparationResponse> RunAsync(ManualCategoryPreparationRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<ManualCategoryPreparationStepResult>();
        var warnings = new List<string>
        {
            "Publishing is disabled for category preparation flow until category output quality is verified."
        };
        RunPipelineRequest? runPipelineRequest = null;
        Guid? planId = null;
        ContentGenerationPlan? plan = null;
        CategoryPipelineRequirement? requirement = null;
        StellariumSceneCapturePlan? scenePlan = null;

        var generatePlanStep = await ExecuteStepAsync("GeneratePlan", async () =>
        {
            var generated = await planning.GenerateDailyPlanAsync(request.ContentCategoryCode, request.Language, request.RegionId, request.ScheduledUtc, request.PrimaryCelestialObjectCode, cancellationToken);
            planId = generated.Id;
            plan = generated;
            return ("Plan created successfully.", (string?)null, Array.Empty<string>());
        });
        steps.Add(generatePlanStep);
        if (generatePlanStep.Status == "Failed")
        {
            return BuildResponse(false, request.ContentCategoryCode, planId, steps, runPipelineRequest, warnings, generatePlanStep.ErrorMessage ?? "Plan generation failed.");
        }

        var resolveRequirementStep = await ExecuteStepAsync("ResolveCategoryRequirements", async () =>
        {
            requirement = await requirementResolver.ResolveAsync(request.ContentCategoryCode, cancellationToken);
            return ("Category requirements resolved.", (string?)null, requirement.Warnings);
        });
        steps.Add(resolveRequirementStep);
        if (resolveRequirementStep.Status == "Failed" || plan is null)
        {
            return BuildResponse(false, request.ContentCategoryCode, planId, steps, runPipelineRequest, warnings, resolveRequirementStep.ErrorMessage ?? "Category requirement resolution failed.");
        }

        steps.Add(await ExecuteStepAsync("VisualStrategyPreview", async () =>
        {
            _ = await visualStrategyResolver.ResolveAsync(plan, cancellationToken);
            return ("Visual strategy resolved.", (string?)null, Array.Empty<string>());
        }));

        if (requirement?.RequiresSkyfield == true)
            steps.Add(await ExecuteStepAsync("AstronomyVisibilityPreview", async () => { _ = await planning.BuildAstronomyVisibilityPreviewAsync(plan.Id, cancellationToken); return ("Astronomy visibility preview completed.", null, Array.Empty<string>()); }));
        else
            steps.Add(Skipped("AstronomyVisibilityPreview", "Category does not require Skyfield."));

        if (string.Equals(request.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
            steps.Add(await ExecuteStepAsync("DailySkyContextPreview", async () => { _ = await planning.BuildDailySkyGuideContextPreviewAsync(plan.Id, cancellationToken); return ("DailySkyGuide context built.", null, Array.Empty<string>()); }));
        else
            steps.Add(Skipped("DailySkyContextPreview", "Only supported for DailySkyGuide."));

        if (requirement?.RequiresStellarium == true)
            steps.Add(await ExecuteStepAsync("StellariumScenePlanPreview", async () => { scenePlan = await planning.BuildStellariumScenePlanPreviewAsync(plan.Id, cancellationToken); return ("Stellarium scene plan preview built.", null, scenePlan.Warnings); }));
        else
            steps.Add(Skipped("StellariumScenePlanPreview", "Category does not require Stellarium."));

        if (requirement?.RequiresSscScript == true)
        {
            steps.Add(await ExecuteStepAsync("SscScriptGeneration", async () =>
            {
                scenePlan ??= await planning.BuildStellariumScenePlanPreviewAsync(plan.Id, cancellationToken);
                foreach (var scene in scenePlan.Scenes.OrderBy(s => s.SortOrder))
                    _ = await scriptGenerator.GenerateAsync(scenePlan, scene, cancellationToken);
                return ($"Generated {scenePlan.Scenes.Count} SSC scripts.", null, scenePlan.Warnings);
            }));
        }
        else
            steps.Add(Skipped("SscScriptGeneration", "Category does not require SSC scripts."));

        if (request.CaptureStellariumScenes)
        {
            steps.Add(await ExecuteStepAsync("StellariumCapture", async () =>
            {
                scenePlan ??= await planning.BuildStellariumScenePlanPreviewAsync(plan.Id, cancellationToken);
                var capture = await stellariumCaptureExecutor.CaptureAsync(scenePlan, new(plan.Id, DryRun: false, request.OverwriteExisting, request.Diagnostics), cancellationToken);
                if (!capture.Success) return ("Stellarium capture failed.", capture.ErrorMessage ?? "Capture failed.", capture.Warnings);
                return ("Stellarium capture completed.", null, capture.Warnings);
            }, allowBusinessFailure: true));
        }
        else steps.Add(Skipped("StellariumCapture", "captureStellariumScenes is false."));

        steps.Add(await ExecuteStepAsync("VisualAssetsPreview", async () => { var p = await visualAssetPackager.BuildPackageAsync(plan.Id, cancellationToken); return ("Visual assets preview built.", p.Success ? null : "Visual assets preview failed.", p.Warnings); }, allowBusinessFailure: true));
        steps.Add(await ExecuteStepAsync("AssetAwareManualRunPackage", async () => { var pkg = await manualRunPreparationService.PrepareAsync(plan.Id, cancellationToken); return ("Manual run package prepared.", pkg.CanRunManually ? null : "Manual run package is not runnable yet.", pkg.Warnings); }, allowBusinessFailure: true));

        if (string.Equals(request.ContentCategoryCode, "DailySkyGuide", StringComparison.OrdinalIgnoreCase))
            steps.Add(await ExecuteStepAsync("DailySkyGuideAssetContext", async () => { var ctx = await dailySkyGuideAssetContextService.BuildAsync(plan.Id, cancellationToken); return ("DailySkyGuide asset context prepared.", null, ctx.Warnings); }));
        else
            steps.Add(Skipped("DailySkyGuideAssetContext", "Only supported for DailySkyGuide."));

        var compositionPlanner = compositionPlannerResolver.Resolve(request.ContentCategoryCode);
        if (compositionPlanner is null)
            steps.Add(Skipped("CompositionPlan", "Composition planner is not configured for this category."));
        else
            steps.Add(await ExecuteStepAsync("CompositionPlan", async () => { var cp = await compositionPlanner.BuildAsync(plan.Id, cancellationToken); return ("Composition plan generated.", cp.ReadyForComposition ? null : "Composition plan is not ready for composition.", cp.Warnings); }, allowBusinessFailure: true));

        if (request.GeneratePreviewVideo)
            steps.Add(await ExecuteStepAsync("PreviewVideoGeneration", async () => { var pv = await previewVideoGenerator.GenerateAsync(plan.Id, new(OverwriteExisting: request.OverwriteExisting, Diagnostics: request.Diagnostics), cancellationToken); return ("Preview video generation completed.", pv.Success ? null : pv.ErrorMessage ?? "Preview generation failed.", pv.Warnings); }, allowBusinessFailure: true));
        else
            steps.Add(Skipped("PreviewVideoGeneration", "generatePreviewVideo is false."));

        steps.Add(await ExecuteStepAsync("PipelineRequestPreview", async () =>
        {
            var preview = await planning.BuildPipelineRequestPreviewAsync(plan.Id, cancellationToken);
            runPipelineRequest = preview.PipelineRequest with { PublishToYouTube = false };
            return ("Pipeline request preview built.", null, preview.Warnings);
        }));

        var success = steps.All(s => s.Status is "Completed" or "Skipped");
        warnings.AddRange(steps.SelectMany(s => s.Warnings));
        return BuildResponse(success, request.ContentCategoryCode, planId, steps, runPipelineRequest, warnings, success ? null : "One or more preparation steps failed.");
    }

    private static ManualCategoryPreparationResponse BuildResponse(bool success, string categoryCode, Guid? planId, IReadOnlyList<ManualCategoryPreparationStepResult> steps, RunPipelineRequest? runPipelineRequest, IReadOnlyList<string> warnings, string? error)
    {
        var safetyWarnings = warnings.ToList();
        const string publishingDisabledWarning = "Publishing is disabled for category preparation flow until category output quality is verified.";
        if (!safetyWarnings.Contains(publishingDisabledWarning, StringComparer.Ordinal))
            safetyWarnings.Add(publishingDisabledWarning);

        var sanitizedRequest = runPipelineRequest is null
            ? null
            : runPipelineRequest with { PublishToYouTube = false };

        return new(
            planId,
            categoryCode,
            success,
            steps,
            sanitizedRequest,
            safetyWarnings,
            error,
            PublishingEnabled: false,
            PublishToYouTube: false,
            PublishToFacebook: false,
            PublishToInstagram: false);
    }

    private static ManualCategoryPreparationStepResult Skipped(string name, string message) => new(name, "Skipped", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, message, null, []);

    private async Task<ManualCategoryPreparationStepResult> ExecuteStepAsync(string name, Func<Task<(string Message, string? Error, IReadOnlyCollection<string> Warnings)>> action, bool allowBusinessFailure = false)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            logger.LogInformation("Starting manual category preparation step {StepName}", name);
            var (message, error, warnings) = await action();
            var status = error is null ? "Completed" : (allowBusinessFailure ? "Failed" : "Failed");
            var finished = DateTimeOffset.UtcNow;
            logger.LogInformation("Finished manual category preparation step {StepName} with status {Status}", name, status);
            return new(name, status, started, finished, (long)(finished - started).TotalMilliseconds, message, error, warnings.ToList());
        }
        catch (Exception ex)
        {
            var finished = DateTimeOffset.UtcNow;
            logger.LogError(ex, "Manual category preparation step {StepName} failed", name);
            return new(name, "Failed", started, finished, (long)(finished - started).TotalMilliseconds, $"{name} failed.", ex.Message, []);
        }
    }
}
