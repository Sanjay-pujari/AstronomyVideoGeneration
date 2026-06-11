using System.Reflection;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanBatchGenerationService(
    MediaFactoryDbContext db,
    IAstronomyAssetPlanningService assetPlanning,
    IAstronomyAssetProductionJobService assetJobs,
    IVisualAssetGenerationService visualAssets,
    ISceneRenderer sceneRenderer,
    IContentPlanProductionExecutionService productionExecution,
    ILogger<ContentPlanBatchGenerationService> logger) : IContentPlanBatchGenerationService, IContentPlanGenerationReadinessService
{
    private const int DefaultMaxPlans = 1;
    private const int MaxPlanLimit = 10;
    private static readonly string[] RunnableStatuses = ["Draft", "Planned", "Approved"];
    private static readonly string[] DryRunSteps =
    [
        "Would create content_pipeline_execution",
        "Would generate compatible AssetPlanJson for selected plans when missing or imported without asset requirements",
        "Would generate hero asset",
        "Would generate thumbnail",
        "Would generate short narration",
        "Would generate long narration",
        "Would generate short TTS",
        "Would generate long TTS",
        "Would generate short video",
        "Would generate long video"
    ];

    public async Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var requestedTitles = request.PlanTitles!
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var maxPlans = Math.Clamp(request.MaxPlans <= 0 ? DefaultMaxPlans : request.MaxPlans, 1, MaxPlanLimit);
        var candidates = await LoadPlanCandidatesAsync(request.Year, request.RegionId, request.Language, cancellationToken);

        var selection = SelectPlans(candidates, requestedTitles, request.OnlyHighPriority, maxPlans);
        var selectedPlanEntities = selection.SelectedPlans;
        var warnings = selection.Warnings;
        var selectedPlans = selectedPlanEntities
            .Select(ToSelectedPlan)
            .ToArray();

        if (selectedPlans.Length == 0)
        {
            return new BatchGenerateFromPlansResponse(
                Success: true,
                DryRun: request.DryRun,
                RequestedTitleCount: requestedTitles.Length,
                SelectedPlanCount: 0,
                MaxPlans: maxPlans,
                SelectedPlans: selectedPlans,
                Steps: request.DryRun ? DryRunSteps.Cast<object>().ToArray() : [],
                Warnings: warnings,
                Errors: [],
                UseProductionPipeline: request.UseProductionPipeline,
                UsedPlaceholderVisuals: !request.UseProductionPipeline);
        }

        if (request.UseProductionPipeline)
        {
            if (selectedPlans.Length != 1)
                throw new ArgumentException("Production pipeline batch generation is currently locked to exactly one selected plan.");

            logger.LogInformation("Using Astronomy V1 production pipeline for content plan {PlanId}", selectedPlans[0].ContentGenerationPlanId);

            var execution = await productionExecution.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                selectedPlans[0].ContentGenerationPlanId,
                request.DryRun,
                request.OverwriteExisting,
                request.StartPhaseNo,
                request.EndPhaseNo,
                request.RetryFailedOnly), cancellationToken);

            return new BatchGenerateFromPlansResponse(
                Success: execution.Success,
                DryRun: request.DryRun,
                RequestedTitleCount: requestedTitles.Length,
                SelectedPlanCount: selectedPlans.Length,
                MaxPlans: maxPlans,
                SelectedPlans: selectedPlans,
                Steps: (execution.PhaseResults is { Count: > 0 } ? execution.PhaseResults.Cast<object>().ToArray() : execution.PlannedProductionSteps.Cast<object>().ToArray()),
                Warnings: warnings.Concat(execution.Warnings.Select(w => new BatchGenerateFromPlansWarning(selectedPlans[0].Title, true, true, w))).ToArray(),
                Errors: execution.Errors,
                Results: [execution],
                UseProductionPipeline: true,
                UsedPlaceholderVisuals: false,
                PlanId: execution.PlanId,
                Title: execution.Title,
                OutputRoot: execution.OutputRoot,
                QuestionEngineCompleted: execution.QuestionEngineCompleted,
                ShortScenesGenerated: execution.ShortScenesGenerated,
                LongScenesGenerated: execution.LongScenesGenerated,
                HeroGenerated: execution.HeroGenerated,
                ThumbnailsGenerated: execution.ThumbnailsGenerated,
                ShortNarrationGenerated: execution.ShortNarrationGenerated,
                LongNarrationGenerated: execution.LongNarrationGenerated,
                ShortTtsGenerated: execution.ShortTtsGenerated,
                LongTtsGenerated: execution.LongTtsGenerated,
                ShortVideoGenerated: execution.ShortVideoGenerated,
                LongVideoGenerated: execution.LongVideoGenerated,
                FinalShortVideoPath: execution.FinalShortVideoPath,
                FinalLongVideoPath: execution.FinalLongVideoPath,
                ProductionPipelineRequest: execution.ProductionPipelineRequest,
                PlannedSteps: execution.PlannedProductionSteps);
        }

        logger.LogInformation("Using placeholder planning pipeline");

        if (request.DryRun)
        {
            return new BatchGenerateFromPlansResponse(
                Success: true,
                DryRun: true,
                RequestedTitleCount: requestedTitles.Length,
                SelectedPlanCount: selectedPlans.Length,
                MaxPlans: maxPlans,
                SelectedPlans: selectedPlans,
                Steps: DryRunSteps.Cast<object>().ToArray(),
                Warnings: warnings,
                Errors: []);
        }

        var planIds = selectedPlans.Select(p => p.ContentGenerationPlanId).ToArray();
        var steps = new List<object>();

        steps.Add(await ExecuteStepAsync(
            "GenerateAssetPlans",
            () => assetPlanning.GenerateAssetPlansAsync(new AstronomyAssetPlanningRequest(
                RegionId: request.RegionId,
                PlanIds: planIds,
                MaxPlans: selectedPlans.Length,
                DryRun: false,
                OverwriteExisting: false), cancellationToken)));

        steps.Add(await ExecuteStepAsync(
            "CreateAssetProductionJobs",
            () => assetJobs.CreateAssetProductionJobsAsync(new AstronomyAssetProductionJobRequest(
                PlanIds: planIds,
                RegionId: request.RegionId,
                MaxPlans: selectedPlans.Length,
                DryRun: false), cancellationToken)));

        steps.Add(await ExecuteStepAsync(
            "GenerateVisualAssets",
            () => visualAssets.GenerateVisualAssetsAsync(new VisualAssetGenerationRequest(
                RegionId: request.RegionId,
                PlanIds: planIds,
                MaxPlans: selectedPlans.Length,
                DryRun: false,
                OverwriteExisting: false), cancellationToken)));

        steps.Add(await ExecuteStepAsync(
            "RenderSceneVideos",
            () => sceneRenderer.RenderScenesAsync(new SceneRenderingRequest(
                RegionId: request.RegionId,
                PlanIds: planIds,
                MaxPlans: selectedPlans.Length,
                DryRun: false,
                OverwriteExisting: false), cancellationToken)));

        var errors = steps.OfType<BatchGenerateFromPlansStepResult>()
            .Where(s => string.Equals(s.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(s => $"{s.StepName}: {s.ErrorMessage}")
            .ToArray();

        var counters = BuildCounters(selectedPlans.Length, steps, errors.Length);
        return new BatchGenerateFromPlansResponse(
            errors.Length == 0,
            false,
            requestedTitles.Length,
            selectedPlans.Length,
            maxPlans,
            selectedPlans,
            steps,
            warnings,
            errors,
            counters.AssetPlansGenerated,
            counters.AssetJobsCreated,
            counters.VisualAssetsGenerated,
            counters.SceneVideosRendered,
            0,
            0,
            counters.FailedPlans,
            []);
    }

    public async Task<PlansReadyForGenerationResponse> GetPlansReadyForGenerationAsync(
        int year,
        string regionId,
        string language,
        bool onlyHighPriority,
        int? maxPlans,
        CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100) throw new ArgumentException("Year must be between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(regionId)) throw new ArgumentException("RegionId is required.");
        if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("Language is required.");
        if (maxPlans is < 1) throw new ArgumentException("MaxPlans must be greater than zero.");

        var plans = (await LoadPlanCandidatesAsync(year, regionId.Trim(), language.Trim(), cancellationToken))
            .Where(p => IsStatusRunnable(p) && IsAstronomyEventRunnable(p) && (!onlyHighPriority || IsHighPriority(p)))
            .OrderByDescending(p => p.PriorityScore ?? 0m)
            .ThenBy(p => p.Priority)
            .ThenBy(p => p.ScheduledUtc)
            .ToArray();
        var returnedPlans = plans
            .Take(maxPlans ?? plans.Length)
            .Select(ToReadyForGenerationItem)
            .ToArray();

        return new PlansReadyForGenerationResponse(year, regionId, language, plans.Length, returnedPlans);
    }

    private async Task<ContentGenerationPlan[]> LoadPlanCandidatesAsync(int year, string regionId, string language, CancellationToken cancellationToken)
    {
        var yearStart = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = yearStart.AddYears(1);

        return await db.ContentGenerationPlans.AsNoTracking()
            .Include(p => p.AstronomyEventIntelligence)
            .Where(p => p.RegionId == regionId && p.Language == language)
            .Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value >= yearStart && p.ScheduledUtc.Value < yearEnd)
            .ToArrayAsync(cancellationToken);
    }

    private static SelectionResult SelectPlans(IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, bool onlyHighPriority, int maxPlans)
    {
        var selected = new List<ContentGenerationPlan>();
        var warnings = new List<BatchGenerateFromPlansWarning>();
        var selectedIds = new HashSet<Guid>();

        foreach (var requestedTitle in requestedTitles)
        {
            var matches = FindMatches(candidates, requestedTitle)
                .Where(p => !selectedIds.Contains(p.Id))
                .ToArray();

            if (matches.Length == 0)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, false, false, "No title/source/event match found"));
                continue;
            }

            var runnableMatches = matches
                .Where(IsStatusRunnable)
                .Where(IsAstronomyEventRunnable)
                .Where(p => !onlyHighPriority || IsHighPriority(p))
                .OrderBy(p => IsExactMatch(p, requestedTitle) ? 0 : 1)
                .ThenByDescending(p => p.PriorityScore ?? 0m)
                .ThenBy(p => p.Priority)
                .ToArray();

            if (runnableMatches.Length == 0)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, false, BuildExclusionReason(matches[0], onlyHighPriority)));
                continue;
            }

            if (selected.Count >= maxPlans)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, false, $"Excluded because selection was capped to maxPlans={maxPlans}"));
                continue;
            }

            selected.Add(runnableMatches[0]);
            selectedIds.Add(runnableMatches[0].Id);
        }

        return new SelectionResult(selected, warnings);
    }

    private static IReadOnlyList<ContentGenerationPlan> FindMatches(IEnumerable<ContentGenerationPlan> candidates, string requestedTitle)
    {
        var exact = candidates.Where(p => IsExactMatch(p, requestedTitle)).ToArray();
        return exact.Length > 0 ? exact : candidates.Where(p => IsContainsMatch(p, requestedTitle)).ToArray();
    }

    private static bool IsExactMatch(ContentGenerationPlan plan, string requestedTitle)
    {
        var normalizedRequestedTitle = Normalize(requestedTitle);
        return normalizedRequestedTitle.Length > 0 && MatchValues(plan).Any(value =>
        {
            var normalizedValue = Normalize(value);
            return normalizedValue.Length > 0 && string.Equals(normalizedValue, normalizedRequestedTitle, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool IsContainsMatch(ContentGenerationPlan plan, string requestedTitle)
    {
        var normalizedRequestedTitle = Normalize(requestedTitle);
        if (normalizedRequestedTitle.Length == 0) return false;
        return MatchValues(plan).Any(value =>
        {
            var normalizedValue = Normalize(value);
            return normalizedValue.Length > 0
                && (normalizedValue.Contains(normalizedRequestedTitle, StringComparison.OrdinalIgnoreCase)
                    || normalizedRequestedTitle.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static IEnumerable<string?> MatchValues(ContentGenerationPlan plan)
    {
        yield return plan.Title;
        yield return ReadOptionalStringProperty(plan, "Name");
        yield return plan.SourceExternalEventId;
        yield return plan.AstronomyEventIntelligence?.Title;
        yield return ReadOptionalStringProperty(plan.AstronomyEventIntelligence, "ShortTitle") ?? plan.AstronomyEventIntelligence?.Summary;
        yield return plan.AstronomyEventIntelligence?.ExternalEventId;
    }

    private static string? ReadOptionalStringProperty(object? value, string propertyName)
        => value?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(value) as string;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static bool IsStatusRunnable(ContentGenerationPlan plan)
        => RunnableStatuses.Contains(plan.Status, StringComparer.OrdinalIgnoreCase)
            || RunnableStatuses.Contains(plan.PlanStatus, StringComparer.OrdinalIgnoreCase);

    private static bool IsAstronomyEventRunnable(ContentGenerationPlan plan)
    {
        var evt = plan.AstronomyEventIntelligence;
        return evt is not null
            && evt.AutoGenerateAllowed
            && !string.Equals(evt.VerificationStatus, "NeedsManualReview", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(evt.ContentStrategy, "SkipAutoGeneration", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(evt.ContentStrategy, "EducationalOnly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHighPriority(ContentGenerationPlan plan) => plan.Priority <= 10 || plan.PriorityScore >= 7.5m;

    private static string BuildExclusionReason(ContentGenerationPlan plan, bool onlyHighPriority)
    {
        if (!IsStatusRunnable(plan))
            return $"Excluded because status was {plan.Status} and planStatus was {plan.PlanStatus}; allowed status or planStatus values are Draft, Planned, Approved";
        if (plan.AstronomyEventIntelligence is null)
            return "Excluded because linked AstronomyEventIntelligence was missing";
        if (!plan.AstronomyEventIntelligence.AutoGenerateAllowed)
            return "Excluded because linked astronomy event AutoGenerateAllowed was false";
        if (string.Equals(plan.AstronomyEventIntelligence.VerificationStatus, "NeedsManualReview", StringComparison.OrdinalIgnoreCase))
            return "Excluded because linked astronomy event VerificationStatus was NeedsManualReview";
        if (string.Equals(plan.AstronomyEventIntelligence.ContentStrategy, "SkipAutoGeneration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.AstronomyEventIntelligence.ContentStrategy, "EducationalOnly", StringComparison.OrdinalIgnoreCase))
            return $"Excluded because linked astronomy event ContentStrategy was {plan.AstronomyEventIntelligence.ContentStrategy}";
        if (onlyHighPriority && !IsHighPriority(plan))
            return "Excluded because onlyHighPriority was true and plan priority was not high";
        return "Excluded by runnable filters";
    }

    private static BatchCounters BuildCounters(int selectedPlanCount, IReadOnlyList<object> steps, int errorCount)
    {
        var assetPlansGenerated = 0;
        var assetJobsCreated = 0;
        var visualAssetsGenerated = 0;
        var sceneVideosRendered = 0;

        foreach (var step in steps.OfType<BatchGenerateFromPlansStepResult>())
        {
            switch (step.Result)
            {
                case AstronomyAssetPlanningResult assetPlanningResult:
                    assetPlansGenerated += assetPlanningResult.SavedCount;
                    break;
                case AstronomyAssetProductionJobResult jobResult:
                    assetJobsCreated += jobResult.SavedCount;
                    break;
                case VisualAssetGenerationResponse visualResult:
                    visualAssetsGenerated += visualResult.GeneratedVisualCount;
                    break;
                case SceneRenderingResponse sceneResult:
                    sceneVideosRendered += sceneResult.CompletedCount;
                    break;
            }
        }

        return new BatchCounters(assetPlansGenerated, assetJobsCreated, visualAssetsGenerated, sceneVideosRendered, errorCount == 0 ? 0 : selectedPlanCount);
    }

    private sealed record BatchCounters(int AssetPlansGenerated, int AssetJobsCreated, int VisualAssetsGenerated, int SceneVideosRendered, int FailedPlans);

    private async Task<BatchGenerateFromPlansStepResult> ExecuteStepAsync<T>(string stepName, Func<Task<T>> action)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await action();
            var finished = DateTimeOffset.UtcNow;
            return new BatchGenerateFromPlansStepResult(stepName, "Completed", started, finished, (long)(finished - started).TotalMilliseconds, $"{stepName} completed.", null, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Batch content plan generation step {StepName} failed with a business validation error.", stepName);
            var finished = DateTimeOffset.UtcNow;
            return new BatchGenerateFromPlansStepResult(stepName, "Failed", started, finished, (long)(finished - started).TotalMilliseconds, $"{stepName} failed.", ex.Message, null);
        }
    }

    private static BatchGenerateFromPlansSelectedPlan ToSelectedPlan(ContentGenerationPlan plan)
        => new(
            plan.Id,
            plan.Title ?? string.Empty,
            plan.ContentCategoryCode,
            plan.PlannedFormat,
            plan.RegionId,
            plan.Language,
            plan.ScheduledUtc,
            plan.Status,
            plan.PlanStatus,
            plan.Priority,
            plan.PriorityScore,
            plan.SourceExternalEventId,
            plan.AstronomyEventIntelligence?.Title,
            ReadOptionalStringProperty(plan.AstronomyEventIntelligence, "ShortTitle") ?? plan.AstronomyEventIntelligence?.Summary,
            plan.AstronomyEventIntelligence?.ExternalEventId);

    private static PlanReadyForGenerationItem ToReadyForGenerationItem(ContentGenerationPlan plan)
        => new(
            plan.Id,
            plan.Title ?? string.Empty,
            plan.SourceExternalEventId,
            plan.Status,
            plan.PlanStatus,
            ResolvePriorityLabel(plan),
            plan.PriorityScore ?? 0m,
            plan.ContentCategoryCode,
            plan.PlannedFormat,
            plan.ScheduledUtc,
            plan.RequestedOutputTypesJson,
            plan.AstronomyEventIntelligence?.Title,
            ReadOptionalStringProperty(plan.AstronomyEventIntelligence, "ShortTitle") ?? plan.AstronomyEventIntelligence?.Summary,
            plan.AstronomyEventIntelligence?.EventType,
            plan.AstronomyEventIntelligence?.VerificationStatus,
            plan.AstronomyEventIntelligence?.AutoGenerateAllowed,
            plan.AstronomyEventIntelligence?.ContentStrategy);

    private static string ResolvePriorityLabel(ContentGenerationPlan plan)
        => IsHighPriority(plan) ? "High" : plan.Priority <= 50 || plan.PriorityScore >= 5m ? "Medium" : "Low";

    private static void ValidateRequest(BatchGenerateFromPlansRequest request)
    {
        if (request.Year is < 2000 or > 2100) throw new ArgumentException("Year must be between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(request.RegionId)) throw new ArgumentException("RegionId is required.");
        if (string.IsNullOrWhiteSpace(request.Language)) throw new ArgumentException("Language is required.");
        if (request.MaxPlans is < 1 or > MaxPlanLimit) throw new ArgumentException($"MaxPlans must be between 1 and {MaxPlanLimit}.");
        if (request.PlanTitles is not { Count: > 0 } || request.PlanTitles.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one explicit plan title is required for safe batch generation.");
    }

    private sealed record SelectionResult(IReadOnlyList<ContentGenerationPlan> SelectedPlans, IReadOnlyList<BatchGenerateFromPlansWarning> Warnings);
}
