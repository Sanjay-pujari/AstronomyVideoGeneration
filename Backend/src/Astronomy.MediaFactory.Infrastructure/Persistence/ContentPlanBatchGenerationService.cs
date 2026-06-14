using System.Reflection;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanBatchGenerationService(
    MediaFactoryDbContext db,
    IAstronomyAssetPlanningService assetPlanning,
    IAstronomyAssetProductionJobService assetJobs,
    IVisualAssetGenerationService visualAssets,
    ISceneRenderer sceneRenderer,
    IContentPlanProductionExecutionService productionExecution,
    IProductionRunningRecoveryService runningRecovery,
    IOptions<ProductionPipelineOptions> productionPipelineOptions,
    ILogger<ContentPlanBatchGenerationService> logger) : IContentPlanBatchGenerationService, IContentPlanGenerationReadinessService
{
    private const int DefaultMaxPlans = 1;
    private const int MaxPlanLimit = 10;
    private static readonly string[] RunnableStatuses = ["Draft", "Planned", "Approved"];
    private static readonly string[] RetryRunnableStatuses = ["ProductionFailed"];
    private static readonly string[] RunningRecoveryStatuses = ["ProductionRunning"];
    private static readonly string[] RebuildRunnableStatuses = ["Draft", "Planned", "Approved", "ProductionFailed", "ProductionCompleted"];
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

        var requestedTitles = (request.PlanTitles ?? [])
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var maxPlans = Math.Clamp(request.MaxPlans <= 0 ? DefaultMaxPlans : request.MaxPlans, 1, MaxPlanLimit);
        var candidates = await LoadPlanCandidatesAsync(request.Year, request.RegionId, request.Language, cancellationToken);

        IReadOnlyList<BatchGenerateFromPlansWarning> recoveryWarnings = request.DryRun
            ? Array.Empty<BatchGenerateFromPlansWarning>()
            : await RecoverRunningCandidatesAsync(candidates, requestedTitles, request, cancellationToken);
        var executionMode = ResolveExecutionMode(request);
        if (recoveryWarnings.Count > 0 && request.RetryFailedOnly && request.AllowFailedPlanRetry)
            executionMode = ContentPlanExecutionMode.RetryFailed;
        var recoveryMode = executionMode == ContentPlanExecutionMode.RecoverRunning;
        var selection = SelectPlans(candidates, requestedTitles, request.PlanId, request.OnlyHighPriority, maxPlans, executionMode, recoveryMode, ResolveRunningPlanRecoveryStaleAfter(request), request.AllowCompletedPlanRerun);
        var selectedPlanEntities = selection.SelectedPlans;
        var warnings = recoveryWarnings.Concat(selection.Warnings).ToArray();
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

            var requestedStartPhaseNo = ResolveStartPhaseNo(request, executionMode);
            var requestedEndPhaseNo = ResolveEndPhaseNo(request);

            var execution = await productionExecution.ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(
                selectedPlans[0].ContentGenerationPlanId,
                request.DryRun,
                request.OverwriteExisting,
                requestedStartPhaseNo,
                requestedEndPhaseNo,
                executionMode == ContentPlanExecutionMode.RetryFailed || recoveryMode || request.RetryFailedOnly,
                executionMode,
                request.AllowCompletedPlanRerun,
                request.ArchivePreviousRun,
                request.RebuildIntelligence,
                request.EnableSceneVariants), cancellationToken);

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
                PlannedSteps: execution.PlannedProductionSteps,
                LastCompletedPhaseNo: execution.LastCompletedPhaseNo,
                LastFailedPhaseNo: execution.LastFailedPhaseNo,
                ExecutionMode: execution.ExecutionMode,
                CompletedPlanRerun: execution.CompletedPlanRerun,
                PreviousOutputArchived: execution.PreviousOutputArchived,
                ArchivePath: execution.ArchivePath,
                DeletedOutputFolders: execution.DeletedOutputFolders,
                StartPhaseNo: execution.StartPhaseNo,
                EndPhaseNo: execution.EndPhaseNo,
                RequestedOutputCompletion: execution.RequestedOutputCompletion,
                PartialPhaseExecution: execution.PartialPhaseExecution,
                RequestedStartPhase: execution.RequestedStartPhase ?? requestedStartPhaseNo,
                RequestedEndPhase: execution.RequestedEndPhase ?? requestedEndPhaseNo,
                ExpandedStartPhase: execution.ExpandedStartPhase ?? execution.StartPhaseNo ?? requestedStartPhaseNo,
                ExpandedEndPhase: execution.ExpandedEndPhase ?? execution.EndPhaseNo ?? requestedEndPhaseNo,
                PartialPhaseSuccess: execution.PartialPhaseSuccess,
                DependencyExpansionApplied: execution.DependencyExpansionApplied);
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

        return await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
            .Where(p => p.RegionId == regionId && p.Language == language)
            .Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value >= yearStart && p.ScheduledUtc.Value < yearEnd)
            .ToArrayAsync(cancellationToken);
    }

    private static SelectionResult SelectPlans(IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, Guid? requestedPlanId, bool onlyHighPriority, int maxPlans, ContentPlanExecutionMode executionMode, bool recoveryMode, TimeSpan runningPlanRecoveryStaleAfter, bool allowCompletedPlanRerun)
    {
        var allowedStatuses = AllowedStatusesFor(executionMode);
        var completedRerunMode = IsCompletedRerunMode(executionMode);
        var selected = new List<ContentGenerationPlan>();
        var warnings = new List<BatchGenerateFromPlansWarning>();
        var selectedIds = new HashSet<Guid>();

        if (requestedPlanId is { } planId)
        {
            SelectRequestedPlan(candidates, planId, onlyHighPriority, maxPlans, recoveryMode, runningPlanRecoveryStaleAfter, allowedStatuses, selected, warnings, selectedIds, completedRerunMode, allowCompletedPlanRerun);
        }

        foreach (var requestedTitle in requestedTitles)
        {
            var matches = (recoveryMode || completedRerunMode ? FindExactMatches(candidates, requestedTitle) : FindMatches(candidates, requestedTitle))
                .Where(p => !selectedIds.Contains(p.Id))
                .ToArray();

            if (matches.Length == 0)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, false, false, "No title/source/event match found"));
                continue;
            }

            var runnableMatches = matches
                .Where(p => IsStatusRunnable(p, allowedStatuses))
                .Where(p => !IsProductionRunning(p) || CanRecoverRunningPlan(p, recoveryMode, runningPlanRecoveryStaleAfter))
                .Where(p => IsAstronomyEventRunnable(p, allowManualValidationAutoGenerateBypass: false))
                .Where(p => !onlyHighPriority || IsHighPriority(p))
                .OrderBy(p => IsExactMatch(p, requestedTitle) ? 0 : 1)
                .ThenByDescending(p => p.PriorityScore ?? 0m)
                .ThenBy(p => p.Priority)
                .ToArray();

            if (runnableMatches.Length == 0)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, false, BuildExclusionReason(
                    matches[0],
                    onlyHighPriority,
                    allowedStatuses,
                    recoveryMode,
                    runningPlanRecoveryStaleAfter,
                    completedRerunMode,
                    allowCompletedPlanRerun,
                    requestedPlanTitle: requestedTitle,
                    isExactTarget: false)));
                continue;
            }

            if (selected.Count >= maxPlans)
            {
                warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, false, $"Excluded because selection was capped to maxPlans={maxPlans}"));
                continue;
            }

            var selectedPlan = runnableMatches[0];
            selected.Add(selectedPlan);
            selectedIds.Add(selectedPlan.Id);
            AddManualValidationAutoGenerateWarningIfNeeded(selectedPlan, requestedTitle, warnings, isExactPlanIdTarget: false);
        }

        return new SelectionResult(selected, warnings);
    }

    private static void SelectRequestedPlan(IReadOnlyList<ContentGenerationPlan> candidates, Guid requestedPlanId, bool onlyHighPriority, int maxPlans, bool recoveryMode, TimeSpan runningPlanRecoveryStaleAfter, IReadOnlyCollection<string> allowedStatuses, List<ContentGenerationPlan> selected, List<BatchGenerateFromPlansWarning> warnings, HashSet<Guid> selectedIds, bool completedRerunMode, bool allowCompletedPlanRerun)
    {
        var plan = candidates.FirstOrDefault(p => p.Id == requestedPlanId);
        if (plan is null)
        {
            warnings.Add(new BatchGenerateFromPlansWarning(requestedPlanId.ToString("D"), false, false, "No planId match found"));
            return;
        }

        if (selected.Count >= maxPlans)
        {
            warnings.Add(new BatchGenerateFromPlansWarning(requestedPlanId.ToString("D"), true, false, $"Excluded because selection was capped to maxPlans={maxPlans}"));
            return;
        }

        if (selectedIds.Contains(plan.Id)) return;
        if (!IsStatusRunnable(plan, allowedStatuses)
            || (IsProductionRunning(plan) && !CanRecoverRunningPlan(plan, recoveryMode, runningPlanRecoveryStaleAfter))
            || !IsAstronomyEventRunnable(plan, AllowManualValidationAutoGenerateBypass(plan, isExactPlanIdTarget: true))
            || (onlyHighPriority && !IsHighPriority(plan)))
        {
            warnings.Add(new BatchGenerateFromPlansWarning(requestedPlanId.ToString("D"), true, false, BuildExclusionReason(
                plan,
                onlyHighPriority,
                allowedStatuses,
                recoveryMode,
                runningPlanRecoveryStaleAfter,
                completedRerunMode,
                allowCompletedPlanRerun,
                requestedPlanTitle: null,
                isExactTarget: true)));
            return;
        }

        selected.Add(plan);
        selectedIds.Add(plan.Id);
        AddManualValidationAutoGenerateWarningIfNeeded(plan, requestedPlanId.ToString("D"), warnings, isExactPlanIdTarget: true);
    }

    private async Task<IReadOnlyList<BatchGenerateFromPlansWarning>> RecoverRunningCandidatesAsync(IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        if (!request.UseProductionPipeline) return [];

        var targets = ResolveRequestedRunningRecoveryCandidates(candidates, requestedTitles, request)
            .Where(IsProductionRunning)
            .ToArray();
        if (targets.Length == 0) return [];

        var warnings = new List<BatchGenerateFromPlansWarning>();
        var forceRecovery = request.AllowRunningPlanRecovery && IsExplicitRunningRecoveryRequest(request);
        foreach (var target in targets)
        {
            var result = forceRecovery
                ? await runningRecovery.RecoverRunningExecutionAsync(target.Id, cancellationToken)
                : await runningRecovery.RecoverStaleRunningExecutionAsync(target.Id, cancellationToken);

            if (!result.Recovered) continue;

            target.Status = "ProductionFailed";
            target.PlanStatus = "ProductionFailed";
            target.FailureReason = "Automatically marked failed due to stale running execution.";
            warnings.Add(new BatchGenerateFromPlansWarning(target.Title ?? target.Id.ToString("D"), true, true, result.Warning ?? $"Recovered stale running execution for plan {target.Id:D}."));
        }

        return warnings;
    }

    private static IReadOnlyList<ContentGenerationPlan> ResolveRequestedRunningRecoveryCandidates(IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, BatchGenerateFromPlansRequest request)
    {
        var selected = new List<ContentGenerationPlan>();
        var selectedIds = new HashSet<Guid>();
        if (request.PlanId is { } planId)
        {
            var plan = candidates.FirstOrDefault(p => p.Id == planId);
            if (plan is not null && selectedIds.Add(plan.Id)) selected.Add(plan);
        }

        foreach (var title in requestedTitles)
        {
            foreach (var plan in FindMatches(candidates, title))
            {
                if (selectedIds.Add(plan.Id)) selected.Add(plan);
            }
        }

        return selected;
    }

    private static bool IsExplicitRunningRecoveryRequest(BatchGenerateFromPlansRequest request)
    {
        var hasPlanTitle = request.PlanTitles is { Count: > 0 } && request.PlanTitles.Count(title => !string.IsNullOrWhiteSpace(title)) == 1;
        return request.AllowRunningPlanRecovery
            && request.RetryFailedOnly
            && request.AllowFailedPlanRetry
            && request.StartPhaseNo.HasValue
            && request.EndPhaseNo.HasValue
            && (request.PlanId.HasValue || hasPlanTitle);
    }

    private static IReadOnlyList<ContentGenerationPlan> FindMatches(IEnumerable<ContentGenerationPlan> candidates, string requestedTitle)
    {
        var exact = FindExactMatches(candidates, requestedTitle);
        return exact.Count > 0 ? exact : candidates.Where(p => IsContainsMatch(p, requestedTitle)).ToArray();
    }

    private static IReadOnlyList<ContentGenerationPlan> FindExactMatches(IEnumerable<ContentGenerationPlan> candidates, string requestedTitle)
        => candidates.Where(p => IsExactMatch(p, requestedTitle)).ToArray();

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

    private static ContentPlanExecutionMode ResolveExecutionMode(BatchGenerateFromPlansRequest request)
    {
        if (request.AllowRunningPlanRecovery && IsExplicitRunningRecoveryRequest(request)) return ContentPlanExecutionMode.RetryFailed;
        if (request.ExecutionMode != ContentPlanExecutionMode.Normal) return request.ExecutionMode;
        if (request.AllowRunningPlanRecovery) return ContentPlanExecutionMode.RecoverRunning;
        if (request.RetryFailedOnly || request.AllowFailedPlanRetry || request.StartPhaseNo.HasValue) return ContentPlanExecutionMode.RetryFailed;
        return ContentPlanExecutionMode.Normal;
    }

    private static IReadOnlyCollection<string> AllowedStatusesFor(ContentPlanExecutionMode executionMode)
        => executionMode switch
        {
            ContentPlanExecutionMode.RetryFailed => RetryRunnableStatuses,
            ContentPlanExecutionMode.RecoverRunning => RunningRecoveryStatuses,
            ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase or ContentPlanExecutionMode.FullRebuild => RebuildRunnableStatuses,
            _ => RunnableStatuses
        };

    private static bool IsCompletedRerunMode(ContentPlanExecutionMode executionMode)
        => executionMode is ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase or ContentPlanExecutionMode.FullRebuild;

    private static int ResolveStartPhaseNo(BatchGenerateFromPlansRequest request, ContentPlanExecutionMode executionMode)
    {
        if (request.RebuildIntelligence && executionMode == ContentPlanExecutionMode.RebuildOutputs) return 1;
        return request.StartPhaseNo ?? (executionMode == ContentPlanExecutionMode.FullRebuild ? 1 : executionMode is ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase ? 3 : 1);
    }

    private static int ResolveEndPhaseNo(BatchGenerateFromPlansRequest request) => request.EndPhaseNo ?? 20;


    private TimeSpan ResolveRunningPlanRecoveryStaleAfter(BatchGenerateFromPlansRequest request)
    {
        var minutes = request.RunningPlanRecoveryStaleAfterMinutes;
        if (!minutes.HasValue && int.TryParse(Environment.GetEnvironmentVariable("ASTROPULSE_RUNNING_PLAN_RECOVERY_STALE_AFTER_MINUTES"), out var configuredMinutes))
            minutes = configuredMinutes;
        return TimeSpan.FromMinutes(Math.Max(1, minutes ?? productionPipelineOptions.Value.StaleRunningThresholdMinutes));
    }

    private static bool IsStatusRunnable(ContentGenerationPlan plan)
        => IsStatusRunnable(plan, RunnableStatuses);

    private static bool IsStatusRunnable(ContentGenerationPlan plan, IReadOnlyCollection<string> allowedStatuses)
        => allowedStatuses.Contains(plan.Status, StringComparer.OrdinalIgnoreCase)
            || allowedStatuses.Contains(plan.PlanStatus, StringComparer.OrdinalIgnoreCase);

    private static bool IsProductionRunning(ContentGenerationPlan plan)
        => string.Equals(plan.Status, "ProductionRunning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.PlanStatus, "ProductionRunning", StringComparison.OrdinalIgnoreCase);

    private static bool IsProductionCompleted(ContentGenerationPlan plan)
        => string.Equals(plan.Status, "ProductionCompleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.PlanStatus, "ProductionCompleted", StringComparison.OrdinalIgnoreCase);

    private static bool CanRecoverRunningPlan(ContentGenerationPlan plan, bool recoveryMode, TimeSpan staleAfter)
    {
        if (!recoveryMode) return false;

        var lastActivityUtc = ResolveRunningPlanLastActivityUtc(plan);
        var isStale = DateTimeOffset.UtcNow - lastActivityUtc >= staleAfter;
        var requireStaleRecovery = string.Equals(Environment.GetEnvironmentVariable("ASTROPULSE_REQUIRE_STALE_RUNNING_PLAN_RECOVERY"), "true", StringComparison.OrdinalIgnoreCase);
        return isStale || !requireStaleRecovery;
    }

    private static DateTimeOffset ResolveRunningPlanLastActivityUtc(ContentGenerationPlan plan)
        => ReadOptionalDateTimeOffsetProperty(plan, "LastRunHeartbeat")
            ?? plan.UpdatedUtc
            ?? plan.CreatedUtc;

    private static DateTimeOffset? ReadOptionalDateTimeOffsetProperty(object? value, string propertyName)
        => value?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(value) as DateTimeOffset?;

    private static bool IsAstronomyEventRunnable(ContentGenerationPlan plan, bool allowManualValidationAutoGenerateBypass = false)
    {
        var evt = plan.AstronomyEventIntelligence;
        return evt is not null
            && (evt.AutoGenerateAllowed || allowManualValidationAutoGenerateBypass)
            && (!string.Equals(evt.VerificationStatus, "NeedsManualReview", StringComparison.OrdinalIgnoreCase) || allowManualValidationAutoGenerateBypass)
            && !string.Equals(evt.ContentStrategy, "SkipAutoGeneration", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(evt.ContentStrategy, "EducationalOnly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManualValidationPlan(ContentGenerationPlan plan)
        => !plan.GeneratedByAi
            && IsManualValidationPlanningReason(plan.PlanningReason);

    private static bool IsManualValidationPlanningReason(string? planningReason)
        => !string.IsNullOrWhiteSpace(planningReason)
            && planningReason.Contains("manual validation", StringComparison.OrdinalIgnoreCase);

    private static bool IsExactPlanTitleTarget(ContentGenerationPlan plan, string requestedTitle, int requestedTitleCount)
        => requestedTitleCount == 1
            && !string.IsNullOrWhiteSpace(requestedTitle)
            && string.Equals(Normalize(plan.Title), Normalize(requestedTitle), StringComparison.OrdinalIgnoreCase);

    private static bool AllowManualValidationAutoGenerateBypass(ContentGenerationPlan plan, bool isExactPlanIdTarget)
        => isExactPlanIdTarget
            && IsStatusRunnable(plan, RunnableStatuses)
            && plan.AstronomyEventIntelligence is { AutoGenerateAllowed: false }
            && IsManualValidationPlan(plan);

    private static void AddManualValidationAutoGenerateWarningIfNeeded(ContentGenerationPlan plan, string requestedTitle, List<BatchGenerateFromPlansWarning> warnings, bool isExactPlanIdTarget)
    {
        if (AllowManualValidationAutoGenerateBypass(plan, isExactPlanIdTarget))
            warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, true, "Selected manual validation plan even though linked event AutoGenerateAllowed=false."));
    }

    private static string BuildAutoGenerateAllowedExclusionReason(ContentGenerationPlan plan, string? requestedPlanTitle, bool isExactTarget)
    {
        var isManualValidationPlan = IsManualValidationPlan(plan);
        var shouldBypassAutoGenerateAllowed = AllowManualValidationAutoGenerateBypass(plan, isExactTarget);

        return string.Join(Environment.NewLine,
            "Excluded because linked astronomy event AutoGenerateAllowed was false.",
            $"planId={plan.Id:D}",
            $"planTitle={plan.Title}",
            $"GeneratedByAi={FormatDiagnosticBoolean(plan.GeneratedByAi)}",
            $"PlanningReason={plan.PlanningReason}",
            $"requestedPlanTitle={requestedPlanTitle}",
            $"isExactTarget={FormatDiagnosticBoolean(isExactTarget)}",
            $"isManualValidationPlan={FormatDiagnosticBoolean(isManualValidationPlan)}",
            $"shouldBypassAutoGenerateAllowed={FormatDiagnosticBoolean(shouldBypassAutoGenerateAllowed)}");
    }

    private static string FormatDiagnosticBoolean(bool value) => value ? "true" : "false";

    private static bool IsHighPriority(ContentGenerationPlan plan) => plan.Priority <= 10 || plan.PriorityScore >= 7.5m;

    private static string BuildExclusionReason(
        ContentGenerationPlan plan,
        bool onlyHighPriority,
        IReadOnlyCollection<string> allowedStatuses,
        bool recoveryMode = false,
        TimeSpan? runningPlanRecoveryStaleAfter = null,
        bool completedRerunMode = false,
        bool allowCompletedPlanRerun = false,
        string? requestedPlanTitle = null,
        bool isExactTarget = false)
    {
        if (IsProductionRunning(plan) && !CanRecoverRunningPlan(plan, recoveryMode, runningPlanRecoveryStaleAfter ?? TimeSpan.Zero))
            return "Excluded because ProductionRunning plans require explicit recovery mode with allowRunningPlanRecovery=true, retryFailedOnly=true, allowFailedPlanRetry=true, startPhaseNo, endPhaseNo, and an exact planTitle or planId";
        if (IsProductionCompleted(plan) && (!completedRerunMode || !allowCompletedPlanRerun))
            return "Excluded because ProductionCompleted plans require executionMode RebuildOutputs, RerunPhase, or FullRebuild with allowCompletedPlanRerun=true and an exact planTitle or planId";
        if (!IsStatusRunnable(plan, allowedStatuses))
            return $"Excluded because status was {plan.Status} and planStatus was {plan.PlanStatus}; allowed status or planStatus values are {string.Join(", ", allowedStatuses)}";
        if (plan.AstronomyEventIntelligence is null)
            return "Excluded because linked AstronomyEventIntelligence was missing";
        if (!plan.AstronomyEventIntelligence.AutoGenerateAllowed)
            return BuildAutoGenerateAllowedExclusionReason(plan, requestedPlanTitle, isExactTarget);
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
        var hasPlanTitle = request.PlanTitles is { Count: > 0 } && request.PlanTitles.Any(title => !string.IsNullOrWhiteSpace(title));
        if (!hasPlanTitle && !request.PlanId.HasValue)
            throw new ArgumentException("At least one explicit plan title or planId is required for safe batch generation.");
        var executionMode = ResolveExecutionMode(request);
        if (executionMode != ContentPlanExecutionMode.Normal && !request.UseProductionPipeline) throw new ArgumentException("executionMode requires useProductionPipeline=true.");
        if (IsCompletedRerunMode(executionMode))
        {
            if (!request.AllowCompletedPlanRerun) throw new ArgumentException("Completed plan reruns require allowCompletedPlanRerun=true.");
            if (!request.PlanId.HasValue && !hasPlanTitle) throw new ArgumentException("Completed plan reruns require an exact planTitle or planId.");
            if (!request.PlanId.HasValue && request.PlanTitles!.Count(title => !string.IsNullOrWhiteSpace(title)) != 1) throw new ArgumentException("Completed plan reruns require exactly one exact planTitle or planId.");
            if (request.MaxPlans != 1) throw new ArgumentException("Completed plan reruns are locked to maxPlans=1.");
            if (!request.OverwriteExisting) throw new ArgumentException("Completed plan reruns require overwriteExisting=true before output folders can be rebuilt.");
        }
        if (executionMode == ContentPlanExecutionMode.RetryFailed && request.AllowCompletedPlanRerun) throw new ArgumentException("RetryFailed mode cannot rerun ProductionCompleted plans.");
        if (executionMode == ContentPlanExecutionMode.RecoverRunning && !request.AllowRunningPlanRecovery) throw new ArgumentException("RecoverRunning requires allowRunningPlanRecovery=true.");
        if (request.AllowRunningPlanRecovery)
        {
            if (!request.UseProductionPipeline) throw new ArgumentException("allowRunningPlanRecovery requires useProductionPipeline=true.");
            if (request.MaxPlans != 1) throw new ArgumentException("allowRunningPlanRecovery is locked to maxPlans=1.");
            if (!IsExplicitRunningRecoveryRequest(request)) throw new ArgumentException("allowRunningPlanRecovery requires retryFailedOnly=true, allowFailedPlanRetry=true, startPhaseNo, endPhaseNo, and exactly one exact planTitle or planId.");
        }
    }

    private sealed record SelectionResult(IReadOnlyList<ContentGenerationPlan> SelectedPlans, IReadOnlyList<BatchGenerateFromPlansWarning> Warnings);
}
