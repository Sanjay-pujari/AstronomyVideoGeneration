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
        var requestedLanguage = NormalizeLanguage(request.Language);
        var languageMismatch = await ResolveLanguageMismatchSiblingAsync(request, cancellationToken);
        var effectivePlanId = languageMismatch.SelectedPlanId ?? request.PlanId;
        var candidates = await LoadPlanCandidatesAsync(request.Year, request.RegionId, requestedLanguage, cancellationToken);
        if (effectivePlanId.HasValue)
            candidates = await EnsureExactPlanCandidateAsync(candidates, effectivePlanId.Value, cancellationToken);

        IReadOnlyList<BatchGenerateFromPlansWarning> recoveryWarnings = request.DryRun
            ? Array.Empty<BatchGenerateFromPlansWarning>()
            : await RecoverRunningCandidatesAsync(candidates, requestedTitles, request, cancellationToken);
        var executionMode = ResolveExecutionMode(request);
        if (recoveryWarnings.Count > 0 && request.RetryFailedOnly && request.AllowFailedPlanRetry)
            executionMode = ContentPlanExecutionMode.RetryFailed;
        var recoveryMode = executionMode == ContentPlanExecutionMode.RecoverRunning;
        var exactPlanIdMode = IsExactPlanIdMode(request);
        var manualPlanExecution = request.PlanId.HasValue;
        var selection = SelectPlans(candidates, requestedTitles, effectivePlanId, request.OnlyHighPriority, maxPlans, executionMode, recoveryMode, ResolveRunningPlanRecoveryStaleAfter(request), request.AllowCompletedPlanRerun, request.UseProductionPipeline, exactPlanIdMode);
        var selectedPlanEntities = selection.SelectedPlans;
        var warnings = recoveryWarnings.Concat(selection.Warnings).ToArray();
        var selectedPlans = selectedPlanEntities
            .Select(ToSelectedPlan)
            .ToArray();

        LogExactPlanIdDiagnostics(request, selectedPlanEntities, candidates, requestedTitles, exactPlanIdMode);
        ValidateExactPlanIdSelection(effectivePlanId, selectedPlanEntities, exactPlanIdMode);

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
                UsedPlaceholderVisuals: !request.UseProductionPipeline,
                RequestedPlanId: request.PlanId,
                SelectedPlanId: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].Id : null,
                ManualPlanExecution: manualPlanExecution,
                AutoGenerateAllowed: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed : null,
                AutoGenerateAllowedIgnoredForManualRun: manualPlanExecution && selectedPlanEntities.Count == 1 && selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed == false,
                SelectionMode: manualPlanExecution ? "ManualPlanId" : "Automatic",
                RequestedPlanLanguage: languageMismatch.RequestedPlanLanguage,
                RequestedLanguage: languageMismatch.RequestedLanguage,
                LanguageMismatchDetected: languageMismatch.LanguageMismatchDetected,
                SiblingPlanFound: languageMismatch.SiblingPlanFound,
                SiblingPlanCreated: languageMismatch.SiblingPlanCreated);
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
                request.EnableSceneVariants,
                EnableSceneAssetsV3: request.EnableSceneAssetsV3,
                EnableAccurateSkyGuideV2: request.EnableAccurateSkyGuideV2,
                EnableSubtitles: request.EnableSubtitles,
                PublishApproved: request.PublishApproved,
                MotionPreviewOnly: request.MotionPreviewOnly,
                MotionV2Strength: request.MotionV2Strength,
                DependencyExpansionMode: request.DependencyExpansionMode), cancellationToken);

            ValidateExactPlanIdExecutionResult(effectivePlanId, execution.PlanId, exactPlanIdMode);

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
                DependencyExpansionApplied: execution.DependencyExpansionApplied,
                RequestedPlanId: request.PlanId,
                SelectedPlanId: execution.PlanId,
                ManualPlanExecution: manualPlanExecution,
                AutoGenerateAllowed: selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed,
                AutoGenerateAllowedIgnoredForManualRun: manualPlanExecution && selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed == false,
                SelectionMode: manualPlanExecution ? "ManualPlanId" : "Automatic",
                PublishGateChecked: execution.PublishGateChecked,
                PublishApproved: execution.PublishApproved,
                Phase19ReviewApproved: execution.Phase19ReviewApproved,
                RequestedPlanLanguage: languageMismatch.RequestedPlanLanguage,
                RequestedLanguage: languageMismatch.RequestedLanguage,
                LanguageMismatchDetected: languageMismatch.LanguageMismatchDetected,
                SiblingPlanFound: languageMismatch.SiblingPlanFound,
                SiblingPlanCreated: languageMismatch.SiblingPlanCreated,
                SuccessDiagnostics: execution.SuccessDiagnostics);
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
                Errors: [],
                RequestedPlanId: request.PlanId,
                SelectedPlanId: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].Id : null,
                ManualPlanExecution: manualPlanExecution,
                AutoGenerateAllowed: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed : null,
                AutoGenerateAllowedIgnoredForManualRun: manualPlanExecution && selectedPlanEntities.Count == 1 && selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed == false,
                SelectionMode: manualPlanExecution ? "ManualPlanId" : "Automatic",
                RequestedPlanLanguage: languageMismatch.RequestedPlanLanguage,
                RequestedLanguage: languageMismatch.RequestedLanguage,
                LanguageMismatchDetected: languageMismatch.LanguageMismatchDetected,
                SiblingPlanFound: languageMismatch.SiblingPlanFound,
                SiblingPlanCreated: languageMismatch.SiblingPlanCreated);
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
            [],
            RequestedPlanId: request.PlanId,
            SelectedPlanId: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].Id : null,
            ManualPlanExecution: manualPlanExecution,
            AutoGenerateAllowed: selectedPlanEntities.Count == 1 ? selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed : null,
            AutoGenerateAllowedIgnoredForManualRun: manualPlanExecution && selectedPlanEntities.Count == 1 && selectedPlanEntities[0].AstronomyEventIntelligence?.AutoGenerateAllowed == false,
            SelectionMode: manualPlanExecution ? "ManualPlanId" : "Automatic",
            RequestedPlanLanguage: languageMismatch.RequestedPlanLanguage,
            RequestedLanguage: languageMismatch.RequestedLanguage,
            LanguageMismatchDetected: languageMismatch.LanguageMismatchDetected,
            SiblingPlanFound: languageMismatch.SiblingPlanFound,
            SiblingPlanCreated: languageMismatch.SiblingPlanCreated);
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
                .ThenInclude(e => e!.Objects)
            .Where(p => p.RegionId == regionId && p.Language == language)
            .Where(p => p.ScheduledUtc.HasValue && p.ScheduledUtc.Value >= yearStart && p.ScheduledUtc.Value < yearEnd)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<ContentGenerationPlan[]> EnsureExactPlanCandidateAsync(ContentGenerationPlan[] candidates, Guid requestedPlanId, CancellationToken cancellationToken)
    {
        if (candidates.Any(p => p.Id == requestedPlanId)) return candidates;

        var exactPlan = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == requestedPlanId, cancellationToken);

        if (exactPlan is null) return candidates;

        return candidates.Concat([exactPlan]).ToArray();
    }

    private async Task<LanguageMismatchPlanResolution> ResolveLanguageMismatchSiblingAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
    {
        if (!request.PlanId.HasValue) return LanguageMismatchPlanResolution.None(request.Language);

        var requestedLanguage = NormalizeLanguage(request.Language);
        var sourcePlan = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == request.PlanId.Value, cancellationToken);

        if (sourcePlan is null) return LanguageMismatchPlanResolution.None(request.Language);

        var sourceLanguage = NormalizeLanguage(sourcePlan.Language);
        if (string.Equals(sourceLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            await EnsurePlanIntelligenceObjectsFromSourceAsync(sourcePlan, requestedLanguage, cancellationToken);
            return new LanguageMismatchPlanResolution(sourcePlan.Language, requestedLanguage, false, false, false, sourcePlan.Id);
        }

        var sibling = await FindSiblingPlanAsync(sourcePlan, request.RegionId, requestedLanguage, cancellationToken);
        if (sibling is not null)
        {
            await EnsurePlanIntelligenceObjectsFromSourceAsync(sibling, requestedLanguage, cancellationToken, sourcePlan);
            return new LanguageMismatchPlanResolution(sourcePlan.Language, requestedLanguage, true, true, false, sibling.Id);
        }

        sibling = await CreateSiblingPlanAsync(sourcePlan, requestedLanguage, cancellationToken);
        return new LanguageMismatchPlanResolution(sourcePlan.Language, requestedLanguage, true, false, true, sibling.Id);
    }

    private async Task<ContentGenerationPlan?> FindSiblingPlanAsync(ContentGenerationPlan sourcePlan, string requestedRegionId, string requestedLanguage, CancellationToken cancellationToken)
    {
        var externalIds = new[]
            {
                sourcePlan.AstronomyEventIntelligence?.ExternalEventId,
                sourcePlan.SourceExternalEventId
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (externalIds.Length == 0) return null;

        return await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .Where(p => p.RegionId == requestedRegionId && p.Language == requestedLanguage)
            .Where(p => (p.SourceExternalEventId != null && externalIds.Contains(p.SourceExternalEventId))
                || (p.AstronomyEventIntelligence != null && externalIds.Contains(p.AstronomyEventIntelligence.ExternalEventId)))
            .OrderByDescending(p => p.PriorityScore ?? 0m)
            .ThenBy(p => p.Priority)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ContentGenerationPlan> CreateSiblingPlanAsync(ContentGenerationPlan sourcePlan, string requestedLanguage, CancellationToken cancellationToken)
    {
        var sourceEvent = sourcePlan.AstronomyEventIntelligence;
        var siblingEvent = sourceEvent is null
            ? null
            : await db.AstronomyEventIntelligences
                .Include(e => e.Objects)
                .FirstOrDefaultAsync(e => e.ExternalEventId == sourceEvent.ExternalEventId
                    && e.RegionId == sourceEvent.RegionId
                    && e.Language == requestedLanguage, cancellationToken);

        if (sourceEvent is not null && siblingEvent is null)
        {
            siblingEvent = CopyEventForLanguage(sourceEvent, requestedLanguage);
            db.AstronomyEventIntelligences.Add(siblingEvent);
        }

        var siblingPlan = new ContentGenerationPlan
        {
            ContentCategoryCode = sourcePlan.ContentCategoryCode,
            PipelineRunId = sourcePlan.PipelineRunId,
            Title = sourcePlan.Title,
            Language = requestedLanguage,
            RegionId = sourcePlan.RegionId,
            ScheduledUtc = sourcePlan.ScheduledUtc,
            Status = "Planned",
            AstronomyContentOpportunityId = sourcePlan.AstronomyContentOpportunityId,
            AstronomyEventIntelligenceId = siblingEvent?.Id ?? sourcePlan.AstronomyEventIntelligenceId,
            AstronomyEventIntelligence = siblingEvent ?? sourcePlan.AstronomyEventIntelligence,
            SourceExternalEventId = sourcePlan.SourceExternalEventId ?? sourceEvent?.ExternalEventId,
            RequestedOutputTypesJson = sourcePlan.RequestedOutputTypesJson,
            SourceEventObjectIdsJson = sourcePlan.SourceEventObjectIdsJson,
            PlannedObjectNamesJson = sourcePlan.PlannedObjectNamesJson,
            PlanStatus = "Planned",
            PlannedFormat = sourcePlan.PlannedFormat,
            PriorityScore = sourcePlan.PriorityScore,
            PrimaryCelestialObjectCode = sourcePlan.PrimaryCelestialObjectCode,
            PrimaryAstronomyEventTypeCode = sourcePlan.PrimaryAstronomyEventTypeCode,
            HookStyleCode = sourcePlan.HookStyleCode,
            NarrationStyleCode = sourcePlan.NarrationStyleCode,
            ThumbnailStyleCode = sourcePlan.ThumbnailStyleCode,
            GeneratedByAi = sourcePlan.GeneratedByAi,
            ManualValidation = sourcePlan.ManualValidation,
            Priority = sourcePlan.Priority,
            PlanningReason = $"Created as {requestedLanguage} sibling for source plan {sourcePlan.Id:D}.",
            AssetPlanJson = sourcePlan.AssetPlanJson,
            AssetPlanStatus = "Planned"
        };

        db.ContentGenerationPlans.Add(siblingPlan);
        await db.SaveChangesAsync(cancellationToken);
        LogSiblingPlanDatabaseLinkageDiagnostics(sourcePlan, siblingPlan, sourceEvent, siblingEvent);
        return siblingPlan;
    }

    private async Task EnsurePlanIntelligenceObjectsFromSourceAsync(ContentGenerationPlan targetPlan, string requestedLanguage, CancellationToken cancellationToken, ContentGenerationPlan? knownSourcePlan = null)
    {
        try
        {
            await EnsurePlanIntelligenceObjectsFromSourceCoreAsync(targetPlan, requestedLanguage, cancellationToken, knownSourcePlan);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict while linking content plan {PlanId} to {Language} astronomy intelligence; reloading the plan and retrying once.", targetPlan.Id, requestedLanguage);
            await RetryEnsurePlanIntelligenceObjectsFromSourceAsync(targetPlan.Id, requestedLanguage, knownSourcePlan?.Id, cancellationToken);
        }
    }

    private async Task RetryEnsurePlanIntelligenceObjectsFromSourceAsync(Guid targetPlanId, string requestedLanguage, Guid? knownSourcePlanId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var reloadedTargetPlan = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == targetPlanId, cancellationToken);

        if (reloadedTargetPlan is null)
        {
            logger.LogWarning("Skipping astronomy intelligence linkage because content plan {PlanId} no longer exists after a concurrency conflict.", targetPlanId);
            return;
        }

        var reloadedSourcePlan = knownSourcePlanId.HasValue
            ? await db.ContentGenerationPlans
                .Include(p => p.AstronomyEventIntelligence)
                    .ThenInclude(e => e!.Objects)
                .FirstOrDefaultAsync(p => p.Id == knownSourcePlanId.Value, cancellationToken)
            : null;

        await EnsurePlanIntelligenceObjectsFromSourceCoreAsync(reloadedTargetPlan, requestedLanguage, cancellationToken, reloadedSourcePlan);
    }

    private async Task EnsurePlanIntelligenceObjectsFromSourceCoreAsync(ContentGenerationPlan targetPlan, string requestedLanguage, CancellationToken cancellationToken, ContentGenerationPlan? knownSourcePlan = null)
    {
        var targetPlanId = targetPlan.Id;

        foreach (var localTargetPlan in db.ContentGenerationPlans.Local.Where(p => p.Id == targetPlanId).ToArray())
            db.Entry(localTargetPlan).State = EntityState.Detached;

        var trackedTargetPlan = await db.ContentGenerationPlans
            .FirstOrDefaultAsync(p => p.Id == targetPlanId, cancellationToken);

        if (trackedTargetPlan is null)
        {
            LogEnsurePlanIntelligenceObjectsFromSourceDiagnostics(targetPlanId, null, null, 0, null);
            throw new InvalidOperationException($"Cannot link astronomy intelligence objects because target content plan {targetPlanId:D} was not found after reloading it inside {nameof(EnsurePlanIntelligenceObjectsFromSourceCoreAsync)}.");
        }

        AstronomyEventIntelligence? targetEvent = null;
        if (trackedTargetPlan.AstronomyEventIntelligenceId.HasValue)
        {
            targetEvent = await db.AstronomyEventIntelligences
                .Include(e => e.Objects)
                .FirstOrDefaultAsync(e => e.Id == trackedTargetPlan.AstronomyEventIntelligenceId.Value, cancellationToken);
        }

        if (targetEvent?.Objects.Count > 0)
        {
            LogEnsurePlanIntelligenceObjectsFromSourceDiagnostics(targetPlanId, trackedTargetPlan, null, 0, targetEvent);
            LogSiblingPlanDatabaseLinkageDiagnostics(knownSourcePlan ?? trackedTargetPlan, trackedTargetPlan, knownSourcePlan?.AstronomyEventIntelligence, targetEvent);
            return;
        }

        var sourcePlan = knownSourcePlan ?? await FindSourcePlanIgnoringLanguageAsync(trackedTargetPlan, requestedLanguage, cancellationToken);
        var sourceEvent = sourcePlan?.AstronomyEventIntelligence;
        if (sourceEvent is null || sourceEvent.Objects.Count == 0)
        {
            LogEnsurePlanIntelligenceObjectsFromSourceDiagnostics(targetPlanId, trackedTargetPlan, null, 0, targetEvent);
            LogSiblingPlanDatabaseLinkageDiagnostics(sourcePlan ?? trackedTargetPlan, trackedTargetPlan, sourceEvent, targetEvent);
            return;
        }

        db.Entry(sourcePlan!).State = EntityState.Unchanged;
        db.Entry(sourceEvent).State = EntityState.Unchanged;

        var siblingEventCode = BuildSiblingEventCode(sourceEvent.EventCode, requestedLanguage);
        var siblingEventId = await db.AstronomyEventIntelligences
            .Where(e => e.EventCode == siblingEventCode
                || (e.Language == requestedLanguage
                    && e.RegionId == sourceEvent.RegionId
                    && e.ExternalEventId == sourceEvent.ExternalEventId
                    && e.Objects.Any()))
            .OrderByDescending(e => e.EventCode == siblingEventCode)
            .ThenByDescending(e => e.Objects.Count)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var copiedObjectCount = 0;
        AstronomyEventIntelligence trackedSiblingEvent;
        if (siblingEventId.HasValue)
        {
            trackedSiblingEvent = await db.AstronomyEventIntelligences
                .Include(e => e.Objects)
                .FirstOrDefaultAsync(e => e.Id == siblingEventId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Reusable astronomy event intelligence {siblingEventId.Value:D} disappeared before linking target content plan {targetPlanId:D}.");

            db.Entry(trackedSiblingEvent).State = EntityState.Unchanged;

            if (trackedSiblingEvent.Objects.Count == 0)
            {
                foreach (var sourceObject in sourceEvent.Objects)
                {
                    var newObject = CopyEventObject(sourceObject);
                    newObject.AstronomyEventIntelligenceId = trackedSiblingEvent.Id;
                    db.AstronomyEventObjects.Add(newObject);
                    copiedObjectCount++;
                }
            }
        }
        else
        {
            trackedSiblingEvent = CopyEventForLanguage(sourceEvent, requestedLanguage);
            copiedObjectCount = trackedSiblingEvent.Objects.Count;
            db.AstronomyEventIntelligences.Add(trackedSiblingEvent);
        }

        trackedTargetPlan.AstronomyEventIntelligenceId = trackedSiblingEvent.Id;
        trackedTargetPlan.SourceExternalEventId = trackedTargetPlan.SourceExternalEventId ?? sourcePlan?.SourceExternalEventId ?? sourceEvent.ExternalEventId;
        trackedTargetPlan.PlannedObjectNamesJson = trackedTargetPlan.PlannedObjectNamesJson ?? sourcePlan?.PlannedObjectNamesJson;
        trackedTargetPlan.PrimaryCelestialObjectCode = trackedTargetPlan.PrimaryCelestialObjectCode ?? sourcePlan?.PrimaryCelestialObjectCode;
        trackedTargetPlan.Touch();

        db.Entry(trackedTargetPlan).State = EntityState.Modified;
        db.Entry(sourceEvent).State = EntityState.Unchanged;
        db.Entry(sourcePlan!).State = EntityState.Unchanged;
        if (siblingEventId.HasValue)
            db.Entry(trackedSiblingEvent).State = EntityState.Unchanged;

        LogEnsurePlanIntelligenceChangeTrackerEntries(targetPlanId);
        ThrowIfUnexpectedModifiedEntityBeforeSave(trackedTargetPlan);

        await db.SaveChangesAsync(cancellationToken);
        LogEnsurePlanIntelligenceObjectsFromSourceDiagnostics(targetPlanId, trackedTargetPlan, trackedSiblingEvent, copiedObjectCount, trackedSiblingEvent);
        LogSiblingPlanDatabaseLinkageDiagnostics(sourcePlan, trackedTargetPlan, sourceEvent, trackedSiblingEvent);
    }

    private void LogEnsurePlanIntelligenceChangeTrackerEntries(Guid targetPlanId)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != EntityState.Detached))
        {
            logger.LogInformation(
                "Ensure plan intelligence ChangeTracker entry before SaveChanges: targetPlanId={TargetPlanId}, entityName={EntityName}, state={State}, primaryKey={PrimaryKey}, foreignKeys={ForeignKeys}",
                targetPlanId,
                entry.Metadata.ClrType.Name,
                entry.State,
                FormatPrimaryKey(entry),
                FormatForeignKeys(entry));
        }
    }

    private void ThrowIfUnexpectedModifiedEntityBeforeSave(ContentGenerationPlan trackedTargetPlan)
    {
        var unexpectedModifiedEntries = db.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified && !ReferenceEquals(e.Entity, trackedTargetPlan))
            .Select(e => $"{e.Metadata.ClrType.Name}({FormatPrimaryKey(e)})")
            .ToArray();

        if (unexpectedModifiedEntries.Length > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to save astronomy intelligence linkage because unexpected Modified entities are tracked before SaveChanges. Only target content plan {trackedTargetPlan.Id:D} may be Modified. UnexpectedModified={string.Join(", ", unexpectedModifiedEntries)}");
        }
    }

    private static string FormatPrimaryKey(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return "<none>";

        return string.Join(",", key.Properties.Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}"));
    }

    private static string FormatForeignKeys(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var foreignKeyProperties = entry.Metadata.GetForeignKeys()
            .SelectMany(fk => fk.Properties)
            .Distinct()
            .ToArray();

        return foreignKeyProperties.Length == 0
            ? "<none>"
            : string.Join(",", foreignKeyProperties.Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}"));
    }

    private void LogEnsurePlanIntelligenceObjectsFromSourceDiagnostics(Guid targetPlanId, ContentGenerationPlan? trackedTargetPlan, AstronomyEventIntelligence? siblingEvent, int copiedObjectCount, AstronomyEventIntelligence? resolvedEvent)
    {
        var trackedTargetPlanState = trackedTargetPlan is null
            ? null
            : db.Entry(trackedTargetPlan).State.ToString();
        var primaryObjectsResolvedAfterSave = ResolveDiagnosticObjects(resolvedEvent, primary: true);
        var secondaryObjectsResolvedAfterSave = ResolveDiagnosticObjects(resolvedEvent, primary: false);

        logger.LogInformation(
            "Ensure plan intelligence objects from source diagnostics: targetPlanId={TargetPlanId}, trackedTargetPlanFound={TrackedTargetPlanFound}, trackedTargetPlanState={TrackedTargetPlanState}, siblingEventId={SiblingEventId}, copiedObjectCount={CopiedObjectCount}, primaryObjectsResolvedAfterSave={PrimaryObjectsResolvedAfterSave}, secondaryObjectsResolvedAfterSave={SecondaryObjectsResolvedAfterSave}",
            targetPlanId,
            trackedTargetPlan is not null,
            trackedTargetPlanState,
            siblingEvent?.Id,
            copiedObjectCount,
            string.Join(",", primaryObjectsResolvedAfterSave),
            string.Join(",", secondaryObjectsResolvedAfterSave));
    }

    private async Task<ContentGenerationPlan?> FindSourcePlanIgnoringLanguageAsync(ContentGenerationPlan targetPlan, string requestedLanguage, CancellationToken cancellationToken)
    {
        var externalIds = new[]
            {
                targetPlan.AstronomyEventIntelligence?.ExternalEventId,
                targetPlan.SourceExternalEventId
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (externalIds.Length == 0) return null;

        return await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)
                .ThenInclude(e => e!.Objects)
            .Where(p => p.Id != targetPlan.Id)
            .Where(p => p.Language != requestedLanguage)
            .Where(p => (p.SourceExternalEventId != null && externalIds.Contains(p.SourceExternalEventId))
                || (p.AstronomyEventIntelligence != null && externalIds.Contains(p.AstronomyEventIntelligence.ExternalEventId)))
            .Where(p => p.AstronomyEventIntelligence != null && p.AstronomyEventIntelligence.Objects.Any())
            .OrderByDescending(p => p.PriorityScore ?? 0m)
            .ThenBy(p => p.Priority)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private void LogSiblingPlanDatabaseLinkageDiagnostics(ContentGenerationPlan? sourcePlan, ContentGenerationPlan siblingPlan, AstronomyEventIntelligence? sourceEvent, AstronomyEventIntelligence? siblingEvent)
    {
        var primaryObjectsResolved = ResolveDiagnosticObjects(siblingEvent, primary: true);
        var secondaryObjectsResolved = ResolveDiagnosticObjects(siblingEvent, primary: false);

        logger.LogInformation(
            "Sibling plan database linkage diagnostics: sourcePlanId={SourcePlanId}, siblingPlanId={SiblingPlanId}, sourceIntelligenceId={SourceIntelligenceId}, siblingIntelligenceId={SiblingIntelligenceId}, sourceObjectCount={SourceObjectCount}, siblingObjectCount={SiblingObjectCount}, primaryObjectsResolved={PrimaryObjectsResolved}, secondaryObjectsResolved={SecondaryObjectsResolved}, plannedObjectNamesJson={PlannedObjectNamesJson}, primaryCelestialObjectCode={PrimaryCelestialObjectCode}",
            sourcePlan?.Id,
            siblingPlan.Id,
            sourceEvent?.Id,
            siblingEvent?.Id,
            sourceEvent?.Objects.Count ?? 0,
            siblingEvent?.Objects.Count ?? 0,
            string.Join(",", primaryObjectsResolved),
            string.Join(",", secondaryObjectsResolved),
            siblingPlan.PlannedObjectNamesJson,
            siblingPlan.PrimaryCelestialObjectCode);
    }

    private static string[] ResolveDiagnosticObjects(AstronomyEventIntelligence? intelligence, bool primary)
        => intelligence?.Objects
            .Where(o => primary ? IsDiagnosticPrimaryObject(o) : !IsDiagnosticPrimaryObject(o))
            .Select(o => o.ObjectName)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static bool IsDiagnosticPrimaryObject(AstronomyEventObject obj)
        => string.Equals(obj.ObjectRole, "Primary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(obj.ObjectRole, "Radiant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(obj.ObjectType, "Radiant", StringComparison.OrdinalIgnoreCase);

    private static AstronomyEventIntelligence CopyEventForLanguage(AstronomyEventIntelligence sourceEvent, string requestedLanguage)
        => new()
        {
            EventCode = BuildSiblingEventCode(sourceEvent.EventCode, requestedLanguage),
            ExternalEventId = sourceEvent.ExternalEventId,
            Year = sourceEvent.Year,
            Language = requestedLanguage,
            VerificationStatus = sourceEvent.VerificationStatus,
            AutoGenerateAllowed = sourceEvent.AutoGenerateAllowed,
            ContentStrategy = sourceEvent.ContentStrategy,
            EventType = sourceEvent.EventType,
            Title = sourceEvent.Title,
            Summary = sourceEvent.Summary,
            Description = sourceEvent.Description,
            StartUtc = sourceEvent.StartUtc,
            PeakUtc = sourceEvent.PeakUtc,
            EndUtc = sourceEvent.EndUtc,
            RegionId = sourceEvent.RegionId,
            LocationName = sourceEvent.LocationName,
            TimeZone = sourceEvent.TimeZone,
            RecommendedCategory = sourceEvent.RecommendedCategory,
            Status = sourceEvent.Status,
            SourcePipelineRunId = sourceEvent.SourcePipelineRunId,
            ConfidenceScore = sourceEvent.ConfidenceScore,
            RarityScore = sourceEvent.RarityScore,
            VisibilityScore = sourceEvent.VisibilityScore,
            AudienceInterestScore = sourceEvent.AudienceInterestScore,
            TimingUrgencyScore = sourceEvent.TimingUrgencyScore,
            ContentOpportunityScore = sourceEvent.ContentOpportunityScore,
            RawDataJson = sourceEvent.RawDataJson,
            RulesAppliedJson = sourceEvent.RulesAppliedJson,
            MetadataJson = sourceEvent.MetadataJson,
            Objects = sourceEvent.Objects
                .Select(CopyEventObject)
                .ToArray()
        };

    private static AstronomyEventObject CopyEventObject(AstronomyEventObject sourceObject)
        => new()
        {
            ObjectName = sourceObject.ObjectName,
            ObjectType = sourceObject.ObjectType,
            ObjectRole = sourceObject.ObjectRole,
            CatalogId = sourceObject.CatalogId,
            Magnitude = sourceObject.Magnitude,
            VisibilityScore = sourceObject.VisibilityScore,
            MetadataJson = sourceObject.MetadataJson
        };

    private static string BuildSiblingEventCode(string sourceEventCode, string requestedLanguage)
        => string.IsNullOrWhiteSpace(sourceEventCode)
            ? requestedLanguage
            : $"{sourceEventCode}-{requestedLanguage}";

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static SelectionResult SelectPlans(IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, Guid? requestedPlanId, bool onlyHighPriority, int maxPlans, ContentPlanExecutionMode executionMode, bool recoveryMode, TimeSpan runningPlanRecoveryStaleAfter, bool allowCompletedPlanRerun, bool useProductionPipeline, bool exactPlanIdMode)
    {
        var allowedStatuses = AllowedStatusesFor(executionMode);
        var completedRerunMode = IsCompletedRerunMode(executionMode);
        var selected = new List<ContentGenerationPlan>();
        var warnings = new List<BatchGenerateFromPlansWarning>();
        var selectedIds = new HashSet<Guid>();

        if (requestedPlanId is { } planId)
        {
            SelectRequestedPlan(candidates, planId, onlyHighPriority, maxPlans, recoveryMode, runningPlanRecoveryStaleAfter, allowedStatuses, selected, warnings, selectedIds, completedRerunMode, allowCompletedPlanRerun, useProductionPipeline, exactPlanIdMode);
        }

        if (requestedPlanId.HasValue)
            return new SelectionResult(selected, warnings);

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

    private static void SelectRequestedPlan(IReadOnlyList<ContentGenerationPlan> candidates, Guid requestedPlanId, bool onlyHighPriority, int maxPlans, bool recoveryMode, TimeSpan runningPlanRecoveryStaleAfter, IReadOnlyCollection<string> allowedStatuses, List<ContentGenerationPlan> selected, List<BatchGenerateFromPlansWarning> warnings, HashSet<Guid> selectedIds, bool completedRerunMode, bool allowCompletedPlanRerun, bool useProductionPipeline, bool exactPlanIdMode)
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
        var manualPlanExecution = exactPlanIdMode;
        if (manualPlanExecution)
        {
            selected.Add(plan);
            selectedIds.Add(plan.Id);
            if (plan.AstronomyEventIntelligence?.AutoGenerateAllowed == false)
                warnings.Add(new BatchGenerateFromPlansWarning(requestedPlanId.ToString("D"), true, true, BuildAutoGenerateBypassReason(plan, requestedPlanId.ToString("D"), exactPlanIdMode, useProductionPipeline, allowCompletedPlanRerun)));
            return;
        }

        var statusAllowed = IsStatusRunnable(plan, allowedStatuses) || (allowCompletedPlanRerun && IsProductionCompleted(plan));
        if (!statusAllowed
            || (IsProductionRunning(plan) && !CanRecoverRunningPlan(plan, recoveryMode, runningPlanRecoveryStaleAfter))
            || (!IsAstronomyEventRunnable(plan, allowManualValidationAutoGenerateBypass: false))
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
                isExactTarget: true,
                exactPlanIdMode: exactPlanIdMode,
                useProductionPipeline: useProductionPipeline)));
            return;
        }

        selected.Add(plan);
        selectedIds.Add(plan.Id);
        if (plan.AstronomyEventIntelligence?.AutoGenerateAllowed == false)
            warnings.Add(new BatchGenerateFromPlansWarning(requestedPlanId.ToString("D"), true, true, BuildAutoGenerateBypassReason(plan, requestedPlanId.ToString("D"), exactPlanIdMode, useProductionPipeline, allowCompletedPlanRerun)));
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

    private static bool ShouldBypassAutoGenerateAllowedForExactPlanId(ContentGenerationPlan plan, bool useProductionPipeline, bool exactPlanIdMode, bool allowCompletedPlanRerun)
        => exactPlanIdMode
            && useProductionPipeline
            && allowCompletedPlanRerun
            && plan.AstronomyEventIntelligence is { AutoGenerateAllowed: false };

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

    private static void AddAutoGenerateBypassWarningIfNeeded(ContentGenerationPlan plan, string requestedTitle, List<BatchGenerateFromPlansWarning> warnings, bool useProductionPipeline, bool exactPlanIdMode, bool allowCompletedPlanRerun)
    {
        if (ShouldBypassAutoGenerateAllowedForExactPlanId(plan, useProductionPipeline, exactPlanIdMode, allowCompletedPlanRerun))
            warnings.Add(new BatchGenerateFromPlansWarning(requestedTitle, true, true, BuildAutoGenerateBypassReason(plan, requestedTitle, exactPlanIdMode, useProductionPipeline, allowCompletedPlanRerun)));
        else
            AddManualValidationAutoGenerateWarningIfNeeded(plan, requestedTitle, warnings, isExactPlanIdTarget: true);
    }

    private static string BuildAutoGenerateAllowedExclusionReason(ContentGenerationPlan plan, string? requestedPlanTitle, bool isExactTarget, bool exactPlanIdMode = false, bool useProductionPipeline = false, bool allowCompletedPlanRerun = false)
    {
        var isManualValidationPlan = IsManualValidationPlan(plan);
        var shouldBypassAutoGenerateAllowed = ShouldBypassAutoGenerateAllowedForExactPlanId(plan, useProductionPipeline, exactPlanIdMode, allowCompletedPlanRerun) || AllowManualValidationAutoGenerateBypass(plan, isExactTarget);

        return string.Join(Environment.NewLine,
            "Excluded because linked astronomy event AutoGenerateAllowed was false.",
            $"planId={plan.Id:D}",
            $"planTitle={plan.Title}",
            $"GeneratedByAi={FormatDiagnosticBoolean(plan.GeneratedByAi)}",
            $"PlanningReason={plan.PlanningReason}",
            $"requestedPlanTitle={requestedPlanTitle}",
            $"requestedPlanId={(isExactTarget ? plan.Id.ToString("D") : null)}",
            $"selectedPlanId=",
            $"exactPlanIdMode={FormatDiagnosticBoolean(exactPlanIdMode)}",
            $"linkedEventAutoGenerateAllowed={FormatDiagnosticBoolean(plan.AstronomyEventIntelligence?.AutoGenerateAllowed == true)}",
            $"autoGenerateAllowedBypassed={FormatDiagnosticBoolean(shouldBypassAutoGenerateAllowed)}",
            $"bypassReason={(shouldBypassAutoGenerateAllowed ? "exact planId production rerun request" : "not eligible")}",
            $"matchedDifferentPlanDetected=false",
            $"isExactTarget={FormatDiagnosticBoolean(isExactTarget)}",
            $"isManualValidationPlan={FormatDiagnosticBoolean(isManualValidationPlan)}",
            $"shouldBypassAutoGenerateAllowed={FormatDiagnosticBoolean(shouldBypassAutoGenerateAllowed)}");
    }


    private static string BuildAutoGenerateBypassReason(ContentGenerationPlan plan, string requestedPlanId, bool exactPlanIdMode, bool useProductionPipeline, bool allowCompletedPlanRerun)
        => string.Join(Environment.NewLine,
            "Selected exact planId even though linked event AutoGenerateAllowed=false.",
            $"requestedPlanId={requestedPlanId}",
            $"selectedPlanId={plan.Id:D}",
            $"exactPlanIdMode={FormatDiagnosticBoolean(exactPlanIdMode)}",
            $"linkedEventAutoGenerateAllowed={FormatDiagnosticBoolean(plan.AstronomyEventIntelligence?.AutoGenerateAllowed == true)}",
            "autoGenerateAllowedBypassed=true",
            $"bypassReason={(useProductionPipeline && allowCompletedPlanRerun ? "planId + useProductionPipeline + allowCompletedPlanRerun" : "not eligible")}",
            "matchedDifferentPlanDetected=false");

    private static bool IsExactPlanIdMode(BatchGenerateFromPlansRequest request)
        => request.PlanId.HasValue;

    private void LogExactPlanIdDiagnostics(BatchGenerateFromPlansRequest request, IReadOnlyList<ContentGenerationPlan> selectedPlans, IReadOnlyList<ContentGenerationPlan> candidates, IReadOnlyList<string> requestedTitles, bool exactPlanIdMode)
    {
        if (!request.PlanId.HasValue) return;

        var selectedPlanId = selectedPlans.Count == 1 ? selectedPlans[0].Id : (Guid?)null;
        var requestedPlan = candidates.FirstOrDefault(p => p.Id == request.PlanId.Value);
        var matchedDifferentPlanDetected = requestedTitles
            .SelectMany(title => FindMatches(candidates, title))
            .Any(plan => plan.Id != request.PlanId.Value);
        var autoGenerateAllowedBypassed = exactPlanIdMode && requestedPlan?.AstronomyEventIntelligence?.AutoGenerateAllowed == false && selectedPlanId == request.PlanId.Value;

        logger.LogInformation(
            "Content plan execution diagnostics: requestedPlanId={RequestedPlanId}; selectedPlanId={SelectedPlanId}; manualPlanExecution={ManualPlanExecution}; autoGenerateAllowed={AutoGenerateAllowed}; autoGenerateAllowedIgnoredForManualRun={AutoGenerateAllowedIgnoredForManualRun}; selectionMode={SelectionMode}; exactPlanIdMode={ExactPlanIdMode}; matchedDifferentPlanDetected={MatchedDifferentPlanDetected}",
            request.PlanId.Value,
            selectedPlanId,
            true,
            requestedPlan?.AstronomyEventIntelligence?.AutoGenerateAllowed,
            autoGenerateAllowedBypassed,
            "ManualPlanId",
            exactPlanIdMode,
            matchedDifferentPlanDetected);
    }

    private static void ValidateExactPlanIdSelection(Guid? requestedPlanId, IReadOnlyList<ContentGenerationPlan> selectedPlans, bool exactPlanIdMode)
    {
        if (!requestedPlanId.HasValue || selectedPlans.Count == 0) return;
        if (selectedPlans.Count != 1 || selectedPlans[0].Id != requestedPlanId.Value)
            throw new InvalidOperationException($"Exact planId validation failed: selectedPlanId={selectedPlans.FirstOrDefault()?.Id:D} did not match requestedPlanId={requestedPlanId.Value:D}.");
    }


    private static void ValidateExactPlanIdExecutionResult(Guid? requestedPlanId, Guid selectedPlanId, bool exactPlanIdMode)
    {
        if (!requestedPlanId.HasValue) return;
        if (selectedPlanId != requestedPlanId.Value)
            throw new InvalidOperationException($"Exact planId validation failed: selectedPlanId={selectedPlanId:D} did not match requestedPlanId={requestedPlanId.Value:D}.");
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
        bool isExactTarget = false,
        bool exactPlanIdMode = false,
        bool useProductionPipeline = false)
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
            return BuildAutoGenerateAllowedExclusionReason(plan, requestedPlanTitle, isExactTarget, exactPlanIdMode, useProductionPipeline, allowCompletedPlanRerun);
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
            if (!request.OverwriteExisting && !IsPhase4CommittedAuthorityReuse(request))
                throw new ArgumentException("Completed plan reruns require overwriteExisting=true before output folders can be rebuilt.");
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

    private static bool IsPhase4CommittedAuthorityReuse(BatchGenerateFromPlansRequest request)
        => request.ExecutionMode == ContentPlanExecutionMode.RerunPhase
            && request.StartPhaseNo == 4
            && request.EndPhaseNo == 4
            && !request.OverwriteExisting;

    private sealed record SelectionResult(IReadOnlyList<ContentGenerationPlan> SelectedPlans, IReadOnlyList<BatchGenerateFromPlansWarning> Warnings);

    private sealed record LanguageMismatchPlanResolution(
        string? RequestedPlanLanguage,
        string? RequestedLanguage,
        bool LanguageMismatchDetected,
        bool SiblingPlanFound,
        bool SiblingPlanCreated,
        Guid? SelectedPlanId)
    {
        public static LanguageMismatchPlanResolution None(string? requestedLanguage)
            => new(null, NormalizeLanguage(requestedLanguage), false, false, false, null);
    }
}
