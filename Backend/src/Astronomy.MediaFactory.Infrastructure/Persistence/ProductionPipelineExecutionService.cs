using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class ProductionPipelineExecutionService(
    IQuestionEngine questionEngine,
    IQuestionScenePlanner scenePlanner,
    IQuestionSceneIntentEnricher sceneIntentEnricher,
    IQuestionDrivenNarrationGenerator narrationGenerator,
    IEditorialAstronomyInfographicComposer sceneEngine,
    IHeroAssetIntelligenceEngine heroEngine,
    IThumbnailAssetIntelligenceService thumbnailEngine,
    IVideoAssemblyIntelligenceService videoAssemblyEngine,
    IEventProductionIntelligenceAdapter intelligenceAdapter,
    IMediaEventStrategyResolver strategyResolver,
    IProductionPipelineQualityValidator qualityValidator,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ProductionPipelineExecutionService> logger,
    IOptions<VideoAssemblyOptions>? videoAssemblyOptions = null) : IProductionPipelineExecutionService, IProductionPhaseRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const double CalibratedShortNarrationSecondsPerWord = 32.328 / 57.0;
    private const double DefaultLongNarrationWordsPerMinute = 135.0;
    private const int ShortNarrationMinimumWords = 45;
    private const int ShortNarrationMaximumWords = 79;
    private const double ShortNarrationTargetMinimumSeconds = 30.0;
    private const double ShortNarrationTargetMaximumSeconds = 40.0;
    private const double ShortNarrationMinimumSeconds = 25.0;
    private const double ShortNarrationMaximumSeconds = 45.0;
    private const double LongNarrationMinimumSeconds = 120.0;
    private const double LongNarrationMaximumSeconds = 180.0;

    public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
        => RunAsync(request, cancellationToken);

    public async Task<ProductionPipelineExecutionResult> RunAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);

        var productionRequest = request.Request;
        var eventIdResolution = ResolveAstronomyEventId(request);
        var eventId = eventIdResolution.EventId?.ToString("D") ?? string.Empty;
        var warnings = new List<string>(productionRequest.Warnings);
        var errors = new List<string>();
        var generatedFiles = new List<string>();
        var outputRoot = request.OutputRoot;
        var productionIntelligence = intelligenceAdapter.Normalize(request);
        var strategy = strategyResolver.Resolve(productionIntelligence.EventType, productionIntelligence.Title);
        var executionContext = BuildProductionExecutionContext(request.ExecutionContext, productionRequest, eventIdResolution.EventId ?? Guid.Empty, outputRoot, productionIntelligence, strategy);
        var startPhaseNo = Math.Clamp(request.StartPhaseNo ?? 1, 1, 19);
        var endPhaseNo = Math.Clamp(request.EndPhaseNo ?? 19, startPhaseNo, 19);
        var phaseResults = new List<ProductionPhaseResult>();
        var deletedFilesDueToOverwrite = new List<string>();

        if (request.OverwriteExisting && startPhaseNo <= 1)
            ClearProductionOutputRoot(outputRoot);

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(executionContext.ValidationRoot!);

        var context = new ProductionPhaseContext(request, productionRequest, eventIdResolution.EventId ?? Guid.Empty, eventId, outputRoot, executionContext, productionIntelligence, strategy, request.DryRun, request.OverwriteExisting, startPhaseNo, endPhaseNo, request.RetryFailedOnly, request.ExecutionMode, deletedFilesDueToOverwrite);
        if (request.OverwriteExisting)
            ClearPhaseRangeOutputsForOverwrite(context);

        Directory.CreateDirectory(Path.Combine(executionContext.SceneRoot!, "short"));
        Directory.CreateDirectory(Path.Combine(executionContext.SceneRoot!, "long"));
        warnings.Add($"sceneApprovalStagingRoot={NormalizePath(executionContext.SceneRoot!)}");
        warnings.Add($"sceneApprovalNormalizedRoot={NormalizePath(GetSceneApprovalNormalizedRoot(outputRoot))}");

        if (request.DryRun)
        {
            foreach (var phase in PhaseDefinitions().Where(p => p.No >= startPhaseNo && p.No <= endPhaseNo))
            {
                phaseResults.Add(await WritePhaseValidationAsync(context, phase.No, phase.Name, ProductionPhaseStatus.Skipped, [], [], [], [], "Dry run: phase was planned but not executed.", false, cancellationToken));
            }
            await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
            return BuildResult(true, true, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, phaseResults);
        }

        foreach (var phase in PhaseDefinitions())
        {
            if (phase.No < startPhaseNo || phase.No > endPhaseNo) continue;
            if (!IsPhaseRequiredForRequestedOutputs(context, phase.No))
            {
                var skipped = await WritePhaseValidationAsync(context, phase.No, phase.Name, ProductionPhaseStatus.Skipped, [], [], [], [], OutputTypeNotRequestedReason, false, cancellationToken);
                phaseResults.Add(skipped);
                await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
                continue;
            }
            if (request.RetryFailedOnly && PreviousPhaseSucceeded(context, phase.No) && PreviousPhaseRequiredOutputsExist(context, phase.No))
            {
                var skipped = await WritePhaseValidationAsync(context, phase.No, phase.Name, ProductionPhaseStatus.Skipped, [], [], [], [], "retryFailedOnly=true: previous successful phase was not rerun.", false, cancellationToken);
                phaseResults.Add(skipped);
                await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
                continue;
            }
            var result = await ExecutePhaseAsync(context, phase.No, phase.Name, phase.Action, cancellationToken);
            phaseResults.Add(result);
            generatedFiles.AddRange(result.OutputFiles.Where(File.Exists));
            warnings.AddRange(result.Warnings);
            errors.AddRange(result.Errors);
            await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
            if (result.Status == ProductionPhaseStatus.Failed)
            {
                logger.LogWarning("Production phase {PhaseNo} {PhaseName} failed for plan {PlanId}: {Errors}", result.PhaseNo, result.PhaseName, productionRequest.PlanId, string.Join(" | ", result.Errors));
                break;
            }
        }

        var shortVideo = Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4");
        var longVideo = Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4");
        var shortScenesGenerated = DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short"))
            || DirectoryHasPng(Path.Combine(executionContext.SceneRoot!, "short"))
            || PreviousPhaseSucceeded(context, 8);
        var longScenesGenerated = DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long"))
            || DirectoryHasPng(Path.Combine(executionContext.SceneRoot!, "long"))
            || PreviousPhaseSucceeded(context, 9);
        var requestedOutputCompletion = BuildRequestedOutputCompletion(context, phaseResults);
        var success = CalculatePipelineSuccess(context, phaseResults, errors);
        return BuildResult(success, false, outputRoot, File.Exists(Path.Combine(outputRoot, "question-engine", "question-answer-set.json")), shortScenesGenerated, longScenesGenerated, HeroContractExists(outputRoot), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "short", "video-narration-script.json")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "long", "video-long-narration-script.json")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.mp3")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "short", "video-tts-audio.mp3")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.mp3")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "long", "video-long-tts-audio.mp3")), File.Exists(shortVideo), File.Exists(longVideo), File.Exists(shortVideo) ? shortVideo : string.Empty, File.Exists(longVideo) ? longVideo : string.Empty, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), phaseResults, requestedOutputCompletion);
    }

    private IReadOnlyList<(int No, string Name, Func<ProductionPhaseContext, CancellationToken, Task<IReadOnlyList<string>>> Action)> PhaseDefinitions() =>
    [
        (1, "Load Plan", PhaseLoadPlanAsync),
        (2, "Build ProductionEventIntelligence", PhaseBuildProductionIntelligenceAsync),
        (3, "Generate QuestionAnswerSet", PhaseGenerateQuestionsAsync),
        (4, "Validate Questions", PhaseValidateQuestionsAsync),
        (5, "Generate Scene Plan", PhaseGenerateScenePlanAsync),
        (6, "Enrich Scene Plan", PhaseEnrichScenePlanAsync),
        (7, "Generate Narration Plan", PhaseGenerateNarrationPlanAsync),
        (8, "Generate Short Scene Images", PhaseGenerateSceneImagesAsync),
        (9, "Generate Long Scene Images", PhaseValidateLongSceneImagesAsync),
        (10, "Validate Scene Assets", PhaseValidateSceneAssetsAsync),
        (11, "Generate Hero", PhaseGenerateHeroAsync),
        (12, "Generate Thumbnails", PhaseGenerateThumbnailsAsync),
        (13, "Generate Short Narration", (ctx, ct) => PhaseGenerateVideoNarrationAsync(ctx, ScenePresentationProfile.ShortForm, ct)),
        (14, "Generate Long Narration", (ctx, ct) => PhaseGenerateVideoNarrationAsync(ctx, ScenePresentationProfile.LongForm, ct)),
        (15, "Generate Short TTS", (ctx, ct) => PhaseGenerateTtsAsync(ctx, ScenePresentationProfile.ShortForm, ct)),
        (16, "Generate Long TTS", (ctx, ct) => PhaseGenerateTtsAsync(ctx, ScenePresentationProfile.LongForm, ct)),
        (17, "Assemble Short Video", (ctx, ct) => PhaseAssembleVideoAsync(ctx, ScenePresentationProfile.ShortForm, ct)),
        (18, "Assemble Long Video", (ctx, ct) => PhaseAssembleVideoAsync(ctx, ScenePresentationProfile.LongForm, ct)),
        (19, "Final Validation", PhaseFinalValidationAsync)
    ];


    private const string OutputTypeNotRequestedReason = "Output type not requested";

    private static bool IsPhaseRequiredForRequestedOutputs(ProductionPhaseContext context, int phaseNo)
        => phaseNo switch
        {
            <= 10 => true,
            11 => IsRequestedOutput(context, "HeroAsset"),
            12 => IsRequestedOutput(context, "Thumbnail"),
            13 or 15 or 17 => IsRequestedOutput(context, "ShortVideo"),
            14 or 16 or 18 => IsRequestedOutput(context, "LongVideo"),
            19 => true,
            _ => true
        };

    private static bool IsRequestedOutput(ProductionPhaseContext context, string outputType)
        => context.Request.RequestedOutputs.Any(output => string.Equals(output, outputType, StringComparison.OrdinalIgnoreCase));

    private static bool CalculatePipelineSuccess(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults, IReadOnlyList<string> errors)
    {
        if (errors.Count > 0) return false;
        foreach (var result in phaseResults)
        {
            if (result.Status == ProductionPhaseStatus.Failed) return false;
            if (result.Status == ProductionPhaseStatus.Skipped && IsPhaseRequiredForRequestedOutputs(context, result.PhaseNo) && !string.Equals(result.Reason, "retryFailedOnly=true: previous successful phase was not rerun.", StringComparison.OrdinalIgnoreCase)) return false;
            if (result.Status == ProductionPhaseStatus.Skipped && !IsPhaseRequiredForRequestedOutputs(context, result.PhaseNo) && !string.Equals(result.Reason, OutputTypeNotRequestedReason, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static IReadOnlyList<RequestedOutputCompletion> BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults)
        => new[]
        {
            BuildRequestedOutputCompletion(context, phaseResults, "ShortVideo", [13, 15, 17]),
            BuildRequestedOutputCompletion(context, phaseResults, "LongVideo", [14, 16, 18]),
            BuildRequestedOutputCompletion(context, phaseResults, "HeroAsset", [11]),
            BuildRequestedOutputCompletion(context, phaseResults, "Thumbnail", [12])
        };

    private static RequestedOutputCompletion BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults, string outputType, IReadOnlyList<int> requiredPhases)
    {
        var requested = IsRequestedOutput(context, outputType);
        var related = phaseResults.Where(p => requiredPhases.Contains(p.PhaseNo)).ToArray();
        var succeeded = related.Where(p => p.Status == ProductionPhaseStatus.Succeeded).Select(p => p.PhaseNo).ToArray();
        var failed = related.Where(p => p.Status == ProductionPhaseStatus.Failed).Select(p => p.PhaseNo).ToArray();
        var skipped = related.Where(p => p.Status == ProductionPhaseStatus.Skipped).Select(p => p.PhaseNo).ToArray();
        var status = !requested ? "Skipped" : failed.Length > 0 ? "Failed" : requiredPhases.All(phaseNo => succeeded.Contains(phaseNo) || PreviousPhaseSucceeded(context, phaseNo)) ? "Succeeded" : "Failed";
        return new RequestedOutputCompletion(outputType, requested, status, requested ? requiredPhases : Array.Empty<int>(), succeeded, failed, skipped);
    }

    private static bool PreviousPhaseSucceeded(ProductionPhaseContext context, int phaseNo)
    {
        var path = Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("status", out var status)
                && string.Equals(status.GetString(), ProductionPhaseStatus.Succeeded.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool PreviousPhaseRequiredOutputsExist(ProductionPhaseContext context, int phaseNo)
        => phaseNo switch
        {
            6 => File.Exists(BuildEnrichedScenePlanPath(context)),
            7 => File.Exists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json")),
            _ => true
        };

    private static void ValidatePhaseInputContract(ProductionPhaseContext context, int phaseNo)
    {
        var missing = new List<string>();
        if (context.Request.PlanId == Guid.Empty) missing.Add("planId");
        if (string.IsNullOrWhiteSpace(context.Request.Title)) missing.Add("title");
        if (string.IsNullOrWhiteSpace(context.Request.ShortTitle)) missing.Add("shortTitle");
        if (string.IsNullOrWhiteSpace(context.Request.EventType)) missing.Add("eventType");
        if (context.Request.PrimaryObjects.Count == 0) missing.Add("primaryObjects");
        if (context.Request.RequestedOutputs.Count == 0) missing.Add("requestedOutputs");
        if (string.IsNullOrWhiteSpace(context.Request.RegionId)) missing.Add("regionId");
        if (string.IsNullOrWhiteSpace(context.Request.Language)) missing.Add("language");
        if (context.ProductionEventIntelligence is null) missing.Add("productionEventIntelligence");
        if (context.MediaEventStrategy is null) missing.Add("mediaEventStrategy");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Phase {phaseNo} input contract is invalid for current event lock: missing {string.Join(", ", missing)}.");
    }

    private static object BuildCurrentEventLock(ProductionPhaseContext context)
        => new
        {
            context.Request.PlanId,
            context.Request.Title,
            context.Request.ShortTitle,
            context.Request.EventType,
            context.Request.PrimaryObjects,
            context.Request.SecondaryObjects,
            timing = new { context.Request.StartUtc, context.Request.PeakUtc, context.Request.EndUtc, context.Request.ScheduledUtc, context.Request.LocalPeakTime, context.Request.BestViewingWindowLocal },
            direction = context.Request.SkyDirectionHint,
            strategy = context.Request.ContentStrategy ?? context.MediaEventStrategy?.EventType,
            context.Request.RequestedOutputs
        };

    private async Task<ProductionPhaseResult> ExecutePhaseAsync(ProductionPhaseContext context, int phaseNo, string phaseName, Func<ProductionPhaseContext, CancellationToken, Task<IReadOnlyList<string>>> action, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            if (phaseNo <= 14) ValidatePhaseInputContract(context, phaseNo);
            var outputs = (await action(context, cancellationToken)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = outputs.Where(p => !File.Exists(p) && !Directory.Exists(p)).Select(p => $"Expected output was not found: {p}").ToArray();
            var phase10TitleDiagnostics = phaseNo == 10 ? ReadPhase10TitleDiagnostics(outputs) : null;
            return await WritePhaseValidationAsync(context, phaseNo, phaseName, missing.Length == 0 ? ProductionPhaseStatus.Succeeded : ProductionPhaseStatus.Failed, [], outputs, [], missing, missing.Length == 0 ? "Validation passed." : "Validation failed: required output missing.", missing.Length > 0, cancellationToken, started, phase10TitleDiagnostics);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            var phase10TitleDiagnostics = phaseNo == 10
                ? ReadPhase10TitleDiagnostics([Path.Combine(context.ExecutionContext.QuestionRoot!, "production-quality-validation-before-assembly.json")])
                : null;
            return await WritePhaseValidationAsync(context, phaseNo, phaseName, ProductionPhaseStatus.Failed, [], [], [], [ex.Message], ex.Message, true, cancellationToken, started, phase10TitleDiagnostics);
        }
    }

    private async Task<IReadOnlyList<string>> PhaseLoadPlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        await WritePlanInputAsync(context.OutputRoot, context.Request, context.ProductionEventIntelligence, cancellationToken);
        return [Path.Combine(context.OutputRoot, "plan-input", "content-plan-production-request.json"), Path.Combine(context.OutputRoot, "plan-input", "production-event-intelligence.json")];
    }

    private async Task<IReadOnlyList<string>> PhaseBuildProductionIntelligenceAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
        => [await WriteProductionIntelligenceAsync(context.OutputRoot, context.ProductionEventIntelligence, cancellationToken)];

    private async Task<IReadOnlyList<string>> PhaseGenerateQuestionsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await questionEngine.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(context.Request.RegionId, PlanIds: [context.Request.PlanId.ToString("D")], MaxEvents: 1, Language: context.Request.Language, DryRun: false, OverwriteExisting: context.OverwriteExisting, ProductionContext: context.ExecutionContext), cancellationToken);
        return response.GeneratedFiles;
    }

    private Task<IReadOnlyList<string>> PhaseValidateQuestionsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([RequireFile(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-answer-set.json"), "QuestionAnswerSet")]);

    private async Task<IReadOnlyList<string>> PhaseGenerateScenePlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await scenePlanner.GenerateQuestionScenePlanAsync(new QuestionScenePlanRequest(context.Request.RegionId, context.EventId, context.Request.Language, false, context.OverwriteExisting, context.ExecutionContext), cancellationToken);
        return response.GeneratedFiles;
    }

    private async Task<IReadOnlyList<string>> PhaseEnrichScenePlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var enrichedPath = BuildEnrichedScenePlanPath(context);
        var response = await sceneIntentEnricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(context.EventId, context.Request.RegionId, context.Request.Language, DryRun: false, OverwriteExisting: context.OverwriteExisting, ProductionContext: context.ExecutionContext), cancellationToken);
        if (!response.IsValid)
            throw new InvalidOperationException("Phase 6 scene plan enrichment failed validation: " + string.Join(" | ", response.Warnings));

        await ValidatePhase6EnrichedScenePlanContractAsync(context, enrichedPath, cancellationToken);
        return response.GeneratedFiles.Concat([enrichedPath]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateNarrationPlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        RequireFile(BuildEnrichedScenePlanPath(context), "Enriched question-driven scene plan");
        var narrationRequest = BuildQuestionDrivenNarrationRequest(context);
        ValidatePhase7NarrationRequest(narrationRequest, context);
        var response = await narrationGenerator.GenerateQuestionDrivenNarrationAsync(narrationRequest, cancellationToken);
        var outputs = new List<string>(response.GeneratedFiles);
        Directory.CreateDirectory(context.ExecutionContext.SceneRoot!);
        CopyFile(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json"), Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration.json"), outputs);
        CopyFile(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json"), Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration-review.json"), outputs);
        return outputs;
    }



    private static string BuildEnrichedScenePlanPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");

    private static string BuildLongNarrationRequestPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.NarrationRoot!, "long", "long-narration-request.json");

    private static string BuildLongNarrationOutputPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.NarrationRoot!, "long", "narration.txt");

    private static string BuildLongSceneApprovalRoot(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.SceneRoot!, "long");

    private static async Task ValidatePhase6EnrichedScenePlanContractAsync(ProductionPhaseContext context, string enrichedPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(enrichedPath))
            throw new InvalidOperationException($"Phase 6 scene plan enrichment did not write required output file at '{NormalizePath(enrichedPath)}'.");

        var json = await File.ReadAllTextAsync(enrichedPath, cancellationToken);
        var plan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Phase 6 enriched scene plan could not be parsed.");

        var issues = new List<string>();
        if (!plan.IsValid)
            issues.Add("enriched plan is marked invalid");
        if (plan.Scenes.Count == 0)
            issues.Add("enriched plan must include at least one scene");
        if (plan.Diagnostics?.LeakageTermsFound is { Count: > 0 })
            issues.Add("diagnostics reported forbidden leakage: " + string.Join(", ", plan.Diagnostics.LeakageTermsFound));

        var generatedFields = ExtractEnrichedSceneGeneratedText(plan).ToArray();
        var enrichedSceneIntents = BuildPhase6ValidationIntentDiagnostics(plan).ToArray();
        var planetGroupingValidation = ResolvePlanetGroupingPhase6Validation(context, enrichedSceneIntents);
        if (planetGroupingValidation.PlanetGroupingValidationPathExecuted)
        {
            if (!planetGroupingValidation.PlanetGroupingVisualContractPassed)
            {
                if (!planetGroupingValidation.PlanetGroupingIntentInjected)
                    issues.Add("PlanetGrouping visual contract failed: planetGroupingIntentInjected=false");
                if (!planetGroupingValidation.GuidedScanPathInjected)
                    issues.Add("PlanetGrouping visual contract failed: guidedScanPathInjected=false");
            }
        }
        else
        {
            var requiredObjects = ResolveRequiredVisualObjectsForPhase6(context, plan).ToArray();
            foreach (var requiredObject in requiredObjects)
            {
                if (!generatedFields.Any(field => ContainsToken(field, requiredObject)))
                    issues.Add($"required visual object '{requiredObject}' was not present in enriched scene intents");
            }
        }

        var forbiddenTerms = BuildForbiddenTermsForStrategy(context).ToArray();
        foreach (var forbiddenTerm in forbiddenTerms)
        {
            if (generatedFields.Any(field => ContainsToken(field, forbiddenTerm)))
                issues.Add($"forbidden term '{forbiddenTerm}' was present in enriched scene intents");
        }

        if (issues.Count > 0)
            throw new InvalidOperationException("Phase 6 enriched scene plan failed output contract validation: " + string.Join("; ", issues));
    }

    private static IEnumerable<string> ResolveRequiredVisualObjectsForPhase6(ProductionPhaseContext context, EnrichedQuestionScenePlanDto plan)
    {
        var requiredObjects = plan.Diagnostics?.RequiredVisualObjects is { Count: > 0 } diagnosticRequiredObjects
            ? diagnosticRequiredObjects
            : context.ProductionEventIntelligence.RequiredVisualObjects ?? Array.Empty<string>();

        return requiredObjects
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractEnrichedSceneGeneratedText(EnrichedQuestionScenePlanDto plan)
    {
        foreach (var scene in plan.Scenes)
        {
            yield return scene.ViewerTakeaway;
            yield return scene.NarrationIntent;
            yield return scene.VisualIntent;
            yield return scene.ImagePromptIntent;
            yield return scene.OverlayIntent;
            yield return scene.AccessibilityIntent;
        }
    }

    private static QuestionDrivenNarrationRequest BuildQuestionDrivenNarrationRequest(ProductionPhaseContext context)
    {
        var source = ResolveEventIdSource(context);
        var eventId = source.EventId?.ToString("D") ?? string.Empty;
        var intelligence = context.ProductionEventIntelligence;
        return new QuestionDrivenNarrationRequest(
            eventId,
            context.Request.RegionId,
            context.Request.Language,
            DryRun: false,
            OverwriteExisting: context.OverwriteExisting,
            ProductionContext: context.ExecutionContext,
            PlanId: context.Request.PlanId,
            EventType: FirstNonEmpty(intelligence.EventType, context.Request.EventType, context.ExecutionContext.EventType),
            Title: FirstNonEmpty(intelligence.Title, context.Request.Title),
            ShortTitle: FirstNonEmpty(intelligence.ShortTitle, context.Request.ShortTitle),
            PrimaryObjects: intelligence.PrimaryObjects.Count > 0 ? intelligence.PrimaryObjects : context.Request.PrimaryObjects,
            SecondaryObjects: intelligence.SecondaryObjects.Count > 0 ? intelligence.SecondaryObjects : context.Request.SecondaryObjects,
            LocalPeakTime: FirstNonEmpty(intelligence.LocalPeakTime, context.Request.LocalPeakTime),
            SkyDirectionHint: FirstNonEmpty(intelligence.SkyDirectionHint, context.Request.SkyDirectionHint),
            BestViewingWindowLocal: FirstNonEmpty(intelligence.BestViewingWindowLocal, context.Request.BestViewingWindowLocal),
            StrategyId: FirstNonEmpty(intelligence.StrategyId, context.MediaEventStrategy.EventType),
            SourceOfEventId: source.Source);
    }

    private static void ValidatePhase7NarrationRequest(QuestionDrivenNarrationRequest request, ProductionPhaseContext context)
    {
        var diagnostics = BuildPhase7NarrationDiagnostics(request, context);
        if (diagnostics.PlanIdPresent && diagnostics.EventIdPresent && diagnostics.RegionIdPresent && diagnostics.LanguagePresent)
            return;

        throw new ArgumentException(
            "Phase 7 narration request mapping is incomplete: "
            + $"planIdPresent={diagnostics.PlanIdPresent}, "
            + $"eventIdPresent={diagnostics.EventIdPresent}, "
            + $"regionIdPresent={diagnostics.RegionIdPresent}, "
            + $"languagePresent={diagnostics.LanguagePresent}, "
            + $"eventType={diagnostics.EventType ?? "<null>"}, "
            + $"strategyId={diagnostics.StrategyId ?? "<null>"}, "
            + $"sourceOfEventId={diagnostics.SourceOfEventId ?? "<null>"}.",
            nameof(request));
    }

    private static Phase7NarrationDiagnostics BuildPhase7NarrationDiagnostics(QuestionDrivenNarrationRequest request, ProductionPhaseContext context)
        => new(
            PlanIdPresent: (request.PlanId ?? context.ExecutionContext.ContentGenerationPlanId ?? context.Request.PlanId) != Guid.Empty,
            EventIdPresent: Guid.TryParse(request.EventId, out var eventGuid) && eventGuid != Guid.Empty,
            RegionIdPresent: !string.IsNullOrWhiteSpace(request.RegionId),
            LanguagePresent: !string.IsNullOrWhiteSpace(request.Language),
            EventType: FirstNonEmpty(request.EventType, context.ProductionEventIntelligence.EventType, context.Request.EventType),
            StrategyId: FirstNonEmpty(request.StrategyId, context.ProductionEventIntelligence.StrategyId, context.MediaEventStrategy.EventType),
            SourceOfEventId: request.SourceOfEventId);

    private static (Guid? EventId, string Source) ResolveEventIdSource(ProductionPhaseContext context)
    {
        if (context.AstronomyEventIntelligenceId != Guid.Empty) return (context.AstronomyEventIntelligenceId, "ProductionPipelineRequest.AstronomyEventIntelligenceId");
        if (context.ExecutionContext.AstronomyEventIntelligenceId is { } contextEventId && contextEventId != Guid.Empty) return (contextEventId, "ProductionPipelineExecutionContext.AstronomyEventIntelligenceId");
        if (context.ExecutionContext.ProductionExecutionContext?.AstronomyEventIntelligenceId is { } contractEventId && contractEventId != Guid.Empty) return (contractEventId, "ProductionExecutionContext.AstronomyEventIntelligenceId");
        return (null, "missing");
    }

    private static (Guid? EventId, string Source) ResolveAstronomyEventId(ProductionPipelineRequest request)
    {
        if (request.AstronomyEventIntelligenceId != Guid.Empty) return (request.AstronomyEventIntelligenceId, "ProductionPipelineRequest.AstronomyEventIntelligenceId");
        if (request.ExecutionContext?.AstronomyEventIntelligenceId is { } contextEventId && contextEventId != Guid.Empty) return (contextEventId, "ProductionPipelineExecutionContext.AstronomyEventIntelligenceId");
        if (request.ExecutionContext?.ProductionExecutionContext?.AstronomyEventIntelligenceId is { } contractEventId && contractEventId != Guid.Empty) return (contractEventId, "ProductionExecutionContext.AstronomyEventIntelligenceId");
        return (null, "missing");
    }

    private sealed record Phase7NarrationDiagnostics(
        bool PlanIdPresent,
        bool EventIdPresent,
        bool RegionIdPresent,
        bool LanguagePresent,
        string? EventType,
        string? StrategyId,
        string? SourceOfEventId);

    private async Task<IReadOnlyList<string>> PhaseGenerateSceneImagesAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(context.ExecutionContext.SceneRoot!, "short"));
        Directory.CreateDirectory(Path.Combine(context.ExecutionContext.SceneRoot!, "long"));
        ValidateSceneApprovalTextBeforeRendering(context);
        var response = await sceneEngine.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, context.ExecutionContext), cancellationToken);
        var shortRoot = Path.Combine(context.ExecutionContext.SceneRoot!, "short");
        if (!DirectoryHasPng(shortRoot)) throw new InvalidOperationException($"Short scene image validation failed: no .png files were found in '{shortRoot}'.");
        return response.GeneratedFiles.Concat(Directory.EnumerateFiles(shortRoot, "*.png")).ToArray();
    }

    private Task<IReadOnlyList<string>> PhaseValidateLongSceneImagesAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var longRoot = Path.Combine(context.ExecutionContext.SceneRoot!, "long");
        if (!DirectoryHasPng(longRoot)) throw new InvalidOperationException($"Long scene image validation failed: no .png files were found in '{longRoot}'.");
        return Task.FromResult<IReadOnlyList<string>>(Directory.EnumerateFiles(longRoot, "*.png").ToArray());
    }

    private async Task<IReadOnlyList<string>> PhaseValidateSceneAssetsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var currentRunValidationRoot = context.ExecutionContext.QuestionRoot!;
        var validation = await qualityValidator.ValidateBeforeVideoAssemblyAsync(context.ProductionEventIntelligence, currentRunValidationRoot, cancellationToken);
        if (!validation.IsValid) throw new InvalidOperationException("Scene asset validation failed: " + string.Join("; ", validation.Errors));
        var materialized = await MaterializeSceneApprovalAsync(context.ExecutionContext.SceneRoot!, GetSceneApprovalNormalizedRoot(context.OutputRoot), cancellationToken);
        return materialized.Concat([Path.Combine(context.ExecutionContext.SceneRoot!, "short"), Path.Combine(context.ExecutionContext.SceneRoot!, "long"), Path.Combine(currentRunValidationRoot, "production-quality-validation-before-assembly.json")]).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateHeroAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await heroEngine.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, HeroAssetGenerationPhase.Full, context.ExecutionContext), cancellationToken);
        return await ValidateAndMaterializeHeroContractAsync(context, response, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateThumbnailsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        foreach (var phase in new[] { "Intelligence", "Composition", "SceneSelection", "Images" })
        {
            var response = await thumbnailEngine.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest { EventId = context.EventId, RegionId = context.Request.RegionId, Language = context.Request.Language, Phase = phase, DryRun = false, OverwriteExisting = context.OverwriteExisting, ThumbnailStyle = "ScrollStopping", ThumbnailVisualStyle = "PhotoCinematic", ProductionContext = context.ExecutionContext }, cancellationToken);
            outputs.AddRange(response.GeneratedFiles);
        }

        var thumbnailSceneManifestPath = Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-scene-manifest.json");
        if (!File.Exists(thumbnailSceneManifestPath))
            throw new InvalidOperationException($"Thumbnail generation failed contract validation: thumbnail-scene-manifest.json is required at '{NormalizePath(thumbnailSceneManifestPath)}'.");

        CopyFile(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-landscape.png"), Path.Combine(context.ExecutionContext.ThumbnailRoot!, "landscape.png"), outputs);
        CopyFile(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-square.png"), Path.Combine(context.ExecutionContext.ThumbnailRoot!, "square.png"), outputs);
        CopyFile(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-portrait.png"), Path.Combine(context.ExecutionContext.ThumbnailRoot!, "portrait.png"), outputs);
        if (!ThumbnailsExist(context.OutputRoot))
            throw new InvalidOperationException("Thumbnail generation failed contract validation: landscape.png, square.png, and portrait.png are required.");
        outputs.Add(thumbnailSceneManifestPath);
        return outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }



    private static async Task<IReadOnlyList<string>> ValidateAndMaterializeHeroContractAsync(ProductionPhaseContext context, HeroAssetGenerationResponse response, CancellationToken cancellationToken)
    {
        var outputs = new List<string>(response.GeneratedFiles);
        var heroRoot = context.ExecutionContext.HeroRoot!;
        var storyPath = Path.Combine(heroRoot, "hero-asset-story.json");
        var blueprintPath = Path.Combine(heroRoot, "hero-asset-blueprint.json");
        var layoutValidationPath = Path.Combine(heroRoot, "hero-layout-validation.json");
        var sceneManifestPath = Path.Combine(heroRoot, "hero-scene-manifest.json");
        var heroLandscapePath = Path.Combine(heroRoot, "hero-landscape.png");
        var heroPath = Path.Combine(heroRoot, "hero.png");

        if (!response.IsValid)
            throw new InvalidOperationException("Hero generation failed contract validation: " + string.Join("; ", response.Warnings.DefaultIfEmpty("hero engine returned IsValid=false")));

        CopyFile(heroLandscapePath, heroPath, outputs);

        var requiredFiles = new[]
        {
            storyPath,
            blueprintPath,
            layoutValidationPath,
            sceneManifestPath,
            heroPath
        };
        var missing = requiredFiles.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Hero generation failed contract validation: required hero files are missing: " + string.Join(", ", missing));

        var heroInfo = new FileInfo(heroPath);
        if (heroInfo.Length <= 0)
            throw new InvalidOperationException($"Hero generation failed contract validation: image file is empty: {NormalizePath(heroPath)}.");

        await ValidateHeroSceneManifestContractAsync(context, sceneManifestPath, cancellationToken);
        var compositionModelPath = Path.Combine(heroRoot, "hero-composition-model.json");
        ValidateHeroForbiddenLeakage(context, [storyPath, blueprintPath, layoutValidationPath, sceneManifestPath, compositionModelPath]);

        outputs.AddRange(requiredFiles);
        return outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task ValidateHeroSceneManifestContractAsync(ProductionPhaseContext context, string sceneManifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sceneManifestPath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        RequireJsonString(root, "eventId", context.EventId, "current astronomy event id");
        RequireJsonString(root, "planId", context.Request.PlanId.ToString("D"), "current planId");
        RequireJsonString(root, "eventTitle", context.ProductionEventIntelligence.Title, "current event title");
        RequireJsonString(root, "eventType", context.ProductionEventIntelligence.EventType, "current event type");
    }

    private static void RequireJsonString(JsonElement root, string propertyName, string expectedValue, string label)
    {
        if (string.IsNullOrWhiteSpace(expectedValue)) return;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Hero generation failed contract validation: hero-scene-manifest.json must reference the {label} in '{propertyName}'.");
        if (!string.Equals(property.GetString(), expectedValue, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Hero generation failed contract validation: hero-scene-manifest.json {propertyName} value '{property.GetString()}' does not match expected {label} '{expectedValue}'.");
    }

    private static void ValidateHeroForbiddenLeakage(ProductionPhaseContext context, IReadOnlyList<string> paths)
    {
        var forbiddenTerms = BuildForbiddenTermsForStrategy(context).ToArray();
        if (forbiddenTerms.Length == 0) return;

        var hits = new List<string>();
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(path);
            var pathHits = forbiddenTerms.Where(term => ContainsToken(text, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (pathHits.Length > 0) hits.Add($"{NormalizePath(path)} => {string.Join(", ", pathHits)}");
        }

        if (hits.Count > 0)
            throw new InvalidOperationException("Hero generation failed contract validation: hero files contain forbidden terms for the selected event strategy: " + string.Join("; ", hits));
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateVideoNarrationAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        var intelligenceRequest = BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Intelligence" : "LongFormIntelligence");
        var scriptRequest = BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Script" : "LongFormScript");
        if (profile == ScenePresentationProfile.LongForm)
        {
            var requestPath = await WriteLongNarrationRequestAsync(context, scriptRequest, cancellationToken);
            outputs.Add(requestPath);
        }

        var intelligence = await videoAssemblyEngine.GenerateVideoAssemblyAsync(intelligenceRequest, cancellationToken);
        outputs.AddRange(intelligence.GeneratedFiles);
        var script = await videoAssemblyEngine.GenerateVideoAssemblyAsync(scriptRequest, cancellationToken);
        if (script is null)
            throw new InvalidOperationException(profile == ScenePresentationProfile.LongForm ? "Long narration generation returned empty output." : "Short narration generation returned empty output.");
        if (profile == ScenePresentationProfile.LongForm && !script.VideoNarrationScriptGenerated && string.IsNullOrWhiteSpace(script.VideoNarrationScriptPath))
            throw new InvalidOperationException("Long narration generation returned empty output.");

        outputs.AddRange(script.GeneratedFiles);
        var scriptPath = profile == ScenePresentationProfile.ShortForm ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-narration-script.json") : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-narration-script.json");
        var target = Path.Combine(context.ExecutionContext.NarrationRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long", "narration.txt");
        CopyFile(scriptPath, target, outputs, jsonNarrationToText: true);
        if (profile == ScenePresentationProfile.LongForm && (!File.Exists(target) || string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(target, cancellationToken))))
            throw new InvalidOperationException("Long narration generation returned empty output.");

        var validationPath = await ValidateNarrationContractAsync(context, profile, scriptPath, target, cancellationToken);
        outputs.Add(validationPath);
        return outputs;
    }

    private static async Task<string> WriteLongNarrationRequestAsync(ProductionPhaseContext context, VideoAssemblyGenerationRequest request, CancellationToken cancellationToken)
    {
        var path = BuildLongNarrationRequestPath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            narrationRequestBuilt = true,
            narrationInputScenePlanPath = NormalizePath(BuildEnrichedScenePlanPath(context)),
            narrationInputSceneApprovalRoot = NormalizePath(BuildLongSceneApprovalRoot(context)),
            narrationOutputPath = NormalizePath(BuildLongNarrationOutputPath(context)),
            request
        }, JsonOptions), cancellationToken);
        return path;
    }

    private async Task<string> ValidateNarrationContractAsync(ProductionPhaseContext context, ScenePresentationProfile profile, string scriptPath, string narrationPath, CancellationToken cancellationToken)
    {
        var expansionApplied = false;
        ShortNarrationTrimDiagnostics? shortTrimDiagnostics = null;
        if (profile == ScenePresentationProfile.ShortForm)
        {
            var initial = await BuildNarrationValidationReportAsync(context, profile, scriptPath, narrationPath, false, cancellationToken);
            if (initial.EstimatedDurationSeconds < ShortNarrationTargetMinimumSeconds || initial.WordCount < ShortNarrationMinimumWords)
            {
                expansionApplied = await ExpandShortNarrationBeforeValidationAsync(context, scriptPath, narrationPath, cancellationToken);
            }
            else if (!initial.IsValid && (initial.WordCount > ShortNarrationMaximumWords || initial.EstimatedDurationSeconds > ShortNarrationMaximumSeconds))
            {
                expansionApplied = await RewriteShortNarrationToDurationTargetAsync(context, scriptPath, narrationPath, cancellationToken, "adjusted");
            }

            shortTrimDiagnostics = await TrimShortNarrationToValidationLimitsAsync(context, scriptPath, narrationPath, cancellationToken);
        }
        else
        {
            if (!File.Exists(narrationPath) || string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(narrationPath, cancellationToken)))
                throw new InvalidOperationException("Long narration generation returned empty output.");

            var initial = await BuildNarrationValidationReportAsync(context, profile, scriptPath, narrationPath, false, cancellationToken);
            if (initial.EstimatedDurationSeconds < LongNarrationMinimumSeconds || initial.WordCount < ResolveLongNarrationMinimumWords())
                expansionApplied = await ExpandLongNarrationBeforeValidationAsync(context, scriptPath, narrationPath, cancellationToken);
        }

        var report = await BuildNarrationValidationReportAsync(context, profile, scriptPath, narrationPath, expansionApplied, cancellationToken, shortTrimDiagnostics);
        Directory.CreateDirectory(Path.GetDirectoryName(report.ValidationPath)!);
        await File.WriteAllTextAsync(report.ValidationPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        if (!report.IsValid)
            throw new InvalidOperationException($"{profile} narration validation failed before TTS: " + string.Join("; ", report.Errors));

        return report.ValidationPath;
    }

    private async Task<NarrationValidationReport> BuildNarrationValidationReportAsync(ProductionPhaseContext context, ScenePresentationProfile profile, string scriptPath, string narrationPath, bool expansionApplied, CancellationToken cancellationToken, ShortNarrationTrimDiagnostics? shortTrimDiagnostics = null)
    {
        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var validationPath = Path.Combine(context.ExecutionContext.NarrationRoot!, profileFolder, "narration-validation.json");
        var text = File.Exists(narrationPath) ? (await File.ReadAllTextAsync(narrationPath, cancellationToken)).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(text) && File.Exists(scriptPath))
            text = ExtractNarrationText(await File.ReadAllTextAsync(scriptPath, cancellationToken)).Trim();

        var wordCount = CountSpokenWords(text);
        var wordsPerMinute = profile == ScenePresentationProfile.LongForm ? ResolveLongNarrationWordsPerMinute() : Math.Round(60.0 / CalibratedShortNarrationSecondsPerWord, 3, MidpointRounding.AwayFromZero);
        var estimatedDurationSeconds = profile == ScenePresentationProfile.ShortForm
            ? Math.Round(wordCount * CalibratedShortNarrationSecondsPerWord, 3, MidpointRounding.AwayFromZero)
            : EstimateLongNarrationSeconds(wordCount);
        var errors = new List<string>();
        var warnings = new List<string>();
        var forbiddenHits = BuildForbiddenTermsForStrategy(context).Where(term => ContainsToken(text, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var titlePresent = HasRequiredTitle(text, context.ProductionEventIntelligence);
        var viewingWindowPresent = HasRequiredViewingWindow(text, context.ProductionEventIntelligence);
        var actionCtaPresent = HasActionCta(text, profile, scriptPath);
        var completeEventStoryPresent = profile == ScenePresentationProfile.ShortForm || HasCompleteLongEventStory(text, scriptPath);

        if (string.IsNullOrWhiteSpace(text)) errors.Add($"Narration text is empty or missing: {NormalizePath(narrationPath)}.");
        if (profile == ScenePresentationProfile.ShortForm)
        {
            if (wordCount < ShortNarrationMinimumWords || wordCount > ShortNarrationMaximumWords)
                errors.Add($"Short narration word count must be {ShortNarrationMinimumWords}-{ShortNarrationMaximumWords} words for the calibrated TTS voice; actualWordCount={wordCount}.");
            if (estimatedDurationSeconds < ShortNarrationMinimumSeconds || estimatedDurationSeconds > ShortNarrationMaximumSeconds)
                errors.Add($"Short narration estimated duration must be {ShortNarrationMinimumSeconds:0}-{ShortNarrationMaximumSeconds:0} seconds before TTS; estimatedDurationSeconds={estimatedDurationSeconds:0.###}.");
            if (!titlePresent) errors.Add("Short narration must include the event title or short title.");
            if (!viewingWindowPresent) errors.Add("Short narration must include the viewing window.");
            if (!actionCtaPresent) errors.Add("Short narration must include an action CTA.");
        }
        else
        {
            if (wordCount < ResolveLongNarrationMinimumWords() || wordCount > ResolveLongNarrationMaximumWords())
                errors.Add($"Long narration word count must be {ResolveLongNarrationMinimumWords()}-{ResolveLongNarrationMaximumWords()} words; actualWordCount={wordCount}.");
            if (estimatedDurationSeconds < LongNarrationMinimumSeconds || estimatedDurationSeconds > LongNarrationMaximumSeconds)
                errors.Add($"Long narration estimated duration must be {LongNarrationMinimumSeconds:0}-{LongNarrationMaximumSeconds:0} seconds before TTS; estimatedDurationSeconds={estimatedDurationSeconds:0.###}.");
            if (!completeEventStoryPresent) errors.Add("Long narration must include the complete event story section sequence.");
        }

        if (forbiddenHits.Length > 0)
            errors.Add("Narration contains forbidden terms for the selected event strategy: " + string.Join(", ", forbiddenHits));

        return new NarrationValidationReport(
            ValidationPath: validationPath,
            Profile: profile.ToString(),
            NarrationPath: NormalizePath(narrationPath),
            ScriptPath: NormalizePath(scriptPath),
            IsValid: errors.Count == 0,
            WordCount: wordCount,
            MinimumWords: profile == ScenePresentationProfile.ShortForm ? ShortNarrationMinimumWords : ResolveLongNarrationMinimumWords(),
            MaximumWords: profile == ScenePresentationProfile.ShortForm ? ShortNarrationMaximumWords : ResolveLongNarrationMaximumWords(),
            EstimatedDurationSeconds: estimatedDurationSeconds,
            MinimumDurationSeconds: profile == ScenePresentationProfile.ShortForm ? ShortNarrationMinimumSeconds : LongNarrationMinimumSeconds,
            MaximumDurationSeconds: profile == ScenePresentationProfile.ShortForm ? ShortNarrationMaximumSeconds : LongNarrationMaximumSeconds,
            SecondsPerWord: profile == ScenePresentationProfile.ShortForm ? CalibratedShortNarrationSecondsPerWord : 60.0 / wordsPerMinute,
            TitlePresent: titlePresent,
            ViewingWindowPresent: viewingWindowPresent,
            ActionCtaPresent: actionCtaPresent,
            CompleteEventStoryPresent: completeEventStoryPresent,
            ForbiddenTermsChecked: BuildForbiddenTermsForStrategy(context).ToArray(),
            ForbiddenTermHits: forbiddenHits,
            Warnings: warnings,
            Errors: errors,
            TargetSeconds: profile == ScenePresentationProfile.LongForm ? ResolveLongNarrationTargetSeconds() : ShortNarrationTargetMinimumSeconds + ((ShortNarrationTargetMaximumSeconds - ShortNarrationTargetMinimumSeconds) / 2.0),
            WordsPerMinute: wordsPerMinute,
            ExpansionApplied: expansionApplied,
            FinalValidationPassed: errors.Count == 0,
            PreTrimWordCount: shortTrimDiagnostics?.PreTrimWordCount ?? wordCount,
            PostTrimWordCount: shortTrimDiagnostics?.PostTrimWordCount ?? wordCount,
            PreTrimDuration: shortTrimDiagnostics?.PreTrimDuration ?? estimatedDurationSeconds,
            PostTrimDuration: shortTrimDiagnostics?.PostTrimDuration ?? estimatedDurationSeconds,
            TrimApplied: shortTrimDiagnostics?.TrimApplied ?? false);
    }

    private static Task<bool> ExpandShortNarrationBeforeValidationAsync(ProductionPhaseContext context, string scriptPath, string narrationPath, CancellationToken cancellationToken)
        => RewriteShortNarrationToDurationTargetAsync(context, scriptPath, narrationPath, cancellationToken, "expanded");

    private static async Task<bool> RewriteShortNarrationToDurationTargetAsync(ProductionPhaseContext context, string scriptPath, string narrationPath, CancellationToken cancellationToken, string action)
    {
        var narration = BuildDurationTargetedShortNarration(context);

        Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
        await File.WriteAllTextAsync(narrationPath, narration, cancellationToken);
        await PersistShortNarrationScriptAsync(scriptPath, narration, action, cancellationToken);
        return true;
    }

    private static async Task<ShortNarrationTrimDiagnostics> TrimShortNarrationToValidationLimitsAsync(ProductionPhaseContext context, string scriptPath, string narrationPath, CancellationToken cancellationToken)
    {
        var text = File.Exists(narrationPath) ? (await File.ReadAllTextAsync(narrationPath, cancellationToken)).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(text) && File.Exists(scriptPath))
            text = ExtractNarrationText(await File.ReadAllTextAsync(scriptPath, cancellationToken)).Trim();

        var preTrimWordCount = CountSpokenWords(text);
        var preTrimDuration = EstimateShortNarrationSeconds(text);
        var trimmed = TrimLowestPriorityShortNarrationSentences(text, context);
        var postTrimWordCount = CountSpokenWords(trimmed);
        var postTrimDuration = EstimateShortNarrationSeconds(trimmed);
        var trimApplied = !string.Equals(text, trimmed, StringComparison.Ordinal);

        if (trimApplied)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
            await File.WriteAllTextAsync(narrationPath, trimmed, cancellationToken);
            await PersistShortNarrationScriptAsync(scriptPath, trimmed, "trimmed", cancellationToken);
        }

        return new ShortNarrationTrimDiagnostics(preTrimWordCount, postTrimWordCount, preTrimDuration, postTrimDuration, trimApplied);
    }

    private static async Task PersistShortNarrationScriptAsync(string scriptPath, string narration, string action, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) return;

        var json = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        try
        {
            var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(json, JsonOptions);
            if (script is null) return;
            var totalDurationSeconds = EstimateShortNarrationSeconds(narration);
            var scenes = BuildDurationTargetedShortSceneScripts(narration, totalDurationSeconds);
            var updated = script with
            {
                FullNarrationText = narration,
                TotalEstimatedDurationSeconds = scenes.Sum(scene => scene.DurationSeconds),
                SceneScripts = scenes,
                Warnings = script.Warnings.Concat([$"Short narration was {action} by Phase 13 narration-duration contract before TTS."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            await File.WriteAllTextAsync(scriptPath, JsonSerializer.Serialize(updated, JsonOptions), cancellationToken);
        }
        catch (JsonException)
        {
            // narration.txt remains the source consumed by TTS; leave unparseable script untouched.
        }
    }

    private static string TrimLowestPriorityShortNarrationSentences(string text, ProductionPhaseContext context)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var original = text.Trim();
        var sentences = SplitNarrationSentences(original).ToList();
        if (sentences.Count == 0) return original;

        while (sentences.Count > 1 && (CountSpokenWords(string.Join(" ", sentences)) > ShortNarrationMaximumWords || EstimateShortNarrationSeconds(string.Join(" ", sentences)) > ShortNarrationMaximumSeconds))
        {
            var removeIndex = sentences
                .Select((sentence, index) => new
                {
                    Index = index,
                    Priority = GetShortNarrationSentencePriority(sentence, context, index, sentences.Count),
                    RemainingWordCount = CountSpokenWords(string.Join(" ", sentences.Where((_, candidateIndex) => candidateIndex != index)))
                })
                .Where(item => item.RemainingWordCount >= ShortNarrationMinimumWords)
                .OrderBy(item => item.Priority)
                .ThenByDescending(item => item.Index)
                .Select(item => (int?)item.Index)
                .FirstOrDefault();

            if (removeIndex is null)
                return TrimToSpokenWords(original, ShortNarrationMaximumWords);

            sentences.RemoveAt(removeIndex.Value);
        }

        var trimmed = string.Join(" ", sentences).Trim();
        return CountSpokenWords(trimmed) <= ShortNarrationMaximumWords && EstimateShortNarrationSeconds(trimmed) <= ShortNarrationMaximumSeconds
            ? trimmed
            : TrimToSpokenWords(trimmed, ShortNarrationMaximumWords);
    }

    private static IReadOnlyList<string> SplitNarrationSentences(string text)
        => Regex.Matches(text.Trim(), @"[^.!?]+[.!?]?")
            .Select(match => match.Value.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToArray();

    private static int GetShortNarrationSentencePriority(string sentence, ProductionPhaseContext context, int index, int sentenceCount)
    {
        var priority = 0;
        if (HasRequiredTitle(sentence, context.ProductionEventIntelligence)) priority += 100;
        if (HasRequiredViewingWindow(sentence, context.ProductionEventIntelligence)) priority += 90;
        if (HasActionCta(sentence, ScenePresentationProfile.ShortForm, string.Empty)) priority += 80;
        if (index == 0) priority += 40;
        if (index == sentenceCount - 1) priority += 30;
        return priority;
    }

    private static string BuildDurationTargetedShortNarration(ProductionPhaseContext context)
    {
        var intelligence = context.ProductionEventIntelligence;
        var title = FirstNonEmpty(intelligence.Title, context.Request.Title, intelligence.ShortTitle, context.Request.ShortTitle, "This sky event");
        var shortTitle = FirstNonEmpty(intelligence.ShortTitle, context.Request.ShortTitle, title);
        var eventType = FirstNonEmpty(intelligence.EventType, context.Request.EventType, "sky event");
        var objectNames = (intelligence.ResolvedObjectNames ?? intelligence.PrimaryObjects ?? context.Request.PrimaryObjects)
            .Concat(intelligence.SecondaryObjects ?? context.Request.SecondaryObjects)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        var objects = objectNames.Length == 0 ? shortTitle : string.Join(", ", objectNames);
        var localPeak = FirstNonEmpty(intelligence.LocalPeakTime, context.Request.LocalPeakTime, intelligence.BestViewingWindowLocal, context.Request.BestViewingWindowLocal, "the approved local viewing time");
        var window = FirstNonEmpty(intelligence.BestViewingWindowLocal, intelligence.PreferredViewingWindow, context.Request.BestViewingWindowLocal, localPeak);
        var direction = FirstNonEmpty(intelligence.SkyDirectionHint, context.Request.SkyDirectionHint, "the approved sky direction");
        var viewingTip = TrimToSpokenWords(FirstNonEmpty(
            intelligence.ViewerInstructions.FirstOrDefault(),
            intelligence.ViewingSafetyRules?.FirstOrDefault(),
            context.Request.SourceNotes.FirstOrDefault(),
            "Give your eyes time to adjust and keep bright screens low."), 12);

        return string.Join(" ", new[]
        {
            $"{title} is the current {eventType} highlight, and {shortTitle} is worth planning for tonight.",
            $"Watch for {objects} near {direction}, with peak timing around {localPeak} and the best viewing window at {window}.",
            viewingTip,
            "Check clouds, choose a safe open spot, save this viewing window, share it nearby, and step outside safely."
        }).Trim();
    }

    private static double EstimateShortNarrationSeconds(string text)
        => Math.Round(CountSpokenWords(text) * CalibratedShortNarrationSecondsPerWord, 3, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<VideoNarrationSceneScriptDto> BuildDurationTargetedShortSceneScripts(string narration, double totalDurationSeconds)
    {
        var chunks = SplitIntoSixNarrationChunks(narration);
        var wordCounts = chunks.Select(CountSpokenWords).Select(count => Math.Max(1, count)).ToArray();
        var totalWords = wordCounts.Sum();
        var keys = new[] { "Hook", "What", "Why", "Where", "When", "Action" };
        return keys.Select((key, index) => new VideoNarrationSceneScriptDto(
            key,
            Math.Round(totalDurationSeconds * wordCounts[index] / totalWords, 3, MidpointRounding.AwayFromZero),
            chunks[index],
            key == "Action" ? "Set a reminder" : chunks[index])).ToArray();
    }

    private static string[] SplitIntoSixNarrationChunks(string text)
    {
        var words = SpokenWordRegex().Matches(text).Select(match => match.Value).ToArray();
        if (words.Length == 0) return ["Narration", "Narration", "Narration", "Narration", "Narration", "Set a reminder"];
        return Enumerable.Range(0, 6)
            .Select(index => string.Join(" ", words.Skip((int)Math.Floor(words.Length * index / 6.0)).Take((int)Math.Floor(words.Length * (index + 1) / 6.0) - (int)Math.Floor(words.Length * index / 6.0))))
            .Select((chunk, index) => string.IsNullOrWhiteSpace(chunk) ? (index == 5 ? "Set a reminder" : words[Math.Min(index, words.Length - 1)]) : chunk)
            .ToArray();
    }

    private static bool HasRequiredTitle(string text, ProductionEventIntelligence intelligence)
        => ContainsMeaningfulPhrase(text, intelligence.ShortTitle) || ContainsMeaningfulPhrase(text, intelligence.Title);

    private static bool HasRequiredViewingWindow(string text, ProductionEventIntelligence intelligence)
        => ContainsMeaningfulPhrase(text, intelligence.BestViewingWindowLocal)
            || ContainsMeaningfulPhrase(text, intelligence.PreferredViewingWindow)
            || ContainsMeaningfulPhrase(text, intelligence.LocalPeakTime)
            || ContainsMeaningfulPhrase(text, "viewing window");

    private static bool HasActionCta(string text, ScenePresentationProfile profile, string scriptPath)
    {
        if (profile == ScenePresentationProfile.LongForm && ScriptContainsSceneKey(scriptPath, "Action")) return true;
        var ctaPhrases = new[] { "set a reminder", "check clouds", "choose", "watch", "look", "step outside", "subscribe", "share", "save" };
        return ctaPhrases.Any(phrase => ContainsToken(text, phrase));
    }

    private static bool HasCompleteLongEventStory(string text, string scriptPath)
    {
        var required = new[] { "Hook", "WhatIsHappening", "WhyItMatters", "WhereToLook", "WhenToLook", "HowToObserve", "WhatYouWillSee", "InterestingFact", "ObservationTips", "Recap", "Action" };
        return required.All(key => ScriptContainsSceneKey(scriptPath, key)) && !string.IsNullOrWhiteSpace(text);
    }

    private static bool ScriptContainsSceneKey(string scriptPath, string sceneKey)
    {
        if (!File.Exists(scriptPath)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(scriptPath));
            if (!doc.RootElement.TryGetProperty("sceneScripts", out var scenes) || scenes.ValueKind != JsonValueKind.Array) return false;
            return scenes.EnumerateArray().Any(scene => scene.TryGetProperty("sceneKey", out var key) && string.Equals(key.GetString(), sceneKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return false; }
    }

    private static bool ContainsMeaningfulPhrase(string text, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var normalizedText = NormalizeForPhraseMatch(text);
        var normalizedPhrase = NormalizeForPhraseMatch(phrase);
        if (normalizedPhrase.Length < 3) return false;
        return normalizedText.Contains(normalizedPhrase, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForPhraseMatch(string value)
        => string.Concat((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static int CountSpokenWords(string narration)
        => string.IsNullOrWhiteSpace(narration) ? 0 : SpokenWordRegex().Matches(narration).Count;

    private static string TrimToSpokenWords(string value, int maximumWords)
    {
        if (string.IsNullOrWhiteSpace(value) || maximumWords <= 0) return string.Empty;
        var matches = SpokenWordRegex().Matches(value);
        if (matches.Count <= maximumWords) return value.Trim();
        return string.Join(" ", matches.Cast<Match>().Take(maximumWords).Select(match => match.Value));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’\u2010-\u2015-][\p{L}\p{N}]+)?")]
    private static partial Regex SpokenWordRegex();


    private double ResolveLongNarrationWordsPerMinute()
    {
        var configured = videoAssemblyOptions?.Value.LongNarrationWordsPerMinute ?? DefaultLongNarrationWordsPerMinute;
        return configured > 0 ? configured : DefaultLongNarrationWordsPerMinute;
    }

    private double ResolveLongNarrationTargetSeconds()
        => Math.Round((LongNarrationMinimumSeconds + LongNarrationMaximumSeconds) / 2.0, 3, MidpointRounding.AwayFromZero);

    private int ResolveLongNarrationMinimumWords()
        => (int)Math.Ceiling(LongNarrationMinimumSeconds * ResolveLongNarrationWordsPerMinute() / 60.0);

    private int ResolveLongNarrationMaximumWords()
        => (int)Math.Floor(LongNarrationMaximumSeconds * ResolveLongNarrationWordsPerMinute() / 60.0);

    private double EstimateLongNarrationSeconds(int wordCount)
        => Math.Round(wordCount / ResolveLongNarrationWordsPerMinute() * 60.0, 3, MidpointRounding.AwayFromZero);

    private async Task<bool> ExpandLongNarrationBeforeValidationAsync(ProductionPhaseContext context, string scriptPath, string narrationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) return false;
        var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions);
        if (script is null || script.SceneScripts.Count == 0) return false;

        var targetWords = (int)Math.Ceiling(ResolveLongNarrationTargetSeconds() * ResolveLongNarrationWordsPerMinute() / 60.0);
        var scenes = script.SceneScripts.ToArray();
        for (var round = 0; round < 10 && CountSpokenWords(string.Join(" ", scenes.Select(s => s.Narration))) < targetWords; round++)
        {
            scenes = scenes.Select(scene => scene with { Narration = $"{scene.Narration} {BuildPipelineLongNarrationExpansion(scene.SceneKey, context, round)}" }).ToArray();
            if (EstimateLongNarrationSeconds(CountSpokenWords(string.Join(" ", scenes.Select(s => s.Narration)))) >= LongNarrationMinimumSeconds)
                break;
        }

        var fullText = string.Join(" ", scenes.Select(s => s.Narration));
        var totalSeconds = EstimateLongNarrationSeconds(CountSpokenWords(fullText));
        var updatedScenes = scenes.Select(scene => scene with { DurationSeconds = EstimateLongNarrationSeconds(CountSpokenWords(scene.Narration)) }).ToArray();
        var updatedScript = script with { SceneScripts = updatedScenes, FullNarrationText = fullText, TotalEstimatedDurationSeconds = totalSeconds, DurationValidation = null };
        await File.WriteAllTextAsync(scriptPath, JsonSerializer.Serialize(updatedScript, JsonOptions), cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
        await File.WriteAllTextAsync(narrationPath, fullText, cancellationToken);
        return true;
    }

    private static string BuildPipelineLongNarrationExpansion(string section, ProductionPhaseContext context, int round)
    {
        var eventInfo = context.ProductionEventIntelligence;
        var title = FirstNonEmpty(eventInfo.ShortTitle, eventInfo.Title, context.Request.ShortTitle, context.Request.Title, "this sky event");
        var objects = (eventInfo.ResolvedObjectNames ?? eventInfo.PrimaryObjects ?? context.Request.PrimaryObjects).Where(v => !string.IsNullOrWhiteSpace(v)).Take(5).ToArray();
        var objectText = objects.Length == 0 ? title : string.Join(", ", objects);
        var direction = FirstNonEmpty(eventInfo.SkyDirectionHint, context.Request.SkyDirectionHint, "the approved sky direction");
        var window = FirstNonEmpty(eventInfo.BestViewingWindowLocal, context.Request.BestViewingWindowLocal, eventInfo.LocalPeakTime, context.Request.LocalPeakTime, "the approved viewing window");
        var sourceNotes = context.Request.SourceNotes ?? Array.Empty<string>();
        var sourceNote = sourceNotes.Count == 0 ? string.Empty : sourceNotes[round % sourceNotes.Count];
        return section switch
        {
            "Hook" => $"Keep the opening tied to {title}, so viewers immediately know this is the current event, not a generic astronomy segment.",
            "WhatIsHappening" => $"Explain the event type, {eventInfo.EventType}, through {objectText}, the local timing, and the visible sky geometry in plain language.",
            "WhenToLook" => $"Repeat the practical timing clearly: use {window}, with local weather and horizon conditions deciding the best exact moment.",
            "WhereToLook" => $"Use {direction} as the viewing anchor, then invite a slow scan around the approved scene direction.",
            "WhyItMatters" => string.IsNullOrWhiteSpace(sourceNote) ? $"This matters because {title} has an approved viewing plan and source-backed context." : $"One source note supporting this event is: {sourceNote}.",
            "HowToObserve" or "ObservationTips" => "Add practical guidance: choose a safe open spot, dim screens, avoid bright lights, let eyes adapt, and stop if conditions are unsafe.",
            "Action" => "End with a direct call to action: save the time, check clouds, share the plan, and step outside safely.",
            _ => $"Keep this section grounded in {title}, {objectText}, {direction}, and {window}."
        };
    }

    private sealed record Phase13ShortNarrationDiagnostics(
        int ShortNarrationWordCount,
        double EstimatedDurationSeconds,
        double? ActualDurationSeconds,
        double MinSeconds,
        double MaxSeconds,
        string TargetRange,
        bool ExpansionApplied,
        bool FinalValidationPassed,
        int PreTrimWordCount,
        int PostTrimWordCount,
        double PreTrimDuration,
        double PostTrimDuration,
        bool TrimApplied);

    private Phase13ShortNarrationDiagnostics ReadPhase13ShortNarrationDiagnostics(IReadOnlyList<string> outputFiles, ProductionPhaseContext context)
    {
        var validationPath = outputFiles.FirstOrDefault(p => string.Equals(Path.GetFileName(p), "narration-validation.json", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(context.ExecutionContext.NarrationRoot!, "short", "narration-validation.json");
        var narrationPath = Path.Combine(context.ExecutionContext.NarrationRoot!, "short", "narration.txt");
        var narrationText = File.Exists(narrationPath) ? File.ReadAllText(narrationPath).Trim() : string.Empty;
        var wordCount = CountSpokenWords(narrationText);
        var estimatedSeconds = EstimateShortNarrationSeconds(narrationText);
        var actualDurationSeconds = ReadTtsActualDurationSeconds(context, "short");

        NarrationValidationReport? report = null;
        if (File.Exists(validationPath))
        {
            try
            {
                report = JsonSerializer.Deserialize<NarrationValidationReport>(File.ReadAllText(validationPath), JsonOptions);
            }
            catch (JsonException) { }
        }

        return new Phase13ShortNarrationDiagnostics(
            ShortNarrationWordCount: report?.WordCount ?? wordCount,
            EstimatedDurationSeconds: report?.EstimatedDurationSeconds ?? estimatedSeconds,
            ActualDurationSeconds: actualDurationSeconds,
            MinSeconds: report?.MinimumDurationSeconds ?? ShortNarrationMinimumSeconds,
            MaxSeconds: report?.MaximumDurationSeconds ?? ShortNarrationMaximumSeconds,
            TargetRange: $"{ShortNarrationTargetMinimumSeconds:0}-{ShortNarrationTargetMaximumSeconds:0}",
            ExpansionApplied: report?.ExpansionApplied ?? false,
            FinalValidationPassed: report?.FinalValidationPassed ?? false,
            PreTrimWordCount: report?.PreTrimWordCount ?? wordCount,
            PostTrimWordCount: report?.PostTrimWordCount ?? wordCount,
            PreTrimDuration: report?.PreTrimDuration ?? estimatedSeconds,
            PostTrimDuration: report?.PostTrimDuration ?? estimatedSeconds,
            TrimApplied: report?.TrimApplied ?? false);
    }

    private static double? ReadTtsActualDurationSeconds(ProductionPhaseContext context, string profileFolder)
    {
        foreach (var fileName in new[] { "tts-validation-report.json", "tts-source-validation-report.json" })
        {
            var path = Path.Combine(context.ExecutionContext.TtsRoot!, profileFolder, fileName);
            if (!File.Exists(path)) continue;
            try
            {
                var report = JsonSerializer.Deserialize<TtsValidationReport>(File.ReadAllText(path), JsonOptions);
                if (report is not null && report.DurationSeconds > 0) return report.DurationSeconds;
            }
            catch (JsonException) { }
        }

        return null;
    }

    private sealed record Phase14NarrationDiagnostics(
        bool NarrationRequestBuilt,
        string NarrationRequestPath,
        string NarrationInputScenePlanPath,
        string NarrationInputSceneApprovalRoot,
        string NarrationOutputPath,
        bool NarrationFileExists,
        int GeneratedNarrationTextLength,
        int GeneratedNarrationWordCount,
        int WordCount,
        double EstimatedSeconds,
        double MinSeconds,
        double MaxSeconds,
        double TargetSeconds,
        double WordsPerMinute,
        bool ExpansionApplied,
        bool FinalValidationPassed);

    private Phase14NarrationDiagnostics ReadPhase14NarrationDiagnostics(IReadOnlyList<string> outputFiles, ProductionPhaseContext context)
    {
        var requestPath = BuildLongNarrationRequestPath(context);
        var outputPath = BuildLongNarrationOutputPath(context);
        var narrationFileExists = File.Exists(outputPath);
        var narrationText = narrationFileExists ? File.ReadAllText(outputPath).Trim() : string.Empty;
        var generatedWordCount = CountSpokenWords(narrationText);
        var wordsPerMinute = ResolveLongNarrationWordsPerMinute();
        var estimatedSeconds = EstimateLongNarrationSeconds(generatedWordCount);
        var path = outputFiles.FirstOrDefault(p => string.Equals(Path.GetFileName(p), "narration-validation.json", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(context.ExecutionContext.NarrationRoot!, "long", "narration-validation.json");

        NarrationValidationReport? report = null;
        if (File.Exists(path))
        {
            try
            {
                report = JsonSerializer.Deserialize<NarrationValidationReport>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException) { }
        }

        return new Phase14NarrationDiagnostics(
            NarrationRequestBuilt: File.Exists(requestPath),
            NarrationRequestPath: NormalizePath(requestPath),
            NarrationInputScenePlanPath: NormalizePath(BuildEnrichedScenePlanPath(context)),
            NarrationInputSceneApprovalRoot: NormalizePath(BuildLongSceneApprovalRoot(context)),
            NarrationOutputPath: NormalizePath(outputPath),
            NarrationFileExists: narrationFileExists,
            GeneratedNarrationTextLength: narrationText.Length,
            GeneratedNarrationWordCount: generatedWordCount,
            WordCount: report?.WordCount ?? generatedWordCount,
            EstimatedSeconds: report?.EstimatedDurationSeconds ?? estimatedSeconds,
            MinSeconds: report?.MinimumDurationSeconds ?? LongNarrationMinimumSeconds,
            MaxSeconds: report?.MaximumDurationSeconds ?? LongNarrationMaximumSeconds,
            TargetSeconds: report?.TargetSeconds ?? ResolveLongNarrationTargetSeconds(),
            WordsPerMinute: report is not null && report.WordsPerMinute > 0 ? report.WordsPerMinute : wordsPerMinute,
            ExpansionApplied: report?.ExpansionApplied ?? false,
            FinalValidationPassed: report?.FinalValidationPassed ?? false);
    }

    private sealed record NarrationValidationReport(
        string ValidationPath,
        string Profile,
        string NarrationPath,
        string ScriptPath,
        bool IsValid,
        int WordCount,
        int MinimumWords,
        int MaximumWords,
        double EstimatedDurationSeconds,
        double MinimumDurationSeconds,
        double MaximumDurationSeconds,
        double SecondsPerWord,
        bool TitlePresent,
        bool ViewingWindowPresent,
        bool ActionCtaPresent,
        bool CompleteEventStoryPresent,
        IReadOnlyList<string> ForbiddenTermsChecked,
        IReadOnlyList<string> ForbiddenTermHits,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Errors,
        double TargetSeconds = 0,
        double WordsPerMinute = 0,
        bool ExpansionApplied = false,
        bool FinalValidationPassed = false,
        int PreTrimWordCount = 0,
        int PostTrimWordCount = 0,
        double PreTrimDuration = 0,
        double PostTrimDuration = 0,
        bool TrimApplied = false);

    private sealed record ShortNarrationTrimDiagnostics(
        int PreTrimWordCount,
        int PostTrimWordCount,
        double PreTrimDuration,
        double PostTrimDuration,
        bool TrimApplied);

    private static async Task EnsureNarrationValidationPassedBeforeTtsAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var validationPath = Path.Combine(context.ExecutionContext.NarrationRoot!, profileFolder, "narration-validation.json");
        if (!File.Exists(validationPath))
            throw new InvalidOperationException($"{profile} TTS cannot run because narration validation has not passed: missing {NormalizePath(validationPath)}.");

        NarrationValidationReport? report;
        try
        {
            report = JsonSerializer.Deserialize<NarrationValidationReport>(await File.ReadAllTextAsync(validationPath, cancellationToken), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{profile} TTS cannot run because narration validation is unreadable: {NormalizePath(validationPath)}.", ex);
        }

        if (report is null || !report.IsValid || !report.FinalValidationPassed)
            throw new InvalidOperationException($"{profile} TTS cannot run because narration validation has not passed: {NormalizePath(validationPath)}.");

        if (profile == ScenePresentationProfile.ShortForm && (report.EstimatedDurationSeconds < ShortNarrationMinimumSeconds || report.EstimatedDurationSeconds > ShortNarrationMaximumSeconds))
            throw new InvalidOperationException($"ShortForm TTS cannot run because short narration duration is outside {ShortNarrationMinimumSeconds:0}-{ShortNarrationMaximumSeconds:0} seconds: estimatedDurationSeconds={report.EstimatedDurationSeconds:0.###}.");
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateTtsAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        await EnsureNarrationValidationPassedBeforeTtsAsync(context, profile, cancellationToken);
        var response = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Tts" : "LongFormTts"), cancellationToken);
        var outputs = new List<string>(response.GeneratedFiles);
        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var source = profile == ScenePresentationProfile.ShortForm ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-tts-audio.mp3") : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-tts-audio.mp3");
        var target = Path.Combine(context.ExecutionContext.TtsRoot!, profileFolder, "narration.mp3");

        var timingsPath = profile == ScenePresentationProfile.ShortForm
            ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-tts-timings.json")
            : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-tts-timings.json");
        var scriptPath = profile == ScenePresentationProfile.ShortForm
            ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-narration-script.json")
            : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-narration-script.json");
        var reportPath = Path.Combine(context.ExecutionContext.TtsRoot!, profileFolder, "tts-validation-report.json");
        var sourceReportPath = Path.Combine(context.ExecutionContext.TtsRoot!, profileFolder, "tts-source-validation-report.json");

        var sourceReport = await ValidateTtsOutputAsync(source, scriptPath, timingsPath, sourceReportPath, profile, cancellationToken);
        outputs.Add(sourceReportPath);
        if (!sourceReport.IsValid)
            throw new InvalidOperationException($"{profile} TTS validation failed before final TTS copy: " + string.Join("; ", sourceReport.Errors));

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tempTarget = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        File.Copy(source, tempTarget, overwrite: true);
        var report = await ValidateTtsOutputAsync(tempTarget, scriptPath, timingsPath, reportPath, profile, cancellationToken);
        outputs.Add(reportPath);

        if (!report.IsValid)
            throw new InvalidOperationException($"{profile} TTS validation failed: " + string.Join("; ", report.Errors));

        File.Move(tempTarget, target, overwrite: true);
        outputs.Add(target);

        return outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<TtsValidationReport> ValidateTtsOutputAsync(string audioPath, string scriptPath, string timingsPath, string reportPath, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        const long minimumAudioFileSizeBytes = 1024;
        const double silencePeakThresholdDb = -55.0;
        const double silenceRmsThresholdDb = -60.0;

        var errors = new List<string>();
        var normalizedAudioPath = NormalizePath(audioPath);
        var fileSizeBytes = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0;
        var durationSeconds = File.Exists(audioPath) ? await ProbeAudioDurationSecondsAsync(audioPath, cancellationToken) : 0;
        var (peakDb, rmsDb) = File.Exists(audioPath) && fileSizeBytes >= minimumAudioFileSizeBytes && durationSeconds > 0
            ? await ProbeAudioLevelsAsync(audioPath, cancellationToken)
            : (-120d, -120d);
        var isSilent = peakDb <= silencePeakThresholdDb || rmsDb <= silenceRmsThresholdDb;

        if (!File.Exists(audioPath)) errors.Add($"Audio file is missing: {normalizedAudioPath}.");
        if (fileSizeBytes <= minimumAudioFileSizeBytes) errors.Add($"Audio file size {fileSizeBytes} bytes is at or below minimum threshold {minimumAudioFileSizeBytes} bytes.");
        if (durationSeconds <= 0) errors.Add("Audio duration must be greater than 0 seconds.");
        if (isSilent) errors.Add($"Audio is silent or below threshold: peakDb={RoundDb(peakDb)}, rmsDb={RoundDb(rmsDb)}.");

        var narrationText = File.Exists(scriptPath) ? ExtractNarrationText(await File.ReadAllTextAsync(scriptPath, cancellationToken)) : string.Empty;
        if (string.IsNullOrWhiteSpace(narrationText)) errors.Add($"Narration text is empty or missing: {NormalizePath(scriptPath)}.");

        var timingIssue = string.Empty;
        var usableSegments = File.Exists(timingsPath) && HasUsableTtsTimingSegments(await File.ReadAllTextAsync(timingsPath, cancellationToken), profile, out timingIssue);
        if (!usableSegments) errors.Add($"TTS timing JSON has no usable segments: {NormalizePath(timingsPath)}{(string.IsNullOrWhiteSpace(timingIssue) ? string.Empty : " (" + timingIssue + ")")}.");

        var report = new TtsValidationReport(
            AudioPath: normalizedAudioPath,
            DurationSeconds: Math.Round(durationSeconds, 3, MidpointRounding.AwayFromZero),
            FileSizeBytes: fileSizeBytes,
            PeakDb: RoundDb(peakDb),
            RmsDb: RoundDb(rmsDb),
            IsSilent: isSilent,
            IsValid: errors.Count == 0,
            NarrationTextPresent: !string.IsNullOrWhiteSpace(narrationText),
            TimingSegmentsUsable: usableSegments,
            Errors: errors);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        return report;
    }

    private async Task<double> ProbeAudioDurationSecondsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var result = await RunProcessAsync(ffprobePath, ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", audioPath], cancellationToken);
        return result.ExitCode == 0 && double.TryParse(result.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? duration : 0;
    }

    private async Task<(double PeakDb, double RmsDb)> ProbeAudioLevelsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var result = await RunProcessAsync(ffmpegPath, ["-hide_banner", "-i", audioPath, "-af", "astats=metadata=1:reset=0", "-f", "null", "-"], cancellationToken);
        var output = result.Output + "\n" + result.Error;
        return (ParseLastDbValue(output, "Peak level dB"), ParseLastDbValue(output, "RMS level dB"));
    }

    private static bool HasUsableTtsTimingSegments(string json, ScenePresentationProfile profile, out string issue)
    {
        issue = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var propertyName = profile == ScenePresentationProfile.LongForm && root.TryGetProperty("sectionTimings", out _) ? "sectionTimings" : "sceneTimings";
            if (!root.TryGetProperty(propertyName, out var segments) || segments.ValueKind != JsonValueKind.Array)
            {
                issue = $"missing {propertyName} array";
                return false;
            }

            var usable = 0;
            foreach (var segment in segments.EnumerateArray())
            {
                if (!segment.TryGetProperty("startSeconds", out var start) || !segment.TryGetProperty("endSeconds", out var end)) continue;
                if (!start.TryGetDouble(out var startSeconds) || !end.TryGetDouble(out var endSeconds)) continue;
                if (endSeconds > startSeconds) usable++;
            }

            if (usable == 0) issue = $"{propertyName} contains no positive-duration entries";
            return usable > 0;
        }
        catch (JsonException ex)
        {
            issue = ex.Message;
            return false;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null) return new ProcessResult(-1, string.Empty, string.Empty);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(-1, string.Empty, string.Empty);
        }
    }

    private static double ParseLastDbValue(string output, string label)
    {
        var value = double.NegativeInfinity;
        foreach (var line in output.Split('\n'))
        {
            var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var colon = line.IndexOf(':', index);
            if (colon < 0) continue;
            var raw = line[(colon + 1)..].Trim();
            if (raw.Equals("-inf", StringComparison.OrdinalIgnoreCase)) value = double.NegativeInfinity;
            else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) value = parsed;
        }
        return value;
    }

    private static double RoundDb(double value)
        => double.IsNegativeInfinity(value) || double.IsNaN(value) ? -120 : double.IsPositiveInfinity(value) ? 0 : Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private sealed record TtsValidationReport(
        string AudioPath,
        double DurationSeconds,
        long FileSizeBytes,
        double PeakDb,
        double RmsDb,
        bool IsSilent,
        bool IsValid,
        bool NarrationTextPresent,
        bool TimingSegmentsUsable,
        IReadOnlyList<string> Errors);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private async Task<IReadOnlyList<string>> PhaseAssembleVideoAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        if (profile == ScenePresentationProfile.ShortForm)
        {
            var intelligence = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, "Intelligence"), cancellationToken);
            outputs.AddRange(intelligence.GeneratedFiles);
            if (!string.IsNullOrWhiteSpace(intelligence.VideoAssemblyIntelligencePath)) outputs.Add(intelligence.VideoAssemblyIntelligencePath);
        }

        foreach (var phase in new[] { profile == ScenePresentationProfile.ShortForm ? "Assembly" : "LongFormAssembly", profile == ScenePresentationProfile.ShortForm ? "Render" : "LongFormRender" })
        {
            if (profile == ScenePresentationProfile.ShortForm && string.Equals(phase, "Render", StringComparison.OrdinalIgnoreCase))
                ValidateVideoAssemblyIntelligenceBeforeRendering(context);

            var response = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, phase), cancellationToken);
            outputs.AddRange(response.GeneratedFiles);
        }
        return outputs;
    }

    private async Task<IReadOnlyList<string>> PhaseFinalValidationAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        await WriteScenesManifestsAsync(context.OutputRoot, cancellationToken);
        var copied = await MaterializePlanFolderAsync(context.Request, context.EventId, context.OutputRoot, [], cancellationToken);
        var validation = await qualityValidator.ValidateFinalOutputAsync(context.ProductionEventIntelligence, context.OutputRoot, cancellationToken, context.Request.RequestedOutputs);
        if (!validation.IsValid) throw new InvalidOperationException("Final validation failed: " + string.Join("; ", validation.Errors));
        return copied.Concat([Path.Combine(context.OutputRoot, "phase-manifest.json")]).ToArray();
    }

    private VideoAssemblyGenerationRequest BuildVideoRequest(ProductionPhaseContext context, ScenePresentationProfile profile, string phase)
        => new()
        {
            EventId = context.EventId,
            RegionId = context.Request.RegionId,
            Language = context.Request.Language,
            Platform = profile == ScenePresentationProfile.ShortForm ? "YouTubeShort" : "YouTubeLong",
            Phase = phase,
            DryRun = false,
            OverwriteExisting = context.OverwriteExisting,
            OutputMode = profile == ScenePresentationProfile.ShortForm ? "ShortFormOnly" : "LongFormOnly",
            AllowSyntheticSilentTts = false,
            ShortForm = new VideoAssemblyFormRequest { Enabled = profile == ScenePresentationProfile.ShortForm, Platform = "YouTubeShort", ScenePresentationProfile = ScenePresentationProfile.ShortForm, TargetDurationSeconds = 60, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 12, DuckMusicUnderNarration = true },
            LongForm = new VideoAssemblyFormRequest { Enabled = profile == ScenePresentationProfile.LongForm, Platform = "YouTubeLong", ScenePresentationProfile = ScenePresentationProfile.LongForm, TargetDurationSeconds = 360, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 10, DuckMusicUnderNarration = true },
            ScenePresentationProfile = profile,
            BackgroundMusic = true,
            MusicMood = "WonderCuriosity",
            MusicLevelPercent = profile == ScenePresentationProfile.ShortForm ? 12 : 10,
            DuckMusicUnderNarration = true,
            ProductionContext = context.ExecutionContext,
            SourceNotes = context.Request.SourceNotes ?? Array.Empty<string>()
        };


    private static async Task<IReadOnlyList<string>> MaterializeSceneApprovalAsync(string stagingRoot, string normalizedRoot, CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        if (!Directory.Exists(stagingRoot)) return copied;

        foreach (var source in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(stagingRoot, source);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(source);
            if (parts.Length > 1
                && (string.Equals(parts[0], "short", StringComparison.OrdinalIgnoreCase) || string.Equals(parts[0], "long", StringComparison.OrdinalIgnoreCase))
                && fileName.EndsWith("-final.png", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Replace("-final", string.Empty, StringComparison.OrdinalIgnoreCase);
                relativePath = Path.Combine(parts[..^1].Append(fileName).ToArray());
            }

            CopyFile(source, Path.Combine(normalizedRoot, relativePath), copied);
        }

        await WriteScenesManifestsAsync(Path.GetDirectoryName(normalizedRoot)!, cancellationToken);
        return copied;
    }

    private static void ValidateSceneApprovalTextBeforeRendering(ProductionPhaseContext context)
    {
        var forbiddenTerms = BuildForbiddenTermsForStrategy(context).ToArray();
        if (forbiddenTerms.Length == 0) return;

        var paths = new List<string>();
        var narrationPath = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json");
        if (File.Exists(narrationPath)) paths.Add(narrationPath);
        if (Directory.Exists(context.ExecutionContext.SceneRoot!))
        {
            paths.AddRange(Directory.EnumerateFiles(context.ExecutionContext.SceneRoot!, "*-infographic-spec.json", SearchOption.AllDirectories));
            paths.AddRange(Directory.EnumerateFiles(context.ExecutionContext.SceneRoot!, "*narration*.json", SearchOption.AllDirectories));
            paths.AddRange(Directory.EnumerateFiles(context.ExecutionContext.SceneRoot!, "*narration*.txt", SearchOption.AllDirectories));
        }

        var hits = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(path);
            var pathHits = forbiddenTerms.Where(term => ContainsToken(text, term)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (pathHits.Length > 0) hits.Add($"{NormalizePath(path)} => {string.Join(", ", pathHits)}");
        }

        if (hits.Count > 0)
            throw new InvalidOperationException("Pre-render scene approval validation failed: narration/spec files contain forbidden terms for the selected event strategy: " + string.Join("; ", hits));
    }

    private static void ValidateVideoAssemblyIntelligenceBeforeRendering(ProductionPhaseContext context)
    {
        var path = Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-assembly-intelligence.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Pre-render video assembly validation failed: video-assembly-intelligence.json was not found at '{NormalizePath(path)}'.");

        var forbiddenTerms = BuildForbiddenTermsForStrategy(context).ToArray();
        if (forbiddenTerms.Length == 0) return;

        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        var hits = forbiddenTerms
            .SelectMany(term => FindGeneratedContentTermHits(doc.RootElement, "$", term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hits.Length > 0)
            throw new InvalidOperationException("Pre-render video assembly validation failed: video-assembly-intelligence.json contains forbidden terms for the selected event strategy: " + string.Join("; ", hits.Select(hit => $"{NormalizePath(path)} => {hit}")));
    }

    private static IEnumerable<string> FindGeneratedContentTermHits(JsonElement element, string field, string term, bool parentIsGeneratedContent = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsValidationMetadataField(property.Name)) continue;
                    var childField = field == "$" ? property.Name : $"{field}.{property.Name}";
                    var childIsGeneratedContent = parentIsGeneratedContent || IsGeneratedContentField(property.Name);
                    foreach (var hit in FindGeneratedContentTermHits(property.Value, childField, term, childIsGeneratedContent))
                        yield return hit;
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var hit in FindGeneratedContentTermHits(item, $"{field}[{index}]", term, parentIsGeneratedContent))
                        yield return hit;
                    index++;
                }
                break;
            case JsonValueKind.String:
                if (!parentIsGeneratedContent && !IsGeneratedContentField(LastJsonPathSegment(field))) yield break;
                var value = element.GetString() ?? string.Empty;
                foreach (Match match in Regex.Matches(value, BuildTokenPattern(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    yield return $"field={field}, term={term}, snippet={BuildSnippet(value, match.Index, match.Length)}";
                break;
        }
    }

    private static string LastJsonPathSegment(string field)
    {
        var lastDot = field.LastIndexOf('.');
        var segment = lastDot >= 0 ? field[(lastDot + 1)..] : field;
        var bracket = segment.IndexOf('[');
        return bracket >= 0 ? segment[..bracket] : segment;
    }

    private static bool IsValidationMetadataField(string propertyName)
        => propertyName.Equals("forbiddenTermsChecked", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("allowedTerms", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenTerms", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenObjectNames", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("forbiddenUnrelatedObjects", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("validationRules", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("checks", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("ruleDescriptions", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedContentField(string propertyName)
        => propertyName.Equals("title", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("subtitle", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("narration", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("narrationText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("text", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("overlayText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("prompt", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("purpose", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("description", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("scenePurpose", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("sceneText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("hook", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("cta", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("script", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("content", StringComparison.OrdinalIgnoreCase);

    private static string BuildSnippet(string text, int index, int length)
    {
        const int context = 50;
        var start = Math.Max(0, index - context);
        var end = Math.Min(text.Length, index + length + context);
        var snippet = Regex.Replace(text[start..end].Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }

    private static IEnumerable<string> BuildForbiddenTermsForStrategy(ProductionPhaseContext context)
    {
        var intelligence = context.ProductionEventIntelligence;
        var terms = new List<string>();
        terms.AddRange(intelligence.ForbiddenTerms);
        terms.AddRange(intelligence.ForbiddenObjectNames ?? []);
        if (context.MediaEventStrategy is not null)
            terms.AddRange(context.MediaEventStrategy.BuildDefinition(intelligence).ForbiddenUnrelatedObjects);
        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        return Regex.IsMatch(haystack, BuildTokenPattern(needle), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildTokenPattern(string needle)
    {
        var trimmed = needle.Trim();
        var escaped = Regex.Escape(trimmed);
        escaped = Regex.Replace(escaped, @"\s+", @"\s+");
        var startsWithToken = char.IsLetterOrDigit(trimmed[0]) || trimmed[0] == '_';
        var endsWithToken = char.IsLetterOrDigit(trimmed[^1]) || trimmed[^1] == '_';
        return $"{(startsWithToken ? @"(?<![\p{L}\p{N}_])" : string.Empty)}{escaped}{(endsWithToken ? @"(?![\p{L}\p{N}_])" : string.Empty)}";
    }

    private static string RequireFile(string path, string name)
        => File.Exists(path) ? path : throw new InvalidOperationException($"Required {name} file was not found at '{path}'.");

    private static async Task WritePlanInputAsync(string outputRoot, ContentPlanProductionPipelineRequest request, ProductionEventIntelligence intelligence, CancellationToken cancellationToken)
    {
        var root = Path.Combine(outputRoot, "plan-input");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "content-plan-production-request.json"), JsonSerializer.Serialize(request, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "production-event-intelligence.json"), JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
    }

    private async Task<ProductionPhaseResult> WritePhaseValidationAsync(ProductionPhaseContext context, int phaseNo, string phaseName, ProductionPhaseStatus status, IReadOnlyList<string> inputFiles, IReadOnlyList<string> outputFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, string reason, bool canRetry, CancellationToken cancellationToken, DateTimeOffset? startedUtc = null, Phase10ValidationDiagnostics? phase10TitleDiagnostics = null)
    {
        var started = startedUtc ?? DateTimeOffset.UtcNow;
        var finished = DateTimeOffset.UtcNow;
        var validationPath = Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        var result = new ProductionPhaseResult(phaseNo, phaseName, status, started, finished, (long)(finished - started).TotalMilliseconds, inputFiles, outputFiles, validationPath, warnings, errors, canRetry, reason);
        var phase7NarrationDiagnostics = phaseNo == 7
            ? BuildPhase7NarrationDiagnostics(BuildQuestionDrivenNarrationRequest(context), context)
            : null;
        var phase12ThumbnailDiagnostics = phaseNo == 12
            ? BuildPhase12ThumbnailDiagnostics(context)
            : null;
        var phase13ShortNarrationDiagnostics = phaseNo == 13
            ? ReadPhase13ShortNarrationDiagnostics(outputFiles, context)
            : null;
        var phase14NarrationDiagnostics = phaseNo == 14
            ? ReadPhase14NarrationDiagnostics(outputFiles, context)
            : null;
        var phase6SceneEnrichmentDiagnostics = phaseNo == 6
            ? BuildPhase6SceneEnrichmentDiagnostics(context)
            : null;
        var planetGroupingDiagnostics = phase6SceneEnrichmentDiagnostics?.PlanetGroupingStrategyActivated == true
            ? phase6SceneEnrichmentDiagnostics
            : null;
        if (planetGroupingDiagnostics is not null)
        {
            logger.LogInformation(
                "PlanetGrouping enrichment diagnostics:\n strategyActivated={StrategyActivated}\n enricherExecuted={EnricherExecuted}\n planetGroupingIntentInjected={PlanetGroupingIntentInjected}\n guidedScanPathInjected={GuidedScanPathInjected}\n legacyValidationPathExecuted={LegacyValidationPathExecuted}\n planetGroupingValidationPathExecuted={PlanetGroupingValidationPathExecuted}\n enrichedSceneIntentCount={EnrichedSceneIntentCount}\n visualIntentCount={VisualIntentCount}",
                planetGroupingDiagnostics.PlanetGroupingStrategyActivated,
                planetGroupingDiagnostics.PlanetGroupingEnricherExecuted,
                planetGroupingDiagnostics.PlanetGroupingIntentInjected,
                planetGroupingDiagnostics.GuidedScanPathInjected,
                planetGroupingDiagnostics.LegacyValidationPathExecuted,
                planetGroupingDiagnostics.PlanetGroupingValidationPathExecuted,
                planetGroupingDiagnostics.EnrichedSceneIntentCount,
                planetGroupingDiagnostics.VisualIntentCount);
        }
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo,
            phaseName,
            status = status.ToString(),
            startedUtc = started,
            finishedUtc = finished,
            durationMs = result.DurationMs,
            sceneApprovalStagingRoot = NormalizePath(context.ExecutionContext.SceneRoot!),
            sceneApprovalNormalizedRoot = NormalizePath(GetSceneApprovalNormalizedRoot(context.OutputRoot)),
            filesDeletedDueToOverwrite = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(),
            filesGeneratedThisRun = outputFiles,
            inputFiles,
            outputFiles,
            warnings,
            errors,
            reason,
            canRetry,
            phase6SceneEnrichmentDiagnostics,
            planetGroupingStrategyActivated = planetGroupingDiagnostics?.PlanetGroupingStrategyActivated,
            planetGroupingEnricherExecuted = planetGroupingDiagnostics?.PlanetGroupingEnricherExecuted,
            planetGroupingIntentInjected = planetGroupingDiagnostics?.PlanetGroupingIntentInjected,
            guidedScanPathInjected = planetGroupingDiagnostics?.GuidedScanPathInjected,
            legacyValidationPathExecuted = planetGroupingDiagnostics?.LegacyValidationPathExecuted,
            planetGroupingValidationPathExecuted = planetGroupingDiagnostics?.PlanetGroupingValidationPathExecuted,
            enrichedSceneIntentCount = planetGroupingDiagnostics?.EnrichedSceneIntentCount,
            enrichedSceneIntents = planetGroupingDiagnostics?.EnrichedSceneIntents,
            visualIntentCount = planetGroupingDiagnostics?.VisualIntentCount,
            visualIntents = planetGroupingDiagnostics?.VisualIntents,
            scenePlanInputPath = planetGroupingDiagnostics?.ScenePlanInputPath,
            enrichedScenePlanOutputPath = planetGroupingDiagnostics?.EnrichedScenePlanOutputPath,
            validationIntentSourcePath = planetGroupingDiagnostics?.ValidationIntentSourcePath,
            validationIntentSourceField = planetGroupingDiagnostics?.ValidationIntentSourceField,
            sceneIntents = phase6SceneEnrichmentDiagnostics?.SceneIntents,
            sceneEnrichmentMetadata = phase6SceneEnrichmentDiagnostics?.SceneEnrichmentMetadata,
            phase7NarrationDiagnostics,
            phase12ThumbnailDiagnostics,
            phase13ShortNarrationDiagnostics,
            phase14NarrationDiagnostics,
            shortNarrationWordCount = phase13ShortNarrationDiagnostics?.ShortNarrationWordCount,
            estimatedDurationSeconds = phase13ShortNarrationDiagnostics?.EstimatedDurationSeconds,
            actualDurationSeconds = phase13ShortNarrationDiagnostics?.ActualDurationSeconds,
            minSeconds = phase13ShortNarrationDiagnostics?.MinSeconds ?? phase14NarrationDiagnostics?.MinSeconds,
            maxSeconds = phase13ShortNarrationDiagnostics?.MaxSeconds ?? phase14NarrationDiagnostics?.MaxSeconds,
            targetRange = phase13ShortNarrationDiagnostics?.TargetRange,
            expansionApplied = phase13ShortNarrationDiagnostics?.ExpansionApplied ?? phase14NarrationDiagnostics?.ExpansionApplied,
            finalValidationPassed = phase13ShortNarrationDiagnostics?.FinalValidationPassed ?? phase14NarrationDiagnostics?.FinalValidationPassed,
            preTrimWordCount = phase13ShortNarrationDiagnostics?.PreTrimWordCount,
            postTrimWordCount = phase13ShortNarrationDiagnostics?.PostTrimWordCount,
            preTrimDuration = phase13ShortNarrationDiagnostics?.PreTrimDuration,
            postTrimDuration = phase13ShortNarrationDiagnostics?.PostTrimDuration,
            trimApplied = phase13ShortNarrationDiagnostics?.TrimApplied,
            wordCount = phase14NarrationDiagnostics?.WordCount,
            estimatedSeconds = phase14NarrationDiagnostics?.EstimatedSeconds,
            longMinSeconds = phase14NarrationDiagnostics?.MinSeconds,
            longMaxSeconds = phase14NarrationDiagnostics?.MaxSeconds,
            targetSeconds = phase14NarrationDiagnostics?.TargetSeconds,
            wordsPerMinute = phase14NarrationDiagnostics?.WordsPerMinute,
            longExpansionApplied = phase14NarrationDiagnostics?.ExpansionApplied,
            longFinalValidationPassed = phase14NarrationDiagnostics?.FinalValidationPassed,
            currentEventLock = BuildCurrentEventLock(context),
            thumbnailCurrentEventLock = phase12ThumbnailDiagnostics?.CurrentEventLock,
            thumbnailRequestTitle = phase12ThumbnailDiagnostics?.ThumbnailRequestTitle,
            thumbnailRequestShortTitle = phase12ThumbnailDiagnostics?.ThumbnailRequestShortTitle,
            thumbnailEventType = phase12ThumbnailDiagnostics?.ThumbnailEventType,
            thumbnailPrimaryObjects = phase12ThumbnailDiagnostics?.ThumbnailPrimaryObjects,
            thumbnailSecondaryObjects = phase12ThumbnailDiagnostics?.ThumbnailSecondaryObjects,
            thumbnailSourceManifestPath = phase12ThumbnailDiagnostics?.ThumbnailSourceManifestPath,
            thumbnailSourceScenePath = phase12ThumbnailDiagnostics?.ThumbnailSourceScenePath,
            visualResolverResult = phase12ThumbnailDiagnostics?.VisualResolverResult,
            visualObjectsUsed = phase12ThumbnailDiagnostics?.VisualObjectsUsed,
            labelsUsed = phase12ThumbnailDiagnostics?.LabelsUsed,
            textUsed = phase12ThumbnailDiagnostics?.TextUsed,
            forbiddenObjectsDetected = phase12ThumbnailDiagnostics?.ForbiddenObjectsDetected,
            goldenPilotLeakageDetected = phase12ThumbnailDiagnostics?.GoldenPilotLeakageDetected,
            semanticValidationPassed = phase12ThumbnailDiagnostics?.SemanticValidationPassed,
            phase10TitleDiagnostics,
            titleFoundInCaptionText = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInCaptionText"),
            titleFoundInViewerTakeaway = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInViewerTakeaway"),
            titleFoundInOverlayText = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInOverlayText"),
            titleFoundInMetadata = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInMetadata"),
            titleFoundInReview = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInReview"),
            titleFoundInOcr = GetPhase10TitleDiagnostic(phase10TitleDiagnostics, "titleFoundInOcr")
        }, JsonOptions), cancellationToken);
        return result;
    }



    private static Phase6SceneEnrichmentDiagnostics BuildPhase6SceneEnrichmentDiagnostics(ProductionPhaseContext context)
    {
        var scenePlanInputPath = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.json");
        var enrichedPath = BuildEnrichedScenePlanPath(context);
        var plan = TryReadEnrichedScenePlan(enrichedPath);
        var sceneIntents = plan?.Scenes.Select(scene => new Phase6SceneIntentDiagnostics(
                scene.SceneNumber,
                scene.QuestionType,
                scene.ScenePurpose,
                scene.ViewerTakeaway,
                scene.NarrationIntent,
                scene.VisualIntent,
                scene.ImagePromptIntent,
                scene.OverlayIntent,
                scene.AccessibilityIntent)).ToArray() ?? Array.Empty<Phase6SceneIntentDiagnostics>();
        var visualIntents = plan?.Scenes.Select(scene => new Phase6VisualIntentDiagnostics(
                scene.SceneNumber,
                scene.QuestionType,
                scene.VisualIntent,
                scene.ImagePromptIntent,
                scene.OverlayIntent)).ToArray() ?? Array.Empty<Phase6VisualIntentDiagnostics>();
        var enrichedSceneIntents = BuildPhase6ValidationIntentDiagnostics(plan).ToArray();
        var planetGroupingValidationDiagnostics = ResolvePlanetGroupingPhase6Validation(context, enrichedSceneIntents);

        return new Phase6SceneEnrichmentDiagnostics(
            EnrichedScenePlanPath: NormalizePath(enrichedPath),
            EnrichedScenePlanExists: File.Exists(enrichedPath),
            PlanetGroupingStrategyActivated: IsPlanetGroupingStrategyActivated(context),
            PlanetGroupingEnricherExecuted: plan is not null,
            PlanetGroupingIntentInjected: planetGroupingValidationDiagnostics.PlanetGroupingIntentInjected,
            GuidedScanPathInjected: planetGroupingValidationDiagnostics.GuidedScanPathInjected,
            LegacyValidationPathExecuted: planetGroupingValidationDiagnostics.LegacyValidationPathExecuted,
            PlanetGroupingValidationPathExecuted: planetGroupingValidationDiagnostics.PlanetGroupingValidationPathExecuted,
            EnrichedSceneIntentCount: enrichedSceneIntents.Length,
            EnrichedSceneIntents: enrichedSceneIntents,
            VisualIntentCount: visualIntents.Length,
            ScenePlanInputPath: NormalizePath(scenePlanInputPath),
            EnrichedScenePlanOutputPath: NormalizePath(enrichedPath),
            ValidationIntentSourcePath: NormalizePath(enrichedPath),
            ValidationIntentSourceField: "scenes[*].viewerTakeaway|scenes[*].narrationIntent|scenes[*].visualIntent|scenes[*].imagePromptIntent|scenes[*].overlayIntent|scenes[*].accessibilityIntent",
            SceneIntents: sceneIntents,
            VisualIntents: visualIntents,
            SceneEnrichmentMetadata: plan?.Diagnostics,
            StrategyId: plan?.Diagnostics?.StrategyId ?? context.ProductionEventIntelligence.StrategyId,
            EventType: context.ProductionEventIntelligence.EventType,
            RequiredVisualObjects: plan?.Diagnostics?.RequiredVisualObjects ?? context.ProductionEventIntelligence.RequiredVisualObjects ?? Array.Empty<string>(),
            PrimaryObjects: plan?.Diagnostics?.PrimaryObjects ?? context.ProductionEventIntelligence.PrimaryObjects,
            SecondaryObjects: plan?.Diagnostics?.SecondaryObjects ?? context.ProductionEventIntelligence.SecondaryObjects);
    }

    private static EnrichedQuestionScenePlanDto? TryReadEnrichedScenePlan(string enrichedPath)
    {
        if (!File.Exists(enrichedPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(File.ReadAllText(enrichedPath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool IsPlanetGroupingStrategyActivated(ProductionPhaseContext context)
    {
        var intelligence = context.ProductionEventIntelligence;
        return string.Equals(intelligence.EventType, "PLANET_GROUPING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intelligence.EventType, "PlanetGrouping", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intelligence.StrategyId, "PlanetGrouping", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.MediaEventStrategy?.EventType, "PlanetGrouping", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlanetGroupingEventType(string? eventType)
        => string.Equals(eventType?.Trim(), "PLANET_GROUPING", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanetGroupingPhase6Event(ProductionPhaseContext context)
        => IsPlanetGroupingEventType(context.ProductionEventIntelligence.EventType)
            || IsPlanetGroupingEventType(context.Request.EventType)
            || IsPlanetGroupingEventType(context.ExecutionContext.EventType);

    private static Phase6ContractValidationPathDiagnostics ResolvePlanetGroupingPhase6Validation(
        ProductionPhaseContext context,
        IReadOnlyList<Phase6ValidationIntentDiagnostics> enrichedSceneIntents)
    {
        var planetGroupingValidationPathExecuted = IsPlanetGroupingPhase6Event(context);
        if (!planetGroupingValidationPathExecuted)
            return new(false, false, false, false, true);

        var planetGroupingIntentInjected = ContainsAnyPhase6ValidationIntent(enrichedSceneIntents, "planet grouping");
        var guidedScanPathInjected = ContainsAnyPhase6ValidationIntent(enrichedSceneIntents, "guided scan path", "scan path", "grouping arc");
        return new(
            planetGroupingValidationPathExecuted,
            planetGroupingIntentInjected,
            guidedScanPathInjected,
            planetGroupingIntentInjected && guidedScanPathInjected,
            false);
    }

    private static bool ContainsAnyPhase6ValidationIntent(
        IEnumerable<Phase6ValidationIntentDiagnostics> enrichedSceneIntents,
        params string[] phrases)
        => enrichedSceneIntents.Any(intent => phrases.Any(phrase => ContainsPhraseIgnoreCase(intent.Value, phrase)));

    private static bool ContainsPhraseIgnoreCase(string? value, string phrase)
        => !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(phrase)
            && value.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    private sealed record Phase6ContractValidationPathDiagnostics(
        bool PlanetGroupingValidationPathExecuted,
        bool PlanetGroupingIntentInjected,
        bool GuidedScanPathInjected,
        bool PlanetGroupingVisualContractPassed,
        bool LegacyValidationPathExecuted);

    private sealed record Phase6SceneEnrichmentDiagnostics(
        string EnrichedScenePlanPath,
        bool EnrichedScenePlanExists,
        bool PlanetGroupingStrategyActivated,
        bool PlanetGroupingEnricherExecuted,
        bool PlanetGroupingIntentInjected,
        bool GuidedScanPathInjected,
        bool LegacyValidationPathExecuted,
        bool PlanetGroupingValidationPathExecuted,
        int EnrichedSceneIntentCount,
        IReadOnlyList<Phase6ValidationIntentDiagnostics> EnrichedSceneIntents,
        int VisualIntentCount,
        string ScenePlanInputPath,
        string EnrichedScenePlanOutputPath,
        string ValidationIntentSourcePath,
        string ValidationIntentSourceField,
        IReadOnlyList<Phase6SceneIntentDiagnostics> SceneIntents,
        IReadOnlyList<Phase6VisualIntentDiagnostics> VisualIntents,
        QuestionSceneEnrichmentDiagnostics? SceneEnrichmentMetadata,
        string? StrategyId,
        string? EventType,
        IReadOnlyList<string> RequiredVisualObjects,
        IReadOnlyList<string> PrimaryObjects,
        IReadOnlyList<string> SecondaryObjects);

    private sealed record Phase6SceneIntentDiagnostics(
        int SceneNumber,
        string QuestionType,
        string ScenePurpose,
        string ViewerTakeaway,
        string NarrationIntent,
        string VisualIntent,
        string ImagePromptIntent,
        string OverlayIntent,
        string AccessibilityIntent);

    private sealed record Phase6VisualIntentDiagnostics(
        int SceneNumber,
        string QuestionType,
        string VisualIntent,
        string ImagePromptIntent,
        string OverlayIntent);

    private sealed record Phase6ValidationIntentDiagnostics(
        int SceneNumber,
        string QuestionType,
        string FieldPath,
        string Value);

    private static IEnumerable<Phase6ValidationIntentDiagnostics> BuildPhase6ValidationIntentDiagnostics(EnrichedQuestionScenePlanDto? plan)
    {
        if (plan is null) yield break;

        foreach (var scene in plan.Scenes)
        {
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].viewerTakeaway", scene.ViewerTakeaway);
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].narrationIntent", scene.NarrationIntent);
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].visualIntent", scene.VisualIntent);
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].imagePromptIntent", scene.ImagePromptIntent);
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].overlayIntent", scene.OverlayIntent);
            yield return new(scene.SceneNumber, scene.QuestionType, $"scenes[{scene.SceneNumber}].accessibilityIntent", scene.AccessibilityIntent);
        }
    }



    private static Phase12ThumbnailDiagnostics BuildPhase12ThumbnailDiagnostics(ProductionPhaseContext context)
    {
        var manifestPath = Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-scene-manifest.json");
        IReadOnlyDictionary<string, string> facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("validationFacts", out var validationFacts) && validationFacts.ValueKind == JsonValueKind.Object)
                    facts = validationFacts.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Phase12ThumbnailDiagnostics(
            CurrentEventLock: GetFact(facts, "currentEventLock", string.Empty),
            ThumbnailRequestTitle: GetFact(facts, "thumbnailRequestTitle", context.ProductionEventIntelligence.Title),
            ThumbnailRequestShortTitle: GetFact(facts, "thumbnailRequestShortTitle", context.ProductionEventIntelligence.ShortTitle),
            ThumbnailEventType: GetFact(facts, "thumbnailEventType", context.ProductionEventIntelligence.EventType),
            ThumbnailPrimaryObjects: SplitFact(GetFact(facts, "thumbnailPrimaryObjects", string.Join(", ", context.ProductionEventIntelligence.PrimaryObjects))),
            ThumbnailSecondaryObjects: SplitFact(GetFact(facts, "thumbnailSecondaryObjects", string.Join(", ", context.ProductionEventIntelligence.SecondaryObjects))),
            ThumbnailSourceManifestPath: GetFact(facts, "thumbnailSourceManifestPath", NormalizePath(manifestPath)),
            ThumbnailSourceScenePath: GetFact(facts, "thumbnailSourceScenePath", string.Empty),
            VisualResolverResult: GetFact(facts, "visualResolverResult", string.Empty),
            VisualObjectsUsed: SplitFact(GetFact(facts, "visualObjectsUsed", string.Join(", ", context.ProductionEventIntelligence.PrimaryObjects.Concat(context.ProductionEventIntelligence.SecondaryObjects)))),
            LabelsUsed: SplitFact(GetFact(facts, "labelsUsed", context.ProductionEventIntelligence.ShortTitle)),
            TextUsed: SplitFact(GetFact(facts, "textUsed", context.ProductionEventIntelligence.ShortTitle).Replace(" | ", ", ", StringComparison.OrdinalIgnoreCase)),
            ForbiddenObjectsDetected: SplitFact(GetFact(facts, "forbiddenObjectsDetected", string.Empty)),
            GoldenPilotLeakageDetected: bool.TryParse(GetFact(facts, "goldenPilotLeakageDetected", "false"), out var goldenLeakage) && goldenLeakage,
            SemanticValidationPassed: bool.TryParse(GetFact(facts, "semanticValidationPassed", "false"), out var semanticPassed) && semanticPassed);
    }

    private static string GetFact(IReadOnlyDictionary<string, string> facts, string key, string fallback)
        => facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static IReadOnlyList<string> SplitFact(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private sealed record Phase12ThumbnailDiagnostics(
        string CurrentEventLock,
        string ThumbnailRequestTitle,
        string ThumbnailRequestShortTitle,
        string ThumbnailEventType,
        IReadOnlyList<string> ThumbnailPrimaryObjects,
        IReadOnlyList<string> ThumbnailSecondaryObjects,
        string ThumbnailSourceManifestPath,
        string ThumbnailSourceScenePath,
        string VisualResolverResult,
        IReadOnlyList<string> VisualObjectsUsed,
        IReadOnlyList<string> LabelsUsed,
        IReadOnlyList<string> TextUsed,
        IReadOnlyList<string> ForbiddenObjectsDetected,
        bool GoldenPilotLeakageDetected,
        bool SemanticValidationPassed);

    private static Phase10ValidationDiagnostics? ReadPhase10TitleDiagnostics(IReadOnlyList<string> outputFiles)
    {
        var validationPath = outputFiles.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "production-quality-validation-before-assembly.json", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(validationPath) || !File.Exists(validationPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(validationPath));
            return new Phase10ValidationDiagnostics(
                NormalizePath(validationPath),
                ReadBooleanProperty(doc.RootElement, "titleFoundInCaptionText"),
                ReadBooleanProperty(doc.RootElement, "titleFoundInViewerTakeaway"),
                ReadBooleanProperty(doc.RootElement, "titleFoundInOverlayText"),
                ReadBooleanProperty(doc.RootElement, "titleFoundInMetadata"),
                ReadBooleanProperty(doc.RootElement, "titleFoundInReview"),
                ReadBooleanProperty(doc.RootElement, "titleFoundInOcr"),
                CloneProperty(doc.RootElement, "titleValidationDiagnostics"),
                CloneProperty(doc.RootElement, "titleValidationSourceDiagnostics"),
                CloneProperty(doc.RootElement, "phase10VisualSourceInputDiagnostics"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? CloneProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) ? value.Clone() : null;

    private static bool ReadBooleanProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool? GetPhase10TitleDiagnostic(Phase10ValidationDiagnostics? diagnostics, string propertyName)
        => diagnostics is null
            ? null
            : propertyName switch
            {
                "titleFoundInCaptionText" => diagnostics.TitleFoundInCaptionText,
                "titleFoundInViewerTakeaway" => diagnostics.TitleFoundInViewerTakeaway,
                "titleFoundInOverlayText" => diagnostics.TitleFoundInOverlayText,
                "titleFoundInMetadata" => diagnostics.TitleFoundInMetadata,
                "titleFoundInReview" => diagnostics.TitleFoundInReview,
                "titleFoundInOcr" => diagnostics.TitleFoundInOcr,
                _ => null
            };

    private sealed record Phase10ValidationDiagnostics(
        string ValidationPathUsed,
        bool TitleFoundInCaptionText,
        bool TitleFoundInViewerTakeaway,
        bool TitleFoundInOverlayText,
        bool TitleFoundInMetadata,
        bool TitleFoundInReview,
        bool TitleFoundInOcr,
        JsonElement? TitleValidationDiagnostics,
        JsonElement? TitleValidationSourceDiagnostics,
        JsonElement? Phase10VisualSourceInputDiagnostics);

    private static async Task WritePhaseManifestAsync(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults, CancellationToken cancellationToken)
    {
        var path = Path.Combine(context.OutputRoot, "phase-manifest.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { context.Request.PlanId, context.Request.RegionId, context.Request.Title, executionMode = context.ExecutionMode.ToString(), requestedStartPhase = context.PipelineRequest.RequestedStartPhaseNo ?? context.StartPhaseNo, requestedEndPhase = context.PipelineRequest.RequestedEndPhaseNo ?? context.EndPhaseNo, expandedStartPhase = context.StartPhaseNo, expandedEndPhase = context.EndPhaseNo, dependencyExpansionApplied = context.PipelineRequest.RequestedStartPhaseNo.HasValue && context.PipelineRequest.RequestedStartPhaseNo.Value != context.StartPhaseNo, startPhaseNo = context.StartPhaseNo, endPhaseNo = context.EndPhaseNo, overwriteExisting = context.OverwriteExisting, retryFailedOnly = context.RetryFailedOnly, sceneApprovalStagingRoot = NormalizePath(context.ExecutionContext.SceneRoot!), sceneApprovalNormalizedRoot = NormalizePath(GetSceneApprovalNormalizedRoot(context.OutputRoot)), filesDeletedDueToOverwrite = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(), filesGeneratedThisRun = phaseResults.SelectMany(phase => phase.OutputFiles).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(NormalizePath).ToArray(), phases = phaseResults }, JsonOptions), cancellationToken);
    }

    private async Task<string> WriteProductionIntelligenceAsync(string outputRoot, ProductionEventIntelligence intelligence, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        return path;
    }

    private ProductionPipelineExecutionContext BuildProductionExecutionContext(ProductionPipelineExecutionContext? baseContext, ContentPlanProductionPipelineRequest request, Guid eventId, string planRoot, ProductionEventIntelligence intelligence, IMediaEventStrategy strategy)
    {
        var year = request.ScheduledUtc?.Year ?? request.PeakUtc?.Year ?? request.StartUtc?.Year ?? DateTimeOffset.UtcNow.Year;
        var questionRoot = Path.Combine(planRoot, "question-engine");
        var sceneRoot = Path.Combine(questionRoot, "scene-approval-v3");
        var heroRoot = Path.Combine(planRoot, "hero");
        var thumbnailRoot = Path.Combine(planRoot, "thumbnails");
        var narrationRoot = Path.Combine(planRoot, "narration");
        var ttsRoot = Path.Combine(planRoot, "tts");
        var videoAssemblyRoot = Path.Combine(planRoot, "video-assembly");
        var validationRoot = Path.Combine(planRoot, "validation");
        var contract = new ProductionExecutionContext(request.PlanId, eventId, request.RegionId, request.Language, year, request.EventType, request.Category, planRoot, questionRoot, sceneRoot, heroRoot, thumbnailRoot, narrationRoot, ttsRoot, videoAssemblyRoot, validationRoot, intelligence, strategy);
        return (baseContext ?? new ProductionPipelineExecutionContext(true, request.PlanId, eventId, request.SourceExternalEventId, true)) with
        {
            ContentGenerationPlanId = request.PlanId,
            AstronomyEventIntelligenceId = eventId,
            RegionId = request.RegionId,
            Language = request.Language,
            Category = request.Category,
            Year = year,
            EventType = request.EventType,
            PlanRoot = planRoot,
            QuestionRoot = questionRoot,
            SceneRoot = sceneRoot,
            HeroRoot = heroRoot,
            ThumbnailRoot = thumbnailRoot,
            NarrationRoot = narrationRoot,
            TtsRoot = ttsRoot,
            VideoAssemblyRoot = videoAssemblyRoot,
            ValidationRoot = validationRoot,
            ProductionEventIntelligence = intelligence,
            MediaEventStrategy = strategy,
            ProductionExecutionContext = contract
        };
    }

    private string BuildEventWorkingRoot(ContentPlanProductionPipelineRequest request, string eventId)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "assets", Sanitize(request.RegionId), "events", eventId);

    private static void ClearProductionOutputRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            if (string.Equals(Path.GetFileName(directory), "plan-input", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(directory), "_archive", StringComparison.OrdinalIgnoreCase)) continue;
            Directory.Delete(directory, recursive: true);
        }
        foreach (var file in Directory.EnumerateFiles(path))
            File.Delete(file);
    }

    private static void DeleteProductionSubtree(string path, List<string>? deletedFiles = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        if (deletedFiles is not null)
            deletedFiles.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(NormalizePath));
        Directory.Delete(path, recursive: true);
    }

    private static void ClearPhaseRangeOutputsForOverwrite(ProductionPhaseContext context)
    {
        var deletedFiles = context.DeletedFilesDueToOverwrite as List<string>;
        var deleteStartPhaseNo = context.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs
            ? context.PipelineRequest.RequestedStartPhaseNo ?? context.StartPhaseNo
            : context.StartPhaseNo;
        var deleteEndPhaseNo = context.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs
            ? context.PipelineRequest.RequestedEndPhaseNo ?? context.EndPhaseNo
            : context.EndPhaseNo;

        if (deleteStartPhaseNo <= 6 && deleteEndPhaseNo >= 6)
        {
            DeleteFileIfExists(BuildEnrichedScenePlanPath(context), deletedFiles);
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.ValidationRoot!, "phase-06-validation.json"), deletedFiles);
        }

        if (deleteStartPhaseNo <= 7 && deleteEndPhaseNo >= 7)
        {
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json"), deletedFiles);
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json"), deletedFiles);
        }

        if (deleteStartPhaseNo <= 9 && deleteEndPhaseNo >= 8)
        {
            DeleteProductionSubtree(context.ExecutionContext.SceneRoot!, deletedFiles);
            DeleteProductionSubtree(Path.Combine(context.ExecutionContext.QuestionRoot!, "scene-approval-v3"), deletedFiles);
            DeleteProductionSubtree(GetSceneApprovalNormalizedRoot(context.OutputRoot), deletedFiles);
        }

        if (deleteStartPhaseNo <= 11 && deleteEndPhaseNo >= 11)
            DeleteProductionSubtree(context.ExecutionContext.HeroRoot!, deletedFiles);

        if (deleteStartPhaseNo <= 12 && deleteEndPhaseNo >= 12)
            DeleteProductionSubtree(context.ExecutionContext.ThumbnailRoot!, deletedFiles);

        var firstValidationToDelete = Math.Max(deleteStartPhaseNo, 7);
        var lastValidationToDelete = Math.Min(deleteEndPhaseNo, 12);
        for (var phaseNo = firstValidationToDelete; phaseNo <= lastValidationToDelete; phaseNo++)
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json"), deletedFiles);
    }

    private static void DeleteFileIfExists(string path, List<string>? deletedFiles = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        deletedFiles?.Add(NormalizePath(path));
        File.Delete(path);
    }

    private async Task<IReadOnlyList<string>> MaterializePlanFolderAsync(ContentPlanProductionPipelineRequest request, string eventId, string outputRoot, IReadOnlyList<string> generatedFiles, CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        var eventRoot = BuildEventWorkingRoot(request, eventId);
        if (Directory.Exists(Path.Combine(outputRoot, "question-engine")))
            eventRoot = outputRoot;
        CopyFile(Path.Combine(eventRoot, "question-engine", "question-answer-set.json"), Path.Combine(outputRoot, "question-engine", "question-answer-set.json"), copied);
        CopyFile(Path.Combine(eventRoot, "question-engine", "question-answer-set.json"), Path.Combine(outputRoot, "question-engine", "questions.json"), copied);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "short"), Path.Combine(outputRoot, "scene-approval-v3", "short"), copied, renameFinalScenes: true);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "long"), Path.Combine(outputRoot, "scene-approval-v3", "long"), copied, renameFinalScenes: true);
        CopyFile(Path.Combine(eventRoot, "hero-assets", "hero-landscape.png"), Path.Combine(outputRoot, "hero", "hero.png"), copied);
        CopyFile(Path.Combine(eventRoot, "hero", "hero-landscape.png"), Path.Combine(outputRoot, "hero", "hero.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-landscape.png"), Path.Combine(outputRoot, "thumbnails", "landscape.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-square.png"), Path.Combine(outputRoot, "thumbnails", "square.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-portrait.png"), Path.Combine(outputRoot, "thumbnails", "portrait.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnails", "thumbnail-landscape.png"), Path.Combine(outputRoot, "thumbnails", "landscape.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnails", "thumbnail-square.png"), Path.Combine(outputRoot, "thumbnails", "square.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnails", "thumbnail-portrait.png"), Path.Combine(outputRoot, "thumbnails", "portrait.png"), copied);
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
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
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

    private static ProductionPipelineExecutionResult BuildResult(bool success, bool dryRun, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool heroGenerated, bool thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool shortVideoGenerated, bool longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<ProductionPhaseResult>? phaseResults = null, IReadOnlyList<RequestedOutputCompletion>? requestedOutputCompletion = null)
    {
        var lastCompletedPhaseNo = phaseResults?
            .Where(p => p.Status is ProductionPhaseStatus.Succeeded or ProductionPhaseStatus.Skipped)
            .OrderByDescending(p => p.PhaseNo)
            .Select(p => (int?)p.PhaseNo)
            .FirstOrDefault();
        var lastFailedPhaseNo = phaseResults?
            .Where(p => p.Status == ProductionPhaseStatus.Failed)
            .OrderByDescending(p => p.PhaseNo)
            .Select(p => (int?)p.PhaseNo)
            .FirstOrDefault();

        return new(success, dryRun, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), phaseResults, lastCompletedPhaseNo, lastFailedPhaseNo, RequestedOutputCompletion: requestedOutputCompletion);
    }

    private static string GetSceneApprovalNormalizedRoot(string outputRoot) => Path.Combine(outputRoot, "scene-approval-v3");
    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool HeroContractExists(string outputRoot) => File.Exists(Path.Combine(outputRoot, "hero", "hero.png")) && File.Exists(Path.Combine(outputRoot, "hero", "hero-scene-manifest.json"));
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
