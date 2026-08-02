using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanProductionExecutionService(
    MediaFactoryDbContext db,
    IContentPlanProductionRequestMapper mapper,
    IProductionPipelineExecutionService productionPipeline,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ContentPlanProductionExecutionService> logger,
    IOptions<VisualIntelligenceOptions>? visualIntelligenceOptions = null,
    IVisualIntelligenceOrchestrator? visualIntelligenceOrchestrator = null) : IContentPlanProductionExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ProductionSteps =
    [
        "Question Engine",
        "Scene Engine Short",
        "Scene Engine Long",
        "Hero Engine",
        "Thumbnail Engine",
        "Gallery Engine",
        "Narration Short",
        "Narration Long",
        "TTS Short",
        "TTS Long",
        "Video Assembly Short",
        "Video Assembly Long"
    ];

    public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanAsync(Guid contentGenerationPlanId, bool dryRun, bool overwriteExisting, CancellationToken cancellationToken)
        => ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(contentGenerationPlanId, dryRun, overwriteExisting), cancellationToken);

    public async Task<ContentPlanProductionExecutionResult> ExecuteContentPlanWithProductionPipelineAsync(ContentPlanProductionExecutionRequest request, CancellationToken cancellationToken)
    {
        var plan = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)!.ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == request.ContentGenerationPlanId, cancellationToken)
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.ContentGenerationPlanId}' was not found.", nameof(request));

        var intelligence = plan.AstronomyEventIntelligence
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.ContentGenerationPlanId}' is not linked to AstronomyEventIntelligence.", nameof(request));

        var productionRequest = mapper.Map(plan, intelligence);
        var executionMode = request.ExecutionMode;
        var isCompletedPlanRerun = IsProductionCompleted(plan) && executionMode is ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase or ContentPlanExecutionMode.FullRebuild;
        var requestedStartPhaseNo = ResolveStartPhaseNo(request);
        var requestedEndPhaseNo = request.EndPhaseNo ?? 20;
        var resolvedRange = ResolveExecutionRange(executionMode, requestedStartPhaseNo, requestedEndPhaseNo, request.DependencyExpansionMode);
        var startPhaseNo = resolvedRange.StartPhaseNo;
        var endPhaseNo = resolvedRange.EndPhaseNo;
        var executionContext = BuildExecutionContext(plan, intelligence, productionRequest, request);
        var outputRoot = BuildPlanOutputRoot(productionRequest);
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new InvalidOperationException($"OutputRoot could not be resolved for content generation plan '{plan.Id:D}'.");

        logger.LogInformation("Using Astronomy V1 production pipeline for content plan {PlanId}", plan.Id);
        var warnings = new List<string>(productionRequest.Warnings);
        if (resolvedRange.DependencyExpansionApplied)
            warnings.Add($"Expanded rebuild range from {requestedStartPhaseNo}-{requestedEndPhaseNo} to {startPhaseNo}-{endPhaseNo} because dependencyExpansionMode=Rebuild.");
        var errors = new List<string>();
        var generatedFiles = new List<string>();

        if (request.DryRun)
        {
            return BuildResult(true, true, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, [], executionMode, isCompletedPlanRerun, false, null, [], startPhaseNo, endPhaseNo, requestedOutputCompletion: null, requestedStartPhase: requestedStartPhaseNo, requestedEndPhase: requestedEndPhaseNo, dependencyExpansionApplied: resolvedRange.DependencyExpansionApplied);
        }

        ContentPipelineExecution? execution = null;
        try
        {
            var outputPreparation = PrepareOutputRoot(outputRoot, request, startPhaseNo, endPhaseNo);
            Directory.CreateDirectory(outputRoot);
            await WritePlanInputAsync(outputRoot, plan, intelligence, productionRequest, cancellationToken);
            await ObserveVisualIntelligenceAsync(plan, intelligence, productionRequest, outputRoot, cancellationToken);

            execution = new ContentPipelineExecution
            {
                ContentGenerationPlanId = plan.Id,
                ContentCategoryCode = plan.ContentCategoryCode,
                StartedUtc = DateTimeOffset.UtcNow,
                Status = "Running",
                OutputFolder = outputRoot,
                PublishingCompleted = false,
                AnalyticsInitialized = false
            };
            db.ContentPipelineExecutions.Add(execution);
            plan.PlanStatus = "ProductionRunning";
            plan.Status = "ProductionRunning";
            await db.SaveChangesAsync(cancellationToken);

            var pipelineResult = await productionPipeline.ExecuteAsync(new ProductionPipelineRequest(
                productionRequest,
                intelligence.Id,
                outputRoot,
                DryRun: false,
                OverwriteExisting: request.OverwriteExisting,
                ExecutionContext: executionContext,
                StartPhaseNo: startPhaseNo,
                EndPhaseNo: endPhaseNo,
                RetryFailedOnly: request.RetryFailedOnly,
                ExecutionMode: executionMode,
                EnableSceneVariants: request.EnableSceneVariants,
                RequestedStartPhaseNo: requestedStartPhaseNo,
                RequestedEndPhaseNo: requestedEndPhaseNo,
                EnableSceneAssetsV3: request.EnableSceneAssetsV3,
                EnableAccurateSkyGuideV2: request.EnableAccurateSkyGuideV2,
                EnableSubtitles: request.EnableSubtitles,
                PublishApproved: request.PublishApproved,
                MotionPreviewOnly: request.MotionPreviewOnly,
                MotionV2Strength: request.MotionV2Strength,
                DependencyExpansionMode: request.DependencyExpansionMode), cancellationToken);
            generatedFiles.AddRange(pipelineResult.GeneratedFiles);
            warnings.AddRange(pipelineResult.Warnings);
            errors.AddRange(BuildAuthoritativeErrors(pipelineResult.Errors, pipelineResult.PhaseResults));

            var shortVideo = Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4");
            var longVideo = Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4");
            var shortOk = File.Exists(shortVideo);
            var longOk = File.Exists(longVideo);
            var phase20Succeeded = PhaseSucceeded(pipelineResult.PhaseResults, 20);
            var partialPhaseExecution = IsPartialPhaseExecution(request);
            var successDiagnostics = BuildSuccessAggregationDiagnostics(request, pipelineResult.PhaseResults, pipelineResult.RequestedOutputCompletion);
            await WritePipelinePhaseAggregationDiagnosticsAsync(outputRoot, pipelineResult, successDiagnostics, cancellationToken);
            var thumbnailOnlyExecution = IsThumbnailOnlyExecution(request, productionRequest);
            var aggregationSuccess = successDiagnostics.Success && errors.Count == 0 && pipelineResult.Success;
            var partialPhaseSuccess = partialPhaseExecution && aggregationSuccess;
            var productionCompleted = thumbnailOnlyExecution ? aggregationSuccess : partialPhaseExecution ? partialPhaseSuccess : aggregationSuccess && phase20Succeeded;
            var productionFailed = !productionCompleted && (errors.Count > 0 || !pipelineResult.Success || successDiagnostics.FailedExecutedPhases.Count > 0 || thumbnailOnlyExecution || partialPhaseExecution);
            if (partialPhaseExecution)
            {
                logger.LogDebug(
                    "Partial rebuild detected.\nRequested range: {RequestedStartPhase}-{RequestedEndPhase}\nExecuted range: {ExpandedStartPhase}-{ExpandedEndPhase}\nPartialPhaseSuccess={PartialPhaseSuccess}\nFinalSuccess={FinalSuccess}",
                    requestedStartPhaseNo,
                    requestedEndPhaseNo,
                    startPhaseNo,
                    endPhaseNo,
                    partialPhaseSuccess,
                    productionCompleted);
            }
            execution.Status = productionCompleted ? "Completed" : productionFailed ? "Failed" : "Running";
            execution.FinishedUtc = productionCompleted || productionFailed ? DateTimeOffset.UtcNow : null;
            execution.ErrorMessage = productionFailed ? string.Join("; ", errors.DefaultIfEmpty("Production pipeline failed.")) : null;
            execution.OutputFolder = outputRoot;
            execution.ShortVideoPath = shortOk ? shortVideo : null;
            execution.LongVideoPath = longOk ? longVideo : null;
            execution.ThumbnailLongPath = Path.Combine(outputRoot, "thumbnails", "landscape.png");
            execution.ThumbnailShortPath = Path.Combine(outputRoot, "thumbnails", "portrait.png");
            plan.FinalVideoPath = longOk ? longVideo : shortVideo;
            plan.ThumbnailPath = Path.Combine(outputRoot, "thumbnails", "landscape.png");
            plan.PlanStatus = productionCompleted ? "ProductionCompleted" : productionFailed ? "ProductionFailed" : "ProductionRunning";
            plan.Status = plan.PlanStatus;
            plan.CompletedUtc = productionCompleted ? DateTimeOffset.UtcNow : null;
            plan.FailureReason = productionFailed ? execution.ErrorMessage : null;
            await db.SaveChangesAsync(cancellationToken);

            return BuildResult(productionCompleted, false, plan, productionRequest, outputRoot, true, DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long")), File.Exists(Path.Combine(outputRoot, "hero", "hero.png")), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.mp3")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.mp3")), shortOk, longOk, shortOk ? shortVideo : string.Empty, longOk ? longVideo : string.Empty, generatedFiles, warnings, errors, pipelineResult.PhaseResults ?? [], executionMode, isCompletedPlanRerun, outputPreparation.PreviousOutputArchived, outputPreparation.ArchivePath, outputPreparation.DeletedOutputFolders, startPhaseNo, endPhaseNo, requestedOutputCompletion: pipelineResult.RequestedOutputCompletion, partialPhaseExecution: partialPhaseExecution, requestedStartPhase: requestedStartPhaseNo, requestedEndPhase: requestedEndPhaseNo, dependencyExpansionApplied: resolvedRange.DependencyExpansionApplied, partialPhaseSuccess: partialPhaseSuccess, successDiagnostics: successDiagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("DB-plan production pipeline execution was cancelled by request for plan {PlanId}", request.ContentGenerationPlanId);
            if (execution is not null)
            {
                execution.Status = "Cancelled";
                execution.FinishedUtc = DateTimeOffset.UtcNow;
                execution.ErrorMessage = "Production pipeline execution was cancelled by request.";
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DB-plan production pipeline execution failed for plan {PlanId}", request.ContentGenerationPlanId);
            errors.Add(ex.Message);
            if (execution is not null)
            {
                execution.Status = "Failed";
                execution.FinishedUtc = DateTimeOffset.UtcNow;
                execution.ErrorMessage = ex.Message;
            }
            plan.PlanStatus = "ProductionFailed";
            plan.Status = "ProductionFailed";
            plan.CompletedUtc = null;
            plan.FailureReason = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return BuildResult(false, false, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, [], executionMode, isCompletedPlanRerun, false, null, [], startPhaseNo, endPhaseNo, requestedOutputCompletion: null, requestedStartPhase: requestedStartPhaseNo, requestedEndPhase: requestedEndPhaseNo, dependencyExpansionApplied: resolvedRange.DependencyExpansionApplied);
        }
    }


    private static ProductionPipelineExecutionContext BuildExecutionContext(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence, ContentPlanProductionPipelineRequest productionRequest, ContentPlanProductionExecutionRequest request)
        => new(
            UseProductionPipeline: true,
            ContentGenerationPlanId: plan.Id,
            AstronomyEventIntelligenceId: intelligence.Id,
            SourceExternalEventId: plan.SourceExternalEventId,
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            ContentGenerationPlanStatus: plan.Status,
            ContentGenerationPlanPlanStatus: plan.PlanStatus,
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: intelligence.AutoGenerateAllowed,
            VerificationStatus: intelligence.VerificationStatus,
            ContentStrategy: intelligence.ContentStrategy,
            RegionId: plan.RegionId,
            Language: plan.Language,
            RequestedOutputs: productionRequest.RequestedOutputs,
            Category: productionRequest.Category,
            PlannedFormat: productionRequest.PlannedFormat,
            EnableSubtitles: request.EnableSubtitles);

    private static bool PhaseSucceeded(IReadOnlyList<ProductionPhaseResult>? phaseResults, int phaseNo)
        => phaseResults?.Any(p => p.PhaseNo == phaseNo && p.Status == ProductionPhaseStatus.Succeeded) == true;

    private static bool IsPartialPhaseExecution(ContentPlanProductionExecutionRequest request)
        => request.StartPhaseNo.HasValue && request.EndPhaseNo.HasValue;

    private static bool IsThumbnailOnlyExecution(ContentPlanProductionExecutionRequest request, ContentPlanProductionPipelineRequest productionRequest)
        => request.StartPhaseNo == 12
            && request.EndPhaseNo == 12
            && IsRequestedOutput(productionRequest, "Thumbnail");

    private static bool IsRequestedOutput(ContentPlanProductionPipelineRequest productionRequest, string outputType)
        => productionRequest.RequestedOutputs.Any(output => string.Equals(output, outputType, StringComparison.OrdinalIgnoreCase));

    private async Task ObserveVisualIntelligenceAsync(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence, ContentPlanProductionPipelineRequest productionRequest, string outputRoot, CancellationToken cancellationToken)
    {
        logger.LogInformation("VISUAL_INTELLIGENCE_TOUCHPOINT_ENTERED PlanId={ContentGenerationPlanId} AstronomyEventIntelligenceId={AstronomyEventIntelligenceId}", plan.Id, intelligence.Id);
        var enabled = visualIntelligenceOptions?.Value.Enabled == true;
        var writeDiagnostics = visualIntelligenceOptions?.Value.WriteDiagnostics == true;
        logger.LogInformation("VISUAL_INTELLIGENCE_ENABLED_STATE Enabled={Enabled} WriteDiagnostics={WriteDiagnostics}", enabled, writeDiagnostics);
        logger.LogInformation("VISUAL_INTELLIGENCE_OUTPUT_FOLDER_RESOLVED PlanId={ContentGenerationPlanId} OutputFolder={OutputFolder}", plan.Id, outputRoot);
        var diagnosticsPath = Path.Combine(outputRoot, "diagnostics", "visual-intelligence");
        logger.LogInformation("VISUAL_INTELLIGENCE_DIAGNOSTICS_PATH_RESOLVED PlanId={ContentGenerationPlanId} DiagnosticsPath={DiagnosticsPath}", plan.Id, diagnosticsPath);

        if (!enabled || visualIntelligenceOrchestrator is null)
        {
            logger.LogInformation("VISUAL_INTELLIGENCE_ORCHESTRATION_SKIPPED PlanId={ContentGenerationPlanId} Enabled={Enabled} OrchestratorRegistered={OrchestratorRegistered}", plan.Id, enabled, visualIntelligenceOrchestrator is not null);
            return;
        }

        try
        {
            logger.LogInformation("VISUAL_INTELLIGENCE_ORCHESTRATION_STARTED PlanId={ContentGenerationPlanId} AstronomyEventIntelligenceId={AstronomyEventIntelligenceId}", plan.Id, intelligence.Id);
            var result = await visualIntelligenceOrchestrator.OrchestrateAsync(new VisualIntelligenceOrchestrationRequest
            {
                CorrelationId = plan.Id.ToString("N"),
                ContentGenerationPlanId = plan.Id,
                AstronomyEventIntelligenceId = intelligence.Id,
                EventType = productionRequest.EventType,
                EventName = productionRequest.Title,
                Language = productionRequest.Language,
                Region = productionRequest.RegionId,
                Location = intelligence.LocationName ?? productionRequest.VisibilityRegion ?? string.Empty,
                PrimaryObjects = productionRequest.PrimaryObjects.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SupportingObjects = productionRequest.SecondaryObjects.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Platform = Platform.YouTubeLongForm,
                AspectRatio = AspectRatio.Landscape16x9,
                RequestedAssetType = "batch-production-observation",
                ObservationDateTime = productionRequest.PeakUtc ?? productionRequest.StartUtc ?? productionRequest.ScheduledUtc,
                VisibilityGuidance = productionRequest.BestViewingWindowLocal ?? productionRequest.VisibilityRegion ?? string.Empty,
                RunOutputFolder = outputRoot,
                RequestedProvider = visualIntelligenceOptions?.Value.DefaultProvider ?? ImageProviderType.Unknown
            }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("VISUAL_INTELLIGENCE_ARTIFACTS_WRITTEN PlanId={ContentGenerationPlanId} Status={Status} DiagnosticsPath={DiagnosticsPath}", plan.Id, result.Status, diagnosticsPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "VISUAL_INTELLIGENCE_ORCHESTRATION_FAILED_NON_BLOCKING PlanId={ContentGenerationPlanId}; continuing production pipeline.", plan.Id);
        }
    }

    private static bool IsThumbnailV9Success(IReadOnlyList<ProductionPhaseResult>? phaseResults, IReadOnlyList<string> generatedFiles, IReadOnlyList<int> executedPhaseNumbers)
    {
        var phase12 = phaseResults?.LastOrDefault(p => p.PhaseNo == 12);
        if (phase12?.Status != ProductionPhaseStatus.Succeeded || !executedPhaseNumbers.Contains(12)) return false;

        var validationPath = phase12.ValidationReportPath;
        if (string.IsNullOrWhiteSpace(validationPath) || !File.Exists(validationPath)) return false;

        var generated = generatedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var phaseOutputFiles = phase12.OutputFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedExpectedThumbnails = generated
            .Concat(phaseOutputFiles)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var landscapeGenerated = generatedExpectedThumbnails.Contains("thumbnail-landscape.png") || generatedExpectedThumbnails.Contains("landscape.png");
        var portraitGenerated = generatedExpectedThumbnails.Contains("thumbnail-portrait.png") || generatedExpectedThumbnails.Contains("portrait.png");
        var squareGenerated = generatedExpectedThumbnails.Contains("thumbnail-square.png") || generatedExpectedThumbnails.Contains("square.png");
        return landscapeGenerated && portraitGenerated && squareGenerated && ThumbnailV9ReportSucceeded(validationPath);
    }

    private static bool ThumbnailV9ReportSucceeded(string path)
    {
        if (!File.Exists(path)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var validationPassed = ReadJsonBool(root, "validationPassed") == true;
        var status = ReadJsonString(root, "status");
        return validationPassed && (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(status));
    }

    private static bool? ReadJsonBool(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static string? ReadJsonString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static SuccessAggregationDiagnostics BuildSuccessAggregationDiagnostics(ContentPlanProductionExecutionRequest request, IReadOnlyList<ProductionPhaseResult>? phaseResults, IReadOnlyList<RequestedOutputCompletion>? requestedOutputCompletion)
    {
        var requestedStartPhase = request.StartPhaseNo.HasValue ? Math.Clamp(request.StartPhaseNo.Value, 1, 20) : (int?)null;
        var requestedEndPhase = request.EndPhaseNo.HasValue ? Math.Clamp(request.EndPhaseNo.Value, requestedStartPhase ?? 1, 20) : (int?)null;
        var inRange = (phaseResults ?? [])
            .Where(result => (!requestedStartPhase.HasValue || result.PhaseNo >= requestedStartPhase) &&
                             (!requestedEndPhase.HasValue || result.PhaseNo <= requestedEndPhase))
            .OrderBy(result => result.PhaseNo)
            .ToArray();
        var reusedResults = inRange.Where(ProductionPhaseSatisfaction.IsRecognizedReuse).ToArray();
        var satisfiedResults = inRange.Where(ProductionPhaseSatisfaction.IsSatisfied).ToArray();
        // Executed means the production body ran; reuse is reported separately even when a
        // legacy phase convention represents reuse with Succeeded rather than Skipped.
        var executed = inRange
            .Where(result => !ProductionPhaseSatisfaction.IsRecognizedReuse(result) && result.Status != ProductionPhaseStatus.Skipped)
            .Select(result => result.PhaseNo)
            .Distinct()
            .OrderBy(phaseNo => phaseNo)
            .ToArray();
        var failed = inRange
            .Where(result => result.Status == ProductionPhaseStatus.Failed)
            .Select(result => result.PhaseNo)
            .Distinct()
            .OrderBy(phaseNo => phaseNo)
            .ToArray();
        var allSucceeded = inRange.Length > 0 && inRange.All(ProductionPhaseSatisfaction.IsSatisfied);
        var outOfScope = (requestedOutputCompletion ?? [])
            .Where(output => string.Equals(output.Status, "OutOfScope", StringComparison.OrdinalIgnoreCase) || string.Equals(output.Status, "NotRun", StringComparison.OrdinalIgnoreCase))
            .Select(output => output.OutputType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(output => output, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var satisfied = satisfiedResults.Select(result => result.PhaseNo).Distinct().OrderBy(x => x).ToArray();
        var reused = reusedResults.Select(result => result.PhaseNo).Distinct().OrderBy(x => x).ToArray();
        var lastCompletedPhaseNo = satisfied.Cast<int?>().LastOrDefault();
        var lastFailedPhaseNo = failed.Cast<int?>().LastOrDefault();
        var success = allSucceeded && failed.Length == 0;
        return new(requestedStartPhase, requestedEndPhase, executed, allSucceeded, failed, outOfScope, lastCompletedPhaseNo, lastFailedPhaseNo, success, success, success ? 0 : 1, "PartialPhaseRange", satisfied, reused);
    }

    public static IReadOnlyList<string> BuildAuthoritativeErrors(IReadOnlyList<string>? orchestrationErrors, IReadOnlyList<ProductionPhaseResult>? phaseResults)
    {
        var messages = new List<string>();
        messages.AddRange((orchestrationErrors ?? []).Where(e => !string.IsNullOrWhiteSpace(e)));
        foreach (var phase in (phaseResults ?? []).Where(p => p.Status == ProductionPhaseStatus.Failed))
        {
            var structured = (phase.Errors ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
            if (structured.Length > 0) messages.AddRange(structured);
            else if (!string.IsNullOrWhiteSpace(phase.Reason)) messages.Add(phase.Reason!);
        }
        return messages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task WritePipelinePhaseAggregationDiagnosticsAsync(string outputRoot, ProductionPipelineExecutionResult pipelineResult, SuccessAggregationDiagnostics successDiagnostics, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "pipeline-phase-aggregation-diagnostics.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            authority = nameof(BuildSuccessAggregationDiagnostics),
            observesAuthoritativeAggregation = true,
            overallSuccess = pipelineResult.Success,
            pipelineResult.LastCompletedPhaseNo,
            pipelineResult.LastFailedPhaseNo,
            successDiagnostics
        }, JsonOptions), cancellationToken);
    }

    private static int? CalculateLastCompletedPhaseNo(IReadOnlyList<ProductionPhaseResult> phaseResults)
    {
        var ordered = phaseResults.OrderBy(p => p.PhaseNo).ToArray();
        int? last = null;
        foreach (var phase in ordered)
        {
            if (phase.Status == ProductionPhaseStatus.Failed) break;
            if (ProductionPhaseSatisfaction.IsSatisfied(phase)) last = phase.PhaseNo;
        }
        return last;
    }

    private async Task WritePlanInputAsync(string outputRoot, ContentGenerationPlan plan, AstronomyEventIntelligence intelligence, ContentPlanProductionPipelineRequest productionRequest, CancellationToken cancellationToken)
    {
        var inputRoot = Path.Combine(outputRoot, "plan-input");
        Directory.CreateDirectory(inputRoot);
        await File.WriteAllTextAsync(Path.Combine(inputRoot, "content-generation-plan.json"), JsonSerializer.Serialize(ToPlanSnapshot(plan), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(inputRoot, "astronomy-event-intelligence.json"), JsonSerializer.Serialize(ToIntelligenceSnapshot(intelligence), JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(inputRoot, "production-pipeline-request.json"), JsonSerializer.Serialize(productionRequest, JsonOptions), cancellationToken);
    }

    private static object ToPlanSnapshot(ContentGenerationPlan plan) => new
    {
        plan.Id,
        plan.ContentCategoryCode,
        plan.PipelineRunId,
        plan.Title,
        plan.Language,
        plan.RegionId,
        plan.ScheduledUtc,
        plan.Status,
        plan.PlanStatus,
        plan.AstronomyEventIntelligenceId,
        plan.SourceExternalEventId,
        plan.RequestedOutputTypesJson,
        plan.PlannedFormat,
        plan.PriorityScore,
        plan.PrimaryAstronomyEventTypeCode,
        plan.Priority
    };

    private static object ToIntelligenceSnapshot(AstronomyEventIntelligence intelligence) => new
    {
        intelligence.Id,
        intelligence.EventCode,
        intelligence.ExternalEventId,
        intelligence.Year,
        intelligence.Language,
        intelligence.VerificationStatus,
        intelligence.AutoGenerateAllowed,
        intelligence.ContentStrategy,
        intelligence.EventType,
        intelligence.Title,
        intelligence.Summary,
        intelligence.Description,
        intelligence.StartUtc,
        intelligence.PeakUtc,
        intelligence.EndUtc,
        intelligence.RegionId,
        intelligence.LocationName,
        intelligence.TimeZone,
        intelligence.RarityScore,
        intelligence.VisibilityScore,
        intelligence.AudienceInterestScore,
        intelligence.ContentOpportunityScore,
        intelligence.MetadataJson,
        intelligence.RawDataJson,
        Objects = intelligence.Objects.Select(o => new { o.Id, o.ObjectName, o.ObjectType, o.ObjectRole, o.CatalogId, o.Magnitude, o.VisibilityScore, o.MetadataJson }).ToArray()
    };

    private async Task<IReadOnlyList<string>> MaterializePlanFolderAsync(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence, string outputRoot, IReadOnlyList<string> generatedFiles, CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        var eventRoot = Path.Combine(ResolveWorkingDirectoryRoot(), "assets", Sanitize(plan.RegionId), "events", intelligence.Id.ToString("D"));
        CopyFile(Path.Combine(eventRoot, "question-engine", "question-answer-set.json"), Path.Combine(outputRoot, "question-engine", "question-answer-set.json"), copied);
        CopyFile(Path.Combine(eventRoot, "question-engine", "question-answer-set.json"), Path.Combine(outputRoot, "question-engine", "questions.json"), copied);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "short"), Path.Combine(outputRoot, "scene-approval-v3", "short"), copied, renameFinalScenes: true);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "long"), Path.Combine(outputRoot, "scene-approval-v3", "long"), copied, renameFinalScenes: true);
        CopyFile(Path.Combine(eventRoot, "hero-assets", "hero-landscape.png"), Path.Combine(outputRoot, "hero", "hero.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-landscape.png"), Path.Combine(outputRoot, "thumbnails", "landscape.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-square.png"), Path.Combine(outputRoot, "thumbnails", "square.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-portrait.png"), Path.Combine(outputRoot, "thumbnails", "portrait.png"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "video-narration-script.json"), Path.Combine(outputRoot, "narration", "short", "narration.txt"), copied, jsonNarrationToText: true);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "video-long-narration-script.json"), Path.Combine(outputRoot, "narration", "long", "narration.txt"), copied, jsonNarrationToText: true);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "video-tts-audio.mp3"), Path.Combine(outputRoot, "tts", "short", "narration.mp3"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "video-long-tts-audio.mp3"), Path.Combine(outputRoot, "tts", "long", "narration.mp3"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "final-video-short.mp4"), Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "final-video-long.mp4"), Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "video-assembly-plan.json"), Path.Combine(outputRoot, "video-assembly", "short", "assembly-manifest.json"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "video-long-assembly-plan.json"), Path.Combine(outputRoot, "video-assembly", "long", "assembly-manifest.json"), copied);
        await WriteScenesManifestsAsync(outputRoot, cancellationToken);
        copied.AddRange(generatedFiles.Where(File.Exists));
        return copied.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task WriteScenesManifestsAsync(string outputRoot, CancellationToken cancellationToken)
    {
        foreach (var profile in new[] { "short", "long" })
        {
            var root = Path.Combine(outputRoot, "scene-approval-v3", profile);
            Directory.CreateDirectory(root);
            var scenes = Directory.EnumerateFiles(root, "scene-*.png").OrderBy(x => x).Select((path, index) => new { sceneNumber = index + 1, path }).ToArray();
            await File.WriteAllTextAsync(Path.Combine(root, "scenes.json"), JsonSerializer.Serialize(new { profile, scenes }, JsonOptions), cancellationToken);
        }
    }

    private static void CopyDirectoryFiles(string sourceRoot, string targetRoot, List<string> copied, bool renameFinalScenes)
    {
        if (!Directory.Exists(sourceRoot)) return;
        Directory.CreateDirectory(targetRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot))
        {
            var fileName = Path.GetFileName(source);
            if (renameFinalScenes && fileName.EndsWith("-final.png", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Replace("-final", string.Empty, StringComparison.OrdinalIgnoreCase);
            CopyFile(source, Path.Combine(targetRoot, fileName), copied);
        }
    }

    private static void CopyFile(string source, string target, List<string> copied, bool jsonNarrationToText = false)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (jsonNarrationToText)
        {
            var text = ExtractNarrationText(File.ReadAllText(source));
            File.WriteAllText(target, text);
        }
        else
        {
            File.Copy(source, target, overwrite: true);
        }
        copied.Add(target);
    }

    private static string ExtractNarrationText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("fullNarrationText", out var text)) return text.GetString() ?? json;
        }
        catch (JsonException) { }
        return json;
    }

    private ContentPlanProductionExecutionResult BuildResult(bool success, bool dryRun, ContentGenerationPlan plan, ContentPlanProductionPipelineRequest productionRequest, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool? heroGenerated, bool? thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool? shortVideoGenerated, bool? longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<ProductionPhaseResult> phaseResults, ContentPlanExecutionMode executionMode, bool completedPlanRerun, bool previousOutputArchived, string? archivePath, IReadOnlyList<string> deletedOutputFolders, int startPhaseNo, int endPhaseNo, IReadOnlyList<RequestedOutputCompletion>? requestedOutputCompletion = null, bool partialPhaseExecution = false, int? requestedStartPhase = null, int? requestedEndPhase = null, bool dependencyExpansionApplied = false, bool partialPhaseSuccess = false, SuccessAggregationDiagnostics? successDiagnostics = null)
    {
        var lastCompletedPhaseNo = successDiagnostics?.LastCompletedPhaseNo ?? CalculateLastCompletedPhaseNo(phaseResults);
        var lastFailedPhaseNo = successDiagnostics?.LastFailedPhaseNo ?? phaseResults
            .Where(p => p.Status == ProductionPhaseStatus.Failed)
            .OrderByDescending(p => p.PhaseNo)
            .Select(p => (int?)p.PhaseNo)
            .FirstOrDefault();

        if (successDiagnostics is not null)
        {
            success = successDiagnostics.Success && (!partialPhaseExecution || successDiagnostics.PartialPhaseSuccess);
            partialPhaseSuccess = successDiagnostics.PartialPhaseSuccess;
        }

        if (partialPhaseExecution)
        {
            heroGenerated = RequestedOutputSucceeded(requestedOutputCompletion, "HeroAsset");
            thumbnailsGenerated = RequestedOutputSucceeded(requestedOutputCompletion, "Thumbnail");
            shortVideoGenerated = RequestedOutputSucceeded(requestedOutputCompletion, "ShortVideo");
            longVideoGenerated = RequestedOutputSucceeded(requestedOutputCompletion, "LongVideo");
        }

        var publishGateDiagnosticsPath = Path.Combine(outputRoot, "validation", "phase-20-publish-gate-diagnostics.json");
        var publishGateChecked = File.Exists(publishGateDiagnosticsPath);
        var publishApproved = ReadDiagnosticBool(publishGateDiagnosticsPath, "publishApproved") == true;
        var phase19ReviewApproved = ReadDiagnosticBool(publishGateDiagnosticsPath, "phase19ReviewApproved") == true;

        var authoritativeErrors = BuildAuthoritativeErrors(errors, phaseResults);
        return new(success, dryRun, true, false, 1, plan.Id, plan.Title ?? string.Empty, outputRoot, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, productionRequest, ProductionSteps, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), authoritativeErrors, phaseResults, lastCompletedPhaseNo, lastFailedPhaseNo, executionMode, completedPlanRerun, previousOutputArchived, archivePath, deletedOutputFolders, startPhaseNo, endPhaseNo, requestedOutputCompletion, partialPhaseExecution, requestedStartPhase ?? startPhaseNo, requestedEndPhase ?? endPhaseNo, startPhaseNo, endPhaseNo, partialPhaseSuccess, dependencyExpansionApplied, plan.Id, plan.Id, true, plan.AstronomyEventIntelligence?.AutoGenerateAllowed, plan.AstronomyEventIntelligence?.AutoGenerateAllowed == false, "ManualPlanId", publishGateChecked, publishApproved, phase19ReviewApproved, successDiagnostics);
    }


    private static bool? RequestedOutputSucceeded(IReadOnlyList<RequestedOutputCompletion>? requestedOutputCompletion, string outputType)
    {
        var completion = (requestedOutputCompletion ?? [])
            .FirstOrDefault(output => string.Equals(output.OutputType, outputType, StringComparison.OrdinalIgnoreCase));
        if (completion is null) return null;
        if (string.Equals(completion.Status, "OutOfScope", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(completion.Status, "NotRun", StringComparison.OrdinalIgnoreCase)) return null;
        return string.Equals(completion.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ReadDiagnosticBool(string path, string propertyName)
    {
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static bool IsProductionCompleted(ContentGenerationPlan plan)
        => string.Equals(plan.Status, "ProductionCompleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.PlanStatus, "ProductionCompleted", StringComparison.OrdinalIgnoreCase);

    private static int ResolveStartPhaseNo(ContentPlanProductionExecutionRequest request)
    {
        if (request.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs && request.RebuildIntelligence) return 1;
        return request.StartPhaseNo ?? (request.ExecutionMode == ContentPlanExecutionMode.FullRebuild ? 1 : request.ExecutionMode is ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase ? 3 : 1);
    }

    private static PhaseRangeResolution ResolveExecutionRange(ContentPlanExecutionMode executionMode, int requestedStartPhaseNo, int requestedEndPhaseNo, DependencyExpansionMode dependencyExpansionMode)
    {
        var requestedStart = Math.Clamp(requestedStartPhaseNo, 1, 20);
        var requestedEnd = Math.Clamp(requestedEndPhaseNo, requestedStart, 20);
        if (executionMode != ContentPlanExecutionMode.RebuildOutputs || dependencyExpansionMode != DependencyExpansionMode.Rebuild)
            return new(requestedStart, requestedEnd, false);

        var expandedStart = requestedStart;
        for (var phaseNo = requestedStart; phaseNo <= requestedEnd; phaseNo++)
            expandedStart = Math.Min(expandedStart, ResolveEarliestPrerequisitePhase(phaseNo));

        return new(expandedStart, requestedEnd, expandedStart != requestedStart);
    }

    private static int ResolveEarliestPrerequisitePhase(int phaseNo)
    {
        var visited = new HashSet<int>();
        return ResolveEarliestPrerequisitePhase(phaseNo, visited);
    }

    private static int ResolveEarliestPrerequisitePhase(int phaseNo, HashSet<int> visited)
    {
        if (!visited.Add(phaseNo)) return phaseNo;

        var earliest = phaseNo;
        foreach (var dependencyPhaseNo in ResolvePrerequisitePhases(phaseNo))
            earliest = Math.Min(earliest, ResolveEarliestPrerequisitePhase(dependencyPhaseNo, visited));

        return earliest;
    }

    private static IReadOnlyList<int> ResolvePrerequisitePhases(int phaseNo)
        => phaseNo switch
        {
            4 => [3],
            5 => [4],
            6 => [5],
            7 => [4, 5, 6],
            8 => [3, 6, 7],
            9 => [8],
            10 => [3, 5, 6, 7, 8, 9],
            11 => [10],
            12 => [10, 11],
            13 => [],
            14 => [],
            >= 15 => [14],
            _ => []
        };

    private static OutputPreparationResult PrepareOutputRoot(string outputRoot, ContentPlanProductionExecutionRequest request, int cleanupStartPhaseNo, int cleanupEndPhaseNo)
    {
        if (request.ExecutionMode is not (ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.RerunPhase or ContentPlanExecutionMode.FullRebuild))
            return new(false, null, []);

        // Phase 4 owns an idempotent committed-authority reuse path. Preserve its files so
        // the integration service can verify the existing publication and report
        // P4PUB_ALREADY_PUBLISHED instead of forcing a destructive rebuild.
        if (request.ExecutionMode == ContentPlanExecutionMode.RerunPhase
            && cleanupStartPhaseNo == 4
            && cleanupEndPhaseNo == 4
            && !request.OverwriteExisting)
            return new(false, null, []);

        if (Directory.Exists(outputRoot) && !request.OverwriteExisting)
            throw new InvalidOperationException("Completed plan rerun output cleanup requires overwriteExisting=true.");

        if (!Directory.Exists(outputRoot))
            return new(false, null, []);

        if (request.ArchivePreviousRun)
        {
            var archivePath = ArchivePreviousRun(outputRoot, request.ExecutionMode, request.RebuildIntelligence, cleanupStartPhaseNo, cleanupEndPhaseNo);
            return new(true, archivePath, []);
        }

        var deleted = request.ExecutionMode == ContentPlanExecutionMode.FullRebuild && cleanupStartPhaseNo <= 1
            ? DeleteFullPlanOutput(outputRoot)
            : DeleteRebuildOutputs(outputRoot, request.RebuildIntelligence, cleanupStartPhaseNo, cleanupEndPhaseNo);
        return new(false, null, deleted);
    }

    private static string ArchivePreviousRun(string outputRoot, ContentPlanExecutionMode executionMode, bool rebuildIntelligence, int requestedStartPhaseNo, int requestedEndPhaseNo)
    {
        var archivePath = Path.Combine(outputRoot, "_archive", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(archivePath);
        if (executionMode == ContentPlanExecutionMode.FullRebuild)
        {
            foreach (var directory in Directory.EnumerateDirectories(outputRoot).ToArray())
            {
                if (string.Equals(Path.GetFileName(directory), "_archive", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.Move(directory, Path.Combine(archivePath, Path.GetFileName(directory)));
            }

            foreach (var file in Directory.EnumerateFiles(outputRoot).ToArray())
                File.Move(file, Path.Combine(archivePath, Path.GetFileName(file)));
            return archivePath;
        }

        foreach (var entry in ResolveRebuildOutputEntriesToDelete(requestedStartPhaseNo, requestedEndPhaseNo))
        {
            var path = Path.Combine(outputRoot, entry);
            var destination = Path.Combine(archivePath, entry);
            if (Directory.Exists(path))
                Directory.Move(path, destination);
            else if (File.Exists(path))
                File.Move(path, destination);
        }

        if (rebuildIntelligence)
        {
            var productionIntelligence = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
            if (File.Exists(productionIntelligence))
            {
                var destination = Path.Combine(archivePath, "plan-input", "production-event-intelligence.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(productionIntelligence, destination);
            }
        }

        return archivePath;
    }

    private static IReadOnlyList<string> DeleteFullPlanOutput(string outputRoot)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(outputRoot)) return deleted;
        foreach (var directory in Directory.EnumerateDirectories(outputRoot).ToArray())
        {
            deleted.Add(directory);
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(outputRoot).ToArray())
        {
            deleted.Add(file);
            File.Delete(file);
        }
        return deleted;
    }

    private static IReadOnlyList<string> DeleteRebuildOutputs(string outputRoot, bool rebuildIntelligence, int requestedStartPhaseNo, int requestedEndPhaseNo)
    {
        var deleted = new List<string>();
        foreach (var entry in ResolveRebuildOutputEntriesToDelete(requestedStartPhaseNo, requestedEndPhaseNo))
        {
            var path = Path.Combine(outputRoot, entry);
            if (Directory.Exists(path))
            {
                deleted.Add(path);
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                deleted.Add(path);
                File.Delete(path);
            }
        }

        if (rebuildIntelligence)
        {
            var productionIntelligence = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
            if (File.Exists(productionIntelligence))
            {
                deleted.Add(productionIntelligence);
                File.Delete(productionIntelligence);
            }
        }

        return deleted;
    }

    private static IReadOnlyList<string> ResolveRebuildOutputEntriesToDelete(int requestedStartPhaseNo, int requestedEndPhaseNo)
    {
        var entries = new List<string>();
        if (requestedStartPhaseNo <= 9 && requestedEndPhaseNo >= 8)
            entries.Add("scene-approval-v3");
        if (requestedStartPhaseNo <= 11 && requestedEndPhaseNo >= 11)
            entries.Add("hero");
        if (requestedStartPhaseNo <= 12 && requestedEndPhaseNo >= 12)
            entries.Add("thumbnails");
        if (requestedStartPhaseNo <= 13 && requestedEndPhaseNo >= 13)
            entries.Add("gallery");
        if (requestedStartPhaseNo <= 14 && requestedEndPhaseNo >= 14)
            entries.Add("sync");
        if (requestedStartPhaseNo <= 15 && requestedEndPhaseNo >= 15)
            entries.Add("narration");
        if (requestedStartPhaseNo <= 17 && requestedEndPhaseNo >= 16)
            entries.Add("tts");
        if (requestedStartPhaseNo <= 19 && requestedEndPhaseNo >= 18)
            entries.Add("video-assembly");
        if (requestedStartPhaseNo <= 20 && requestedEndPhaseNo >= 10 && !(requestedStartPhaseNo == 14 && requestedEndPhaseNo == 14))
            entries.Add("validation");
        if (requestedStartPhaseNo <= 20 && requestedEndPhaseNo >= 1)
            entries.Add("phase-manifest.json");
        if (requestedStartPhaseNo <= 3 && requestedEndPhaseNo >= 3)
            entries.Add("question-engine");
        return entries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed record PhaseRangeResolution(int StartPhaseNo, int EndPhaseNo, bool DependencyExpansionApplied);

    private sealed record OutputPreparationResult(bool PreviousOutputArchived, string? ArchivePath, IReadOnlyList<string> DeletedOutputFolders);

    private string BuildPlanOutputRoot(ContentPlanProductionPipelineRequest request)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "plans", Sanitize(request.RegionId), (request.ScheduledUtc?.Year ?? request.PeakUtc?.Year ?? DateTimeOffset.UtcNow.Year).ToString(), request.PlanId.ToString("D"));

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
