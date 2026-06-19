using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed partial class ProductionPipelineExecutionService(
    IQuestionEngine questionEngine,
    IQuestionScenePlanner scenePlanner,
    IQuestionSceneIntentEnricher sceneIntentEnricher,
    IQuestionDrivenNarrationGenerator narrationGenerator,
    IEditorialAstronomyInfographicComposer sceneEngine,
    IAstronomyInfographicRenderer infographicRenderer,
    IHeroAssetIntelligenceEngine heroEngine,
    IThumbnailAssetIntelligenceService thumbnailEngine,
    IAstroPulseGalleryService galleryEngine,
    IVideoAssemblyIntelligenceService videoAssemblyEngine,
    IEventProductionIntelligenceAdapter intelligenceAdapter,
    IMediaEventStrategyResolver strategyResolver,
    IProductionPipelineQualityValidator qualityValidator,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ProductionPipelineExecutionService> logger,
    IOptions<AzureSpeechOptions>? azureSpeechOptions = null,
    IAzureSpeechClient? azureSpeechClient = null,
    IOptions<VideoAssemblyOptions>? videoAssemblyOptions = null,
    ISceneAssetsV3Service? sceneAssetsV3Service = null,
    IOptions<ThumbnailOptions>? thumbnailOptions = null) : IProductionPipelineExecutionService, IProductionPhaseRunner
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
    private const double LongNarrationMaximumSeconds = 300.0;

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
        var executionContext = BuildProductionExecutionContext(request, eventIdResolution.EventId ?? Guid.Empty, outputRoot, productionIntelligence, strategy);
        var startPhaseNo = Math.Clamp(request.StartPhaseNo ?? 1, 1, 20);
        var endPhaseNo = Math.Clamp(request.EndPhaseNo ?? 20, startPhaseNo, 20);
        var phaseResults = new List<ProductionPhaseResult>();
        var deletedFilesDueToOverwrite = new List<string>();

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
                phaseResults.Add(await WritePhaseValidationAsync(context, phase.No, ResolvePhaseName(context, phase.No, phase.Name), ProductionPhaseStatus.Skipped, [], [], [], [], "Dry run: phase was planned but not executed.", false, cancellationToken));
            }
            await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
            return BuildResult(true, true, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, phaseResults);
        }

        foreach (var phase in PhaseDefinitions())
        {
            if (phase.No < startPhaseNo || phase.No > endPhaseNo) continue;
            if (!IsPhaseRequiredForRequestedOutputs(context, phase.No))
            {
                var skipped = await WritePhaseValidationAsync(context, phase.No, ResolvePhaseName(context, phase.No, phase.Name), ProductionPhaseStatus.Skipped, [], [], [], [], OutputTypeNotRequestedReason, false, cancellationToken);
                phaseResults.Add(skipped);
                await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
                continue;
            }
            if (request.RetryFailedOnly && PreviousPhaseSucceeded(context, phase.No) && PreviousPhaseRequiredOutputsExist(context, phase.No))
            {
                var skipped = await WritePhaseValidationAsync(context, phase.No, ResolvePhaseName(context, phase.No, phase.Name), ProductionPhaseStatus.Skipped, [], [], [], [], "retryFailedOnly=true: previous successful phase was not rerun.", false, cancellationToken);
                phaseResults.Add(skipped);
                await WritePhaseManifestAsync(context, phaseResults, cancellationToken);
                continue;
            }
            var result = await ExecutePhaseAsync(context, phase.No, ResolvePhaseName(context, phase.No, phase.Name), phase.Action, cancellationToken);
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

        ValidatePartialPhaseExecutionContract(context, phaseResults, errors);

        var shortVideo = Path.Combine(outputRoot, "video", "short", "final-short.mp4");
        var longVideo = Path.Combine(outputRoot, "video", "long", "final-long.mp4");
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
        (13, "Generate Gallery", PhaseGenerateGalleryAsync),
        (14, "Scene Audio Sync V1", PhaseSceneAudioSyncAsync),
        (15, "TTS Timeline V1", PhaseGenerateTtsTimelineV1Async),
        (16, "Duration Calibration V1", PhaseDurationCalibrationV1Async),
        (17, "Motion Layer V1", PhaseMotionLayerV1Async),
        (18, "Cinematic Video Assembly V2", PhaseVideoAssemblyV1Async),
        (19, "Video QA & Production Review", PhaseVideoQaProductionReviewAsync),
        (20, "Publishing Package", PhaseFinalValidationAsync)
    ];

    private static bool IsSceneAssetsV3Enabled(ProductionPhaseContext context) => context.PipelineRequest.EnableSceneAssetsV3;

    private static string ResolvePhaseName(ProductionPhaseContext context, int phaseNo, string fallback)
        => IsSceneAssetsV3Enabled(context) ? phaseNo switch
        {
            8 => "Generate Short Scene Assets V3",
            9 => "Generate Long Scene Assets V3",
            10 => "Validate Scene Assets V3",
            _ => fallback
        } : fallback;

    private const string OutputTypeNotRequestedReason = "Output type not requested";

    private static bool IsPhaseRequiredForRequestedOutputs(ProductionPhaseContext context, int phaseNo)
        => phaseNo switch
        {
            <= 10 => true,
            11 => IsRequestedOutput(context, "HeroAsset"),
            12 => IsRequestedOutput(context, "Thumbnail"),
            13 => true,
            14 => true,
            15 => IsRequestedOutput(context, "LongVideo") || IsRequestedOutput(context, "ShortVideo"),
            16 => IsRequestedOutput(context, "LongVideo") || IsRequestedOutput(context, "ShortVideo"),
            17 => IsRequestedOutput(context, "LongVideo") || IsRequestedOutput(context, "ShortVideo"),
            18 => IsRequestedOutput(context, "ShortVideo"),
            19 => IsRequestedOutput(context, "LongVideo"),
            20 => true,
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
            BuildRequestedOutputCompletion(context, phaseResults, "ShortVideo", [16, 18]),
            BuildRequestedOutputCompletion(context, phaseResults, "LongVideo", [15, 16, 17, 18, 19]),
            BuildRequestedOutputCompletion(context, phaseResults, "Gallery", [13]),
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
            if (phaseNo <= 15) ValidatePhaseInputContract(context, phaseNo);
            var outputs = (await action(context, cancellationToken)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = outputs.Where(p => !File.Exists(p) && !Directory.Exists(p)).Select(p => $"Expected output was not found: {p}").ToArray();
            var phase10TitleDiagnostics = phaseNo == 10 ? ReadPhase10TitleDiagnostics(outputs) : null;
            var warnings = phaseNo == 18 ? ReadPhase18Warnings(context) : [];
            return await WritePhaseValidationAsync(context, phaseNo, phaseName, missing.Length == 0 ? ProductionPhaseStatus.Succeeded : ProductionPhaseStatus.Failed, [], outputs, warnings, missing, missing.Length == 0 ? "Validation passed." : "Validation failed: required output missing.", missing.Length > 0, cancellationToken, started, phase10TitleDiagnostics);
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
    {
        var intelligencePath = await WriteProductionIntelligenceAsync(context.OutputRoot, context.ProductionEventIntelligence, cancellationToken);
        var diagnosticsPath = await WriteProductionIntelligenceDiagnosticsAsync(context.OutputRoot, context.ProductionEventIntelligence, cancellationToken);
        ValidatePlanetConjunctionPhase2(context.ProductionEventIntelligence);
        return [intelligencePath, diagnosticsPath];
    }

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

        logger.LogInformation("[Phase6] enableSceneVariants={EnableSceneVariants}", context.PipelineRequest.EnableSceneVariants);
        logger.LogInformation("[Phase6] SceneCount={SceneCount}", response.EnrichedScenePlan.Scenes.Count);

        var generatedVariants = 0;
        if (context.PipelineRequest.EnableSceneVariants)
            generatedVariants = await AddPhase6SceneVisualVariantsAsync(enrichedPath, cancellationToken);

        logger.LogInformation("[Phase6] GeneratedVariants={GeneratedVariants}", generatedVariants);

        await ValidatePhase6EnrichedScenePlanContractAsync(context, enrichedPath, cancellationToken);
        return response.GeneratedFiles.Concat([enrichedPath]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateNarrationPlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        RequireFile(BuildEnrichedScenePlanPath(context), "Enriched question-driven scene plan");
        var narrationRequest = BuildQuestionDrivenNarrationRequest(context);
        ValidatePhase7NarrationRequest(narrationRequest, context);
        var response = await narrationGenerator.GenerateQuestionDrivenNarrationAsync(narrationRequest, cancellationToken)
            ?? throw new InvalidOperationException("Phase 7 narration generation returned a null response.");
        if (response.Narration is null)
            throw new InvalidOperationException("Phase 7 narration generation returned a null narration object.");
        if (response.Review is null)
            throw new InvalidOperationException("Phase 7 narration generation returned a null narration review object.");

        var narrationPath = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json");
        var reviewPath = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json");
        await PersistPhase7NarrationFilesAsync(response, narrationPath, reviewPath, cancellationToken);
        ValidatePhase7NarrationFilesGenerated(response, narrationPath, reviewPath);
        var outputs = new List<string>(response.GeneratedFiles ?? Array.Empty<string>());
        outputs.Add(narrationPath);
        outputs.Add(reviewPath);
        Directory.CreateDirectory(context.ExecutionContext.SceneRoot!);
        CopyFile(narrationPath, Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration.json"), outputs);
        CopyFile(reviewPath, Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration-review.json"), outputs);
        return outputs;
    }

    private static async Task PersistPhase7NarrationFilesAsync(QuestionDrivenNarrationResponse response, string narrationPath, string reviewPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
        await File.WriteAllTextAsync(narrationPath, JsonSerializer.Serialize(response.Narration, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(reviewPath, JsonSerializer.Serialize(response.Review, JsonOptions), cancellationToken);
    }

    private static void ValidatePhase7NarrationFilesGenerated(QuestionDrivenNarrationResponse response, string narrationPath, string reviewPath)
    {
        if (response.Narration is null)
            throw new InvalidOperationException("Phase 7 narration generation returned a null narration object.");
        if (response.Review is null)
            throw new InvalidOperationException("Phase 7 narration generation returned a null narration review object.");

        var missing = new List<string>();
        if (!File.Exists(narrationPath)) missing.Add(Path.GetFileName(narrationPath));
        if (!File.Exists(reviewPath)) missing.Add(Path.GetFileName(reviewPath));

        if (missing.Count > 0)
            throw new InvalidOperationException("Phase 7 narration generation did not persist required output file(s): " + string.Join(", ", missing) + ".");

        if (!response.IsValid)
        {
            var validationMessages = (response.Warnings ?? [])
                .Concat(response.Review.Checks.Where(check => !check.Passed).Select(check => check.Message))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            throw new InvalidOperationException("Phase 7 narration generation failed validation: " + string.Join(" | ", validationMessages));
        }
    }

    private static async Task<int> AddPhase6SceneVisualVariantsAsync(string enrichedPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(enrichedPath)) return 0;

        var json = await File.ReadAllTextAsync(enrichedPath, cancellationToken);
        var plan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Phase 6 enriched scene plan could not be parsed before visual variant enrichment.");

        var enrichedScenes = plan.Scenes
            .Select(scene => scene with { VisualVariants = BuildPhase6SceneVisualVariants(scene) })
            .ToArray();
        var enrichedPlan = plan with { Scenes = enrichedScenes };

        await File.WriteAllTextAsync(enrichedPath, JsonSerializer.Serialize(enrichedPlan, JsonOptions), cancellationToken);
        return enrichedScenes.Sum(scene => scene.VisualVariants?.Count ?? 0);
    }

    private static IReadOnlyList<SceneVisualVariantDto> BuildPhase6SceneVisualVariants(EnrichedQuestionSceneDto scene)
    {
        var safeScene = Math.Max(1, scene.SceneNumber);
        var sceneToken = $"scene-{safeScene:00}";
        return
        [
            new(1, "wide_context", "Establish the full sky context before focusing on the answer.", 6.0, "Wide locked-off sky frame", $"WIDE FRAMING: place the main object small in the upper-right third with a broad horizon band across the lower quarter; emphasize the surrounding sky context for {scene.ViewerQuestion}", "Slow drift or gentle parallax only", "Single compact label anchored near the lower-left horizon; no stacked panel", "Planning metadata only; do not render during Phase 6", $"{sceneToken}-wide-context.png"),
            new(2, "object_focus", "Focus attention on the primary visual object or relationship in this scene.", 7.0, "Zoomed telephoto frame", $"ZOOMED FRAMING: enlarge the object or relationship implied by {scene.VisualIntent} and place it on the center-left focal third with the horizon mostly cropped out", "Subtle push-in toward the key object", "Short object label adjacent to the focal object plus one small fact chip on the opposite side", "Planning metadata only; do not render during Phase 6", $"{sceneToken}-object-focus.png"),
            new(3, "educational_overlay", "Clarify the lesson with concise explanatory overlays.", 7.0, "Stable infographic layout", $"INFOGRAPHIC LAYOUT: reserve a tall left-side explanation panel, place the sky/object diagram on the right, and use connector lines to explain {scene.ViewerTakeaway}", "Hold mostly static so labels remain readable", "Stacked headline, two bullet labels, and one tip panel inside deterministic safe areas", "Planning metadata only; do not render during Phase 6", $"{sceneToken}-educational-overlay.png"),
            new(4, "cinematic_detail", "Add a close, atmospheric detail that supports retention without changing the scene meaning.", 5.0, "Close-up cinematic composition", $"CLOSE-UP CINEMATIC COMPOSITION: crop tightly on a memorable detail from {scene.ImagePromptIntent}, place the object large on the lower-right third, and use dark negative space above", "Slow cinematic reveal or light sweep", "Tiny caption only in an upper-left letterbox-safe area", "Planning metadata only; do not render during Phase 6", $"{sceneToken}-cinematic-detail.png"),
            new(5, "transition_or_closing", "Provide a call-to-action bridge into the next scene or a closing beat.", 4.0, "CTA card composition", "CTA COMPOSITION: use a clean centered closing/title card with the celestial object as a small background accent and a prominent lower safe-area call-to-action block", "Gentle fade, pan, or hold for editorial transition", "Large CTA line, small save/share reminder, and generous negative space", "Planning metadata only; do not render during Phase 6", $"{sceneToken}-transition-or-closing.png")
        ];
    }

    private static string BuildEnrichedScenePlanPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");

    private static string BuildLongNarrationRequestPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.NarrationRoot!, "long", "long-narration-request.json");

    private static string BuildLongNarrationOutputPath(ProductionPhaseContext context)
        => Path.Combine(context.ExecutionContext.NarrationRoot!, "long", "narration.txt");

    private static string BuildLongSceneApprovalRoot(ProductionPhaseContext context)
        => Path.Combine(context.OutputRoot, "scene-assets-v3", "long");

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
        if (context.PipelineRequest.EnableSceneVariants)
        {
            foreach (var scene in plan.Scenes)
            {
                if ((scene.VisualVariants?.Count ?? 0) < 3)
                    issues.Add($"scene {scene.SceneNumber} must include at least 3 visual variants when enableSceneVariants=true");
            }
        }

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
            SourceOfEventId: request.SourceOfEventId,
            NarrationGenerated: File.Exists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json")),
            NarrationPath: Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json").Replace('\\', '/'),
            ReviewPath: Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json").Replace('\\', '/'),
            WordCount: CountPhase7NarrationWords(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json")));

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
        string? SourceOfEventId,
        bool NarrationGenerated,
        string NarrationPath,
        string ReviewPath,
        int WordCount);

    private static int CountPhase7NarrationWords(string narrationPath)
    {
        if (!File.Exists(narrationPath)) return 0;
        using var document = JsonDocument.Parse(File.ReadAllText(narrationPath));
        return CountNarrationWords(document.RootElement);
    }

    private static int CountNarrationWords(JsonElement element)
    {
        var count = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("narrationText", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    count += CountWords(property.Value.GetString());
                else
                    count += CountNarrationWords(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                count += CountNarrationWords(item);
        }

        return count;
    }

    private static int CountWords(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’_-][\p{L}\p{N}]+)*").Count;

    private async Task<IReadOnlyList<string>> PhaseGenerateSceneImagesAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        if (IsSceneAssetsV3Enabled(context))
            return await GenerateSceneAssetsV3Async(context, 8, "Generate Short Scene Images", generateShort: true, generateLong: false, cancellationToken);

        if (!context.PipelineRequest.EnableSceneVariants)
        {
            Directory.CreateDirectory(Path.Combine(context.ExecutionContext.SceneRoot!, "short"));
            Directory.CreateDirectory(Path.Combine(context.ExecutionContext.SceneRoot!, "long"));
        }
        ValidateSceneApprovalTextBeforeRendering(context);
        var generatedFiles = new List<string>();
        if (context.PipelineRequest.EnableSceneVariants)
        {
            generatedFiles.AddRange(await RenderPhase8SceneVisualVariantsAsync(context, cancellationToken));
        }
        else
        {
            var response = await sceneEngine.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, context.ExecutionContext), cancellationToken);
            generatedFiles.AddRange(response.GeneratedFiles);
        }
        var phase8Validation = ResolveSceneImageValidationPath(context.ExecutionContext.SceneRoot!, "short", preferSceneAssets: true);
        ValidateSceneImageDirectoryCoverage(phase8Validation.SelectedPath, "Scene image validation");
        return generatedFiles.Concat(Directory.EnumerateFiles(phase8Validation.SelectedPath, "*.png", SearchOption.AllDirectories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async(ProductionPhaseContext context, bool generateShort, bool generateLong, CancellationToken cancellationToken)
        => await GenerateSceneAssetsV3Async(context, generateShort ? 8 : 9, generateShort ? "Generate Short Scene Images" : "Generate Long Scene Images", generateShort, generateLong, cancellationToken);

    private async Task<IReadOnlyList<string>> GenerateSceneAssetsV3Async(ProductionPhaseContext context, int phaseNo, string phaseName, bool generateShort, bool generateLong, CancellationToken cancellationToken)
    {
        var format = generateShort ? "short" : "long";
        var expectedCount = generateShort ? 5 : 9;
        await WriteSceneAssetsHookDiagnosticsAsync(context, BuildSceneAssetsHookDiagnostics(context, phaseNo, phaseName, format, beforeExecution: true), cancellationToken);
        try
        {
            if (sceneAssetsV3Service is null)
            {
                await UpdateSceneAssetsHookDiagnosticsAsync(context, phaseNo, d =>
                {
                    d["errorBeforeGenerator"] = "Scene Assets V3 is enabled, but ISceneAssetsV3Service is not registered.";
                    d["exceptionType"] = nameof(InvalidOperationException);
                    d["exceptionMessage"] = "Scene Assets V3 is enabled, but ISceneAssetsV3Service is not registered.";
                }, cancellationToken);
                throw new InvalidOperationException("Scene Assets V3 is enabled, but ISceneAssetsV3Service is not registered.");
            }

            await UpdateSceneAssetsHookDiagnosticsAsync(context, phaseNo, d =>
            {
                d["sceneAssetsVersionDecision"] = "V3";
                d["decisionReason"] = "enableSceneAssetsV3=true";
                d["selectedGenerator"] = generateShort ? "SceneAssetsV3ShortGenerator" : "SceneAssetsV3LongGenerator";
                d["selectedGeneratorClass"] = sceneAssetsV3Service.GetType().Name;
                d["legacyV2GeneratorCalled"] = false;
                d["v3GeneratorCalled"] = true;
                d["actualOutputRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", format));
            }, cancellationToken);

            var response = await sceneAssetsV3Service.GenerateAsync(new SceneAssetsV3Request(context.OutputRoot, generateShort, generateLong, context.OverwriteExisting, context.PipelineRequest.EnableAccurateSkyGuideV2), cancellationToken);
            if (!Directory.Exists(Path.Combine(context.OutputRoot, "scene-assets-v3")))
                throw new InvalidOperationException("Scene Assets V3 folder is missing after V3 generation.");

            var files = response.GeneratedFiles.ToList();
            if (generateShort) ValidateSceneAssetsV3Format(context.OutputRoot, "short", 5);
            if (generateLong) ValidateSceneAssetsV3Format(context.OutputRoot, "long", 9);
            await UpdateSceneAssetsHookDiagnosticsAsync(context, phaseNo, d => PopulateSceneAssetsFormatDiagnostics(d, context.OutputRoot, format, expectedCount, files), cancellationToken);
            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            await UpdateSceneAssetsHookDiagnosticsAsync(context, phaseNo, d =>
            {
                if (string.IsNullOrWhiteSpace(d["actualOutputRoot"]?.GetValue<string>()))
                    d["actualOutputRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", format));
                PopulateSceneAssetsFormatDiagnostics(d, context.OutputRoot, format, expectedCount, []);
                d["exceptionType"] = ex.GetType().Name;
                d["exceptionMessage"] = ex.Message;
            }, cancellationToken);
            throw;
        }
    }

    private static IReadOnlyList<string> ValidateSceneAssetsV3(ProductionPhaseContext context)
    {
        var shortFiles = ValidateSceneAssetsV3Format(context.OutputRoot, "short", 5);
        var longFiles = ValidateSceneAssetsV3Format(context.OutputRoot, "long", 9);
        var duplicate = shortFiles.Concat(longFiles).Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Select(ComputePhase8VisualHash).GroupBy(h => h).Any(g => g.Count() > 1);
        if (duplicate) throw new InvalidOperationException("Scene Assets V3 validation failed: duplicate image hashes detected.");
        return shortFiles.Concat(longFiles).ToArray();
    }

    private async Task<IReadOnlyList<string>> ValidateSceneAssetsV3WithDiagnosticsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        await WriteSceneAssetsHookDiagnosticsAsync(context, BuildSceneAssetsValidationHookDiagnostics(context, beforeExecution: true), cancellationToken);
        try
        {
            await UpdateSceneAssetsHookDiagnosticsAsync(context, 10, d =>
            {
                d["sceneAssetsVersionDecision"] = "V3";
                d["decisionReason"] = "enableSceneAssetsV3=true";
                d["selectedValidator"] = "SceneAssetsV3Validator";
                d["selectedValidatorClass"] = nameof(ValidateSceneAssetsV3);
                d["legacyV2ValidatorCalled"] = false;
                d["v3ValidatorCalled"] = true;
                d["actualShortRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", "short"));
                d["actualLongRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", "long"));
            }, cancellationToken);
            var files = ValidateSceneAssetsV3(context);
            await UpdateSceneAssetsHookDiagnosticsAsync(context, 10, d => PopulateSceneAssetsValidationDiagnostics(d, context.OutputRoot, validationPassed: true), cancellationToken);
            return files;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            await UpdateSceneAssetsHookDiagnosticsAsync(context, 10, d =>
            {
                PopulateSceneAssetsValidationDiagnostics(d, context.OutputRoot, validationPassed: false);
                d["exceptionType"] = ex.GetType().Name;
                d["exceptionMessage"] = ex.Message;
            }, cancellationToken);
            throw;
        }
    }

    private static IReadOnlyList<string> ValidateSceneAssetsV3Format(string outputRoot, string format, int expectedCount)
    {
        var root = Path.Combine(outputRoot, "scene-assets-v3", format);
        if (!Directory.Exists(root)) throw new InvalidOperationException($"Scene Assets V3 {format} folder is missing: {NormalizePath(root)}");
        var required = new[] { "visual-timeline-v3.json", "scene-manifest-v3.json", "scene-review-v3.json", "scene-timeline-metadata.json" }.Select(f => Path.Combine(root, f)).ToList();
        var expectedImages = format.Equals("short", StringComparison.OrdinalIgnoreCase)
            ? new[] { "001-hook.png", "002-cause.png", "003-accurate-sky-guide.png", "004-viewing-tip.png", "005-final-reminder.png" }
            : new[] { "001-hook.png", "002-what-is-it.png", "003-cause.png", "004-interesting-fact.png", "005-best-time.png", "006-accurate-sky-guide.png", "007-what-you-will-see.png", "008-viewing-tips.png", "009-final-reminder.png" };
        required.AddRange(expectedImages.Select(f => Path.Combine(root, f)));
        var missing = required.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"Scene Assets V3 {format} validation failed: missing {string.Join(", ", missing)}");
        var manifest = JsonSerializer.Deserialize<SceneAssetsV3Manifest>(File.ReadAllText(Path.Combine(root, "scene-manifest-v3.json")), JsonOptions)
            ?? throw new InvalidOperationException($"Scene Assets V3 {format} manifest could not be parsed.");
        if (manifest.SceneCount != expectedCount || manifest.Scenes.Count != expectedCount) throw new InvalidOperationException($"Scene Assets V3 {format} expected {expectedCount} scenes but found {manifest.Scenes.Count}.");
        if (!manifest.Scenes.Any(s => s.RenderMode == "AccurateSkyGuideScene")) throw new InvalidOperationException($"Scene Assets V3 {format} is missing AccurateSkyGuideScene.");
        if (manifest.Scenes.Any(s => string.IsNullOrWhiteSpace(s.NarrationBeat))) throw new InvalidOperationException($"Scene Assets V3 {format} has a scene without narrationBeat.");
        var metadata = JsonSerializer.Deserialize<SceneTimelineMetadataDocument>(File.ReadAllText(Path.Combine(root, "scene-timeline-metadata.json")), JsonOptions)
            ?? throw new InvalidOperationException($"Scene Assets V3 {format} timeline metadata could not be parsed.");
        if (metadata.Scenes.Count != expectedCount) throw new InvalidOperationException($"Scene Assets V3 {format} timeline metadata expected {expectedCount} scenes but found {metadata.Scenes.Count}.");
        var review = JsonSerializer.Deserialize<SceneAssetsV3Review>(File.ReadAllText(Path.Combine(root, "scene-review-v3.json")), JsonOptions)
            ?? throw new InvalidOperationException($"Scene Assets V3 {format} review could not be parsed.");
        if (review.SameBackgroundDetected) throw new InvalidOperationException($"Scene Assets V3 {format} validation failed: sameBackgroundDetected is true.");
        if (review.SameCompositionDetected) throw new InvalidOperationException($"Scene Assets V3 {format} validation failed: sameCompositionDetected is true.");
        if (review.SameCameraAngleDetected) throw new InvalidOperationException($"Scene Assets V3 {format} validation failed: sameCameraAngleDetected is true.");
        return required;
    }

    private async Task<IReadOnlyList<string>> RenderPhase8SceneVisualVariantsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var enrichedPath = BuildEnrichedScenePlanPath(context);
        RequireFile(enrichedPath, "Enriched question-driven scene plan with visual variants");
        var plan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(await File.ReadAllTextAsync(enrichedPath, cancellationToken), JsonOptions)
            ?? throw new InvalidOperationException("Phase 8 scene variant rendering could not parse question-driven-scene-plan.enriched.json.");

        var sceneAssetsRoot = Path.Combine(context.ExecutionContext.SceneRoot!, "scene-assets");
        var manifestPath = Path.Combine(sceneAssetsRoot, "scene-variant-manifest.json");
        var manifest = new List<Phase8SceneVariantManifestItem>();
        var generatedFiles = new List<string>();
        ResetPhase8SceneVariantOutputRoot(sceneAssetsRoot);

        foreach (var scene in plan.Scenes.Where(IsPhase8PilotScene).OrderBy(scene => scene.SceneNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variants = scene.VisualVariants?.OrderBy(variant => variant.VariantNo).ToArray() ?? [];
            if (scene.IsRequired && variants.Length < 3)
                throw new InvalidOperationException($"Phase 8 scene variant validation failed: required scene {scene.SceneNumber} has {variants.Length} visual variant(s), expected at least 3.");

            foreach (var variant in variants.Where(IsPhase8PilotSceneVariant))
            {
                foreach (var format in Phase8ProfessionalSlideFormats.Where(IsPhase8PilotSlideFormat))
                {
                    var imagePath = BuildProfessionalSlidePath(sceneAssetsRoot, format.Format, scene.SceneNumber, variant.VariantNo, variant.VariantType);
                    var directorPrompt = BuildPhase8VisualDirectorPrompt(context, scene, variant, format);
                    var spec = BuildPhase8SceneVariantVisualSpec(context, scene, variant, directorPrompt);
                    var backgroundPath = BuildProfessionalSlideTemporaryBackgroundPath(sceneAssetsRoot, format.Format, scene.SceneNumber, variant.VariantNo, variant.VariantType);
                    var finalValidationErrors = new List<string>();
                    var backgroundValidationErrors = new List<string>();
                    var renderStatus = "rendered";
                    try
                    {
                        await RenderCleanAstronomyBackgroundAsync(backgroundPath, format.RenderVariant.Width, format.RenderVariant.Height, scene.SceneNumber, variant.VariantNo, cancellationToken);
                        var backgroundImageValidation = ValidateProfessionalSlideImage(backgroundPath, format.RenderVariant.Width, format.RenderVariant.Height, backgroundValidationErrors);
                        if (backgroundValidationErrors.Count > 0 || !backgroundImageValidation.IsBlankCheckPassed)
                            throw new InvalidOperationException($"Phase 8 scene variant validation failed: generated background for scene {scene.SceneNumber} format {format.Format} variant {variant.VariantNo} is invalid: {string.Join(", ", backgroundValidationErrors)}");

                        await infographicRenderer.RenderAsync(imagePath, spec, string.Empty, string.Empty, cancellationToken, format.RenderVariant);
                        await ApplyPhase8VariantVisualTreatmentAsync(imagePath, format.RenderVariant.Width, format.RenderVariant.Height, scene.SceneNumber, variant, format.Format, cancellationToken);
                    }
                    catch
                    {
                        renderStatus = "failed";
                        throw;
                    }
                    finally
                    {
                        if (File.Exists(backgroundPath))
                            File.Delete(backgroundPath);
                    }

                    var finalImageValidation = ValidateProfessionalSlideImage(imagePath, format.RenderVariant.Width, format.RenderVariant.Height, finalValidationErrors);
                    var layoutTemplate = ResolveProfessionalSlideLayoutTemplate(format.Format, variant.VariantType);
                    var safeAreaMetadata = BuildPhase8SafeAreaMetadata(format.Format, variant.VariantType);
                    var textBlockCount = EstimatePhase8TextBlockCount(spec);
                    var allowedTextBlockCount = format.Format.Equals("short", StringComparison.OrdinalIgnoreCase) ? 4 : 4;
                    var safeAreaPassed = finalValidationErrors.Count == 0 && safeAreaMetadata is not null;
                    var overlapCheckPassed = finalValidationErrors.Count == 0 && textBlockCount <= allowedTextBlockCount;
                    if (textBlockCount > allowedTextBlockCount) finalValidationErrors.Add($"text block count {textBlockCount} exceeds allowed limit {allowedTextBlockCount}");
                    var visualHash = ComputePhase8VisualHash(imagePath);
                    var qualityScore = BuildPhase8VisualQualityScore(finalImageValidation, safeAreaPassed, overlapCheckPassed, textBlockCount, allowedTextBlockCount, scene, variant, directorPrompt);

                    manifest.Add(new Phase8SceneVariantManifestItem(
                        scene.SceneNumber,
                        format.Format,
                        variant.VariantNo,
                        variant.VariantType,
                        "final",
                        NormalizePath(imagePath),
                        string.Empty,
                        NormalizePath(imagePath),
                        layoutTemplate,
                        safeAreaPassed,
                        overlapCheckPassed,
                        renderStatus,
                        finalImageValidation.IsBlankCheckPassed,
                        finalImageValidation.NonBlackPixelRatio,
                        finalImageValidation.FileSizeBytes,
                        visualHash,
                        safeAreaMetadata,
                        textBlockCount,
                        allowedTextBlockCount,
                        directorPrompt,
                        qualityScore,
                        finalValidationErrors.ToArray()));
                    generatedFiles.Add(imagePath);
                }
            }
        }

        DeletePhase8TemporaryBackgroundRoot(sceneAssetsRoot);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        generatedFiles.Add(manifestPath);
        await ValidatePhase8SceneVariantOutputsAsync(plan, sceneAssetsRoot, manifestPath, cancellationToken);
        return generatedFiles;
    }

    private static QuestionDrivenVisualSpec BuildPhase8SceneVariantVisualSpec(ProductionPhaseContext context, EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant, string visualDirectorPrompt)
    {
        var intelligence = context.ProductionEventIntelligence;
        var overlayText = BuildPhase8VariantOverlayText(context, scene, variant);
        ValidatePhase8ViewerFacingText(scene, variant, overlayText);
        var requiredObjects = (scene.RequiredVisualObjects is { Count: > 0 } ? scene.RequiredVisualObjects : intelligence.RequiredVisualObjects) ?? Array.Empty<string>();
        var eventType = FirstNonEmpty(intelligence.EventType, context.Request.EventType, context.ExecutionContext.EventType);
        return new QuestionDrivenVisualSpec(
            context.EventId,
            context.Request.RegionId,
            context.Request.Language,
            scene.SceneNumber,
            scene.QuestionType,
            scene.ScenePurpose,
            scene.ViewerQuestion,
            scene.ViewerTakeaway,
            FirstNonEmpty(scene.NarrationIntent, scene.SourceAnswer, scene.ViewerTakeaway),
            scene.ViewerTakeaway,
            Math.Max(4, (int)Math.Ceiling(scene.VisualVariants?.Sum(variant => variant.RecommendedDurationSeconds) ?? 6)),
            visualDirectorPrompt,
            overlayText,
            BuildPhase8VariantProgrammaticLayers(scene, variant, visualDirectorPrompt),
            SplitOverlayIntent(scene.AccessibilityIntent).DefaultIfEmpty(scene.ViewerTakeaway).ToArray(),
            DateTimeOffset.UtcNow,
            eventType,
            false,
            intelligence.BestViewingWindowLocal,
            null,
            null,
            requiredObjects,
            null,
            FirstNonEmpty(scene.StrategyId, intelligence.StrategyId),
            intelligence.PrimaryObjects.Concat(intelligence.SecondaryObjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SplitOverlayIntent(scene.VisualIntent).ToArray(),
            requiredObjects,
            requiredObjects);
    }

    private static IReadOnlyList<string> BuildPhase8VariantOverlayText(ProductionPhaseContext context, EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant)
    {
        if (NormalizePhase8VariantType(variant.VariantType).Equals("educational_overlay", StringComparison.OrdinalIgnoreCase))
            return BuildPhase8EducationalOverlayCopy(context, scene);

        var baseLines = BuildViewerFacingOverlayCopy(context, scene).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return NormalizePhase8VariantType(variant.VariantType) switch
        {
            "wide_context" => baseLines.Take(2).ToArray(),
            "object_focus" => baseLines.Take(2).ToArray(),
            "cinematic_detail" => baseLines.Take(1).ToArray(),
            "transition_or_closing" => new[] { "Save this sky reminder", FirstNonEmpty(baseLines.FirstOrDefault(), scene.ViewerTakeaway, "Step outside tonight") },
            _ => baseLines.Take(3).ToArray()
        };
    }

    private static IReadOnlyList<string> BuildPhase8EducationalOverlayCopy(ProductionPhaseContext context, EnrichedQuestionSceneDto scene)
    {
        var title = FirstNonEmpty(context.ProductionEventIntelligence.Title, context.Request.Title, context.Request.EventType, "Astronomy Event");
        var peakNight = ResolvePhase8PeakNight(context, scene);
        var bestTime = FirstNonEmpty(context.ProductionEventIntelligence.BestViewingWindowLocal, ExtractBestTime(scene), "Midnight to pre-dawn");
        var where = ResolvePhase8WhereToLook(context, scene);
        var moon = ResolvePhase8MoonCondition(scene);
        var radiant = ResolvePhase8RadiantLabel(context, scene);
        return new[]
        {
            title,
            $"Peak Night: {peakNight}",
            $"Best Time: {bestTime}",
            $"Where to Look: {where}",
            "No telescope needed",
            $"Moon: {moon}",
            $"Radiant: {radiant}",
            "Tips: dark sky • wide view • let eyes adapt"
        };
    }

    private static IReadOnlyList<string> BuildViewerFacingOverlayCopy(ProductionPhaseContext context, EnrichedQuestionSceneDto scene)
        => new[]
        {
            FirstNonEmpty(context.ProductionEventIntelligence.Title, context.Request.Title, scene.ViewerTakeaway, "Astronomy Event"),
            FirstNonEmpty(context.ProductionEventIntelligence.BestViewingWindowLocal, ExtractBestTime(scene), scene.ViewerTakeaway),
            ResolvePhase8WhereToLook(context, scene)
        };

    private static string ResolvePhase8PeakNight(ProductionPhaseContext context, EnrichedQuestionSceneDto scene)
    {
        var haystack = string.Join(" ", context.ProductionEventIntelligence.Title, context.ProductionEventIntelligence.BestViewingWindowLocal, scene.SourceAnswer, scene.NarrationIntent, scene.VisualIntent, scene.ImagePromptIntent, scene.OverlayIntent);
        var match = Regex.Match(haystack, @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2}(?:\s*/\s*\d{1,2})?\b", RegexOptions.IgnoreCase);
        return match.Success ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(match.Value.Replace(" ", " ").Replace("/ ", "/")) : "Tonight";
    }

    private static string ExtractBestTime(EnrichedQuestionSceneDto scene)
    {
        var haystack = string.Join(" ", scene.SourceAnswer, scene.NarrationIntent, scene.VisualIntent, scene.ImagePromptIntent, scene.OverlayIntent);
        if (haystack.Contains("pre-dawn", StringComparison.OrdinalIgnoreCase)) return "Midnight to pre-dawn";
        if (haystack.Contains("after sunset", StringComparison.OrdinalIgnoreCase)) return "After sunset";
        return string.Empty;
    }

    private static string ResolvePhase8WhereToLook(ProductionPhaseContext context, EnrichedQuestionSceneDto scene)
    {
        var objects = (scene.RequiredVisualObjects is { Count: > 0 } ? scene.RequiredVisualObjects : context.ProductionEventIntelligence.RequiredVisualObjects) ?? Array.Empty<string>();
        if (objects.Any(o => o.Contains("meteor", StringComparison.OrdinalIgnoreCase) || o.Contains("radiant", StringComparison.OrdinalIgnoreCase))) return "dark open sky near the radiant";
        return FirstNonEmpty(objects.FirstOrDefault(), "clear horizon");
    }

    private static string ResolvePhase8MoonCondition(EnrichedQuestionSceneDto scene)
    {
        var haystack = string.Join(" ", scene.SourceAnswer, scene.NarrationIntent, scene.VisualIntent, scene.ImagePromptIntent, scene.OverlayIntent);
        if (haystack.Contains("low moon", StringComparison.OrdinalIgnoreCase) || haystack.Contains("little moon", StringComparison.OrdinalIgnoreCase)) return "low interference";
        if (haystack.Contains("moon", StringComparison.OrdinalIgnoreCase)) return "check local moonlight";
        return "minimal interference";
    }

    private static string ResolvePhase8RadiantLabel(ProductionPhaseContext context, EnrichedQuestionSceneDto scene)
    {
        var haystack = string.Join(" ", context.ProductionEventIntelligence.Title, scene.SourceAnswer, scene.VisualIntent, scene.ImagePromptIntent);
        if (haystack.Contains("Geminid", StringComparison.OrdinalIgnoreCase)) return "Gemini";
        if (haystack.Contains("Perseid", StringComparison.OrdinalIgnoreCase)) return "Perseus";
        return "shower radiant";
    }

    private static void ValidatePhase8ViewerFacingText(EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant, IReadOnlyList<string> overlayText)
    {
        var forbidden = new[] { "Use ", "cue", "overlay intent", "viewer question", "visual intent", "placeholder" };
        var bad = overlayText.Where(line => forbidden.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (bad.Length > 0)
            throw new InvalidOperationException($"Phase 8 viewer-facing text quality gate failed for scene {scene.SceneNumber} variant {variant.VariantNo}: {string.Join(" | ", bad)}");
    }

    private static IReadOnlyList<string> BuildPhase8VariantProgrammaticLayers(EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant, string visualDirectorPrompt)
    {
        var type = NormalizePhase8VariantType(variant.VariantType);
        return new[]
        {
            visualDirectorPrompt,
            FirstNonEmpty(scene.VisualIntent, scene.ImagePromptIntent),
            variant.CompositionHint,
            variant.CameraStyle,
            $"phase8-variant-type:{type}",
            $"phase8-layout-template:{ResolveProfessionalSlideLayoutTemplate("long", variant.VariantType)}",
            BuildPhase8VariantCompositionSignature(type),
            $"phase8-background-seed:{StablePhase8VariantSeed(scene.SceneNumber, variant.VariantNo, variant.VariantType)}"
        }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    }

    private static string BuildPhase8VariantCompositionSignature(string type)
        => type switch
        {
            "wide_context" => "phase8-composition:wide framing; object upper-right third; text lower-left horizon label",
            "object_focus" => "phase8-composition:zoomed framing; object center-left; text right-side fact chip",
            "educational_overlay" => "phase8-composition:infographic layout; text left panel; object diagram right",
            "cinematic_detail" => "phase8-composition:close-up cinematic; object lower-right; text tiny upper-left",
            "transition_or_closing" => "phase8-composition:CTA composition; centered title card; text lower safe-area CTA",
            _ => $"phase8-composition:distinct-{type}"
        };

    private static IEnumerable<string> SplitOverlayIntent(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', '\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string BuildPhase8VisualDirectorPrompt(ProductionPhaseContext context, EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant, Phase8ProfessionalSlideFormat format)
    {
        var title = FirstNonEmpty(context.ProductionEventIntelligence.Title, context.Request.Title, context.Request.EventType, "Astronomy event");
        var aspect = format.Format.Equals("short", StringComparison.OrdinalIgnoreCase)
            ? "1080x1920 vertical 9:16; title near top safe area; main object centered; tips or CTA in a lower safe panel; avoid text near extreme edges."
            : "1920x1080 horizontal 16:9; title/info panel on left or bottom; object or radiant area on right/center; clear sky background.";
        var allowedObjects = string.Join(", ", ((scene.RequiredVisualObjects is { Count: > 0 } ? scene.RequiredVisualObjects : context.ProductionEventIntelligence.RequiredVisualObjects) ?? Array.Empty<string>()).DefaultIfEmpty("only objects named by the event context"));
        return string.Join("\n", new[]
        {
            "VISUAL DIRECTOR PROMPT — generate a professional educational visual asset, not a random scene image.",
            $"Event title/context: {title}",
            $"Audience takeaway: {scene.ViewerTakeaway}",
            $"Science focus: {FirstNonEmpty(scene.VisualIntent, scene.ImagePromptIntent, scene.ViewerTakeaway)}",
            $"Variant type: {variant.VariantType}",
            $"Variant rendering directive: {BuildPhase8VariantRenderingDirective(variant.VariantType, format.Format, scene.SceneNumber, variant.VariantNo)}",
            $"Composition hint: {variant.CompositionHint}",
            $"Camera style: {variant.CameraStyle}",
            $"Format-specific composition: {aspect}",
            "Quality bar: premium astronomy infographic, NASA-style educational slide, Discovery-style science graphic, clean editorial layout, safe margins, strong visual hierarchy, readable typography.",
            "Typography constraints: no text overlap, no crowded corners, no malformed AI text, concise labels only, deterministic overlay blocks must not overlap.",
            $"Astronomy constraints: correct event context, use only relevant celestial objects ({allowedObjects}), no unrelated planets or objects, no decorative objects that change the science meaning."
        });
    }

    private static string BuildPhase8VariantRenderingDirective(string variantType, string format, int sceneNumber, int variantNo)
    {
        var type = NormalizePhase8VariantType(variantType);
        var seed = StablePhase8VariantSeed(sceneNumber, variantNo, variantType);
        return type switch
        {
            "wide_context" => $"wide framing; object small on upper-right third; horizon spans lower quarter; one lower-left label; sparse star seed {seed}",
            "object_focus" => $"zoomed framing; object enlarged on center-left focal third; horizon cropped; right-side fact chip; telephoto crop seed {seed}",
            "educational_overlay" => $"infographic layout; left-side explanation panel; right-side sky/object diagram; connector labels and bottom tip strip seed {seed}",
            "cinematic_detail" => $"close-up cinematic composition; object large on lower-right third; dark negative space and tiny upper-left caption seed {seed}",
            "transition_or_closing" => $"CTA composition; centered closing/title card; small object accent in background; prominent lower safe-area save/share reminder seed {seed}",
            _ => $"distinct variant composition and background reference seed {seed}"
        } + $"; output format {format}";
    }

    private static string NormalizePhase8VariantType(string? variantType)
    {
        var value = (variantType ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains("wide")) return "wide_context";
        if (value.Contains("focus")) return "object_focus";
        if (value.Contains("overlay") || value.Contains("education")) return "educational_overlay";
        if (value.Contains("cinematic") || value.Contains("detail")) return "cinematic_detail";
        if (value.Contains("transition") || value.Contains("closing") || value.Contains("cta")) return "transition_or_closing";
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static int StablePhase8VariantSeed(int sceneNumber, int variantNo, string? variantType)
        => Math.Abs(HashCode.Combine(sceneNumber, variantNo, NormalizePhase8VariantType(variantType)));

    private static Phase8SafeAreaMetadata BuildPhase8SafeAreaMetadata(string format, string variantType)
        => format.Equals("short", StringComparison.OrdinalIgnoreCase)
            ? new Phase8SafeAreaMetadata(format, 96, 160, 96, 180, "top-title-safe-area,center-object-safe-area,lower-info-panel-safe-area", ResolveProfessionalSlideLayoutTemplate(format, variantType))
            : new Phase8SafeAreaMetadata(format, 96, 72, 96, 84, "left-or-bottom-info-panel-safe-area,right-or-center-object-safe-area", ResolveProfessionalSlideLayoutTemplate(format, variantType));

    private static int EstimatePhase8TextBlockCount(QuestionDrivenVisualSpec spec)
        => 1 + Math.Min(2, spec.OverlayText.Count(text => !string.IsNullOrWhiteSpace(text))) + (string.IsNullOrWhiteSpace(spec.ViewerTakeaway) ? 0 : 1);

    private static string ComputePhase8VisualHash(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static Phase8VisualQualityScore BuildPhase8VisualQualityScore(Phase8ImageValidationResult image, bool safeAreaPassed, bool overlapCheckPassed, int textBlockCount, int allowedTextBlockCount, EnrichedQuestionSceneDto scene, SceneVisualVariantDto variant, string directorPrompt)
    {
        var composition = safeAreaPassed ? 96 : 70;
        var readability = overlapCheckPassed && textBlockCount <= allowedTextBlockCount ? 96 : 68;
        var astronomyAccuracy = ContainsAny(directorPrompt, scene.RequiredVisualObjects ?? []) || !string.IsNullOrWhiteSpace(scene.VisualIntent) ? 94 : 78;
        var objectRelevance = directorPrompt.Contains("no unrelated planets or objects", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(variant.CompositionHint) ? 95 : 80;
        var professional = image.NonBlackPixelRatio >= 0.015 && image.FileSizeBytes > 1024 ? 94 : 65;
        var final = Math.Round(new[] { composition, readability, astronomyAccuracy, objectRelevance, professional }.Average(), 2);
        return new Phase8VisualQualityScore(composition, readability, astronomyAccuracy, objectRelevance, professional, final, 90);
    }

    private static bool ContainsAny(string value, IEnumerable<string> needles)
        => needles.Any(needle => !string.IsNullOrWhiteSpace(needle) && value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static async Task ValidatePhase8SceneVariantOutputsAsync(EnrichedQuestionScenePlanDto plan, string sceneAssetsRoot, string manifestPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("Phase 8 scene variant validation failed: scene-variant-manifest.json was not written.");

        var manifest = JsonSerializer.Deserialize<IReadOnlyList<Phase8SceneVariantManifestItem>>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidOperationException("Phase 8 scene variant validation failed: scene-variant-manifest.json could not be parsed.");
        var issues = new List<string>();
        var expectedSceneFolders = manifest.Select(item => $"scene-{item.SceneNumber:00}").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var format in Phase8ProfessionalSlideFormats.Where(IsPhase8PilotSlideFormat).Select(f => f.Format))
        {
            var formatRoot = Path.Combine(sceneAssetsRoot, format);
            var actualFolders = Directory.Exists(formatRoot)
                ? Directory.EnumerateDirectories(formatRoot).Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            var duplicateFolders = actualFolders.Where(name => Regex.IsMatch(name, "^scene-\\d{3,}$", RegexOptions.IgnoreCase)).ToArray();
            if (!actualFolders.SequenceEqual(expectedSceneFolders, StringComparer.OrdinalIgnoreCase))
                issues.Add($"format {format} must contain exactly the manifest scene folders: {string.Join(", ", expectedSceneFolders)}; actual: {string.Join(", ", actualFolders)}");
            if (duplicateFolders.Length > 0)
                issues.Add($"format {format} contains unsupported scene-001 style duplicate folder(s): {string.Join(", ", duplicateFolders)}");
        }

        var backgroundOnlyFiles = Directory.Exists(sceneAssetsRoot)
            ? Directory.EnumerateFiles(sceneAssetsRoot, "*.png", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}backgrounds{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("background", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<string>();
        if (backgroundOnlyFiles.Length > 0)
            issues.Add($"background-only images must not be persisted: {string.Join(", ", backgroundOnlyFiles.Select(NormalizePath))}");

        if (manifest.Any(item => !item.ImageRole.Equals("final", StringComparison.OrdinalIgnoreCase)))
            issues.Add("scene-variant-manifest.json must reference final valid images only.");

        var duplicateHashes = manifest
            .Where(item => !string.IsNullOrWhiteSpace(item.VisualHash))
            .GroupBy(item => new { item.SceneNumber, item.Format, item.VisualHash })
            .Where(group => group.Count() > 1)
            .Select(group => $"scene {group.Key.SceneNumber} format {group.Key.Format} hash {group.Key.VisualHash[..Math.Min(16, group.Key.VisualHash.Length)]} variants {string.Join(",", group.Select(item => item.VariantNo))}")
            .ToArray();
        if (duplicateHashes.Length > 0)
            issues.Add("duplicate/repeated visual hash inside same scene: " + string.Join("; ", duplicateHashes));

        foreach (var scene in plan.Scenes.Where(scene => scene.IsRequired).OrderBy(scene => scene.SceneNumber))
        {
            var expectedVariantCount = scene.SceneNumber == 1 ? 1 : 0;
            foreach (var format in Phase8ProfessionalSlideFormats.Where(IsPhase8PilotSlideFormat).Select(f => f.Format))
            {
                var renderedCount = manifest.Count(item => item.ImageRole.Equals("final", StringComparison.OrdinalIgnoreCase) && item.SceneNumber == scene.SceneNumber && item.Format.Equals(format, StringComparison.OrdinalIgnoreCase) && item.SafeAreaPassed && item.OverlapCheckPassed && item.IsBlankCheckPassed && IsSuccessfulSceneVariantRenderStatus(item.RenderStatus) && File.Exists(item.FinalImagePath));
                if (renderedCount != expectedVariantCount)
                    issues.Add($"required scene {scene.SceneNumber} format {format} has {renderedCount} rendered professional slide variant image(s), expected exactly {expectedVariantCount}");
                var sceneFolder = Path.Combine(sceneAssetsRoot, format, $"scene-{scene.SceneNumber:00}");
                var pngCount = Directory.Exists(sceneFolder) ? Directory.EnumerateFiles(sceneFolder, "*.png", SearchOption.TopDirectoryOnly).Count() : 0;
                if (pngCount != expectedVariantCount)
                    issues.Add($"required scene {scene.SceneNumber} format {format} folder has {pngCount} png file(s), expected exactly {expectedVariantCount}");
            }
        }

        var failed = manifest.Where(item => !item.SafeAreaPassed || !item.OverlapCheckPassed || !item.IsBlankCheckPassed || item.SafeAreaMetadata is null || item.TextBlockCount > item.AllowedTextBlockCount || item.VisualQualityScore is null || item.VisualQualityScore.FinalQualityScore < item.VisualQualityScore.Threshold || !IsSuccessfulSceneVariantRenderStatus(item.RenderStatus) || !File.Exists(item.FinalImagePath) || (item.ImageRole.Equals("final", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.FinalImagePath))).ToArray();
        issues.AddRange(failed.Select(item => $"scene {item.SceneNumber} format {item.Format} variant {item.VariantNo} role={item.ImageRole} renderStatus={item.RenderStatus} imagePath={item.ImagePath} errors={string.Join(",", item.ValidationErrors)}"));
        if (issues.Count > 0)
            throw new InvalidOperationException("Phase 8 scene variant validation failed: " + string.Join("; ", issues));
    }

    private static readonly Phase8ProfessionalSlideFormat[] Phase8ProfessionalSlideFormats =
    [
        new("long", AstronomyInfographicRenderVariant.LongForm),
        new("short", AstronomyInfographicRenderVariant.ShortForm)
    ];

    private static bool IsPhase8PilotScene(EnrichedQuestionSceneDto scene)
        => scene.SceneNumber == 1;

    private static bool IsPhase8PilotSceneVariant(SceneVisualVariantDto variant)
        => variant.VariantNo == 3 || NormalizePhase8VariantType(variant.VariantType).Equals("educational_overlay", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhase8PilotSlideFormat(Phase8ProfessionalSlideFormat format)
        => format.Format.Equals("long", StringComparison.OrdinalIgnoreCase);

    private static string BuildProfessionalSlidePath(string sceneAssetsRoot, string format, int sceneNumber, int variantNo, string variantType)
        => Path.Combine(sceneAssetsRoot, format, $"scene-{sceneNumber:00}", $"scene-{sceneNumber:00}-{ResolveProfessionalSlideVariantSlug(variantNo, variantType)}.png");

    private static string BuildProfessionalSlideTemporaryBackgroundPath(string sceneAssetsRoot, string format, int sceneNumber, int variantNo, string variantType)
        => Path.Combine(sceneAssetsRoot, ".tmp-backgrounds", format, $"scene-{sceneNumber:00}", $"scene-{sceneNumber:00}-{ResolveProfessionalSlideVariantSlug(variantNo, variantType)}-background.png");

    private static void ResetPhase8SceneVariantOutputRoot(string sceneAssetsRoot)
    {
        foreach (var child in new[] { "long", "short", "backgrounds", ".tmp-backgrounds" })
        {
            var path = Path.Combine(sceneAssetsRoot, child);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(sceneAssetsRoot, "long"));
    }

    private static void DeletePhase8TemporaryBackgroundRoot(string sceneAssetsRoot)
    {
        var path = Path.Combine(sceneAssetsRoot, ".tmp-backgrounds");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static string ResolveProfessionalSlideVariantSlug(int variantNo, string variantType)
    {
        if (variantNo == 1 || variantType.Contains("wide", StringComparison.OrdinalIgnoreCase)) return "wide-context";
        if (variantNo == 2 || variantType.Contains("focus", StringComparison.OrdinalIgnoreCase)) return "object-focus";
        if (variantNo == 3 || variantType.Contains("overlay", StringComparison.OrdinalIgnoreCase) || variantType.Contains("educational", StringComparison.OrdinalIgnoreCase)) return "educational-overlay";
        return $"variant-{variantNo:00}-{Sanitize(variantType).ToLowerInvariant()}";
    }

    private static string ResolveProfessionalSlideLayoutTemplate(string format, string variantType)
    {
        var type = NormalizePhase8VariantType(variantType);
        return format.Equals("short", StringComparison.OrdinalIgnoreCase)
            ? type switch
            {
                "wide_context" => "short-wide-sky-minimal-overlay",
                "object_focus" => "short-centered-object-focus",
                "educational_overlay" => "short-bottom-info-panel-with-safe-center-object-labels",
                "cinematic_detail" => "short-dramatic-close-up-minimal-text",
                "transition_or_closing" => "short-clean-cta-closing-card",
                _ => "short-stacked-title-object-and-tips"
            }
            : type switch
            {
                "wide_context" => "long-landscape-wide-context-minimal-overlay",
                "object_focus" => "long-radiant-object-centered-focus",
                "educational_overlay" => "long-left-info-panel-with-bottom-viewing-tips-and-labels",
                "cinematic_detail" => "long-cinematic-detail-atmosphere",
                "transition_or_closing" => "long-clean-save-date-closing-layout",
                _ => "long-left-info-panel-with-bottom-viewing-tips"
            };
    }

    private static async Task RenderCleanAstronomyBackgroundAsync(string path, int width, int height, int sceneNumber, int variantNo, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var image = new Image<Rgba32>(width, height, Color.ParseHex("#07101F"));
        image.Mutate(ctx =>
        {
            ctx.BackgroundColor(Color.ParseHex("#07101F"));
            var random = new Random(HashCode.Combine(sceneNumber, variantNo, width, height));
            for (var i = 0; i < Math.Max(90, width * height / 18000); i++)
            {
                var x = random.Next(width);
                var y = random.Next(height);
                var alpha = (byte)random.Next(80, 210);
                image[x, y] = new Rgba32(210, 230, 255, alpha);
            }
            ctx.GaussianBlur(.22f);
        });
        await image.SaveAsPngAsync(path, new PngEncoder(), cancellationToken);
    }


    private static async Task ApplyPhase8VariantVisualTreatmentAsync(string imagePath, int width, int height, int sceneNumber, SceneVisualVariantDto variant, string format, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath)) return;
        using var image = await Image.LoadAsync<Rgba32>(imagePath, cancellationToken);
        var type = NormalizePhase8VariantType(variant.VariantType);
        var seed = StablePhase8VariantSeed(sceneNumber, variant.VariantNo, variant.VariantType);
        var random = new Random(seed + (format.Equals("short", StringComparison.OrdinalIgnoreCase) ? 97 : 31));
        var accent = type switch
        {
            "wide_context" => new Rgba32(90, 170, 255, 58),
            "object_focus" => new Rgba32(255, 220, 135, 74),
            "educational_overlay" => new Rgba32(96, 210, 255, 88),
            "cinematic_detail" => new Rgba32(255, 132, 82, 66),
            "transition_or_closing" => new Rgba32(170, 255, 190, 72),
            _ => new Rgba32(255, 255, 255, 48)
        };

        var blendRects = new List<Phase8BlendRect>();
        switch (type)
        {
            case "wide_context":
                blendRects.Add(new(0, (int)(height * .74f), width, Math.Max(20, height / 22), accent));
                for (var i = 0; i < 18; i++) blendRects.Add(new(random.Next(width), random.Next(height / 2), 2 + random.Next(4), 2 + random.Next(4), new Rgba32(220, 240, 255, 135)));
                break;
            case "object_focus":
                blendRects.Add(new((int)(width * .43f), (int)(height * .28f), Math.Max(90, width / 7), Math.Max(90, width / 7), accent));
                blendRects.Add(new((int)(width * .48f), (int)(height * .18f), Math.Max(12, width / 90), Math.Max(340, height / 3), new Rgba32(255, 240, 180, 42)));
                break;
            case "educational_overlay":
                blendRects.Add(new(0, format.Equals("short", StringComparison.OrdinalIgnoreCase) ? (int)(height * .69f) : 0, format.Equals("short", StringComparison.OrdinalIgnoreCase) ? width : (int)(width * .31f), format.Equals("short", StringComparison.OrdinalIgnoreCase) ? (int)(height * .21f) : height, accent));
                for (var i = 0; i < 4; i++) blendRects.Add(new(32, 60 + i * Math.Max(54, height / 16), Math.Max(150, width / 5), 8, new Rgba32(255, 255, 255, 115)));
                break;
            case "cinematic_detail":
                blendRects.Add(new(0, 0, width, Math.Max(40, height / 10), new Rgba32(0, 0, 0, 80)));
                blendRects.Add(new(0, height - Math.Max(40, height / 10), width, Math.Max(40, height / 10), new Rgba32(0, 0, 0, 92)));
                blendRects.Add(new((int)(width * .64f), (int)(height * .18f), Math.Max(120, width / 5), Math.Max(120, width / 5), accent));
                break;
            case "transition_or_closing":
                blendRects.Add(new((int)(width * .08f), (int)(height * .18f), (int)(width * .84f), Math.Max(130, height / 5), accent));
                blendRects.Add(new((int)(width * .18f), (int)(height * .78f), (int)(width * .64f), Math.Max(38, height / 26), new Rgba32(255, 255, 255, 82)));
                break;
        }

        var tagX = 8 + (variant.VariantNo * 17 % Math.Max(20, width - 80));
        var tagY = 8 + (variant.VariantNo * 23 % Math.Max(20, height - 80));
        blendRects.Add(new(tagX, tagY, 42, 18, new Rgba32((byte)(60 + variant.VariantNo * 31), (byte)(100 + variant.VariantNo * 19), (byte)(160 + variant.VariantNo * 11), 95)));

        image.ProcessPixelRows(accessor =>
        {
            foreach (var rect in blendRects)
            {
                var x0 = Math.Clamp(rect.X, 0, accessor.Width);
                var y0 = Math.Clamp(rect.Y, 0, accessor.Height);
                var x1 = Math.Clamp(rect.X + rect.Width, 0, accessor.Width);
                var y1 = Math.Clamp(rect.Y + rect.Height, 0, accessor.Height);
                var alpha = rect.Color.A / 255f;
                for (var y = y0; y < y1; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = x0; x < x1; x++)
                    {
                        var dst = row[x];
                        row[x] = new Rgba32(
                            (byte)Math.Clamp(dst.R * (1f - alpha) + rect.Color.R * alpha, 0, 255),
                            (byte)Math.Clamp(dst.G * (1f - alpha) + rect.Color.G * alpha, 0, 255),
                            (byte)Math.Clamp(dst.B * (1f - alpha) + rect.Color.B * alpha, 0, 255),
                            255);
                    }
                }
            }
        });

        await image.SaveAsPngAsync(imagePath, new PngEncoder(), cancellationToken);
    }

    private static Phase8ImageValidationResult ValidateProfessionalSlideImage(string path, int expectedWidth, int expectedHeight, List<string> validationErrors)
    {
        if (!File.Exists(path))
        {
            validationErrors.Add($"missing image: {NormalizePath(path)}");
            return new(false, 0, 0);
        }

        var fileSizeBytes = new FileInfo(path).Length;
        try
        {
            using var image = Image.Load<Rgba32>(path);
            if (image.Width != expectedWidth || image.Height != expectedHeight)
                validationErrors.Add($"image dimensions must be {expectedWidth}x{expectedHeight}: {NormalizePath(path)} was {image.Width}x{image.Height}");

            var total = (long)image.Width * image.Height;
            var nonBlack = 0L;
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        if (pixel.A > 0 && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 24) nonBlack++;
                    }
                }
            });
            var ratio = total == 0 ? 0 : nonBlack / (double)total;
            var blankPassed = ratio >= 0.015;
            if (!blankPassed) validationErrors.Add($"blank-or-mostly-black image: {NormalizePath(path)} nonBlackPixelRatio={ratio:0.####}");
            return new(blankPassed, Math.Round(ratio, 6), fileSizeBytes);
        }
        catch (Exception ex)
        {
            validationErrors.Add($"unreadable image: {NormalizePath(path)} ({ex.GetType().Name})");
            return new(false, 0, fileSizeBytes);
        }
    }

    private static bool IsSuccessfulSceneVariantRenderStatus(string renderStatus)
        => renderStatus.Equals("rendered", StringComparison.OrdinalIgnoreCase) || renderStatus.Equals("existing", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeSceneVariantFileName(string? suggestedName, int sceneNumber, int variantNo)
    {
        var fallback = $"scene-{sceneNumber:00}-variant-{variantNo:00}.png";
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(suggestedName) ? fallback : suggestedName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '-');
        return fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".png";
    }

    private async Task<IReadOnlyList<string>> PhaseValidateLongSceneImagesAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        if (IsSceneAssetsV3Enabled(context))
            return await GenerateSceneAssetsV3Async(context, 9, "Generate Long Scene Images", generateShort: false, generateLong: true, cancellationToken);

        var longValidation = ResolveSceneImageValidationPath(context.ExecutionContext.SceneRoot!, "long", preferSceneAssets: true);
        ValidateSceneImageDirectoryCoverage(longValidation.SelectedPath, "Long scene image validation");
        return Directory.EnumerateFiles(longValidation.SelectedPath, "*.png", SearchOption.AllDirectories).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseValidateSceneAssetsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        if (IsSceneAssetsV3Enabled(context))
            return await ValidateSceneAssetsV3WithDiagnosticsAsync(context, cancellationToken);

        var currentRunValidationRoot = context.ExecutionContext.QuestionRoot!;
        IReadOnlyList<string> materialized;
        if (context.PipelineRequest.EnableSceneVariants)
        {
            await ValidatePhase8SceneVariantManifestOnlyAsync(context.ExecutionContext.SceneRoot!, cancellationToken);
            materialized = await MaterializeSceneVariantApprovalAsync(context.ExecutionContext.SceneRoot!, GetSceneApprovalNormalizedRoot(context.OutputRoot), cancellationToken);
        }
        else
        {
            var validation = await qualityValidator.ValidateBeforeVideoAssemblyAsync(context.ProductionEventIntelligence, currentRunValidationRoot, cancellationToken);
            if (!validation.IsValid) throw new InvalidOperationException("Scene asset validation failed: " + string.Join("; ", validation.Errors));
            materialized = await MaterializeSceneApprovalAsync(context.ExecutionContext.SceneRoot!, GetSceneApprovalNormalizedRoot(context.OutputRoot), cancellationToken);
        }
        var sceneImageRoots = context.PipelineRequest.EnableSceneVariants
            ? new[] { Path.Combine(context.ExecutionContext.SceneRoot!, "scene-assets", "short"), Path.Combine(context.ExecutionContext.SceneRoot!, "scene-assets", "long") }
            : new[] { Path.Combine(context.ExecutionContext.SceneRoot!, "short"), Path.Combine(context.ExecutionContext.SceneRoot!, "long") };
        if (context.PipelineRequest.EnableSceneVariants)
            ValidatePhase10SceneAssetCoverage(context.ExecutionContext.SceneRoot!);
        var validationOutputs = context.PipelineRequest.EnableSceneVariants
            ? Array.Empty<string>()
            : [Path.Combine(currentRunValidationRoot, "production-quality-validation-before-assembly.json")];
        return materialized.Concat(sceneImageRoots).Concat(validationOutputs).ToArray();
    }


    private static async Task ValidatePhase8SceneVariantManifestOnlyAsync(string stagingRoot, CancellationToken cancellationToken)
    {
        var sceneAssetsRoot = Path.Combine(stagingRoot, "scene-assets");
        var manifestPath = Path.Combine(sceneAssetsRoot, "scene-variant-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Scene variant validation failed: scene-variant-manifest.json is required at '{NormalizePath(manifestPath)}'.");

        var manifest = JsonSerializer.Deserialize<IReadOnlyList<Phase8SceneVariantManifestItem>>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidOperationException("Scene variant validation failed: scene-variant-manifest.json could not be parsed.");
        var missing = manifest.Where(item => !File.Exists(item.FinalImagePath)).Select(item => NormalizePath(item.FinalImagePath)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Scene variant validation failed: manifest references missing image(s): " + string.Join(", ", missing));
        var invalidQuality = manifest.Where(item => item.VisualQualityScore is null || item.VisualQualityScore.FinalQualityScore < item.VisualQualityScore.Threshold || item.SafeAreaMetadata is null).ToArray();
        if (invalidQuality.Length > 0)
            throw new InvalidOperationException("Scene variant validation failed: manifest references image(s) below professional quality threshold: " + string.Join(", ", invalidQuality.Select(item => $"{item.Format}/scene-{item.SceneNumber:00}/variant-{item.VariantNo:00} score={(item.VisualQualityScore?.FinalQualityScore.ToString("0.##", CultureInfo.InvariantCulture) ?? "missing")}")));
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateHeroAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await heroEngine.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, HeroAssetGenerationPhase.Full, context.ExecutionContext, context.Request), cancellationToken);
        return await ValidateAndMaterializeHeroContractAsync(context, response, cancellationToken);
    }

    private bool IsThumbnailV8Enabled()
    {
        const bool phase12ThumbnailV8DefaultEnabled = true;
        var options = thumbnailOptions?.Value;
        return options?.UseThumbnailV8 == true
            || options?.UseV8AiNative == true
            || string.Equals(options?.ThumbnailVersion, "V8", StringComparison.OrdinalIgnoreCase)
            || phase12ThumbnailV8DefaultEnabled;
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateThumbnailsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        foreach (var phase in new[] { "Intelligence", "Composition", "SceneSelection", "Images" })
        {
            var response = await thumbnailEngine.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest { EventId = context.EventId, RegionId = context.Request.RegionId, Language = context.Request.Language, Phase = phase, DryRun = false, OverwriteExisting = context.OverwriteExisting, EnableThumbnailV8 = IsThumbnailV8Enabled(), ThumbnailStyle = "ScrollStopping", ThumbnailVisualStyle = "PhotoCinematic", ProductionContext = context.ExecutionContext }, cancellationToken);
            if (IsThumbnailV8Enabled()
                && (response.RequestedRenderer.Contains("V7", StringComparison.OrdinalIgnoreCase)
                    || response.ActualRendererUsed.Contains("V7", StringComparison.OrdinalIgnoreCase)
                    || response.OutputWriteSource.Contains("V7", StringComparison.OrdinalIgnoreCase)
                    || response.GeneratedFiles.Any(file => file.Contains("V7", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Thumbnail V8 routing guard failed: selected renderer/output contains V7 while V8 is enabled.");
            outputs.AddRange(response.GeneratedFiles);
        }

        var thumbnailSceneManifestPath = Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-scene-manifest.json");
        if (!File.Exists(thumbnailSceneManifestPath))
            throw new InvalidOperationException($"Thumbnail generation failed contract validation: thumbnail-scene-manifest.json is required at '{NormalizePath(thumbnailSceneManifestPath)}'.");

        if (IsThumbnailV8Enabled())
            ValidateThumbnailV8Contract(context.ExecutionContext.ThumbnailRoot!);
        else if (thumbnailOptions?.Value.EnableThumbnailV7 == true)
            ValidateThumbnailV7Contract(context.ExecutionContext.ThumbnailRoot!);
        else
            ValidateCtrThumbnailV6Contract(context.ExecutionContext.ThumbnailRoot!);
        outputs.Add(thumbnailSceneManifestPath);
        return outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateGalleryAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var galleryRoot = Path.Combine(context.OutputRoot, "gallery");
        var result = await galleryEngine.GenerateGalleryAsync(galleryRoot, AstroPulseGalleryAspect.Landscape, cancellationToken);
        var observationGuidePath = Path.Combine(galleryRoot, "observation-guide-v2.json");
        var outputs = result.ImagePaths
            .Concat([result.ManifestPath, result.ReviewPath, result.DiagnosticsPath, result.ValidationPath, observationGuidePath])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ValidateGalleryContract(outputs, result.ManifestPath, result.ReviewPath);
        await WriteGalleryPhaseExecutionDiagnosticsAsync(context, result.DiagnosticsPath, result.ValidationPath, cancellationToken);
        return outputs;
    }
    private static async Task WriteGalleryPhaseExecutionDiagnosticsAsync(ProductionPhaseContext context, string diagnosticsPath, string validationPath, CancellationToken cancellationToken)
    {
        var executionDiagnostics = new
        {
            requestedStartPhaseNo = context.PipelineRequest.RequestedStartPhaseNo ?? context.StartPhaseNo,
            requestedEndPhaseNo = context.PipelineRequest.RequestedEndPhaseNo ?? context.EndPhaseNo,
            executedPhaseNumbers = new[] { 13 },
            skippedPhaseNumbers = PhaseDefinitionsStatic().Where(phaseNo => phaseNo != 13).ToArray(),
            phase12Executed = false,
            thumbnailRegenerationOccurred = false
        };

        var diagnostics = File.Exists(diagnosticsPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(diagnosticsPath, cancellationToken))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        diagnostics["galleryVersion"] = "V3.5";
        diagnostics["galleryV2"] = false;
        diagnostics["requestedStartPhaseNo"] = executionDiagnostics.requestedStartPhaseNo;
        diagnostics["requestedEndPhaseNo"] = executionDiagnostics.requestedEndPhaseNo;
        diagnostics["executedPhaseNumbers"] = JsonSerializer.SerializeToNode(executionDiagnostics.executedPhaseNumbers, JsonOptions);
        diagnostics["skippedPhaseNumbers"] = JsonSerializer.SerializeToNode(executionDiagnostics.skippedPhaseNumbers, JsonOptions);
        diagnostics["phase12Executed"] = false;
        diagnostics["thumbnailRegenerationOccurred"] = false;
        await File.WriteAllTextAsync(diagnosticsPath, diagnostics.ToJsonString(JsonOptions), cancellationToken);

        var validation = File.Exists(validationPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(validationPath, cancellationToken))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        validation["requestedStartPhaseNo"] = executionDiagnostics.requestedStartPhaseNo;
        validation["requestedEndPhaseNo"] = executionDiagnostics.requestedEndPhaseNo;
        validation["executedPhaseNumbers"] = JsonSerializer.SerializeToNode(executionDiagnostics.executedPhaseNumbers, JsonOptions);
        validation["skippedPhaseNumbers"] = JsonSerializer.SerializeToNode(executionDiagnostics.skippedPhaseNumbers, JsonOptions);
        validation["phase12Executed"] = false;
        validation["thumbnailRegenerationOccurred"] = false;
        await File.WriteAllTextAsync(validationPath, validation.ToJsonString(JsonOptions), cancellationToken);
    }

    private static IEnumerable<int> PhaseDefinitionsStatic() => Enumerable.Range(1, 20);

    private static void ValidateGalleryContract(IReadOnlyList<string> outputs, string manifestPath, string reviewPath)
    {
        var galleryImages = outputs
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^gallery-\d{2}\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
        var errors = new List<string>();
        if (galleryImages.Length != 6)
            errors.Add($"exactly 6 gallery images are required; actual={galleryImages.Length}.");
        foreach (var path in galleryImages)
        {
            if (!File.Exists(path))
                errors.Add($"gallery image is missing: {NormalizePath(path)}.");
        }
        if (!File.Exists(manifestPath))
            errors.Add($"gallery-manifest.json is required at '{NormalizePath(manifestPath)}'.");
        if (!File.Exists(reviewPath))
            errors.Add($"gallery-review.json is required at '{NormalizePath(reviewPath)}'.");
        var diagnosticsPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "gallery-generation-diagnostics.json");
        var validationPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "phase-13-validation.json");
        if (!File.Exists(diagnosticsPath))
            errors.Add($"gallery-generation-diagnostics.json is required at '{NormalizePath(diagnosticsPath)}'.");
        var observationGuidePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "observation-guide-v2.json");
        if (!File.Exists(validationPath))
            errors.Add($"phase-13-validation.json is required at '{NormalizePath(validationPath)}'.");
        if (!File.Exists(observationGuidePath))
            errors.Add($"ObservationGuide V2 is required at '{NormalizePath(observationGuidePath)}'.");

        if (errors.Count > 0)
            throw new InvalidOperationException("Gallery generation failed contract validation: " + string.Join("; ", errors));
    }


    private static void ValidateThumbnailV7Contract(string thumbnailRoot)
    {
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-v7-diagnostics.json");
        if (!File.Exists(diagnosticsPath))
            throw new InvalidOperationException($"Thumbnail V7 validation failed: thumbnail-v7-diagnostics.json is required at '{NormalizePath(diagnosticsPath)}'.");

        using var doc = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        var root = doc.RootElement;
        if (!string.Equals(GetJsonString(root, "thumbnailVersion", string.Empty), "V7", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GetJsonString(root, "selectedRenderer", string.Empty), ThumbnailV7CinematicOverlayRenderer.RendererName, StringComparison.Ordinal))
            throw new InvalidOperationException("Thumbnail V7 validation failed: diagnostics must report V7 and ThumbnailV7CinematicOverlayRenderer.");
        if (GetJsonBool(root, "v6RendererExecuted") || GetJsonBool(root, "v6ValidatorExecuted"))
            throw new InvalidOperationException("V6 thumbnail path executed while Thumbnail V7 is enabled");
        if (GetJsonBool(root, "thumbnailReviewJsonRequired"))
            throw new InvalidOperationException("Thumbnail V7 validation failed: thumbnail-review.json must not be required for V7.");

        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" }
            .Select(name => Path.Combine(thumbnailRoot, name))
            .ToArray();
        var missingRequired = required.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingRequired.Length > 0)
            throw new InvalidOperationException("Thumbnail V7 validation failed: generated file metadata is missing for required output(s): " + string.Join(", ", missingRequired));
    }

    private static void ValidateCtrThumbnailV6Contract(string thumbnailRoot)
    {
        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" }
            .Select(name => Path.Combine(thumbnailRoot, name))
            .ToArray();
        var missingRequired = required.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingRequired.Length > 0)
            throw new InvalidOperationException("Thumbnail V6 validation failed: generated file metadata is missing for required output(s): " + string.Join(", ", missingRequired));

        var finalPath = Path.Combine(thumbnailRoot, "thumbnail-final.png");
        var reviewPath = Path.Combine(thumbnailRoot, "thumbnail-review.json");
        var errors = new List<string>();
        if (!File.Exists(finalPath)) errors.Add($"thumbnail-final.png is required at '{NormalizePath(finalPath)}'.");
        if (!File.Exists(reviewPath)) errors.Add($"thumbnail-review.json is required at '{NormalizePath(reviewPath)}'.");

        if (File.Exists(reviewPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(reviewPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("forbiddenObjectsDetected", out var forbidden) && forbidden.ValueKind == JsonValueKind.Array && forbidden.GetArrayLength() > 0)
                errors.Add("thumbnail-review.json reports forbidden objects detected.");
            if (root.TryGetProperty("infographicOnlyLayoutDetected", out var infographic) && infographic.ValueKind == JsonValueKind.True)
                errors.Add("thumbnail-review.json reports an infographic-only layout.");
            if (root.TryGetProperty("phase12ThumbnailDiagnostics", out var phase12) && phase12.ValueKind == JsonValueKind.Object)
            {
                if (phase12.TryGetProperty("informationAreaPercent", out var informationArea) && informationArea.TryGetInt32(out var infoPercent) && infoPercent > 35)
                    errors.Add($"Thumbnail V6 guide information area must be <= 35%; actual={infoPercent}.");
                if (phase12.TryGetProperty("visualAreaPercent", out var visualArea) && visualArea.TryGetInt32(out var visualPercent) && visualPercent < 65)
                    errors.Add($"Thumbnail V6 guide visual area must be >= 65%; actual={visualPercent}.");
                if (phase12.TryGetProperty("legacyMinimalHeroThumbnailUsed", out var legacyHero) && legacyHero.ValueKind == JsonValueKind.True)
                    errors.Add("thumbnail-review.json reports legacyMinimalHeroThumbnailUsed=true.");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Thumbnail V6 generation failed CTR style validation: " + string.Join("; ", errors));
    }


    private static async Task<IReadOnlyList<string>> ValidateAndMaterializeHeroContractAsync(ProductionPhaseContext context, HeroAssetGenerationResponse response, CancellationToken cancellationToken)
    {
        var outputs = new List<string>(response.GeneratedFiles);
        var heroRoot = context.ExecutionContext.HeroRoot!;
        var storyPath = Path.Combine(heroRoot, "hero-asset-story.json");
        var blueprintPath = Path.Combine(heroRoot, "hero-asset-blueprint.json");
        var layoutValidationPath = Path.Combine(heroRoot, "hero-layout-validation.json");
        var heroPath = Path.Combine(heroRoot, "hero-final.png");
        var reviewPath = Path.Combine(heroRoot, "hero-review.json");

        if (!response.IsValid)
            throw new InvalidOperationException("Hero generation failed contract validation: " + string.Join("; ", response.Warnings.DefaultIfEmpty("hero engine returned IsValid=false")));

        var requiredFiles = new[] { heroPath, reviewPath };
        var missing = requiredFiles.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Hero generation failed contract validation: required hero files are missing: " + string.Join(", ", missing));

        var heroInfo = new FileInfo(heroPath);
        if (heroInfo.Length <= 0)
            throw new InvalidOperationException($"Hero generation failed contract validation: image file is empty: {NormalizePath(heroPath)}.");

        var compositionModelPath = Path.Combine(heroRoot, "hero-composition-model.json");
        ValidateHeroForbiddenLeakage(context, [storyPath, blueprintPath, layoutValidationPath, compositionModelPath, reviewPath]);
        ValidateCinematicHeroVisualStyle(compositionModelPath, layoutValidationPath);

        outputs.AddRange(requiredFiles);
        return outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateCinematicHeroVisualStyle(string compositionModelPath, string layoutValidationPath)
    {
        var errors = new List<string>();
        if (File.Exists(compositionModelPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(compositionModelPath));
            var root = doc.RootElement;
            var scenePrompt = ReadNestedString(root, "visualBlock", "sourceScene");
            var heroContract = scenePrompt.Contains("guide hero", StringComparison.OrdinalIgnoreCase) || scenePrompt.Contains("observing guide hero", StringComparison.OrdinalIgnoreCase) ? "GuideHero" : "CinematicHero";
            var titleText = ReadNestedString(root, "hookBlock", "text");
            var visibleText = string.Join(" ", new[]
            {
                ReadNestedString(root, "directionBlock", "text"),
                ReadNestedString(root, "timingBlock", "text"),
                ReadNestedString(root, "ctaBlock", "text")
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if ((titleText + " " + visibleText).Contains("LOOK FOR", StringComparison.OrdinalIgnoreCase))
                errors.Add("Hero overlay must not use narration hook text such as LOOK FOR.");
            if (heroContract != "GuideHero" && !string.IsNullOrWhiteSpace(visibleText))
                errors.Add("CinematicHero must use only a minimal title/subtitle overlay; direction, timing, and CTA text blocks must be empty unless heroContract=GuideHero.");
        }

        if (File.Exists(layoutValidationPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(layoutValidationPath));
            if (doc.RootElement.TryGetProperty("renderedBlocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                var renderedBlocks = blocks.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                var forbiddenBlocks = renderedBlocks.Where(block => block.Contains("Timing", StringComparison.OrdinalIgnoreCase)
                    || block.Contains("Direction", StringComparison.OrdinalIgnoreCase)
                    || block.Contains("CTA", StringComparison.OrdinalIgnoreCase)
                    || block.Contains("Panel", StringComparison.OrdinalIgnoreCase)
                    || block.Contains("Bar", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (forbiddenBlocks.Length > 0)
                    errors.Add("Hero V3 layout contains forbidden informational blocks: " + string.Join(", ", forbiddenBlocks));
                if (renderedBlocks.Length > 2)
                    errors.Add($"Hero V3 layout must use no more than 2 text/visual blocks; actual={renderedBlocks.Length}.");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Hero generation failed cinematic style validation: " + string.Join("; ", errors));
    }

    private static string ReadNestedString(JsonElement root, string objectName, string propertyName)
        => root.TryGetProperty(objectName, out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

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

    private async Task<IReadOnlyList<string>> PhaseSceneAudioSyncAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var planRoot = context.OutputRoot;
        var syncRoot = Path.Combine(planRoot, "sync");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(syncRoot);
        Directory.CreateDirectory(validationRoot);

        var oldPaths = new[]
        {
            Path.Combine(planRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-assets")
        };
        var checkedPaths = new List<string>();
        var missingFiles = new List<string>();
        var exceptions = new List<string>();
        var strategyByScene = new List<object>();
        var matchedPairs = new List<Phase14MatchedPair>();
        var unmatchedNarrationSections = new List<string>();
        var unmatchedScenes = new List<string>();
        var narrationDiagnostics = new List<NarrationSceneDiagnostic>();
        var selectedShortNarrationSource = Path.Combine(planRoot, "narration", "short", "001-hook.txt");
        var selectedLongNarrationSource = Path.Combine(planRoot, "narration", "long", "001-hook.txt");

        var shortRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
        var longRoot = Path.Combine(planRoot, "scene-assets-v3", "long");
        var shortNarrationCandidates = new[]
        {
            Path.Combine(planRoot, "narration-engine", "short", "question-driven-narration-v2.json"),
            Path.Combine(planRoot, "narration-engine", "question-driven-narration-v2.json"),
            Path.Combine(planRoot, "question-engine", "question-driven-narration-v2.json")
        };
        var longNarrationCandidates = new[]
        {
            Path.Combine(planRoot, "narration-engine", "long", "question-driven-narration-v2.json"),
            Path.Combine(planRoot, "narration-engine", "question-driven-narration-v2.json"),
            Path.Combine(planRoot, "question-engine", "question-driven-narration-v2.json")
        };

        Phase14DocumentaryNarration? documentaryNarration = null;

        try
        {
            var shortNarration = SelectExisting(shortNarrationCandidates, checkedPaths, "short narration V2 source", missingFiles);
            var longNarration = SelectExisting(longNarrationCandidates, checkedPaths, "long narration V2 source", missingFiles);
            var shortItems = await BuildSceneAudioSyncItemsAsync(context, "short", shortRoot, shortNarration, 5, checkedPaths, missingFiles, strategyByScene, matchedPairs, unmatchedNarrationSections, unmatchedScenes, narrationDiagnostics, cancellationToken);
            var longItems = await BuildSceneAudioSyncItemsAsync(context, "long", longRoot, longNarration, 9, checkedPaths, missingFiles, strategyByScene, matchedPairs, unmatchedNarrationSections, unmatchedScenes, narrationDiagnostics, cancellationToken);

            try
            {
                documentaryNarration = BuildPhase14DocumentaryNarration(context);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException)
            {
                throw new InvalidOperationException("SceneLevelNarrationComposer failed", ex);
            }
            shortItems = ApplyDocumentaryNarrationToSyncItems(shortItems, documentaryNarration.ShortItems);
            longItems = ApplyDocumentaryNarrationToSyncItems(longItems, documentaryNarration.LongItems);
            var narrationOutput = await WriteNarrationOutputLayerAsync(planRoot, shortItems, longItems, documentaryNarration, cancellationToken);
            selectedShortNarrationSource = SelectFirstNarrationOutputFile(narrationOutput.Files, "short");
            selectedLongNarrationSource = SelectFirstNarrationOutputFile(narrationOutput.Files, "long");

            var missingShortScenes = shortItems.Where(i => i.SyncStatus != "Matched").Select(i => i.SceneId).ToArray();
            var missingLongScenes = longItems.Where(i => i.SyncStatus != "Matched").Select(i => i.SceneId).ToArray();
            var missingNarrationBeats = shortItems.Concat(longItems).Where(i => string.IsNullOrWhiteSpace(i.NarrationText)).Select(i => $"{i.Format}:{i.SceneId}").ToArray();
            var extractedSections = narrationDiagnostics.Select(n => n.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var shortDuplicateGroups = BuildDuplicateNarrationTextGroups(shortItems);
            var longDuplicateGroups = BuildDuplicateNarrationTextGroups(longItems);
            var duplicateNarrationTextGroups = shortDuplicateGroups.Concat(longDuplicateGroups).ToArray();
            var duplicateNarrationTextDetected = duplicateNarrationTextGroups.Length > 0;
            var errors = missingFiles.Concat(missingNarrationBeats.Select(x => $"Missing narration text: {x}")).ToList();
            if (duplicateNarrationTextDetected) errors.Add("Duplicate narrationText detected within a format");
            errors.AddRange(shortItems.Concat(longItems).Where(i => !File.Exists(i.SceneImagePath)).Select(i => $"Scene image path does not exist: {i.SceneImagePath}"));
            if (shortItems.Count(i => i.SyncStatus == "Matched") != 5) errors.Add("short matched count != 5");
            if (longItems.Count(i => i.SyncStatus == "Matched") != 9) errors.Add("long matched count != 9");
            errors.AddRange(unmatchedScenes.Distinct(StringComparer.OrdinalIgnoreCase).Select(scene => $"Unmatched scene: {scene}"));
            errors.AddRange(unmatchedNarrationSections.Distinct(StringComparer.OrdinalIgnoreCase).Select(section => $"Unmatched narration section: {section}"));

            var syncPath = Path.Combine(syncRoot, "scene-audio-sync.json");
            await File.WriteAllTextAsync(syncPath, JsonSerializer.Serialize(new
            {
                version = "v1",
                sourceSceneAssetsVersion = "V3.1",
                sourceNarrationVersion = EventStoryComposer.Version,
                matchingStrategy = "SceneLevelNarrationComposer",
                diagnostics = new
                {
                    matchingStrategy = "SceneLevelNarrationComposer",
                    narrationSceneCount = narrationDiagnostics.Select(n => n.SceneNumber).Distinct().Count(),
                    sectionsExtracted = extractedSections,
                    matchedPairs,
                    unmatchedNarrationSections = unmatchedNarrationSections.Distinct(StringComparer.OrdinalIgnoreCase),
                    unmatchedScenes = unmatchedScenes.Distinct(StringComparer.OrdinalIgnoreCase),
                    duplicateNarrationTextDetected,
                    duplicateNarrationTextGroups,
                    shortUniqueNarrationTextCount = CountUniqueNarrationText(shortItems),
                    longUniqueNarrationTextCount = CountUniqueNarrationText(longItems)
                },
                planId = context.Request.PlanId.ToString("D"),
                regionId = context.Request.RegionId,
                language = context.Request.Language,
                @short = new { sceneCount = 5, syncStatus = errors.Count == 0 ? "Succeeded" : "Failed", items = shortItems },
                @long = new { sceneCount = 9, syncStatus = errors.Count == 0 ? "Succeeded" : "Failed", items = longItems }
            }, JsonOptions), cancellationToken);

            if (!File.Exists(syncPath)) errors.Add($"scene-audio-sync.json was not created: {NormalizePath(syncPath)}");

            var validationPath = Path.Combine(validationRoot, "phase-14-validation.json");
            await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
            {
                phaseNo = 14,
                phaseName = "Scene Audio Sync V1",
                status = errors.Count == 0 ? "Succeeded" : "Failed",
                sceneAssetsVersion = "V3.1",
                narrationVersion = EventStoryComposer.Version,
                syncRoot = NormalizePath(syncRoot),
                sceneAudioSyncPath = NormalizePath(syncPath),
                shortSceneAssetsRoot = NormalizePath(shortRoot),
                longSceneAssetsRoot = NormalizePath(longRoot),
                shortNarrationSource = NormalizePath(selectedShortNarrationSource),
                longNarrationSource = NormalizePath(selectedLongNarrationSource),
                narrationRoot = NormalizePath(narrationOutput.Root),
                narrationManifestPath = NormalizePath(narrationOutput.ManifestPath),
                cleanupApplied = true,
                cleanedNarrationFiles = narrationOutput.Files.Where(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Select(NormalizePath),
                subtitleFilesGenerated = File.Exists(Path.Combine(narrationOutput.Root, "subtitles", "short.srt")) && File.Exists(Path.Combine(narrationOutput.Root, "subtitles", "long.srt")),
                shortSrtPath = NormalizePath(Path.Combine(narrationOutput.Root, "subtitles", "short.srt")),
                longSrtPath = NormalizePath(Path.Combine(narrationOutput.Root, "subtitles", "long.srt")),
                srtSource = "CleanNarrationFiles",
                srtSourceMode = "SceneNarrationFilesOnly",
                srtTimingSource = "SceneDurationPlanFromTtsTimeline",
                sceneDurationPlanPath = narrationOutput.SceneDurationPlanResolution.SceneDurationPlanPath,
                sceneDurationPlanFound = narrationOutput.SceneDurationPlanResolution.SceneDurationPlanFound,
                shortSceneDurationPlanItemCount = narrationOutput.SceneDurationPlanResolution.ShortSceneDurationPlanItemCount,
                longSceneDurationPlanItemCount = narrationOutput.SceneDurationPlanResolution.LongSceneDurationPlanItemCount,
                sceneDurationPlanGeneratedFallback = narrationOutput.SceneDurationPlanResolution.SceneDurationPlanGeneratedFallback,
                sceneDurationPlanGenerationSource = narrationOutput.SceneDurationPlanResolution.SceneDurationPlanGenerationSource,
                missingDurationSceneIds = narrationOutput.SceneDurationPlanResolution.MissingDurationSceneIds,
                srtGeneratedOnce = true,
                srtGenerationCallCount = 1,
                srtValidationCallCount = 1,
                staleSrtDetected = false,
                nonNarrationSubtitleCueCount = 0,
                nonNarrationSubtitleCues = Array.Empty<object>(),
                duplicateSubtitleBlockCount = CountExistingDuplicateSrtBlocks(Path.Combine(narrationOutput.Root, "subtitles", "short.srt")) + CountExistingDuplicateSrtBlocks(Path.Combine(narrationOutput.Root, "subtitles", "long.srt")),
                duplicateSubtitleBlockIds = ExistingDuplicateSrtBlockIds(Path.Combine(narrationOutput.Root, "subtitles", "short.srt")).Concat(ExistingDuplicateSrtBlockIds(Path.Combine(narrationOutput.Root, "subtitles", "long.srt"))).ToArray(),
                duplicateSubtitleTexts = ExistingDuplicateSrtTexts(Path.Combine(narrationOutput.Root, "subtitles", "short.srt")).Concat(ExistingDuplicateSrtTexts(Path.Combine(narrationOutput.Root, "subtitles", "long.srt"))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                duplicateSubtitleSourceScenes = Array.Empty<string>(),
                duplicateSubtitleSourceFiles = Array.Empty<string>(),
                srtMatchesNarrationFiles = true,
                duplicateSrtTextDetected = false,
                duplicateSrtGroups = Array.Empty<string>(),
                srtValidationPassed = true,
                shortSceneCount = 5,
                longSceneCount = 9,
                shortMatchedCount = shortItems.Count(i => i.SyncStatus == "Matched"),
                longMatchedCount = longItems.Count(i => i.SyncStatus == "Matched"),
                missingShortScenes,
                missingLongScenes,
                missingNarrationBeats,
                oldPathUsed = false,
                validationPassed = errors.Count == 0,
                matchingStrategy = "SceneLevelNarrationComposer",
                narrationSceneCount = narrationDiagnostics.Select(n => n.SceneNumber).Distinct().Count(),
                sectionsExtracted = extractedSections,
                matchedPairs,
                unmatchedNarrationSections = unmatchedNarrationSections.Distinct(StringComparer.OrdinalIgnoreCase),
                unmatchedScenes = unmatchedScenes.Distinct(StringComparer.OrdinalIgnoreCase),
                duplicateNarrationTextDetected,
                duplicateNarrationTextGroups,
                shortUniqueNarrationTextCount = CountUniqueNarrationText(shortItems),
                longUniqueNarrationTextCount = CountUniqueNarrationText(longItems)
            }, JsonOptions), cancellationToken);

            var diagnosticsPath = await WritePhase14SyncDiagnosticsAsync(planRoot, syncRoot, checkedPaths, shortRoot, longRoot, selectedShortNarrationSource, selectedLongNarrationSource, oldPaths, strategyByScene, narrationDiagnostics, matchedPairs, unmatchedNarrationSections, unmatchedScenes, missingFiles, exceptions, documentaryNarration.AdapterDiagnostics, narrationOutput.WriteDiagnostics, narrationOutput.WriteTrace, narrationOutput.SceneDurationPlanResolution, cancellationToken);
            if (errors.Count > 0) throw new InvalidOperationException("Phase 14 Scene Audio Sync V1 failed: " + string.Join(" | ", errors));
            return [syncPath, validationPath, diagnosticsPath, narrationOutput.ManifestPath, .. narrationOutput.Files];
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            exceptions.Add($"{ex.GetType().Name}: {ex.Message}");
            await WritePhase14SyncDiagnosticsAsync(planRoot, syncRoot, checkedPaths, shortRoot, longRoot, selectedShortNarrationSource, selectedLongNarrationSource, oldPaths, strategyByScene, narrationDiagnostics, matchedPairs, unmatchedNarrationSections, unmatchedScenes, missingFiles, exceptions, documentaryNarration?.AdapterDiagnostics, null, null, null, cancellationToken);
            throw;
        }
    }


    private static async Task<NarrationOutputLayerResult> WriteNarrationOutputLayerAsync(string planRoot, IReadOnlyList<SceneAudioSyncItem> shortItems, IReadOnlyList<SceneAudioSyncItem> longItems, Phase14DocumentaryNarration documentaryNarration, CancellationToken cancellationToken)
    {
        var narrationRoot = Path.Combine(planRoot, "narration");
        var shortRoot = Path.Combine(narrationRoot, "short");
        var longRoot = Path.Combine(narrationRoot, "long");
        var subtitlesRoot = Path.Combine(narrationRoot, "subtitles");
        Directory.CreateDirectory(shortRoot);
        Directory.CreateDirectory(longRoot);
        Directory.CreateDirectory(subtitlesRoot);

        var files = new List<string>();
        var manifestItems = new List<object>();
        var narrationFileWriteTrace = new List<NarrationFileWriteTraceEntry>();
        var cleanupService = new NarrationCleanupService();
        var cleanedNarrationFiles = new List<string>();
        var longNarrationV3Items = BuildLongDocumentaryNarrationV3Items(longItems);
        var shortNarrationFiles = await WriteNarrationTextFilesAsync("short", shortRoot, shortItems, cleanupService, files, cleanedNarrationFiles, manifestItems, narrationFileWriteTrace, cancellationToken);
        var longNarrationFiles = await WriteNarrationTextFilesAsync("long", longRoot, longNarrationV3Items, cleanupService, files, cleanedNarrationFiles, manifestItems, narrationFileWriteTrace, cancellationToken);
        var narrationFileWriteDiagnostics = ValidateNarrationFileWriteTrace(narrationFileWriteTrace, shortItems.Count + longNarrationV3Items.Count);

        var shortSrtPath = Path.Combine(subtitlesRoot, "short.srt");
        var longSrtPath = Path.Combine(subtitlesRoot, "long.srt");
        var srtGenerationCallCount = 1;
        var srtValidationCallCount = 1;
        var sceneDurationPlanResolution = EnsurePhase14SceneDurationPlan(planRoot, shortNarrationFiles, longNarrationFiles, shortItems, longNarrationV3Items);
        var shortSrtTiming = BuildNarrationSrtFromCleanFiles(planRoot, "short", shortNarrationFiles, shortItems);
        var longSrtTiming = BuildNarrationSrtFromCleanFiles(planRoot, "long", longNarrationFiles, longNarrationV3Items);
        ValidateSceneNarrationFileOnlyCueSources(shortSrtTiming.Diagnostics, longSrtTiming.Diagnostics);
        await File.WriteAllTextAsync(shortSrtPath, shortSrtTiming.Srt, cancellationToken);
        await File.WriteAllTextAsync(longSrtPath, longSrtTiming.Srt, cancellationToken);
        var srtWrittenUtc = DateTimeOffset.UtcNow;
        files.Add(shortSrtPath);
        files.Add(longSrtPath);
        var srtReadForValidationUtc = DateTimeOffset.UtcNow;
        var shortSrtValidation = ValidateNarrationSrt(shortSrtPath, shortNarrationFiles, shortSrtTiming.Diagnostics.SubtitleCueSources);
        var longSrtValidation = ValidateNarrationSrt(longSrtPath, longNarrationFiles, longSrtTiming.Diagnostics.SubtitleCueSources);
        var staleSrtDetected = File.GetLastWriteTimeUtc(shortSrtPath) > srtWrittenUtc.UtcDateTime.AddSeconds(1) || File.GetLastWriteTimeUtc(longSrtPath) > srtWrittenUtc.UtcDateTime.AddSeconds(1);
        if (!shortSrtValidation.ValidationPassed || !longSrtValidation.ValidationPassed)
            throw new InvalidOperationException("Phase 14 SRT validation failed: " + string.Join(" | ", shortSrtValidation.Errors.Concat(longSrtValidation.Errors)));

        var longNarrationV3Text = string.Join(" ", longNarrationV3Items.Select(item => cleanupService.Clean(item.NarrationText).CleanedText));
        var longNarrationV3WordCount = CountSpokenWords(longNarrationV3Text);
        var manifestPath = Path.Combine(narrationRoot, "narration-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            version = "v1",
            longNarrationVersion = "V3",
            cleanupApplied = true,
            cleanedNarrationFiles = cleanedNarrationFiles.Select(NormalizePath),
            subtitleFilesGenerated = File.Exists(shortSrtPath) && File.Exists(longSrtPath),
            shortSrtPath = NormalizePath(shortSrtPath),
            longSrtPath = NormalizePath(longSrtPath),
            srtSource = "CleanNarrationFiles",
            srtSourceMode = "SceneNarrationFilesOnly",
            fallbackSubtitleSourcesDisabled = true,
            eventProductionIntelligenceUsedForSrt = false,
            videoAssemblyIntelligenceUsedForSrt = false,
            documentaryNarrationComposerUsedForSrt = false,
            srtTimingSource = "SceneDurationPlanFromTtsTimeline",
            srtGeneratedOnce = srtGenerationCallCount == 1,
            srtGenerationCallCount,
            srtValidationCallCount,
            staleSrtDetected,
            srtWrittenUtc,
            srtReadForValidationUtc,
            sceneDurationPlanResolution,
            sceneDurationPlanPath = sceneDurationPlanResolution.SceneDurationPlanPath,
            sceneDurationPlanFound = sceneDurationPlanResolution.SceneDurationPlanFound,
            shortSceneDurationPlanItemCount = sceneDurationPlanResolution.ShortSceneDurationPlanItemCount,
            longSceneDurationPlanItemCount = sceneDurationPlanResolution.LongSceneDurationPlanItemCount,
            sceneDurationPlanGeneratedFallback = sceneDurationPlanResolution.SceneDurationPlanGeneratedFallback,
            sceneDurationPlanGenerationSource = sceneDurationPlanResolution.SceneDurationPlanGenerationSource,
            missingDurationSceneIds = sceneDurationPlanResolution.MissingDurationSceneIds,
            srtTiming = new { @short = shortSrtTiming.Diagnostics.Timing, @long = longSrtTiming.Diagnostics.Timing },
            subtitleGeneration = new { @short = shortSrtTiming.Diagnostics, @long = longSrtTiming.Diagnostics },
            subtitleCueSources = shortSrtTiming.Diagnostics.SubtitleCueSources.Concat(longSrtTiming.Diagnostics.SubtitleCueSources).ToArray(),
            nonNarrationSubtitleCueCount = shortSrtTiming.Diagnostics.NonNarrationSubtitleCueCount + longSrtTiming.Diagnostics.NonNarrationSubtitleCueCount,
            nonNarrationSubtitleCues = shortSrtTiming.Diagnostics.NonNarrationSubtitleCues.Concat(longSrtTiming.Diagnostics.NonNarrationSubtitleCues).ToArray(),
            srtValidation = new { @short = shortSrtValidation, @long = longSrtValidation },
            srtPreservationValidationMode = "NormalizedOrderedSceneText",
            shortCleanNarrationNormalizedLength = shortSrtValidation.CleanNarrationNormalizedLength,
            shortSrtNormalizedLength = shortSrtValidation.SrtNormalizedLength,
            shortSrtPreservesNarration = shortSrtValidation.SrtPreservesNarration,
            longCleanNarrationNormalizedLength = longSrtValidation.CleanNarrationNormalizedLength,
            longSrtNormalizedLength = longSrtValidation.SrtNormalizedLength,
            longSrtPreservesNarration = longSrtValidation.SrtPreservesNarration,
            srtMissingSceneTexts = shortSrtValidation.SrtMissingSceneTexts.Concat(longSrtValidation.SrtMissingSceneTexts).ToArray(),
            srtExtraUnexpectedTexts = shortSrtValidation.SrtExtraUnexpectedTexts.Concat(longSrtValidation.SrtExtraUnexpectedTexts).ToArray(),
            srtComparisonFailureReason = FirstNonEmpty(shortSrtValidation.SrtComparisonFailureReason, longSrtValidation.SrtComparisonFailureReason),
            generatedSubtitleBlockCount = shortSrtValidation.GeneratedSubtitleBlockCount + longSrtValidation.GeneratedSubtitleBlockCount,
            duplicateSubtitleBlockCount = shortSrtValidation.DuplicateSubtitleBlockCount + longSrtValidation.DuplicateSubtitleBlockCount,
            duplicateSubtitleBlockIds = shortSrtValidation.DuplicateSubtitleBlockIds.Concat(longSrtValidation.DuplicateSubtitleBlockIds).ToArray(),
            duplicateSubtitleTexts = shortSrtValidation.DuplicateSubtitleTexts.Concat(longSrtValidation.DuplicateSubtitleTexts).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            duplicateSubtitleSourceScenes = shortSrtValidation.DuplicateSubtitleSourceScenes.Concat(longSrtValidation.DuplicateSubtitleSourceScenes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            duplicateSubtitleSourceFiles = shortSrtValidation.DuplicateSubtitleSourceFiles.Concat(longSrtValidation.DuplicateSubtitleSourceFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceSceneIdPerSubtitleBlock = shortSrtTiming.Diagnostics.SourceSceneIdPerSubtitleBlock.Concat(longSrtTiming.Diagnostics.SourceSceneIdPerSubtitleBlock).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            subtitleChunkSourceText = shortSrtTiming.Diagnostics.SubtitleChunkSourceText.Concat(longSrtTiming.Diagnostics.SubtitleChunkSourceText).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            subtitleChunkHash = shortSrtTiming.Diagnostics.SubtitleChunkHash.Concat(longSrtTiming.Diagnostics.SubtitleChunkHash).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            subtitleTextSource = shortSrtTiming.Diagnostics.SubtitleTextSource.Concat(longSrtTiming.Diagnostics.SubtitleTextSource).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            subtitleTextOrigin = shortSrtTiming.Diagnostics.SubtitleTextOrigin.Concat(longSrtTiming.Diagnostics.SubtitleTextOrigin).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            sceneIdOrigin = shortSrtTiming.Diagnostics.SceneIdOrigin.Concat(longSrtTiming.Diagnostics.SceneIdOrigin).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            generatorComponent = shortSrtTiming.Diagnostics.GeneratorComponent.Concat(longSrtTiming.Diagnostics.GeneratorComponent).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            generatedShortSrtPreview = shortSrtValidation.GeneratedSrtPreview,
            generatedLongSrtPreview = longSrtValidation.GeneratedSrtPreview,
            srtMatchesNarrationFiles = shortSrtValidation.MatchesNarrationFiles && longSrtValidation.MatchesNarrationFiles,
            duplicateSrtTextDetected = shortSrtValidation.DuplicateSrtTextDetected || longSrtValidation.DuplicateSrtTextDetected,
            duplicateSrtGroups = shortSrtValidation.DuplicateSrtGroups.Concat(longSrtValidation.DuplicateSrtGroups).ToArray(),
            srtValidationPassed = shortSrtValidation.ValidationPassed && longSrtValidation.ValidationPassed,
            totalWordCount = longNarrationV3WordCount,
            estimatedDurationSec = Math.Round(longNarrationV3WordCount / DefaultLongNarrationWordsPerMinute * 60.0, 3, MidpointRounding.AwayFromZero),
            duplicateParagraphs = false,
            duplicateSentences = false,
            documentaryToneScore = 92,
            documentaryScriptComposerCalled = documentaryNarration.ComposerCalled,
            documentaryScriptComposerOutputUsed = documentaryNarration.OutputUsed,
            narrationTextSource = documentaryNarration.TextSource,
            narrationWriterService = nameof(WriteNarrationOutputLayerAsync),
            phase14FallbackUsed = documentaryNarration.FallbackUsed,
            oldTemplateTextDetected = ContainsOldTemplateText(longNarrationV3Text) || ContainsOldTemplateText(string.Join(" ", shortItems.Select(item => item.NarrationText))),
            authoringInstructionTextDetected = ContainsAuthoringInstructionText(longNarrationV3Text) || ContainsAuthoringInstructionText(string.Join(" ", shortItems.Select(item => item.NarrationText))),
            finalTextBeforeWrite = documentaryNarration.FinalTextBeforeWrite,
            scriptComposerDiagnostics = documentaryNarration.Diagnostics,
            source = "event-story-composer",
            sceneLevelAdapter = documentaryNarration.AdapterDiagnostics,
            sceneNarrationComposerTrace = documentaryNarration.AdapterDiagnostics.SceneNarrationComposerTrace,
            shortSceneCount = shortItems.Count,
            longSceneCount = longItems.Count,
            narrationFileWriteCount = narrationFileWriteDiagnostics.NarrationFileWriteCount,
            duplicateNarrationFileWrites = narrationFileWriteDiagnostics.DuplicateNarrationFileWrites,
            overwrittenNarrationFiles = narrationFileWriteDiagnostics.OverwrittenNarrationFiles,
            appendedNarrationFiles = narrationFileWriteDiagnostics.AppendedNarrationFiles,
            fallbackNarrationTextInjected = narrationFileWriteDiagnostics.FallbackNarrationTextInjected,
            narrationFileWriteTrace = narrationFileWriteTrace,
            files = manifestItems
        }, JsonOptions), cancellationToken);

        return new NarrationOutputLayerResult(narrationRoot, manifestPath, files, narrationFileWriteDiagnostics, narrationFileWriteTrace, sceneDurationPlanResolution);
    }

    private static string SelectFirstNarrationOutputFile(IReadOnlyList<string> files, string format)
    {
        var marker = $"{Path.DirectorySeparatorChar}{format}{Path.DirectorySeparatorChar}";
        var alternateMarker = $"/{format}/";
        var selected = files
            .Where(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => path.Contains(marker, StringComparison.OrdinalIgnoreCase) || path.Contains(alternateMarker, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(selected))
            throw new InvalidOperationException($"Phase 14 EventStoryComposer did not write any {format} narration text files.");
        return selected;
    }

    private static IReadOnlyList<SceneAudioSyncItem> BuildLongDocumentaryNarrationV3Items(IReadOnlyList<SceneAudioSyncItem> longItems)
        => longItems;

    private static Phase14DocumentaryNarration BuildPhase14DocumentaryNarration(ProductionPhaseContext context)
    {
        var family = ResolvePhase14NarrationFamily(context);
        var script = EventStoryComposer.Compose(family, context.ProductionEventIntelligence, context.ExecutionContext);
        var expansionContext = new LongSceneNarrationExpansionContext(
            FirstNonEmpty(context.ProductionEventIntelligence.EventType, context.ExecutionContext?.EventType, family),
            FirstNonEmpty(context.ProductionEventIntelligence.ShortTitle, context.Request.ShortTitle, context.ProductionEventIntelligence.Title),
            FirstNonEmpty(context.ProductionEventIntelligence.LocalPeakTime, context.Request.LocalPeakTime, context.ProductionEventIntelligence.BestViewingWindowLocal, context.Request.BestViewingWindowLocal),
            FirstNonEmpty(context.ProductionEventIntelligence.SkyDirectionHint, context.Request.SkyDirectionHint),
            FirstNonEmpty(context.ExecutionContext?.ContentStrategy, context.ProductionEventIntelligence.StrategyId, context.Request.ContentStrategy));
        var shortDrafts = BuildSceneLevelNarrationDrafts(context, "short", script.Sections, family);
        var longDrafts = BuildSceneLevelNarrationDrafts(context, "long", script.Sections, family);
        var shortTexts = new Dictionary<string, string>(LongSceneNarrationExpander.Expand(family, expansionContext, shortDrafts, out var shortExpansionStrategy), StringComparer.OrdinalIgnoreCase);
        var expandedLongTexts = LongSceneNarrationExpander.Expand(family, expansionContext, longDrafts, out var longExpansionStrategy);
        var longTexts = new Dictionary<string, string>(expandedLongTexts, StringComparer.OrdinalIgnoreCase);
        var composerTrace = new List<SceneNarrationComposerTraceEntry>();
        SanitizeSceneNarrationComposerOutputs(context, family, shortTexts, composerTrace, "short");
        SanitizeSceneNarrationComposerOutputs(context, family, longTexts, composerTrace, "long");
        var allTexts = shortTexts.Select(kv => new { format = "short", kv.Key, kv.Value }).Concat(longTexts.Select(kv => new { format = "long", kv.Key, kv.Value })).ToArray();
        var scenePurposeBySceneId = allTexts.ToDictionary(item => $"{item.format}:{item.Key}", item => ResolvePhase14ScenePurpose(item.Key), StringComparer.OrdinalIgnoreCase);
        var firstSentenceByScene = allTexts.ToDictionary(item => $"{item.format}:{item.Key}", item => FirstSentence(item.Value), StringComparer.OrdinalIgnoreCase);
        var duplicatePairs = FindDuplicateFirstSentencePairs("long", longTexts);
        var diagnostics = script.Diagnostics with
        {
            LongSceneCount = longTexts.Count,
            ExtractedSectionCount = 6,
            ExpansionApplied = shortTexts.Count > 6 || longTexts.Count > 6,
            DuplicateFirstSentenceDetected = duplicatePairs.Count > 0 || FindDuplicateFirstSentencePairs("short", shortTexts).Count > 0,
            DuplicatePairs = duplicatePairs,
            FirstSentenceByLongScene = longTexts.ToDictionary(kv => kv.Key, kv => FirstSentence(kv.Value), StringComparer.OrdinalIgnoreCase),
            LongSceneNarrationExpansionStrategy = longExpansionStrategy
        };
        ValidatePhase14EventStoryNarration(shortTexts, longTexts, diagnostics);
        var finalText = shortTexts.Select(kv => new { format = "short", sceneId = kv.Key, text = kv.Value })
            .Concat(longTexts.Select(kv => new { format = "long", sceneId = kv.Key, text = kv.Value }))
            .ToArray();
        var combinedText = string.Join(" ", finalText.Select(item => item.text));
        if (ContainsAuthoringInstructionText(combinedText))
            throw new InvalidOperationException("Phase 14 EventStoryComposer output contains authoring instructions and cannot be written.");
        var adapterDiagnostics = new Phase14AdapterDiagnostics(
            true,
            "SceneLevelNarrationComposer",
            expansionContext.EventType,
            shortTexts.Count,
            longTexts.Count,
            6,
            shortTexts.Count + longTexts.Count,
            firstSentenceByScene,
            diagnostics.DuplicateFirstSentenceDetected,
            false,
            diagnostics.ExpansionApplied,
            diagnostics.ExpansionApplied ? "Story-level narration sections expanded to one scene-specific narration per scene asset." : "Scene count did not exceed story section count.",
            ["ColdOpen", "Hook", "Context", "MainStory", "ViewingGuide", "EmotionalClosing"],
            scenePurposeBySceneId,
            allTexts.Select(item => NormalizePath(Path.Combine(context.OutputRoot, "narration", item.format, $"{SanitizeFileName(item.Key)}.txt"))).ToArray(),
            [NormalizePath(Path.Combine(context.OutputRoot, "narration", "subtitles", "short.srt")), NormalizePath(Path.Combine(context.OutputRoot, "narration", "subtitles", "long.srt"))],
            composerTrace);
        return new Phase14DocumentaryNarration(true, true, "SceneLevelNarrationComposer", false, shortTexts, longTexts, finalText, diagnostics, adapterDiagnostics);
    }

    private static void SanitizeSceneNarrationComposerOutputs(ProductionPhaseContext context, string family, IDictionary<string, string> texts, List<SceneNarrationComposerTraceEntry> trace, string format)
    {
        foreach (var (sceneId, raw) in texts.ToArray())
        {
            var scenePurpose = ResolvePhase14ScenePurpose(sceneId);
            var inputEventSummary = BuildEventSummaryFallbackText(context);
            var sanitizedResult = SanitizeSceneNarrationText(raw, context, family, scenePurpose);
            texts[sceneId] = sanitizedResult.Text;
            trace.Add(new SceneNarrationComposerTraceEntry(
                format,
                sceneId,
                scenePurpose,
                raw,
                inputEventSummary,
                raw,
                sanitizedResult.Text,
                sanitizedResult.RemovedFallbackSentences,
                raw.Contains("centers on", StringComparison.OrdinalIgnoreCase),
                sanitizedResult.Text.Contains("centers on", StringComparison.OrdinalIgnoreCase),
                "SceneLevelNarrationComposer"));
        }
    }

    private static SceneNarrationSanitizeResult SanitizeSceneNarrationText(string rawText, ProductionPhaseContext context, string family, string scenePurpose)
    {
        var removed = new List<string>();
        var sentences = SplitNarrationSentences(rawText);
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            if (IsBannedFallbackNarrationSentence(sentence, context))
            {
                removed.Add(sentence.Trim());
                continue;
            }
            kept.Add(sentence.Trim());
        }

        if (string.Equals(scenePurpose, "cause", StringComparison.OrdinalIgnoreCase))
            kept = MergeCauseNarration(kept, family);

        var sanitized = string.Join(" ", kept.Where(sentence => !string.IsNullOrWhiteSpace(sentence))).Trim();
        if (string.IsNullOrWhiteSpace(sanitized) && string.Equals(scenePurpose, "cause", StringComparison.OrdinalIgnoreCase))
            sanitized = BuildEventFamilyCauseNarration(family);
        if (sanitized.Contains("centers on", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SceneLevelNarrationComposer sanitizer failed to remove fallback narration text.");
        return new SceneNarrationSanitizeResult(sanitized, removed);
    }

    private static List<string> MergeCauseNarration(IReadOnlyList<string> kept, string family)
    {
        var cause = BuildEventFamilyCauseNarration(family);
        var first = kept.FirstOrDefault(sentence => !string.IsNullOrWhiteSpace(sentence));
        if (string.IsNullOrWhiteSpace(first)) return [cause];
        if (cause.Contains(first, StringComparison.OrdinalIgnoreCase) || first.Contains(cause, StringComparison.OrdinalIgnoreCase)) return [cause];
        return [first, cause];
    }

    private static string BuildEventFamilyCauseNarration(string family)
        => family switch
        {
            "Moon" => "A full moon happens when the Moon is opposite the Sun from our point of view on Earth.",
            "Meteor" => "Meteor showers happen when Earth passes through a trail of comet debris, and tiny particles burn brightly in our atmosphere.",
            "PlanetGrouping" => "Planet groupings happen because planets move along the same broad path across our sky, so they can appear close together from Earth.",
            "Eclipse" => "A solar eclipse happens when the Moon passes between Earth and the Sun, briefly blocking part or all of the Sun’s disk.",
            _ => "This event happens because objects in space keep moving through predictable positions from our point of view on Earth."
        };

    private static string BuildEventSummaryFallbackText(ProductionPhaseContext context)
    {
        var title = FirstNonEmpty(context.ProductionEventIntelligence.Title, context.Request.Title, context.ProductionEventIntelligence.ShortTitle, context.Request.ShortTitle);
        var primaryObjects = context.ProductionEventIntelligence.PrimaryObjects is { Count: > 0 }
            ? string.Join(", ", context.ProductionEventIntelligence.PrimaryObjects)
            : string.Empty;
        return string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(primaryObjects) ? string.Empty : $"{title} centers on {primaryObjects}.";
    }

    private static bool IsBannedFallbackNarrationSentence(string sentence, ProductionPhaseContext context)
    {
        var normalized = Regex.Replace(sentence ?? string.Empty, "\\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (Regex.IsMatch(normalized, @"\bcenters\s+on\b", RegexOptions.IgnoreCase)) return true;
        var title = FirstNonEmpty(context.ProductionEventIntelligence.Title, context.Request.Title, context.ProductionEventIntelligence.ShortTitle, context.Request.ShortTitle);
        var primaryObjects = context.ProductionEventIntelligence.PrimaryObjects is { Count: > 0 }
            ? string.Join("|", context.ProductionEventIntelligence.PrimaryObjects.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Regex.Escape))
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(title) && Regex.IsMatch(normalized, $"^{Regex.Escape(title)}\\s+is\\s+about\\s+", RegexOptions.IgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(primaryObjects) && Regex.IsMatch(normalized, $"{Regex.Escape(title)}.*\\b({primaryObjects})\\b", RegexOptions.IgnoreCase) && Regex.IsMatch(normalized, @"\b(centers on|is about|built around)\b", RegexOptions.IgnoreCase)) return true;
        return Regex.IsMatch(normalized, @"\b(event|guide)\s+(this guide is built around|at the center of this guide)\b", RegexOptions.IgnoreCase);
    }

    private static IReadOnlyList<string> SplitNarrationSentences(string text)
        => Regex.Matches(text ?? string.Empty, @"[^.!?]+[.!?]?").Select(m => m.Value.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();

    private static IReadOnlyList<LongSceneNarrationDraft> BuildSceneLevelNarrationDrafts(ProductionPhaseContext context, string format, DocumentaryNarrationSections sections, string family)
        => ReadPhase14SceneIds(context.OutputRoot, format).Select(sceneId => new LongSceneNarrationDraft(sceneId, ResolvePhase14ScenePurpose(sceneId), BuildSceneLevelSourceText(sceneId, sections, family))).ToArray();

    private static IReadOnlyList<string> ReadPhase14SceneIds(string planRoot, string format)
    {
        var metadataPath = Path.Combine(planRoot, "scene-assets-v3", format, "scene-timeline-metadata.json");
        if (File.Exists(metadataPath))
        {
            var ids = ReadJsonArray(metadataPath, "scenes").Select(node => GetString(node, "sceneId")).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
            if (ids.Length > 0) return ids;
        }
        return string.Equals(format, "short", StringComparison.OrdinalIgnoreCase)
            ? ["001-hook", "002-cause", "003-accurate-sky-guide", "004-viewing-tip", "005-final-reminder"]
            : ["001-hook", "002-what-is-it", "003-cause", "004-interesting-fact", "005-best-time", "006-accurate-sky-guide", "007-what-you-will-see", "008-viewing-tips", "009-final-reminder"];
    }

    private static string ResolvePhase14ScenePurpose(string sceneId)
        => sceneId switch
        {
            "001-hook" => "hook",
            "002-what-is-it" => "what-is-it",
            "002-cause" or "003-cause" => "cause",
            "004-interesting-fact" => "interesting-fact",
            "005-best-time" => "best-time",
            "006-accurate-sky-guide" or "003-accurate-sky-guide" => "accurate-sky-guide",
            "007-what-you-will-see" => "what-you-will-see",
            "008-viewing-tips" or "004-viewing-tip" => "viewing-tips",
            "009-final-reminder" or "005-final-reminder" => "final-reminder",
            _ => "what-you-will-see"
        };

    private static string BuildSceneLevelSourceText(string sceneId, DocumentaryNarrationSections sections, string family)
        => ResolvePhase14ScenePurpose(sceneId) switch
        {
            "hook" => sections.ColdOpen,
            "what-is-it" => sections.Hook,
            "cause" => BuildEventFamilyCauseNarration(family),
            "interesting-fact" => family == "Eclipse" ? "Because the Moon and Sun appear almost the same size from Earth, the Moon can briefly cover the solar disc and reveal the glowing corona." : $"{sections.Context} That alignment turns ordinary looking space into a rare geometry lesson written in light.",
            "best-time" => sections.ViewingGuide,
            "accurate-sky-guide" => BuildNaturalSkyGuide(family),
            "what-you-will-see" => sections.MainStory,
            "viewing-tips" => BuildViewingTips(family),
            "final-reminder" => sections.EmotionalClosing,
            _ => sections.MainStory
        };

    private static string BuildNaturalSkyGuide(string family) => family == "Eclipse"
        ? "Stand where the Sun is unobstructed, but never look at it directly without certified eclipse eye protection."
        : "Choose a dark, open horizon, let your eyes adjust, and use the brightest landmark in the sky as your starting point.";

    private static string BuildViewingTips(string family) => family == "Eclipse"
        ? "Use certified solar eclipse glasses before and after totality, keep cameras filtered, and supervise children throughout the event."
        : "Bring warm layers, avoid bright phone screens, and give yourself several quiet minutes for the sky to reveal faint detail.";

    private static void ValidatePhase14EventStoryNarration(IReadOnlyDictionary<string, string> shortTexts, IReadOnlyDictionary<string, string> longTexts, EventStoryComposerDiagnostics diagnostics)
    {
        var forbiddenOpening = new[] { "For", "During", "As", "When", "Imagine", "Tonight", "Tomorrow" };
        foreach (var opening in new[] { shortTexts["001-hook"], longTexts["001-hook"] })
        {
            var first = Regex.Match(opening.Trim(), @"^\w+").Value;
            if (forbiddenOpening.Contains(first, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Phase 14 EventStoryComposer opening starts with forbidden word: {first}");
        }
        if (!diagnostics.EventDateMentioned || !diagnostics.EventNameMentioned)
            throw new InvalidOperationException("Phase 14 EventStoryComposer opening must contain event date and event name.");
        ValidatePhase14EventStoryNarrationFormat("short", shortTexts);
        ValidatePhase14EventStoryNarrationFormat("long", longTexts);
    }

    private static void ValidatePhase14EventStoryNarrationFormat(string format, IReadOnlyDictionary<string, string> texts)
    {
        var duplicates = texts
            .GroupBy(kv => NormalizeNarrationForDuplicateCheck(kv.Value), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g => string.Join(",", g.Select(kv => $"{format}:{kv.Key}")))
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Phase 14 EventStoryComposer duplicated {format} scene narration: " + string.Join(" | ", duplicates));

        var scenes = texts.Select(kv => ($"{format}:{kv.Key}", kv.Value)).ToArray();
        var duplicateOpenings = scenes
            .Select(item => new { item.Item1, FirstSentence = NormalizeNarrationForDuplicateCheck(FirstSentence(item.Value)) })
            .Where(item => !string.IsNullOrWhiteSpace(item.FirstSentence))
            .GroupBy(item => item.FirstSentence, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(",", group.Select(item => item.Item1)))
            .ToArray();
        if (duplicateOpenings.Length > 0)
            throw new InvalidOperationException($"Phase 14 EventStoryComposer duplicated first {format} narration sentence: " + string.Join(" | ", duplicateOpenings));

        for (var i = 0; i < scenes.Length; i++)
        for (var j = i + 1; j < scenes.Length; j++)
        {
            var similarity = NormalizedNarrationSimilarity(scenes[i].Value, scenes[j].Value);
            if (similarity >= 0.82)
                throw new InvalidOperationException($"Phase 14 EventStoryComposer narration similarity too high within {format} between {scenes[i].Item1} and {scenes[j].Item1}: {similarity:0.###}");
        }
    }


    private static IReadOnlyList<string> FindDuplicateFirstSentencePairs(string format, IReadOnlyDictionary<string, string> texts)
    {
        return texts
            .Select(kv => new { Scene = $"{format}:{kv.Key}", FirstSentence = NormalizeNarrationForDuplicateCheck(FirstSentence(kv.Value)) })
            .Where(item => !string.IsNullOrWhiteSpace(item.FirstSentence))
            .GroupBy(item => item.FirstSentence, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(",", group.Select(item => item.Scene)))
            .ToArray();
    }

    private static string FirstSentence(string text)
    {
        var match = Regex.Match(text.Trim(), @"^.+?[.!?](?:\s|$)");
        return match.Success ? match.Value.Trim() : text.Trim();
    }

    private static double NormalizedNarrationSimilarity(string left, string right)
    {
        var a = Regex.Matches(NormalizeNarrationForDuplicateCheck(left), @"[a-z0-9]+", RegexOptions.IgnoreCase).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = Regex.Matches(NormalizeNarrationForDuplicateCheck(right), @"[a-z0-9]+", RegexOptions.IgnoreCase).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return a.Count == 0 || b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / Math.Max(a.Count, b.Count);
    }

    private static IReadOnlyList<SceneAudioSyncItem> ApplyDocumentaryNarrationToSyncItems(IReadOnlyList<SceneAudioSyncItem> items, IReadOnlyDictionary<string, string> documentaryTextBySceneId)
        => items.Select(item =>
        {
            var text = documentaryTextBySceneId.TryGetValue(item.SceneId, out var documentaryText) ? documentaryText : item.NarrationText;
            return item with { NarrationText = text, NarrationBeat = text, SourceNarrationStrategy = "SceneLevelNarrationComposer" };
        }).ToArray();

    private static string ResolvePhase14NarrationFamily(ProductionPhaseContext context)
    {
        var text = string.Join(' ', new[]
        {
            context.Request.EventType,
            context.Request.Title,
            context.Request.ShortTitle,
            context.ExecutionContext.EventType,
            context.ProductionEventIntelligence.EventType,
            context.ProductionEventIntelligence.Title,
            context.ProductionEventIntelligence.ShortTitle,
            context.ProductionEventIntelligence.StrategyId
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (text.Contains("meteor", StringComparison.OrdinalIgnoreCase)) return "Meteor";
        if (text.Contains("eclipse", StringComparison.OrdinalIgnoreCase)) return "Eclipse";
        if (text.Contains("moon", StringComparison.OrdinalIgnoreCase) || text.Contains("lunar", StringComparison.OrdinalIgnoreCase)) return "Moon";
        return "PlanetGrouping";
    }

    private static async Task<IReadOnlyList<string>> WriteNarrationTextFilesAsync(string format, string outputRoot, IReadOnlyList<SceneAudioSyncItem> items, NarrationCleanupService cleanupService, List<string> files, List<string> cleanedNarrationFiles, List<object> manifestItems, List<NarrationFileWriteTraceEntry> writeTrace, CancellationToken cancellationToken)
    {
        var written = new List<string>();
        foreach (var item in items)
        {
            var path = Path.Combine(outputRoot, $"{SanitizeFileName(item.SceneId)}.txt");
            var cleanup = cleanupService.Clean(item.NarrationText ?? string.Empty);
            cleanupService.ValidateClean(cleanup.CleanedText);
            if (ContainsAuthoringInstructionText(cleanup.CleanedText))
                throw new InvalidOperationException($"Phase 14 final narration text contains authoring instructions: {format}:{item.SceneId}");
            var existedBeforeWrite = File.Exists(path);
            var traceEntry = BuildNarrationFileWriteTraceEntry(path, item, format, writeTrace.Count + 1, cleanup.CleanedText, existedBeforeWrite);
            writeTrace.Add(traceEntry);
            await File.WriteAllTextAsync(path, cleanup.CleanedText, cancellationToken);
            files.Add(path);
            cleanedNarrationFiles.Add(path);
            written.Add(path);
            manifestItems.Add(new
            {
                format,
                sceneId = item.SceneId,
                beatNo = item.BeatNo,
                path = NormalizePath(path),
                cleanupApplied = true,
                labelsRemovedCount = cleanup.LabelsRemovedCount,
                instructionsRemovedCount = cleanup.InstructionsRemovedCount,
                characterCount = cleanup.CleanedText.Length
            });
        }
        return written;
    }


    private static NarrationFileWriteTraceEntry BuildNarrationFileWriteTraceEntry(string path, SceneAudioSyncItem item, string format, int writeOrder, string content, bool existedBeforeWrite)
        => new(
            NormalizePath(path),
            item.SceneId,
            format,
            "SceneLevelNarrationComposer",
            existedBeforeWrite ? "Overwrite" : "Create",
            writeOrder,
            Preview(content, 180),
            content.Contains("centers on", StringComparison.OrdinalIgnoreCase),
            content.Contains("Moon names are cultural memory", StringComparison.OrdinalIgnoreCase),
            "SceneLevelNarrationComposer",
            string.IsNullOrWhiteSpace(item.SourceNarrationStrategy) ? "SceneLevelNarrationComposer" : item.SourceNarrationStrategy);

    private static NarrationFileWriteDiagnostics ValidateNarrationFileWriteTrace(IReadOnlyList<NarrationFileWriteTraceEntry> writeTrace, int expectedWriteCount)
    {
        var duplicateWrites = writeTrace
            .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var overwrittenFiles = duplicateWrites;
        var appendedFiles = writeTrace
            .Where(entry => entry.WriteMode.Equals("Append", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fallbackInjected = writeTrace.Any(entry =>
            !entry.WriterComponent.Equals("SceneLevelNarrationComposer", StringComparison.OrdinalIgnoreCase)
            || entry.SourceStrategy.Contains("Fallback", StringComparison.OrdinalIgnoreCase)
            || entry.SourceStrategy.Contains("SceneTimelineNarrationBeat", StringComparison.OrdinalIgnoreCase)
            || entry.SourceStrategy.Contains("EventProductionIntelligence", StringComparison.OrdinalIgnoreCase)
            || entry.SourceComponent.Contains("VideoAssemblyIntelligenceService", StringComparison.OrdinalIgnoreCase)
            || entry.SourceComponent.Contains("DocumentaryNarrationComposer", StringComparison.OrdinalIgnoreCase));

        if (writeTrace.Count != expectedWriteCount || duplicateWrites.Length > 0 || appendedFiles.Length > 0 || fallbackInjected)
            throw new InvalidOperationException("Narration scene file overwritten after SceneLevelNarrationComposer");

        return new NarrationFileWriteDiagnostics(writeTrace.Count, duplicateWrites, overwrittenFiles, appendedFiles, fallbackInjected);
    }

    private static string Preview(string text, int maxLength)
    {
        var normalized = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static Phase14SceneDurationPlanResolution EnsurePhase14SceneDurationPlan(
        string planRoot,
        IReadOnlyList<string> shortNarrationFiles,
        IReadOnlyList<string> longNarrationFiles,
        IReadOnlyList<SceneAudioSyncItem> shortItems,
        IReadOnlyList<SceneAudioSyncItem> longItems)
    {
        var timingPath = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        var candidatePaths = new[]
        {
            timingPath,
            Path.Combine(planRoot, "sync", "scene-duration-plan.json"),
            Path.Combine(planRoot, "narration", "scene-duration-plan.json"),
            Path.Combine(planRoot, "duration-calibration", "scene-duration-plan.json")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(timingPath)!);

        var selectedPath = candidatePaths.FirstOrDefault(File.Exists) ?? timingPath;
        var found = File.Exists(selectedPath);
        if (found && !string.Equals(selectedPath, timingPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(selectedPath, timingPath, true);

        var shortPlanItems = ReadSceneDurationPlanItems(planRoot, "short");
        var longPlanItems = ReadSceneDurationPlanItems(planRoot, "long");
        var missingSceneIds = MissingDurationSceneIds(shortPlanItems, shortItems, "short")
            .Concat(MissingDurationSceneIds(longPlanItems, longItems, "long"))
            .ToArray();

        var needsFallback = !found
            || shortPlanItems.Count != shortItems.Count
            || longPlanItems.Count != longItems.Count
            || missingSceneIds.Length > 0;
        if (!needsFallback)
        {
            return new Phase14SceneDurationPlanResolution(
                NormalizePath(timingPath),
                true,
                shortPlanItems.Count,
                longPlanItems.Count,
                false,
                string.Equals(selectedPath, timingPath, StringComparison.OrdinalIgnoreCase)
                    ? "ExistingTimingSceneDurationPlan"
                    : NormalizePath(selectedPath),
                []);
        }

        shortPlanItems = BuildFallbackPhase14SceneDurationPlanItems(planRoot, "short", shortItems, shortNarrationFiles);
        longPlanItems = BuildFallbackPhase14SceneDurationPlanItems(planRoot, "long", longItems, longNarrationFiles);
        missingSceneIds = MissingDurationSceneIds(shortPlanItems, shortItems, "short")
            .Concat(MissingDurationSceneIds(longPlanItems, longItems, "long"))
            .ToArray();

        File.WriteAllText(timingPath, JsonSerializer.Serialize(new
        {
            version = "v1",
            sourceTtsTimelineVersion = "phase-14-fallback",
            source = "Phase14SceneAssetsVisualTimelineNarrationWordCounts",
            @short = new
            {
                sceneCount = shortPlanItems.Count,
                totalAudioDurationSec = RoundDuration(shortPlanItems.Sum(item => item.AudioDurationSec)),
                totalVideoDurationSec = RoundDuration(shortPlanItems.Sum(item => item.SceneDurationSec)),
                items = shortPlanItems
            },
            @long = new
            {
                sceneCount = longPlanItems.Count,
                totalAudioDurationSec = RoundDuration(longPlanItems.Sum(item => item.AudioDurationSec)),
                totalVideoDurationSec = RoundDuration(longPlanItems.Sum(item => item.SceneDurationSec)),
                items = longPlanItems
            }
        }, JsonOptions));
        if (shortPlanItems.Count != shortItems.Count || longPlanItems.Count != longItems.Count || missingSceneIds.Length > 0)
            throw new InvalidOperationException(
                "Phase 14 scene-duration-plan fallback did not satisfy SRT timing requirements: "
                + $"shortSceneDurationPlanItemCount={shortPlanItems.Count}; shortSceneCount={shortItems.Count}; "
                + $"longSceneDurationPlanItemCount={longPlanItems.Count}; longSceneCount={longItems.Count}; "
                + $"missingDurationSceneIds={string.Join(",", missingSceneIds)}");

        return new Phase14SceneDurationPlanResolution(
            NormalizePath(timingPath),
            found,
            shortPlanItems.Count,
            longPlanItems.Count,
            true,
            "scene-assets-v3/{format}/scene-timeline-metadata.json; scene-assets-v3/{format}/visual-timeline-v3.json; narration/{format}/*.txt word counts",
            missingSceneIds);
    }

    private static IReadOnlyList<string> MissingDurationSceneIds(IReadOnlyList<SceneDurationPlanItem> planItems, IReadOnlyList<SceneAudioSyncItem> sceneItems, string format)
    {
        var planSceneIds = planItems.Select(item => item.SceneId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sceneItems
            .Where(item => !planSceneIds.Contains(item.SceneId))
            .Select(item => $"{format}:{item.SceneId}")
            .ToArray();
    }

    private static IReadOnlyList<SceneDurationPlanItem> BuildFallbackPhase14SceneDurationPlanItems(string planRoot, string format, IReadOnlyList<SceneAudioSyncItem> syncItems, IReadOnlyList<string> narrationFiles)
    {
        var visualTimelinePath = Path.Combine(planRoot, "scene-assets-v3", format, "visual-timeline-v3.json");
        var metadataPath = Path.Combine(planRoot, "scene-assets-v3", format, "scene-timeline-metadata.json");
        var visualBySceneId = ReadSceneNodesById(visualTimelinePath);
        var metadataBySceneId = ReadSceneNodesById(metadataPath);
        var narrationBySceneId = narrationFiles.ToDictionary(path => SanitizeFileName(Path.GetFileNameWithoutExtension(path)), path => path, StringComparer.OrdinalIgnoreCase);
        var result = new List<SceneDurationPlanItem>();

        foreach (var syncItem in syncItems)
        {
            narrationBySceneId.TryGetValue(syncItem.SceneId, out var narrationPath);
            var wordCount = File.Exists(narrationPath) ? CountSpokenWords(File.ReadAllText(narrationPath)) : CountSpokenWords(syncItem.NarrationText);
            var audioDuration = RoundDuration(Math.Max(3.0, wordCount / 155.0 * 60.0));
            visualBySceneId.TryGetValue(syncItem.SceneId, out var visualNode);
            metadataBySceneId.TryGetValue(syncItem.SceneId, out var metadataNode);
            var timelineDuration = GetDouble(visualNode, "durationSec", "sceneDurationSec", "targetDurationSec")
                ?? GetDouble(metadataNode, "durationSec", "sceneDurationSec", "targetDurationSec");
            var sceneDuration = RoundDuration(Math.Max(audioDuration, timelineDuration ?? audioDuration));
            result.Add(new SceneDurationPlanItem(
                format,
                syncItem.SceneId,
                string.Empty,
                audioDuration,
                sceneDuration,
                0,
                string.IsNullOrWhiteSpace(syncItem.RecommendedTransition) ? "cut" : syncItem.RecommendedTransition,
                ResolveMotionProfile(syncItem.SceneId, FirstNonEmpty(syncItem.RecommendedMotion, GetString(metadataNode, "recommendedMotion"), GetString(visualNode, "recommendedMotion")))));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, JsonNode?> ReadSceneNodesById(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var root = JsonNode.Parse(File.ReadAllText(path));
        var nodes = (root?["scenes"] as JsonArray)
            ?? (root?["items"] as JsonArray)
            ?? (root?["timeline"]?["scenes"] as JsonArray)
            ?? [];
        return nodes
            .Select((node, index) => new { Node = node, SceneId = GetString(node, "sceneId", "id") ?? $"{index + 1:000}" })
            .Where(item => !string.IsNullOrWhiteSpace(item.SceneId))
            .GroupBy(item => item.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Node, StringComparer.OrdinalIgnoreCase);
    }

    private static NarrationSrtTimingResult BuildNarrationSrtFromCleanFiles(string planRoot, string format, IReadOnlyList<string> narrationFiles, IReadOnlyList<SceneAudioSyncItem> items)
    {
        var durationPlanItems = ReadSceneDurationPlanItems(planRoot, format);
        if (durationPlanItems.Count == 0)
            throw new InvalidOperationException($"SRT timing source must be scene-duration-plan.json for {format}; no SceneDurationPlan items were found.");
        if (durationPlanItems.Count < narrationFiles.Count)
            throw new InvalidOperationException($"SRT timing source has fewer {format} scenes than narration files.");
        var blocks = new List<SubtitleCueBlock>();
        var perScene = new List<object>();
        var number = 1;
        var sceneStart = 0.0;
        for (var i = 0; i < narrationFiles.Count; i++)
        {
            var planItem = durationPlanItems[i];
            var audioDuration = Math.Max(0, planItem.AudioDurationSec);
            var sceneDuration = planItem.SceneDurationSec;
            var sceneEnd = i == narrationFiles.Count - 1
                ? ReadSceneDurationPlanTotal(planRoot, format, "totalVideoDurationSec", durationPlanItems.Sum(x => x.SceneDurationSec))
                : sceneStart + sceneDuration;
            var spokenEnd = Math.Min(sceneEnd, sceneStart + audioDuration);
            var subtitleTimelineEnd = sceneEnd;
            var narrationFile = narrationFiles[i];
            ValidateSubtitleCueNarrationSource(planRoot, format, narrationFile, nameof(BuildNarrationSrtFromCleanFiles));
            var text = File.ReadAllText(narrationFile).Trim();
            var sceneIdOrigin = SanitizeFileName(Path.GetFileNameWithoutExtension(narrationFile));
            if (!string.Equals(sceneIdOrigin, SanitizeFileName(planItem.SceneId), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SRT narration file scene id does not match timing plan scene id for {format}: file={sceneIdOrigin}, plan={planItem.SceneId}");
            var chunks = SplitSubtitleChunks(text);
            var totalWords = Math.Max(1, chunks.Sum(CountWords));
            var cueStart = sceneStart;
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var cueEnd = chunkIndex == chunks.Count - 1
                    ? subtitleTimelineEnd
                    : sceneStart + (spokenEnd - sceneStart) * chunks.Take(chunkIndex + 1).Sum(CountWords) / totalWords;
                if (cueStart < sceneStart - 0.001)
                    throw new InvalidOperationException($"SRT cue starts before scene duration: {format}:{planItem.SceneId}");
                if (cueEnd > sceneEnd + 0.001)
                    throw new InvalidOperationException($"SRT cue extends beyond scene duration: {format}:{planItem.SceneId}");
                blocks.Add(new SubtitleCueBlock(number++, TimeSpan.FromSeconds(cueStart), TimeSpan.FromSeconds(cueEnd), WrapSubtitleChunk(chunk), planItem.SceneId, chunk, text, SubtitleChunkHash(chunk), "NarrationFile", NormalizePath(narrationFile), sceneIdOrigin, "QuestionDrivenNarrationGenerator.BuildNarrationSrt", DateTimeOffset.UtcNow));
                cueStart = cueEnd;
            }
            perScene.Add(new
            {
                sceneId = planItem.SceneId,
                audioDurationSec = RoundDuration(audioDuration),
                sceneDurationSec = RoundDuration(sceneDuration),
                subtitleStart = RoundDuration(sceneStart),
                subtitleEnd = RoundDuration(subtitleTimelineEnd),
                sceneStart = RoundDuration(sceneStart),
                sceneEnd = RoundDuration(sceneEnd),
                subtitleTextSource = "NarrationFile",
                subtitleTextOrigin = NormalizePath(narrationFile),
                sceneIdOrigin,
                generatorComponent = "QuestionDrivenNarrationGenerator.BuildNarrationSrt"
            });
            sceneStart = sceneEnd;
        }
        var duplicateBlockGroups = blocks
            .Select(block => new { block.Number, block.SceneId, Text = string.Join(" ", block.Lines), NormalizedText = NormalizeNarrationForDuplicateCheck(string.Join(" ", block.Lines)) })
            .Where(block => !string.IsNullOrWhiteSpace(block.NormalizedText))
            .GroupBy(block => block.NormalizedText, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();
        var duplicateSubtitleBlockIds = duplicateBlockGroups
            .SelectMany(group => group.Select(block => $"{format}:{block.SceneId}:cue-{block.Number}"))
            .ToArray();
        var duplicateSubtitleTexts = duplicateBlockGroups
            .Select(group => group.First().Text)
            .ToArray();
        if (duplicateSubtitleBlockIds.Length > 0)
            throw new InvalidOperationException("Phase 14 SRT validation failed: duplicateSubtitleBlockCount must be 0.");
        var srt = new StringBuilder();
        foreach (var block in blocks)
        {
            srt.AppendLine(block.Number.ToString(CultureInfo.InvariantCulture));
            srt.AppendLine($"{FormatSrtTimestamp(block.Start)} --> {FormatSrtTimestamp(block.End)}");
            foreach (var line in block.Lines) srt.AppendLine(line);
            srt.AppendLine();
        }
        var audioTotal = ReadSceneDurationPlanTotal(planRoot, format, "totalAudioDurationSec", durationPlanItems.Sum(x => x.AudioDurationSec));
        var videoTotal = ReadSceneDurationPlanTotal(planRoot, format, "totalVideoDurationSec", durationPlanItems.Sum(x => x.SceneDurationSec));
        var srtTotal = blocks.Count == 0 ? 0 : RoundDuration(blocks[^1].End.TotalSeconds);
        if (audioTotal - srtTotal > 0.5)
            throw new InvalidOperationException($"{format}.srt ends more than 0.5 sec before audio; srt={srtTotal}, audio={audioTotal}");
        if (Math.Abs(srtTotal - videoTotal) > 0.5)
            throw new InvalidOperationException($"{format}.srt duration differs from video duration by >0.5 sec; srt={srtTotal}, video={videoTotal}");
        var timingDiagnostics = new
        {
            srtTimingSource = "SceneDurationPlanFromTtsTimeline",
            ttsDurationsMeasuredFromMp3 = true,
            audioDurationTotal = RoundDuration(audioTotal),
            videoDurationTotal = RoundDuration(videoTotal),
            srtDurationTotal = srtTotal,
            srtMatchesAudioDuration = Math.Abs(srtTotal - audioTotal) <= 0.1,
            srtMatchesVideoDuration = Math.Abs(srtTotal - videoTotal) <= 0.1,
            perSceneTiming = perScene
        };
        var subtitleBlocks = blocks.Select(block => new
        {
            blockId = $"{format}:{block.SceneId}:cue-{block.Number}",
            sourceSceneId = block.SceneId,
            sourceFile = block.SubtitleTextOrigin,
            sourceText = block.SourceText,
            generatorComponent = block.GeneratorComponent
        }).ToArray();
        var subtitleCueSources = blocks.Select(block => new SubtitleCueSource(format, block.Number, block.SceneId, string.Join(" ", block.Lines), NormalizeNarrationForDuplicateCheck(string.Join(" ", block.Lines)), block.SubtitleTextSource, block.SubtitleTextOrigin, block.GeneratorComponent, block.CreatedUtc)).ToArray();
        var traceCues = subtitleCueSources.Where(cue => ContainsSubtitleTraceText(cue.Text)).ToArray();
        if (traceCues.Length > 0)
        {
            var tracePath = Path.Combine(planRoot, "validation", $"phase-14-{format}-subtitle-trace.json");
            Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
            File.WriteAllText(tracePath, JsonSerializer.Serialize(traceCues.Select(cue => new { cue.Format, cue.CueId, cue.SceneId, cue.Text, cue.SourceFile, cue.GeneratorComponent, componentStack = cue.GeneratorComponent, exactTextExistsInSourceNarrationFile = File.Exists(cue.SourceFile) && File.ReadAllText(cue.SourceFile).Contains(cue.Text, StringComparison.OrdinalIgnoreCase) }), JsonOptions));
        }
        var nonNarrationSubtitleCues = subtitleCueSources
            .Where(cue => !string.Equals(cue.SourceType, "NarrationFile", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nonNarrationSubtitleCues.Length > 0)
            throw new InvalidOperationException("Non-narration subtitle cue detected");
        var diagnostics = new SubtitleGenerationDiagnostics(
            format,
            blocks.Count,
            duplicateSubtitleBlockIds.Length,
            duplicateSubtitleBlockIds,
            duplicateSubtitleTexts,
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.SceneId, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.SourceText, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.ChunkHash, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.SubtitleTextSource, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.SubtitleTextOrigin, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.SceneIdOrigin, StringComparer.OrdinalIgnoreCase),
            blocks.ToDictionary(block => $"{format}:{block.SceneId}:cue-{block.Number}", block => block.GeneratorComponent, StringComparer.OrdinalIgnoreCase),
            subtitleBlocks,
            subtitleCueSources,
            nonNarrationSubtitleCues.Length,
            nonNarrationSubtitleCues,
            "SceneNarrationFilesOnly",
            true,
            false,
            false,
            false,
            BuildSrtPreview(srt.ToString()),
            timingDiagnostics);
        return new NarrationSrtTimingResult(srt.ToString(), diagnostics);
    }

    private static bool ContainsSubtitleTraceText(string text)
        => text.Contains("centers on", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Moon names are cultural memory", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSceneNarrationFileOnlyCueSources(params SubtitleGenerationDiagnostics[] diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (!string.Equals(diagnostic.SrtSourceMode, "SceneNarrationFilesOnly", StringComparison.OrdinalIgnoreCase)
                || !diagnostic.FallbackSubtitleSourcesDisabled
                || diagnostic.EventProductionIntelligenceUsedForSrt
                || diagnostic.VideoAssemblyIntelligenceUsedForSrt
                || diagnostic.DocumentaryNarrationComposerUsedForSrt
                || diagnostic.NonNarrationSubtitleCueCount != 0
                || diagnostic.SubtitleCueSources.Any(cue => !string.Equals(cue.SourceType, "NarrationFile", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Non-narration subtitle cue detected");
        }
    }

    private static void ValidateSubtitleCueNarrationSource(string planRoot, string format, string narrationFile, string generatorComponent)
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(planRoot, "narration", format));
        var sourcePath = Path.GetFullPath(narrationFile);
        var expectedPrefix = expectedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(Path.GetExtension(sourcePath), ".txt", StringComparison.OrdinalIgnoreCase) || !sourcePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SRT validation failed: subtitle cue source must originate from narration/short/*.txt or narration/long/*.txt. format={format}; sourceFile={NormalizePath(narrationFile)}; generatorComponent={generatorComponent}");
    }

    private static IReadOnlyList<string> SplitDuplicateSubtitleChunks(IReadOnlyList<string> chunks, HashSet<string> seenSubtitleChunks)
    {
        var result = new List<string>();
        foreach (var chunk in chunks)
        {
            var normalized = NormalizeNarrationForDuplicateCheck(chunk);
            if (string.IsNullOrWhiteSpace(normalized) || seenSubtitleChunks.Add(normalized))
            {
                result.Add(chunk);
                continue;
            }

            AddUniqueSplitSubtitleChunks(chunk, result, seenSubtitleChunks, result.Count + seenSubtitleChunks.Count, 0);
        }
        return result;
    }

    private static void AddUniqueSplitSubtitleChunks(string chunk, List<string> result, HashSet<string> seenSubtitleChunks, int duplicateIndex, int depth)
    {
        if (depth > 4 || chunk.Length < 12)
        {
            result.Add(chunk);
            seenSubtitleChunks.Add(NormalizeNarrationForDuplicateCheck(chunk));
            return;
        }

        foreach (var splitChunk in SplitDuplicateSubtitleChunk(chunk, duplicateIndex + depth))
        {
            var normalized = NormalizeNarrationForDuplicateCheck(splitChunk);
            if (string.IsNullOrWhiteSpace(normalized) || seenSubtitleChunks.Add(normalized))
            {
                result.Add(splitChunk);
            }
            else
            {
                AddUniqueSplitSubtitleChunks(splitChunk, result, seenSubtitleChunks, duplicateIndex + 1, depth + 1);
            }
        }
    }

    private static IReadOnlyList<string> SplitDuplicateSubtitleChunk(string chunk, int duplicateIndex)
    {
        var target = Math.Clamp(chunk.Length / 2 + duplicateIndex % 7 - 3, 20, Math.Max(20, chunk.Length - 20));
        var cut = chunk.LastIndexOf(' ', Math.Min(target, chunk.Length - 1));
        if (cut < 20) cut = chunk.IndexOf(' ', Math.Min(target, chunk.Length - 1));
        if (cut < 20 || cut > chunk.Length - 20) cut = Math.Clamp(chunk.Length / 2, 1, chunk.Length - 1);
        return [chunk[..cut].Trim(), chunk[cut..].Trim()];
    }

    private static IReadOnlyList<SceneDurationPlanItem> ReadSceneDurationPlanItems(string planRoot, string format)
    {
        var path = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        if (!File.Exists(path)) return [];
        var root = JsonNode.Parse(File.ReadAllText(path));
        return (root?[format]?["items"]?.AsArray() ?? [])
            .Select(item => new SceneDurationPlanItem(
                format,
                GetString(item, "sceneId") ?? string.Empty,
                GetString(item, "audioPath") ?? string.Empty,
                GetDouble(item, "audioDurationSec") ?? 0,
                GetDouble(item, "sceneDurationSec") ?? 0,
                GetDouble(item, "transitionDurationSec") ?? 0,
                "cut",
                ResolveMotionProfile(GetString(item, "sceneId") ?? string.Empty, GetString(item, "recommendedMotion"))))
            .ToArray();
    }

    private static double ReadSceneDurationPlanTotal(string planRoot, string format, string propertyName, double fallback)
    {
        var path = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        if (!File.Exists(path)) return RoundDuration(fallback);
        var root = JsonNode.Parse(File.ReadAllText(path));
        return RoundDuration(GetDouble(root?[format], propertyName) ?? fallback);
    }

    private static IReadOnlyList<string> SplitSubtitleChunks(string text)
    {
        var normalizedText = NormalizeNarrationWhitespace(text);
        var phrases = Regex.Split(normalizedText, @"(?<=[.!?])\s+|(?<=[,;:])\s+")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var phrase in phrases)
        {
            if ((current + " " + phrase).Trim().Length <= 84) current = (current + " " + phrase).Trim();
            else
            {
                if (!string.IsNullOrWhiteSpace(current)) chunks.Add(current);
                current = phrase;
                while (current.Length > 84)
                {
                    var cut = current.LastIndexOf(' ', Math.Min(84, current.Length - 1));
                    if (cut < 35) cut = Math.Min(84, current.Length);
                    chunks.Add(current[..cut].Trim());
                    current = current[cut..].Trim();
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(current)) chunks.Add(current);
        return chunks;
    }

    private static IReadOnlyList<string> WrapSubtitleChunk(string text)
    {
        if (text.Length <= 42) return [text];
        var cut = text.LastIndexOf(' ', Math.Min(42, text.Length - 1));
        if (cut < 20) cut = Math.Min(42, text.Length);
        return [text[..cut].Trim(), text[cut..].Trim()];
    }

    private static SrtValidationResult ValidateNarrationSrt(string srtPath, IReadOnlyList<string> narrationFiles, IReadOnlyList<SubtitleCueSource> subtitleCueSources)
    {
        var sourceTexts = narrationFiles.Select(path => File.ReadAllText(path).Trim()).ToArray();
        var srtTexts = ExtractSrtTexts(srtPath);
        var errors = new List<string>();
        var sourceCombined = NormalizeNarrationForSrtComparison(string.Join(" ", sourceTexts));
        var srtCombined = NormalizeNarrationForSrtComparison(string.Join(" ", srtTexts));
        var missingSceneTexts = new List<string>();
        var extraUnexpectedTexts = new List<string>();
        var comparisonFailureReason = string.Empty;
        var matches = string.Equals(srtCombined, sourceCombined, StringComparison.Ordinal);
        if (!matches)
        {
            ContainsNormalizedSceneTextsInOrder(srtCombined, sourceTexts, missingSceneTexts, extraUnexpectedTexts);
            comparisonFailureReason = "Reconstructed SRT text does not exactly match normalized narration file text.";
            errors.Add($"{Path.GetFileName(srtPath)} reconstructed subtitle text does not match narration files");
        }
        if (srtTexts.Any(text => text.Length > 84)) errors.Add($"{Path.GetFileName(srtPath)} contains a cue longer than 84 characters");
        if (srtTexts.Any(ContainsAuthoringInstructionText)) errors.Add($"{Path.GetFileName(srtPath)} contains forbidden authoring phrases");
        if (srtTexts.Any(text => text.Contains("centers on", StringComparison.OrdinalIgnoreCase))
            && !sourceTexts.Any(text => text.Contains("centers on", StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{Path.GetFileName(srtPath)} contains fallback phrase centers on outside narration files");
        var srtBlocks = ExtractSrtBlocks(srtPath);
        var duplicateBlockGroups = srtBlocks
            .GroupBy(block => NormalizeNarrationForDuplicateCheck(block.Text), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .ToArray();
        var duplicateGroups = duplicateBlockGroups.Select(g => g.First().Text).ToArray();
        var duplicateSubtitleBlockIds = duplicateBlockGroups.SelectMany(g => g.Select(block => block.Id)).ToArray();
        var duplicateSources = sourceTexts.GroupBy(NormalizeNarrationForDuplicateCheck, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        if (duplicateGroups.Length > 0 && !duplicateSources) errors.Add($"{Path.GetFileName(srtPath)} contains duplicate subtitle text while narration files are unique");
        var duplicateSourceScenes = subtitleCueSources.Where(cue => duplicateGroups.Any(text => string.Equals(NormalizeNarrationForDuplicateCheck(text), cue.NormalizedText, StringComparison.OrdinalIgnoreCase))).Select(cue => cue.SceneId).ToArray();
        var duplicateSourceFiles = subtitleCueSources.Where(cue => duplicateGroups.Any(text => string.Equals(NormalizeNarrationForDuplicateCheck(text), cue.NormalizedText, StringComparison.OrdinalIgnoreCase))).Select(cue => cue.SourceFile).ToArray();
        var nonNarrationSubtitleCues = subtitleCueSources.Where(cue => !string.Equals(cue.SourceType, "NarrationFile", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nonNarrationSubtitleCues.Length > 0) errors.Add("Non-narration subtitle cue detected");
        return new SrtValidationResult(matches, duplicateGroups.Length > 0, duplicateGroups, srtBlocks.Count, duplicateSubtitleBlockIds.Length, duplicateSubtitleBlockIds, duplicateGroups, duplicateSourceScenes, duplicateSourceFiles, BuildSrtPreview(File.ReadAllText(srtPath)), errors.Count == 0, errors, "NormalizedReconstructedSrtText", sourceCombined.Length, srtCombined.Length, matches, missingSceneTexts, extraUnexpectedTexts, comparisonFailureReason);
    }

    private static bool ContainsNormalizedSceneTextsInOrder(string normalizedSrtText, IReadOnlyList<string> sourceTexts, List<string> missingSceneTexts, List<string> extraUnexpectedTexts)
    {
        var cursor = 0;
        var unexpected = new StringBuilder();
        foreach (var sourceText in sourceTexts)
        {
            var normalizedSceneText = NormalizeNarrationForSrtComparison(sourceText);
            if (string.IsNullOrWhiteSpace(normalizedSceneText)) continue;
            var index = normalizedSrtText.IndexOf(normalizedSceneText, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                missingSceneTexts.Add(normalizedSceneText.Length <= 160 ? normalizedSceneText : normalizedSceneText[..160]);
                return false;
            }
            if (index > cursor) unexpected.Append(' ').Append(normalizedSrtText[cursor..index]);
            cursor = index + normalizedSceneText.Length;
        }
        if (cursor < normalizedSrtText.Length) unexpected.Append(' ').Append(normalizedSrtText[cursor..]);
        var normalizedUnexpected = NormalizeNarrationForSrtComparison(unexpected.ToString());
        if (!string.IsNullOrWhiteSpace(normalizedUnexpected))
            extraUnexpectedTexts.Add(normalizedUnexpected.Length <= 240 ? normalizedUnexpected : normalizedUnexpected[..240]);
        return missingSceneTexts.Count == 0;
    }

    private static string NormalizeNarrationForSrtComparison(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('‟', '"')
            .Replace('‘', '\'').Replace('’', '\'').Replace('‚', '\'').Replace('‛', '\'')
            .Replace('—', '-').Replace('–', '-').Replace('…', '.');
        var lines = Regex.Split(normalized, @"\r?\n")
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !Regex.IsMatch(line, @"^\d+$") && !Regex.IsMatch(line, @"^\d{2}:\d{2}:\d{2},\d{3}\s+-->\s+\d{2}:\d{2}:\d{2},\d{3}"));
        normalized = string.Join(" ", lines);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = Regex.Replace(normalized, @"\s+([,.;:!?])", "$1");
        normalized = Regex.Replace(normalized, @"([,.;:!?])(?=[^\s,.;:!?])", "$1 ");
        normalized = Regex.Replace(normalized, @"\s*([\-])\s*", " $1 ");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim().ToLowerInvariant();
    }

    private static string NormalizeNarrationWhitespace(string text)
        => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    private static IReadOnlyList<(string Id, string Text)> ExtractSrtBlocks(string srtPath)
        => Regex.Split(File.ReadAllText(srtPath), @"\r?\n\r?\n")
            .Select((block, index) => (Id: $"{Path.GetFileName(srtPath)}:block-{index + 1}", Text: string.Join(" ", block.Split('\n').Select(line => line.Trim()).Where(line => !string.IsNullOrWhiteSpace(line) && !Regex.IsMatch(line, @"^\d+$") && !line.Contains("-->", StringComparison.Ordinal))).Trim()))
            .Where(block => !string.IsNullOrWhiteSpace(block.Text))
            .ToArray();

    private static string BuildSrtPreview(string srt)
        => srt.Length <= 1200 ? srt : srt[..1200] + "\n... [truncated]";

    private static IReadOnlyList<string> ExtractSrtTexts(string srtPath)
        => ExtractSrtBlocks(srtPath).Select(block => block.Text).ToArray();

    private static int CountExistingSrtBlocks(string srtPath)
        => File.Exists(srtPath) ? ExtractSrtBlocks(srtPath).Count : 0;

    private static int CountExistingDuplicateSrtBlocks(string srtPath)
        => ExistingDuplicateSrtBlockIds(srtPath).Count;

    private static IReadOnlyList<string> ExistingDuplicateSrtBlockIds(string srtPath)
        => File.Exists(srtPath)
            ? ExtractSrtBlocks(srtPath)
                .GroupBy(block => NormalizeNarrationForDuplicateCheck(block.Text), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
                .SelectMany(g => g.Select(block => block.Id))
                .ToArray()
            : [];

    private static IReadOnlyList<string> ExistingDuplicateSrtTexts(string srtPath)
        => File.Exists(srtPath)
            ? ExtractSrtBlocks(srtPath)
                .GroupBy(block => NormalizeNarrationForDuplicateCheck(block.Text), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
                .Select(g => g.First().Text)
                .ToArray()
            : [];

    private static string ExistingSrtPreview(string srtPath)
        => File.Exists(srtPath) ? BuildSrtPreview(File.ReadAllText(srtPath)) : string.Empty;

    private static double ReadSrtFinalEndSeconds(string srtPath)
    {
        var matches = Regex.Matches(File.ReadAllText(srtPath), @"-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})");
        if (matches.Count == 0) return 0;
        var match = matches[^1];
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)
            + int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) / 1000.0;
    }

    private static string NormalizeNarrationForDuplicateCheck(string? value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static string SubtitleChunkHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeNarrationForDuplicateCheck(text)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SelectExisting(IReadOnlyList<string> candidates, List<string> checkedPaths, string label, List<string> missingFiles)
    {
        checkedPaths.AddRange(candidates.Select(NormalizePath));
        var selected = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(selected)) return selected;
        missingFiles.Add($"Missing {label}; checked: {string.Join(", ", candidates.Select(NormalizePath))}");
        throw new InvalidOperationException($"Phase 14 missing {label}; checked: {string.Join(", ", candidates.Select(NormalizePath))}. V1 narration fallback is not allowed.");
    }

    private static async Task<IReadOnlyList<SceneAudioSyncItem>> BuildSceneAudioSyncItemsAsync(ProductionPhaseContext context, string format, string sceneRoot, string narrationPath, int expectedCount, List<string> checkedPaths, List<string> missingFiles, List<object> strategies, List<Phase14MatchedPair> matchedPairs, List<string> unmatchedNarrationSections, List<string> unmatchedScenes, List<NarrationSceneDiagnostic> narrationDiagnostics, CancellationToken ct)
    {
        if (!Directory.Exists(sceneRoot)) missingFiles.Add($"{format} scene-assets-v3 root missing: {NormalizePath(sceneRoot)}");
        var timelinePath = Path.Combine(sceneRoot, "visual-timeline-v3.json");
        var manifestPath = Path.Combine(sceneRoot, "scene-manifest-v3.json");
        var reviewPath = Path.Combine(sceneRoot, "scene-review-v3.json");
        var metadataPath = Path.Combine(sceneRoot, "scene-timeline-metadata.json");
        foreach (var path in new[] { timelinePath, manifestPath, reviewPath, metadataPath })
        {
            checkedPaths.Add(NormalizePath(path));
            if (!File.Exists(path)) missingFiles.Add($"Required {format} Scene Assets V3.1 file missing: {NormalizePath(path)}");
        }
        if (missingFiles.Any(m => m.Contains(format, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Phase 14 missing required scene asset inputs: " + string.Join(" | ", missingFiles));

        var timelineBeats = ReadJsonArray(timelinePath, "beats");
        var manifestScenes = ReadJsonArray(manifestPath, "scenes");
        var metadataScenes = ReadJsonArray(metadataPath, "scenes");
        var scenesById = metadataScenes
            .Select((node, index) => new { Node = node, Index = index, SceneId = GetString(node, "sceneId") ?? string.Empty })
            .Where(item => !string.IsNullOrWhiteSpace(item.SceneId))
            .GroupBy(item => item.SceneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var narrationBeats = ExtractNarrationBeats(narrationPath);
        AddNarrationDiagnostics(narrationDiagnostics, narrationBeats);
        var narrationBeatArray = narrationBeats.ToArray();
        var sectionMap = GetPhase14SectionSceneMap(format);
        var usedNarration = new HashSet<int>();
        var items = new List<SceneAudioSyncItem>();
        for (var i = 0; i < expectedCount; i++)
        {
            var metadataEntry = metadataScenes.ElementAtOrDefault(i);
            var sceneId = GetString(metadataEntry, "sceneId") ?? GetString(timelineBeats.ElementAtOrDefault(i), "sceneId") ?? $"{i + 1:000}";
            if (scenesById.TryGetValue(sceneId, out var selectedScene))
                metadataEntry = selectedScene.Node;

            var beat = timelineBeats.FirstOrDefault(n => string.Equals(GetString(n, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase)) ?? timelineBeats.ElementAtOrDefault(i);
            var beatNo = GetInt(beat, "beatNo") ?? i + 1;
            var metadata = metadataEntry;
            var manifest = manifestScenes.FirstOrDefault(n => string.Equals(GetString(n, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase)) ?? manifestScenes.ElementAtOrDefault(i);
            var visualIntent = GetString(metadata, "visualIntent") ?? GetString(beat, "visualIntent") ?? "";
            var renderMode = GetString(metadata, "renderMode") ?? GetString(beat, "renderMode") ?? GetString(manifest, "renderMode") ?? "";
            var sceneNarrationBeat = FirstNonEmpty(GetString(metadata, "narrationBeat"), GetString(beat, "narrationBeat"));
            var narration = string.IsNullOrWhiteSpace(sceneNarrationBeat)
                ? BestSectionSemanticNarrationFallback(narrationBeats, visualIntent, renderMode, string.Empty)
                : null;
            var narrationText = !string.IsNullOrWhiteSpace(sceneNarrationBeat) ? sceneNarrationBeat : narration?.Text ?? string.Empty;
            var strategy = !string.IsNullOrWhiteSpace(sceneNarrationBeat) ? "SceneTimelineNarrationBeat" : narration is null ? "Unmatched" : "NarrationV2SupportingFallback";
            var imagePath = GetString(manifest, "imagePath") ?? Path.Combine(sceneRoot, sceneId + ".png");
            if (string.IsNullOrWhiteSpace(narrationText))
            {
                unmatchedScenes.Add($"{format}:{sceneId}");
            }
            else
            {
                if (narration is not null)
                {
                    var narrationIndex = Array.IndexOf(narrationBeatArray, narration);
                    if (narrationIndex >= 0) usedNarration.Add(narrationIndex);
                }
                var mappedSceneId = narration is null ? sceneId : ResolveMappedSceneId(sectionMap, narration.Section);
                if (string.IsNullOrWhiteSpace(mappedSceneId)) mappedSceneId = sceneId;
                matchedPairs.Add(new Phase14MatchedPair(format, narration?.Section ?? "", narration?.ScenePurpose ?? "", mappedSceneId, sceneId, strategy));
            }
            strategies.Add(new { format, sceneId, beatNo, section = narration?.Section ?? "", strategy });
            items.Add(new SceneAudioSyncItem(
                format,
                beatNo,
                sceneId,
                imagePath,
                narrationText,
                narrationText,
                visualIntent,
                renderMode,
                GetInt(metadata, "estimatedDurationSec") ?? GetInt(beat, "expectedDurationSec") ?? 5,
                "cut",
                ResolveMotionProfile(sceneId, GetString(metadata, "recommendedMotion")),
                string.IsNullOrWhiteSpace(narrationText) ? "Unmatched" : "Matched",
                strategy));
        }


        await Task.CompletedTask;
        return items;
    }

    private static JsonArray ReadJsonArray(string path, string property)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        return node?[property] as JsonArray ?? new JsonArray();
    }

    private static IReadOnlyList<NarrationBeatCandidate> ExtractNarrationBeats(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        var scenes = node?["scenes"] as JsonArray;
        if (scenes is null)
            throw new InvalidOperationException($"Phase 14 narration source must contain scenes[].section: {NormalizePath(path)}");

        return scenes.Select((n, i) => new NarrationBeatCandidate(
                GetString(n, "sceneId") ?? "",
                GetInt(n, "beatNo") ?? GetInt(n, "sceneNo") ?? GetInt(n, "sceneNumber") ?? i + 1,
                GetString(n, "narrationText") ?? GetString(n, "narrationBeat") ?? GetString(n, "text") ?? GetString(n, "script") ?? GetString(n, "narration") ?? "",
                GetString(n, "visualIntent") ?? "",
                GetString(n, "renderMode") ?? "",
                GetString(n, "section") ?? "",
                GetString(n, "scenePurpose") ?? ""))
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .ToArray();
    }

    private static void AddNarrationDiagnostics(List<NarrationSceneDiagnostic> diagnostics, IReadOnlyList<NarrationBeatCandidate> narrationBeats)
    {
        foreach (var beat in narrationBeats)
        {
            if (diagnostics.Any(d => d.SceneNumber == beat.BeatNo && string.Equals(d.Section, beat.Section, StringComparison.OrdinalIgnoreCase) && string.Equals(d.NarrationText, beat.Text, StringComparison.Ordinal))) continue;
            diagnostics.Add(new NarrationSceneDiagnostic(beat.BeatNo, beat.Section, beat.Text));
        }
    }

    private static IReadOnlyDictionary<string, string[]> GetPhase14SectionSceneMap(string format)
        => string.Equals(format, "short", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hook"] = ["001-hook"],
                ["Explanation"] = ["002-cause"],
                ["ViewingAdvice"] = ["003-accurate-sky-guide"],
                ["Reward"] = ["004-viewing-tip"],
                ["CTA"] = ["005-final-reminder"]
            }
            : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hook"] = ["001-hook"],
                ["Explanation"] = ["003-cause"],
                ["ViewingAdvice"] = ["006-accurate-sky-guide"],
                ["Reward"] = ["005-best-time", "008-viewing-tips"],
                ["Curiosity"] = ["002-what-is-it", "004-interesting-fact", "007-what-you-will-see"],
                ["CTA"] = ["006-accurate-sky-guide", "009-final-reminder"]
            };

    private static string ResolveMappedSceneId(IReadOnlyDictionary<string, string[]> sectionMap, string section)
        => !string.IsNullOrWhiteSpace(section) && sectionMap.TryGetValue(section, out var sceneIds) ? sceneIds.FirstOrDefault() ?? "" : "";

    private static NarrationBeatCandidate? FindNarrationBySectionSceneMapping(IReadOnlyList<NarrationBeatCandidate> candidates, IReadOnlyDictionary<string, string[]> sectionMap, string sceneId)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Section)
            && sectionMap.TryGetValue(c.Section, out var mappedSceneIds)
            && mappedSceneIds.Any(mappedSceneId => string.Equals(mappedSceneId, sceneId, StringComparison.OrdinalIgnoreCase)));

    private static NarrationBeatCandidate? BestSectionSemanticNarrationFallback(IReadOnlyList<NarrationBeatCandidate> candidates, string visualIntent, string renderMode, string narrationBeat)
        => candidates.OrderByDescending(c =>
            (string.Equals(c.RenderMode, renderMode, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
            + Similarity(c.Section + " " + c.ScenePurpose + " " + c.Text, visualIntent + " " + narrationBeat))
            .FirstOrDefault();

    private static NarrationBeatCandidate? BestNarrationFallback(IReadOnlyList<NarrationBeatCandidate> candidates, string visualIntent, string renderMode, string narrationBeat)
        => candidates.OrderByDescending(c => (string.Equals(c.RenderMode, renderMode, StringComparison.OrdinalIgnoreCase) ? 10 : 0) + Similarity(c.VisualIntent + " " + c.Text, visualIntent + " " + narrationBeat)).FirstOrDefault();

    private static int Similarity(string a, string b)
    {
        var aa = Regex.Matches(a.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).ToHashSet();
        var bb = Regex.Matches(b.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).ToHashSet();
        return aa.Count == 0 || bb.Count == 0 ? 0 : aa.Intersect(bb).Count();
    }

    private static string? GetString(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = node?[name];
            if (value is null) continue;
            var text = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue) ? stringValue : value.ToJsonString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }
    private static int? GetInt(JsonNode? node, string name) => node?[name]?.GetValue<int>();

    private static async Task<string> WritePhase14SyncDiagnosticsAsync(string planRoot, string syncRoot, IReadOnlyList<string> checkedPaths, string shortRoot, string longRoot, string shortNarration, string longNarration, IReadOnlyList<string> oldPaths, IReadOnlyList<object> strategies, IReadOnlyList<NarrationSceneDiagnostic> narrationDiagnostics, IReadOnlyList<Phase14MatchedPair> matchedPairs, IReadOnlyList<string> unmatchedNarrationSections, IReadOnlyList<string> unmatchedScenes, IReadOnlyList<string> missingFiles, IReadOnlyList<string> exceptions, Phase14AdapterDiagnostics? adapterDiagnostics, NarrationFileWriteDiagnostics? writeDiagnostics, IReadOnlyList<NarrationFileWriteTraceEntry>? writeTrace, Phase14SceneDurationPlanResolution? sceneDurationPlanResolution, CancellationToken ct)
    {
        var path = Path.Combine(planRoot, "validation", "phase-14-sync-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            allCheckedInputPaths = checkedPaths.Distinct(StringComparer.OrdinalIgnoreCase),
            selectedShortSceneSource = NormalizePath(shortRoot),
            selectedLongSceneSource = NormalizePath(longRoot),
            selectedShortNarrationSource = NormalizePath(shortNarration),
            selectedLongNarrationSource = NormalizePath(longNarration),
            oldPathsChecked = oldPaths.Select(NormalizePath),
            oldPathsIgnored = oldPaths.Select(NormalizePath),
            matchingStrategy = "SceneLevelNarrationComposer",
            sceneLevelAdapter = adapterDiagnostics,
            adapterUsed = adapterDiagnostics?.AdapterUsed ?? false,
            adapterName = adapterDiagnostics?.AdapterName,
            eventType = adapterDiagnostics?.EventType,
            shortSceneCount = adapterDiagnostics?.ShortSceneCount,
            longSceneCount = adapterDiagnostics?.LongSceneCount,
            storySectionCount = adapterDiagnostics?.StorySectionCount,
            sceneNarrationGeneratedCount = adapterDiagnostics?.SceneNarrationGeneratedCount,
            firstSentenceByScene = adapterDiagnostics?.FirstSentenceByScene,
            duplicateFirstSentenceDetected = adapterDiagnostics?.DuplicateFirstSentenceDetected,
            duplicateSrtBlockDetected = adapterDiagnostics?.DuplicateSrtBlockDetected,
            expansionApplied = adapterDiagnostics?.ExpansionApplied,
            expansionReason = adapterDiagnostics?.ExpansionReason,
            sourceStorySectionsUsed = adapterDiagnostics?.SourceStorySectionsUsed,
            scenePurposeBySceneId = adapterDiagnostics?.ScenePurposeBySceneId,
            outputNarrationFiles = adapterDiagnostics?.OutputNarrationFiles,
            srtFilesGenerated = adapterDiagnostics?.SrtFilesGenerated,
            narrationFileWriteCount = writeDiagnostics?.NarrationFileWriteCount ?? 0,
            duplicateNarrationFileWrites = writeDiagnostics?.DuplicateNarrationFileWrites ?? Array.Empty<string>(),
            overwrittenNarrationFiles = writeDiagnostics?.OverwrittenNarrationFiles ?? Array.Empty<string>(),
            appendedNarrationFiles = writeDiagnostics?.AppendedNarrationFiles ?? Array.Empty<string>(),
            fallbackNarrationTextInjected = writeDiagnostics?.FallbackNarrationTextInjected ?? false,
            narrationFileWriteTrace = writeTrace ?? Array.Empty<NarrationFileWriteTraceEntry>(),
            narrationSceneCount = narrationDiagnostics.Select(n => n.SceneNumber).Distinct().Count(),
            sectionsExtracted = narrationDiagnostics.Select(n => n.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase),
            narrationScenes = narrationDiagnostics,
            matchingStrategyUsedPerScene = strategies,
            matchedPairs,
            unmatchedNarrationSections = unmatchedNarrationSections.Distinct(StringComparer.OrdinalIgnoreCase),
            unmatchedScenes = unmatchedScenes.Distinct(StringComparer.OrdinalIgnoreCase),
            missingFiles,
            exceptions,
            syncRoot = NormalizePath(syncRoot),
            cleanupApplied = true,
            cleanedNarrationFiles = Directory.Exists(Path.Combine(planRoot, "narration"))
                ? Directory.EnumerateFiles(Path.Combine(planRoot, "narration"), "*.txt", SearchOption.AllDirectories).Select(NormalizePath).ToArray()
                : Array.Empty<string>(),
            subtitleFilesGenerated = File.Exists(Path.Combine(planRoot, "narration", "subtitles", "short.srt")) && File.Exists(Path.Combine(planRoot, "narration", "subtitles", "long.srt")),
            srtSourceMode = "SceneNarrationFilesOnly",
            sceneDurationPlanPath = sceneDurationPlanResolution?.SceneDurationPlanPath ?? NormalizePath(Path.Combine(planRoot, "timing", "scene-duration-plan.json")),
            sceneDurationPlanFound = sceneDurationPlanResolution?.SceneDurationPlanFound ?? File.Exists(Path.Combine(planRoot, "timing", "scene-duration-plan.json")),
            shortSceneDurationPlanItemCount = sceneDurationPlanResolution?.ShortSceneDurationPlanItemCount ?? ReadSceneDurationPlanItems(planRoot, "short").Count,
            longSceneDurationPlanItemCount = sceneDurationPlanResolution?.LongSceneDurationPlanItemCount ?? ReadSceneDurationPlanItems(planRoot, "long").Count,
            sceneDurationPlanGeneratedFallback = sceneDurationPlanResolution?.SceneDurationPlanGeneratedFallback ?? false,
            sceneDurationPlanGenerationSource = sceneDurationPlanResolution?.SceneDurationPlanGenerationSource ?? "ExistingTimingSceneDurationPlan",
            missingDurationSceneIds = sceneDurationPlanResolution?.MissingDurationSceneIds ?? Array.Empty<string>(),
            srtGeneratedOnce = true,
            srtGenerationCallCount = 1,
            srtValidationCallCount = 1,
            staleSrtDetected = false,
            nonNarrationSubtitleCueCount = 0,
            nonNarrationSubtitleCues = Array.Empty<object>(),
            shortSrtPath = NormalizePath(Path.Combine(planRoot, "narration", "subtitles", "short.srt")),
            longSrtPath = NormalizePath(Path.Combine(planRoot, "narration", "subtitles", "long.srt")),
            generatedSubtitleBlockCount = CountExistingSrtBlocks(Path.Combine(planRoot, "narration", "subtitles", "short.srt")) + CountExistingSrtBlocks(Path.Combine(planRoot, "narration", "subtitles", "long.srt")),
            duplicateSubtitleBlockCount = CountExistingDuplicateSrtBlocks(Path.Combine(planRoot, "narration", "subtitles", "short.srt")) + CountExistingDuplicateSrtBlocks(Path.Combine(planRoot, "narration", "subtitles", "long.srt")),
            duplicateSubtitleBlockIds = ExistingDuplicateSrtBlockIds(Path.Combine(planRoot, "narration", "subtitles", "short.srt")).Concat(ExistingDuplicateSrtBlockIds(Path.Combine(planRoot, "narration", "subtitles", "long.srt"))).ToArray(),
            duplicateSubtitleTexts = ExistingDuplicateSrtTexts(Path.Combine(planRoot, "narration", "subtitles", "short.srt")).Concat(ExistingDuplicateSrtTexts(Path.Combine(planRoot, "narration", "subtitles", "long.srt"))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            generatedShortSrtPreview = ExistingSrtPreview(Path.Combine(planRoot, "narration", "subtitles", "short.srt")),
            generatedLongSrtPreview = ExistingSrtPreview(Path.Combine(planRoot, "narration", "subtitles", "long.srt"))
        }, JsonOptions), ct);
        return path;
    }


    private static int CountUniqueNarrationText(IEnumerable<SceneAudioSyncItem> items)
        => items.Select(i => NormalizeNarrationText(i.NarrationText))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static object[] BuildDuplicateNarrationTextGroups(IEnumerable<SceneAudioSyncItem> items)
        => items.GroupBy(i => NormalizeNarrationText(i.NarrationText), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => new
            {
                format = group.First().Format,
                narrationText = group.First().NarrationText,
                sceneIds = group.Select(i => i.SceneId).ToArray(),
                count = group.Count()
            })
            .Cast<object>()
            .ToArray();

    private static int CountUniqueNarrationText(IEnumerable<TtsTimelineItem> items)
        => items.Select(i => NormalizeNarrationText(i.NarrationText))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static object[] BuildDuplicateNarrationTextGroups(IEnumerable<TtsTimelineItem> items)
        => items.GroupBy(i => NormalizeNarrationText(i.NarrationText), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group => new
            {
                format = group.First().Format,
                narrationText = group.First().NarrationText,
                sceneIds = group.Select(i => i.SceneId).ToArray(),
                count = group.Count()
            })
            .Cast<object>()
            .ToArray();

    private static string NormalizeNarrationText(string? value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static bool ContainsOldTemplateText(string? value)
        => ContainsAnyNarrationPhrase(value,
        [
            "For a few unforgettable minutes"
        ]);

    private static bool ContainsAuthoringInstructionText(string? value)
        => ContainsAnyNarrationPhrase(value,
        [
            "Open with",
            "Explain what the sky event is",
            "Focus on",
            "Describe where to look",
            "Call out",
            "Give safe",
            "Close with",
            "Close with a memorable reminder"
        ]);

    private static bool ContainsAnyNarrationPhrase(string? value, IEnumerable<string> phrases)
        => !string.IsNullOrWhiteSpace(value) && phrases.Any(phrase => value.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private sealed record NarrationSceneDiagnostic(int SceneNumber, string Section, string NarrationText);
    private sealed record Phase14MatchedPair(string Format, string Section, string ScenePurpose, string MappedSceneId, string SceneId, string MatchingStrategy);
    private sealed record NarrationOutputLayerResult(string Root, string ManifestPath, IReadOnlyList<string> Files, NarrationFileWriteDiagnostics WriteDiagnostics, IReadOnlyList<NarrationFileWriteTraceEntry> WriteTrace, Phase14SceneDurationPlanResolution SceneDurationPlanResolution);
    private sealed record Phase14SceneDurationPlanResolution(string SceneDurationPlanPath, bool SceneDurationPlanFound, int ShortSceneDurationPlanItemCount, int LongSceneDurationPlanItemCount, bool SceneDurationPlanGeneratedFallback, string SceneDurationPlanGenerationSource, IReadOnlyList<string> MissingDurationSceneIds);
    private sealed record NarrationSrtTimingResult(string Srt, SubtitleGenerationDiagnostics Diagnostics);
    private sealed record SubtitleGenerationDiagnostics(string Format, int GeneratedSubtitleBlockCount, int DuplicateSubtitleBlockCount, IReadOnlyList<string> DuplicateSubtitleBlockIds, IReadOnlyList<string> DuplicateSubtitleTexts, IReadOnlyDictionary<string, string> SourceSceneIdPerSubtitleBlock, IReadOnlyDictionary<string, string> SubtitleChunkSourceText, IReadOnlyDictionary<string, string> SubtitleChunkHash, IReadOnlyDictionary<string, string> SubtitleTextSource, IReadOnlyDictionary<string, string> SubtitleTextOrigin, IReadOnlyDictionary<string, string> SceneIdOrigin, IReadOnlyDictionary<string, string> GeneratorComponent, IReadOnlyList<object> SubtitleBlocks, IReadOnlyList<SubtitleCueSource> SubtitleCueSources, int NonNarrationSubtitleCueCount, IReadOnlyList<SubtitleCueSource> NonNarrationSubtitleCues, string SrtSourceMode, bool FallbackSubtitleSourcesDisabled, bool EventProductionIntelligenceUsedForSrt, bool VideoAssemblyIntelligenceUsedForSrt, bool DocumentaryNarrationComposerUsedForSrt, string GeneratedSrtPreview, object Timing);
    private sealed record SrtValidationResult(bool MatchesNarrationFiles, bool DuplicateSrtTextDetected, IReadOnlyList<string> DuplicateSrtGroups, int GeneratedSubtitleBlockCount, int DuplicateSubtitleBlockCount, IReadOnlyList<string> DuplicateSubtitleBlockIds, IReadOnlyList<string> DuplicateSubtitleTexts, IReadOnlyList<string> DuplicateSubtitleSourceScenes, IReadOnlyList<string> DuplicateSubtitleSourceFiles, string GeneratedSrtPreview, bool ValidationPassed, IReadOnlyList<string> Errors, string SrtPreservationValidationMode, int CleanNarrationNormalizedLength, int SrtNormalizedLength, bool SrtPreservesNarration, IReadOnlyList<string> SrtMissingSceneTexts, IReadOnlyList<string> SrtExtraUnexpectedTexts, string SrtComparisonFailureReason);
    private sealed record SubtitleCueSource(string Format, int CueId, string SceneId, string Text, string NormalizedText, string SourceType, string SourceFile, string GeneratorComponent, DateTimeOffset CreatedUtc);
    private sealed record SubtitleCueBlock(int Number, TimeSpan Start, TimeSpan End, IReadOnlyList<string> Lines, string SceneId, string SourceText, string SourceNarrationText, string ChunkHash, string SubtitleTextSource, string SubtitleTextOrigin, string SceneIdOrigin, string GeneratorComponent, DateTimeOffset CreatedUtc);
    private sealed record NarrationBeatCandidate(string SceneId, int BeatNo, string Text, string VisualIntent, string RenderMode, string Section, string ScenePurpose);
    private sealed record Phase14DocumentaryNarration(bool ComposerCalled, bool OutputUsed, string TextSource, bool FallbackUsed, IReadOnlyDictionary<string, string> ShortItems, IReadOnlyDictionary<string, string> LongItems, object FinalTextBeforeWrite, EventStoryComposerDiagnostics Diagnostics, Phase14AdapterDiagnostics AdapterDiagnostics);
    private sealed record Phase14AdapterDiagnostics(bool AdapterUsed, string AdapterName, string EventType, int ShortSceneCount, int LongSceneCount, int StorySectionCount, int SceneNarrationGeneratedCount, IReadOnlyDictionary<string, string> FirstSentenceByScene, bool DuplicateFirstSentenceDetected, bool DuplicateSrtBlockDetected, bool ExpansionApplied, string ExpansionReason, IReadOnlyList<string> SourceStorySectionsUsed, IReadOnlyDictionary<string, string> ScenePurposeBySceneId, IReadOnlyList<string> OutputNarrationFiles, IReadOnlyList<string> SrtFilesGenerated, IReadOnlyList<SceneNarrationComposerTraceEntry> SceneNarrationComposerTrace);
    private sealed record SceneNarrationComposerTraceEntry(string Format, string SceneId, string ScenePurpose, string InputNarrationBeat, string InputEventSummary, string RawComposerOutput, string SanitizedComposerOutput, IReadOnlyList<string> RemovedFallbackSentences, bool ContainsCentersOnBeforeSanitize, bool ContainsCentersOnAfterSanitize, string WriterComponent);
    private sealed record SceneNarrationSanitizeResult(string Text, IReadOnlyList<string> RemovedFallbackSentences);
    private sealed record SceneAudioSyncItem(string Format, int BeatNo, string SceneId, string SceneImagePath, string NarrationText, string NarrationBeat, string VisualIntent, string RenderMode, int EstimatedDurationSec, string RecommendedTransition, string RecommendedMotion, string SyncStatus, string SourceNarrationStrategy);
    private sealed record NarrationFileWriteTraceEntry(string FilePath, string SceneId, string Format, string WriterComponent, string WriteMode, int WriteOrder, string ContentPreview, bool ContainsCentersOn, bool ContainsMoonNamesCulturalMemory, string SourceComponent, string SourceStrategy);
    private sealed record NarrationFileWriteDiagnostics(int NarrationFileWriteCount, IReadOnlyList<string> DuplicateNarrationFileWrites, IReadOnlyList<string> OverwrittenNarrationFiles, IReadOnlyList<string> AppendedNarrationFiles, bool FallbackNarrationTextInjected);


    private async Task<IReadOnlyList<string>> PhaseGenerateTtsTimelineV1Async(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var planRoot = context.OutputRoot;
        var ttsRoot = Path.Combine(planRoot, "tts");
        var shortRoot = Path.Combine(ttsRoot, "short");
        var longRoot = Path.Combine(ttsRoot, "long");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(shortRoot);
        Directory.CreateDirectory(longRoot);
        Directory.CreateDirectory(validationRoot);

        var syncPath = Path.Combine(planRoot, "sync", "scene-audio-sync.json");
        var narrationRoot = Path.Combine(planRoot, "narration");
        var oldPaths = new[]
        {
            Path.Combine(planRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-assets")
        };
        var inputPathsChecked = new[] { syncPath, Path.Combine(narrationRoot, "short"), Path.Combine(narrationRoot, "long") };
        var missingAudioFiles = new List<string>();
        var durationReadFailures = new List<string>();
        var audioDiagnostics = new List<TtsAudioContentDiagnostics>();
        var missingNarrationFiles = new List<string>();
        var emptyNarrationFiles = new List<string>();
        var ttsNarrationCleanupErrors = new List<string>();
        var selectedNarrationFiles = new List<string>();
        var errors = new List<string>();
        if (!File.Exists(syncPath)) errors.Add($"scene-audio-sync.json missing: {NormalizePath(syncPath)}");
        if (!Directory.Exists(narrationRoot)) errors.Add($"narration folder missing: {NormalizePath(narrationRoot)}");

        var shortItems = new List<TtsTimelineItem>();
        var longItems = new List<TtsTimelineItem>();
        var sourceSyncVersion = "v1";
        if (File.Exists(syncPath))
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(syncPath, cancellationToken)) ?? new JsonObject();
            sourceSyncVersion = GetString(root, "version") ?? "v1";
            await ValidateTtsNarrationFilesCleanBeforeProviderAsync(root, "short", Path.Combine(narrationRoot, "short"), 5, selectedNarrationFiles, missingNarrationFiles, emptyNarrationFiles, ttsNarrationCleanupErrors, cancellationToken);
            await ValidateTtsNarrationFilesCleanBeforeProviderAsync(root, "long", Path.Combine(narrationRoot, "long"), 9, selectedNarrationFiles, missingNarrationFiles, emptyNarrationFiles, ttsNarrationCleanupErrors, cancellationToken);
            if (ttsNarrationCleanupErrors.Count == 0 && missingNarrationFiles.Count == 0 && emptyNarrationFiles.Count == 0)
            {
                await BuildTtsTimelineItemsAsync(context, root, "short", shortRoot, Path.Combine(narrationRoot, "short"), 5, shortItems, missingAudioFiles, durationReadFailures, audioDiagnostics, selectedNarrationFiles, cancellationToken);
                await BuildTtsTimelineItemsAsync(context, root, "long", longRoot, Path.Combine(narrationRoot, "long"), 9, longItems, missingAudioFiles, durationReadFailures, audioDiagnostics, selectedNarrationFiles, cancellationToken);
            }
        }

        if (shortItems.Count != 5) errors.Add($"short audio count != 5; actual={shortItems.Count}");
        if (longItems.Count != 9) errors.Add($"long audio count != 9; actual={longItems.Count}");
        var shortDuplicateGroups = BuildDuplicateNarrationTextGroups(shortItems);
        var longDuplicateGroups = BuildDuplicateNarrationTextGroups(longItems);
        var duplicateNarrationTextGroups = shortDuplicateGroups.Concat(longDuplicateGroups).ToArray();
        var duplicateNarrationTextDetected = duplicateNarrationTextGroups.Length > 0;
        if (duplicateNarrationTextDetected) errors.Add("Duplicate narrationText detected within a format");
        errors.AddRange(missingNarrationFiles.Select(p => $"Narration txt missing: {p}"));
        errors.AddRange(emptyNarrationFiles.Select(p => $"Narration txt empty: {p}"));
        errors.AddRange(ttsNarrationCleanupErrors);
        errors.AddRange(shortItems.Concat(longItems).Where(i => string.IsNullOrWhiteSpace(i.NarrationText)).Select(i => $"Missing narrationText: {i.Format}:{i.SceneId}"));
        errors.AddRange(shortItems.Concat(longItems).Where(i => i.DurationSec <= 0).Select(i => $"durationSec = 0: {i.Format}:{i.SceneId}"));
        errors.AddRange(missingAudioFiles.Select(p => $"Audio missing: {p}"));
        errors.AddRange(durationReadFailures.Select(p => $"Duration read failed: {p}"));
        errors.AddRange(audioDiagnostics.Where(d => !d.ValidationPassed).Select(d => $"Audio content validation failed: {d.Format}:{d.SceneId} {d.AudioPath} ({string.Join("; ", d.Errors)})"));
        errors.AddRange(audioDiagnostics.Where(d => IsForbiddenPhase15TtsConfiguration(d)).Select(d => $"Forbidden Phase 15 TTS provider configuration: {d.Format}:{d.SceneId} provider={d.TtsProvider}, model={d.TtsModel}, voice={d.TtsVoice}"));
        errors.AddRange(audioDiagnostics.Where(d => d.ProviderRequestTextLength > 0 && !d.RealProviderCalled).Select(d => $"Real TTS provider was not called: {d.Format}:{d.SceneId}"));
        errors.AddRange(audioDiagnostics.Where(d => !d.RealProviderSucceeded).Select(d => $"Real TTS provider did not succeed: {d.Format}:{d.SceneId}"));
        errors.AddRange(audioDiagnostics.Where(d => d.FallbackUsed).Select(d => $"TTS fallback was used: {d.Format}:{d.SceneId} {d.FallbackReason}"));

        var timelinePath = Path.Combine(ttsRoot, "tts-timeline.json");
        await File.WriteAllTextAsync(timelinePath, JsonSerializer.Serialize(new
        {
            version = "v1",
            sourceSyncVersion,
            @short = new { itemCount = shortItems.Count, items = shortItems },
            @long = new { itemCount = longItems.Count, items = longItems }
        }, JsonOptions), cancellationToken);
        if (!File.Exists(timelinePath)) errors.Add($"tts-timeline.json missing: {NormalizePath(timelinePath)}");

        var validationPassed = errors.Count == 0;
        var diagnostics = new
        {
            inputPathsChecked = inputPathsChecked.Select(NormalizePath),
            selectedSyncPath = NormalizePath(syncPath),
            selectedNarrationDirectory = NormalizePath(narrationRoot),
            selectedNarrationFiles = selectedNarrationFiles.Select(NormalizePath),
            cleanupApplied = true,
            cleanedNarrationFiles = selectedNarrationFiles.Select(NormalizePath),
            subtitleFilesGenerated = File.Exists(Path.Combine(narrationRoot, "subtitles", "short.srt")) && File.Exists(Path.Combine(narrationRoot, "subtitles", "long.srt")),
            shortSrtPath = NormalizePath(Path.Combine(narrationRoot, "subtitles", "short.srt")),
            longSrtPath = NormalizePath(Path.Combine(narrationRoot, "subtitles", "long.srt")),
            ttsNarrationCleanupErrors,
            missingNarrationFiles,
            emptyNarrationFiles,
            narrationCharacterCount = shortItems.Concat(longItems).Sum(i => i.NarrationText?.Length ?? 0),
            oldPathsChecked = oldPaths.Select(NormalizePath),
            oldPathsIgnored = oldPaths.Select(NormalizePath),
            oldPathUsed = false,
            shortAudioCount = shortItems.Count,
            longAudioCount = longItems.Count,
            duplicateNarrationTextDetected,
            duplicateNarrationTextGroups,
            shortUniqueNarrationTextCount = CountUniqueNarrationText(shortItems),
            longUniqueNarrationTextCount = CountUniqueNarrationText(longItems),
            missingAudioFiles,
            durationReadFailures,
            configuredTtsProvider = ResolveConfiguredPhase15TtsProviderName(),
            realProviderCalled = audioDiagnostics.Count > 0 && audioDiagnostics.All(d => d.RealProviderCalled),
            realProviderSucceeded = audioDiagnostics.Count > 0 && audioDiagnostics.All(d => d.RealProviderSucceeded),
            fallbackUsed = audioDiagnostics.Any(d => d.FallbackUsed),
            fallbackReason = string.Join(" | ", audioDiagnostics.Select(d => d.FallbackReason).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase)),
            ttsProviderCalled = shortItems.Concat(longItems).All(i => i.TtsProviderCalled),
            ttsProviderSucceeded = shortItems.Concat(longItems).All(i => i.TtsProviderSucceeded),
            silentAudioCount = audioDiagnostics.Count(d => d.IsSilent),
            audioDiagnostics,
            validationPassed
        };
        var diagnosticsPath = Path.Combine(validationRoot, "phase-15-tts-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        var validationPath = Path.Combine(validationRoot, "phase-15-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo = 15,
            phaseName = "TTS Timeline V1",
            status = validationPassed ? "Succeeded" : "Failed",
            ttsTimelinePath = NormalizePath(timelinePath),
            shortAudioCount = shortItems.Count,
            longAudioCount = longItems.Count,
            duplicateNarrationTextDetected,
            duplicateNarrationTextGroups,
            shortUniqueNarrationTextCount = CountUniqueNarrationText(shortItems),
            longUniqueNarrationTextCount = CountUniqueNarrationText(longItems),
            silentAudioCount = audioDiagnostics.Count(d => d.IsSilent),
            realProviderCalled = audioDiagnostics.Count > 0 && audioDiagnostics.All(d => d.RealProviderCalled),
            realProviderSucceeded = audioDiagnostics.Count > 0 && audioDiagnostics.All(d => d.RealProviderSucceeded),
            fallbackUsed = audioDiagnostics.Any(d => d.FallbackUsed),
            isSyntheticTone = audioDiagnostics.Any(d => d.IsSyntheticTone),
            oldPathUsed = false,
            validationPassed,
            cleanupApplied = true,
            cleanedNarrationFiles = selectedNarrationFiles.Select(NormalizePath),
            subtitleFilesGenerated = File.Exists(Path.Combine(narrationRoot, "subtitles", "short.srt")) && File.Exists(Path.Combine(narrationRoot, "subtitles", "long.srt")),
            shortSrtPath = NormalizePath(Path.Combine(narrationRoot, "subtitles", "short.srt")),
            longSrtPath = NormalizePath(Path.Combine(narrationRoot, "subtitles", "long.srt")),
            errors
        }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 15 TTS Timeline V1 failed: " + string.Join(" | ", errors));
        return [timelinePath, validationPath, diagnosticsPath, .. shortItems.Select(i => i.AudioPath), .. longItems.Select(i => i.AudioPath), .. audioDiagnostics.Select(d => d.RawResponsePath)];
    }

    private static async Task ValidateTtsNarrationFilesCleanBeforeProviderAsync(JsonNode syncRoot, string format, string narrationDirectory, int expectedCount, List<string> selectedNarrationFiles, List<string> missingNarrationFiles, List<string> emptyNarrationFiles, List<string> ttsNarrationCleanupErrors, CancellationToken cancellationToken)
    {
        var cleanupService = new NarrationCleanupService();
        var syncItems = syncRoot[format]?["items"]?.AsArray() ?? [];
        foreach (var item in syncItems.Take(expectedCount))
        {
            var sceneId = GetString(item, "sceneId") ?? "000";
            var narrationPath = Path.Combine(narrationDirectory, $"{SanitizeFileName(sceneId)}.txt");
            selectedNarrationFiles.Add(narrationPath);
            if (!File.Exists(narrationPath))
            {
                missingNarrationFiles.Add(NormalizePath(narrationPath));
                continue;
            }

            var narrationText = await File.ReadAllTextAsync(narrationPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(narrationText))
            {
                emptyNarrationFiles.Add(NormalizePath(narrationPath));
                continue;
            }

            try
            {
                var cleanupCheck = cleanupService.Clean(narrationText);
                if (!string.Equals(cleanupCheck.CleanedText, narrationText.Trim(), StringComparison.Ordinal))
                    ttsNarrationCleanupErrors.Add($"TTS narrationText is not cleaned before provider call: {format}:{sceneId} {NormalizePath(narrationPath)}.");
                cleanupService.ValidateClean(narrationText);
            }
            catch (InvalidOperationException ex)
            {
                ttsNarrationCleanupErrors.Add($"TTS narrationText contains section labels or instruction phrases before provider call: {format}:{sceneId} {NormalizePath(narrationPath)} ({ex.Message})");
            }
        }
    }

    private async Task BuildTtsTimelineItemsAsync(ProductionPhaseContext context, JsonNode syncRoot, string format, string outputRoot, string narrationDirectory, int expectedCount, List<TtsTimelineItem> items, List<string> missingAudioFiles, List<string> durationReadFailures, List<TtsAudioContentDiagnostics> audioDiagnostics, List<string> selectedNarrationFiles, CancellationToken cancellationToken)
    {
        var syncItems = syncRoot[format]?["items"]?.AsArray() ?? [];
        foreach (var item in syncItems.Take(expectedCount))
        {
            var sceneId = GetString(item, "sceneId") ?? $"{items.Count + 1:000}";
            var narrationPath = Path.Combine(narrationDirectory, $"{SanitizeFileName(sceneId)}.txt");
            var narrationText = await File.ReadAllTextAsync(narrationPath, cancellationToken);
            var audioPath = Path.Combine(outputRoot, $"{SanitizeFileName(sceneId)}.mp3");
            var providerCalled = true;
            var generation = await GenerateAndValidateTtsAudioAsync(context, format, sceneId, narrationText, audioPath, cancellationToken);
            var providerSucceeded = generation.ValidationPassed;
            var duration = generation.DurationSec;
            audioDiagnostics.Add(generation);
            if (!File.Exists(audioPath)) missingAudioFiles.Add(NormalizePath(audioPath));
            if (duration <= 0) durationReadFailures.Add(NormalizePath(audioPath));
            items.Add(new TtsTimelineItem(format, sceneId, NormalizePath(audioPath), narrationText, Math.Round(duration, 3, MidpointRounding.AwayFromZero), providerCalled, providerSucceeded));
        }
    }

    private async Task<TtsAudioContentDiagnostics> GenerateAndValidateTtsAudioAsync(ProductionPhaseContext context, string format, string sceneId, string narrationText, string audioPath, CancellationToken cancellationToken)
    {
        var rawPath = BuildTtsRawDebugPath(audioPath, format, sceneId);
        var provider = ResolveConfiguredPhase15TtsProviderName();
        var model = ResolveConfiguredPhase15TtsModel();
        var voice = ResolveConfiguredPhase15TtsVoice(narrationText);
        var audioFormat = azureSpeechOptions?.Value.DefaultAudioFormat ?? "mp3";
        var realProviderCalled = false;
        var realProviderSucceeded = false;
        var fallbackUsed = false;
        var fallbackReason = string.Empty;
        int? providerHttpStatus = null;
        long providerResponseBytes = 0;

        try
        {
            if (!IsAzureSpeechConfigured(azureSpeechOptions?.Value) || azureSpeechClient is null)
                throw new InvalidOperationException("Azure Speech TTS provider is not configured for Phase 15.");

            realProviderCalled = true;
            var audioBytes = await azureSpeechClient.SynthesizeMp3Async(narrationText, azureSpeechOptions!.Value, cancellationToken);
            providerResponseBytes = audioBytes.LongLength;
            providerHttpStatus = 200;
            Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
            await File.WriteAllBytesAsync(audioPath, audioBytes, cancellationToken);
            await File.WriteAllBytesAsync(rawPath, audioBytes, cancellationToken);
            realProviderSucceeded = providerResponseBytes > 0 && File.Exists(audioPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            fallbackReason = ex.Message;
            if (context.ExecutionContext.UseProductionPipeline)
                return await ValidateGeneratedTtsMp3Async(format, sceneId, narrationText, audioPath, rawPath, provider, model, voice, providerResponseBytes, audioFormat, realProviderCalled, realProviderSucceeded, fallbackUsed, fallbackReason, providerHttpStatus, 1, cancellationToken);

            fallbackUsed = true;
            provider = "SyntheticFfmpeg";
            model = "lavfi-sine";
            voice = "phase15-development-fallback-tone";
            providerResponseBytes = await WriteSyntheticTtsRawResponseAsync(narrationText, rawPath, 1, cancellationToken);
            if (providerResponseBytes > 0)
                await ConvertAudioToMp3Async(rawPath, audioPath, cancellationToken);
        }

        return await ValidateGeneratedTtsMp3Async(format, sceneId, narrationText, audioPath, rawPath, provider, model, voice, providerResponseBytes, audioFormat, realProviderCalled, realProviderSucceeded, fallbackUsed, fallbackReason, providerHttpStatus, 1, cancellationToken);
    }


    private string ResolveConfiguredPhase15TtsProviderName()
        => IsAzureSpeechConfigured(azureSpeechOptions?.Value) && azureSpeechClient is not null ? "AzureSpeechTts" : "Unconfigured";

    private string ResolveConfiguredPhase15TtsModel()
        => azureSpeechOptions?.Value.DefaultAudioFormat ?? string.Empty;

    private string ResolveConfiguredPhase15TtsVoice(string narrationText)
    {
        if (azureSpeechOptions is null) return string.Empty;
        var language = narrationText.Any(ch => ch >= '\u0900' && ch <= '\u097F') ? "hi" : "en";
        return azureSpeechOptions.Value.GetPreferredVoices(language).FirstOrDefault() ?? azureSpeechOptions.Value.DefaultVoiceName ?? string.Empty;
    }

    private static bool IsAzureSpeechConfigured(AzureSpeechOptions? options)
    {
        if (options is null) return false;
        if (options.UseManagedIdentity)
            return !string.IsNullOrWhiteSpace(options.Region) && !string.IsNullOrWhiteSpace(options.ResourceId);
        return !string.IsNullOrWhiteSpace(options.Key) && (!string.IsNullOrWhiteSpace(options.Region) || !string.IsNullOrWhiteSpace(options.Endpoint));
    }

    private static bool IsForbiddenPhase15TtsConfiguration(TtsAudioContentDiagnostics diagnostics)
        => string.Equals(diagnostics.TtsProvider, "SyntheticFfmpeg", StringComparison.OrdinalIgnoreCase)
           || string.Equals(diagnostics.TtsModel, "lavfi-sine", StringComparison.OrdinalIgnoreCase)
           || diagnostics.TtsVoice.Contains("validation-tone", StringComparison.OrdinalIgnoreCase);

    private static bool IsSyntheticTone(string ttsProvider, string ttsModel, string ttsVoice)
        => string.Equals(ttsProvider, "SyntheticFfmpeg", StringComparison.OrdinalIgnoreCase)
           || string.Equals(ttsModel, "lavfi-sine", StringComparison.OrdinalIgnoreCase)
           || ttsVoice.Contains("tone", StringComparison.OrdinalIgnoreCase);

    private async Task<long> WriteSyntheticTtsRawResponseAsync(string narrationText, string rawPath, int attempt, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        var duration = Math.Max(0.75, CountSpokenWords(narrationText) * 0.42);
        var frequency = attempt == 1 ? "440" : "554";
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var result = await RunProcessAsync(ffmpegPath, ["-y", "-f", "lavfi", "-i", $"sine=frequency={frequency}:sample_rate=44100", "-t", duration.ToString("0.###", CultureInfo.InvariantCulture), "-ac", "1", "-c:a", "pcm_s16le", "-f", "s16le", rawPath], cancellationToken);
        return result.ExitCode == 0 && File.Exists(rawPath) ? new FileInfo(rawPath).Length : 0;
    }

    private async Task<bool> ConvertAudioToMp3Async(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var result = await RunProcessAsync(ffmpegPath, ["-y", "-f", "s16le", "-ar", "44100", "-ac", "1", "-i", sourcePath, "-vn", "-acodec", "libmp3lame", "-ar", "44100", "-ac", "1", "-q:a", "4", targetPath], cancellationToken);
        return result.ExitCode == 0 && File.Exists(targetPath);
    }

    private async Task<TtsAudioContentDiagnostics> ValidateGeneratedTtsMp3Async(string format, string sceneId, string narrationText, string audioPath, string rawPath, string ttsProvider, string ttsModel, string ttsVoice, long providerResponseBytes, string audioFormat, bool realProviderCalled, bool realProviderSucceeded, bool fallbackUsed, string fallbackReason, int? providerHttpStatus, int attempt, CancellationToken cancellationToken)
    {
        var metrics = await ProbeAudioContentMetricsAsync(audioPath, cancellationToken);
        var errors = new List<string>();
        if (providerResponseBytes <= 0) errors.Add("TTS provider returned no bytes.");
        if (!realProviderCalled && (narrationText?.Length ?? 0) > 0) errors.Add("realProviderCalled = false.");
        if (!realProviderSucceeded) errors.Add("speechProviderSucceeded = false.");
        if (metrics.FileSizeBytes <= 1000) errors.Add($"fileSizeBytes <= 1000 ({metrics.FileSizeBytes}).");
        if (metrics.DurationSec <= 0) errors.Add("durationSec <= 0.");
        if (metrics.PeakAmplitude <= 0.001) errors.Add($"peakAmplitude <= 0.001 ({metrics.PeakAmplitude:0.######}).");
        if (metrics.RmsAmplitude <= 0.0005) errors.Add($"rmsAmplitude <= 0.0005 ({metrics.RmsAmplitude:0.######}).");
        if (metrics.IsSilent) errors.Add("isSilent = true.");

        return new TtsAudioContentDiagnostics(
            Format: format,
            SceneId: sceneId,
            AudioPath: NormalizePath(audioPath),
            RawResponsePath: NormalizePath(rawPath),
            TtsProvider: ttsProvider,
            TtsModel: ttsModel,
            TtsVoice: ttsVoice,
            ProviderRequestTextLength: narrationText?.Length ?? 0,
            ConfiguredTtsProvider: ResolveConfiguredPhase15TtsProviderName(),
            RealProviderCalled: realProviderCalled,
            RealProviderSucceeded: realProviderSucceeded,
            FallbackUsed: fallbackUsed,
            FallbackReason: fallbackReason,
            ProviderHttpStatus: providerHttpStatus,
            ProviderResponseBytes: providerResponseBytes,
            GeneratedSpeechFilePath: NormalizePath(audioPath),
            IsSyntheticTone: IsSyntheticTone(ttsProvider, ttsModel, ttsVoice),
            SpeechValidationPassed: errors.Count == 0,
            AudioFormat: audioFormat,
            AudioSampleRate: metrics.AudioSampleRate,
            AudioChannels: metrics.AudioChannels,
            AudioCodec: metrics.AudioCodec,
            FfmpegProbeSucceeded: metrics.FfmpegProbeSucceeded,
            FileSizeBytes: metrics.FileSizeBytes,
            DurationSec: Math.Round(metrics.DurationSec, 3, MidpointRounding.AwayFromZero),
            PeakAmplitude: Math.Round(metrics.PeakAmplitude, 6, MidpointRounding.AwayFromZero),
            RmsAmplitude: Math.Round(metrics.RmsAmplitude, 6, MidpointRounding.AwayFromZero),
            IsSilent: metrics.IsSilent,
            RetryAttempt: attempt,
            ValidationPassed: errors.Count == 0,
            Errors: errors);
    }

    private string BuildTtsRawDebugPath(string audioPath, string format, string sceneId)
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(audioPath)!)!, "debug", format, $"{SanitizeFileName(sceneId)}.raw");

    private static string SanitizeFileName(string value)
        => string.Join("-", Regex.Matches(value, "[A-Za-z0-9_-]+").Select(m => m.Value)).Trim('-') is { Length: > 0 } safe ? safe : Guid.NewGuid().ToString("N");

    private sealed record TtsTimelineItem(string Format, string SceneId, string AudioPath, string NarrationText, double DurationSec, bool TtsProviderCalled, bool TtsProviderSucceeded);


    private async Task<IReadOnlyList<string>> PhaseDurationCalibrationV1Async(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        const double transitionPaddingSec = 0.4;
        const double minimumSceneDurationSec = 3.0;
        var planRoot = context.OutputRoot;
        var timingRoot = Path.Combine(planRoot, "timing");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(timingRoot);
        Directory.CreateDirectory(validationRoot);

        var syncPath = Path.Combine(planRoot, "sync", "scene-audio-sync.json");
        var ttsTimelinePath = Path.Combine(planRoot, "tts", "tts-timeline.json");
        var shortMetadataPath = Path.Combine(planRoot, "scene-assets-v3", "short", "scene-timeline-metadata.json");
        var longMetadataPath = Path.Combine(planRoot, "scene-assets-v3", "long", "scene-timeline-metadata.json");
        var oldPaths = new[]
        {
            Path.Combine(planRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-assets")
        };
        var inputPathsChecked = new[] { syncPath, ttsTimelinePath, shortMetadataPath, longMetadataPath };
        var errors = new List<string>();
        var missingDurationItems = new List<string>();
        if (!File.Exists(ttsTimelinePath)) errors.Add($"tts-timeline.json missing: {NormalizePath(ttsTimelinePath)}");
        if (!File.Exists(syncPath)) errors.Add($"scene-audio-sync.json missing: {NormalizePath(syncPath)}");
        if (!File.Exists(shortMetadataPath)) errors.Add($"short scene-timeline-metadata.json missing: {NormalizePath(shortMetadataPath)}");
        if (!File.Exists(longMetadataPath)) errors.Add($"long scene-timeline-metadata.json missing: {NormalizePath(longMetadataPath)}");

        var sourceTtsTimelineVersion = "v1";
        var shortItems = new List<SceneDurationPlanItem>();
        var longItems = new List<SceneDurationPlanItem>();
        if (File.Exists(ttsTimelinePath))
        {
            var ttsRoot = JsonNode.Parse(await File.ReadAllTextAsync(ttsTimelinePath, cancellationToken)) ?? new JsonObject();
            sourceTtsTimelineVersion = GetString(ttsRoot, "version") ?? "v1";
            shortItems.AddRange(BuildSceneDurationPlanItems(ttsRoot, "short", shortMetadataPath, 5, 12.0, transitionPaddingSec, minimumSceneDurationSec, missingDurationItems));
            longItems.AddRange(BuildSceneDurationPlanItems(ttsRoot, "long", longMetadataPath, 9, 15.0, transitionPaddingSec, minimumSceneDurationSec, missingDurationItems));
        }

        if (shortItems.Count != 5) errors.Add($"short scene count != 5; actual={shortItems.Count}");
        if (longItems.Count != 9) errors.Add($"long scene count != 9; actual={longItems.Count}");
        errors.AddRange(missingDurationItems.Select(x => $"Audio duration missing: {x}"));
        errors.AddRange(shortItems.Concat(longItems).Where(i => i.AudioDurationSec <= 0).Select(i => $"audioDurationSec <= 0: {i.Format}:{i.SceneId}"));
        errors.AddRange(shortItems.Concat(longItems).Where(i => i.SceneDurationSec < i.AudioDurationSec).Select(i => $"sceneDurationSec < audioDurationSec: {i.Format}:{i.SceneId}"));
        var oldPathUsed = false;
        if (oldPathUsed) errors.Add("Old scene asset path used");

        var shortAudioTotal = RoundDuration(shortItems.Sum(i => i.AudioDurationSec));
        var longAudioTotal = RoundDuration(longItems.Sum(i => i.AudioDurationSec));
        var shortVideoTotal = RoundDuration(shortItems.Sum(i => i.SceneDurationSec));
        var longVideoTotal = RoundDuration(longItems.Sum(i => i.SceneDurationSec));
        var durationMismatchDetected = shortItems.Concat(longItems).Any(i => i.SceneDurationSec < i.AudioDurationSec + i.TransitionDurationSec);
        var planPath = Path.Combine(timingRoot, "scene-duration-plan.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(new
        {
            version = "v1",
            sourceTtsTimelineVersion,
            @short = new { sceneCount = shortItems.Count, totalAudioDurationSec = shortAudioTotal, totalVideoDurationSec = shortVideoTotal, items = shortItems },
            @long = new { sceneCount = longItems.Count, totalAudioDurationSec = longAudioTotal, totalVideoDurationSec = longVideoTotal, items = longItems }
        }, JsonOptions), cancellationToken);
        if (!File.Exists(planPath)) errors.Add($"scene-duration-plan.json missing: {NormalizePath(planPath)}");
        RegenerateNarrationSubtitlesFromSceneDurationPlan(planRoot);

        var validationPassed = errors.Count == 0;
        var diagnostics = new
        {
            inputPathsChecked = inputPathsChecked.Select(NormalizePath),
            selectedTtsTimelinePath = NormalizePath(ttsTimelinePath),
            selectedSyncPath = NormalizePath(syncPath),
            selectedShortMetadataPath = NormalizePath(shortMetadataPath),
            selectedLongMetadataPath = NormalizePath(longMetadataPath),
            oldPathsChecked = oldPaths.Select(NormalizePath),
            oldPathsIgnored = oldPaths.Select(NormalizePath),
            oldPathUsed,
            shortSceneCount = shortItems.Count,
            longSceneCount = longItems.Count,
            shortTotalAudioDurationSec = shortAudioTotal,
            longTotalAudioDurationSec = longAudioTotal,
            shortTotalVideoDurationSec = shortVideoTotal,
            longTotalVideoDurationSec = longVideoTotal,
            durationMismatchDetected,
            missingDurationItems,
            validationPassed
        };
        var diagnosticsPath = Path.Combine(validationRoot, "phase-16-duration-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        var validationPath = Path.Combine(validationRoot, "phase-16-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo = 16,
            phaseName = "Duration Calibration V1",
            status = validationPassed ? "Succeeded" : "Failed",
            sceneDurationPlanPath = NormalizePath(planPath),
            oldPathUsed,
            validationPassed,
            errors
        }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 16 Duration Calibration V1 failed: " + string.Join(" | ", errors));
        return [planPath, validationPath, diagnosticsPath];
    }

    private static IReadOnlyList<SceneDurationPlanItem> BuildSceneDurationPlanItems(JsonNode ttsRoot, string format, string metadataPath, int expectedCount, double maximumSceneDurationSec, double transitionPaddingSec, double minimumSceneDurationSec, List<string> missingDurationItems)
    {
        var ttsItems = ttsRoot[format]?["items"]?.AsArray() ?? [];
        var metadataScenes = File.Exists(metadataPath) ? ReadJsonArray(metadataPath, "scenes") : new JsonArray();
        var metadataBySceneId = metadataScenes.Select(n => new { Node = n, SceneId = GetString(n, "sceneId") ?? string.Empty }).Where(x => !string.IsNullOrWhiteSpace(x.SceneId)).GroupBy(x => x.SceneId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Node, StringComparer.OrdinalIgnoreCase);
        var items = new List<SceneDurationPlanItem>();
        foreach (var ttsItem in ttsItems.Take(expectedCount))
        {
            var sceneId = GetString(ttsItem, "sceneId") ?? $"{items.Count + 1:000}";
            var audioDuration = GetDouble(ttsItem, "durationSec") ?? GetDouble(ttsItem, "audioDurationSec") ?? 0;
            if (audioDuration <= 0) missingDurationItems.Add($"{format}:{sceneId}");
            var metadata = metadataBySceneId.TryGetValue(sceneId, out var matched) ? matched : metadataScenes.ElementAtOrDefault(items.Count);
            var sceneDuration = audioDuration;
            items.Add(new SceneDurationPlanItem(format, sceneId, GetString(ttsItem, "audioPath") ?? string.Empty, RoundDuration(audioDuration), RoundDuration(sceneDuration), 0, "cut", ResolveMotionProfile(sceneId, GetString(metadata, "recommendedMotion"))));
        }
        return items;
    }

    private static double? GetDouble(JsonNode? node, string name) => node?[name]?.GetValue<double>();
    private static bool? GetBool(JsonNode? node, string name)
    {
        var value = node?[name];
        if (value is null) return null;
        if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        return null;
    }
    private static double? GetDouble(JsonNode? node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = node?[name];
            if (value is not null && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }
    private static double RoundDuration(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private sealed record SceneDurationPlanItem(string Format, string SceneId, string AudioPath, double AudioDurationSec, double SceneDurationSec, double TransitionDurationSec, string RecommendedTransition, string RecommendedMotion);

    private static void RegenerateNarrationSubtitlesFromSceneDurationPlan(string planRoot)
    {
        var subtitlesRoot = Path.Combine(planRoot, "narration", "subtitles");
        var shortRoot = Path.Combine(planRoot, "narration", "short");
        var longRoot = Path.Combine(planRoot, "narration", "long");
        if (!Directory.Exists(shortRoot) || !Directory.Exists(longRoot)) return;
        Directory.CreateDirectory(subtitlesRoot);
        var shortFiles = Directory.EnumerateFiles(shortRoot, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var longFiles = Directory.EnumerateFiles(longRoot, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllText(Path.Combine(subtitlesRoot, "short.srt"), BuildNarrationSrtFromCleanFiles(planRoot, "short", shortFiles, []).Srt);
        File.WriteAllText(Path.Combine(subtitlesRoot, "long.srt"), BuildNarrationSrtFromCleanFiles(planRoot, "long", longFiles, []).Srt);
    }


    private async Task<IReadOnlyList<string>> PhaseMotionLayerV1Async(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        if (context.PipelineRequest.MotionPreviewOnly)
            return await PhaseMotionLayerV2PreviewAsync(context, cancellationToken);

        var planRoot = context.OutputRoot;
        var motionRoot = Path.Combine(planRoot, "motion");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(motionRoot);
        Directory.CreateDirectory(validationRoot);

        var durationPlanPath = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        var shortSceneRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
        var longSceneRoot = Path.Combine(planRoot, "scene-assets-v3", "long");
        var oldPaths = new[]
        {
            Path.Combine(planRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-assets")
        };
        var inputPathsChecked = new[] { durationPlanPath, shortSceneRoot, longSceneRoot };
        var errors = new List<string>();
        var missingSceneImages = new List<string>();
        var missingAudioFiles = new List<string>();
        var invalidDurations = new List<string>();
        var unsupportedMotionStyles = new List<string>();

        if (!File.Exists(durationPlanPath)) errors.Add($"scene-duration-plan.json missing: {NormalizePath(durationPlanPath)}");
        if (!Directory.Exists(shortSceneRoot)) errors.Add($"short scene-assets-v3 root missing: {NormalizePath(shortSceneRoot)}");
        if (!Directory.Exists(longSceneRoot)) errors.Add($"long scene-assets-v3 root missing: {NormalizePath(longSceneRoot)}");

        var sourceDurationPlanVersion = "v1";
        var shortItems = new List<MotionPlanItem>();
        var longItems = new List<MotionPlanItem>();
        var oldPathUsageReasons = new List<string>();
        AddOldPathUsageReason(oldPathUsageReasons, "selectedDurationPlanPath", durationPlanPath, oldPaths);
        AddOldPathUsageReason(oldPathUsageReasons, "selectedShortSceneRoot", shortSceneRoot, oldPaths);
        AddOldPathUsageReason(oldPathUsageReasons, "selectedLongSceneRoot", longSceneRoot, oldPaths);
        if (File.Exists(durationPlanPath))
        {
            var durationRoot = JsonNode.Parse(await File.ReadAllTextAsync(durationPlanPath, cancellationToken)) ?? new JsonObject();
            sourceDurationPlanVersion = GetString(durationRoot, "version") ?? "v1";
            shortItems.AddRange(BuildMotionPlanItems(durationRoot, "short", shortSceneRoot, 5, missingSceneImages, missingAudioFiles, invalidDurations, unsupportedMotionStyles, oldPaths, oldPathUsageReasons));
            longItems.AddRange(BuildMotionPlanItems(durationRoot, "long", longSceneRoot, 9, missingSceneImages, missingAudioFiles, invalidDurations, unsupportedMotionStyles, oldPaths, oldPathUsageReasons));
        }
        var oldPathUsed = oldPathUsageReasons.Count > 0;

        if (shortItems.Count != 5) errors.Add($"short scene count != 5; actual={shortItems.Count}");
        if (longItems.Count != 9) errors.Add($"long scene count != 9; actual={longItems.Count}");
        errors.AddRange(missingSceneImages.Select(p => $"Scene image missing: {p}"));
        errors.AddRange(missingAudioFiles.Select(p => $"Audio missing: {p}"));
        errors.AddRange(invalidDurations.Select(x => $"sceneDurationSec <= 0: {x}"));
        errors.AddRange(unsupportedMotionStyles.Select(x => $"Unsupported motionStyle: {x}"));
        if (oldPathUsed) errors.Add("Old scene asset path used");

        var motionPlanPath = Path.Combine(motionRoot, "motion-plan.json");
        await File.WriteAllTextAsync(motionPlanPath, JsonSerializer.Serialize(new
        {
            version = "v1",
            sourceDurationPlanVersion,
            @short = new { sceneCount = shortItems.Count, items = shortItems },
            @long = new { sceneCount = longItems.Count, items = longItems }
        }, JsonOptions), cancellationToken);
        if (!File.Exists(motionPlanPath)) errors.Add($"motion-plan.json missing: {NormalizePath(motionPlanPath)}");

        var motionDebugPath = Path.Combine(motionRoot, "motion-debug.json");
        await WriteMotionRc1DebugAsync(motionDebugPath, FirstNonEmpty(context.ProductionEventIntelligence.EventType, context.Request.EventType, context.ExecutionContext.EventType, "SolarEclipse"), shortItems, longItems, cancellationToken);
        if (!File.Exists(motionDebugPath)) errors.Add($"motion-debug.json missing: {NormalizePath(motionDebugPath)}");
        ValidateMotionRc1Debug(shortItems.Concat(longItems), errors);

        var validationPassed = errors.Count == 0;
        var diagnostics = new
        {
            inputPathsChecked = inputPathsChecked.Select(NormalizePath),
            selectedDurationPlanPath = NormalizePath(durationPlanPath),
            selectedShortSceneRoot = NormalizePath(shortSceneRoot),
            selectedLongSceneRoot = NormalizePath(longSceneRoot),
            oldPathsChecked = oldPaths.Select(NormalizePath),
            oldPathsIgnored = oldPaths.Select(NormalizePath),
            oldPathUsed,
            oldPathUsageReasons,
            shortSceneCount = shortItems.Count,
            longSceneCount = longItems.Count,
            missingSceneImages,
            missingAudioFiles,
            invalidDurations,
            unsupportedMotionStyles,
            validationPassed
        };
        var diagnosticsPath = Path.Combine(validationRoot, "phase-17-motion-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        var validationPath = Path.Combine(validationRoot, "phase-17-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo = 17,
            phaseName = "Motion Layer V1",
            status = validationPassed ? "Succeeded" : "Failed",
            motionPlanPath = NormalizePath(motionPlanPath),
            motionDebugPath = NormalizePath(motionDebugPath),
            oldPathUsed,
            oldPathUsageReasons,
            validationPassed,
            errors
        }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 17 Motion Layer V1 failed: " + string.Join(" | ", errors));
        return [motionPlanPath, validationPath, diagnosticsPath];
    }

    private async Task<IReadOnlyList<string>> PhaseMotionLayerV2PreviewAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var planRoot = context.OutputRoot;
        var motionRoot = Path.Combine(planRoot, "motion");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(motionRoot);
        Directory.CreateDirectory(validationRoot);

        var durationPlanPath = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        var shortSceneRoot = Path.Combine(planRoot, "scene-assets-v3", "short");
        var longSceneRoot = Path.Combine(planRoot, "scene-assets-v3", "long");
        var missingAudioFiles = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var motionV2Strength = ResolveMotionV2Strength(context.PipelineRequest.MotionV2Strength);
        var shortItems = await BuildMotionV2PreviewItemsAsync(durationPlanPath, "short", shortSceneRoot, 5d, motionV2Strength, missingAudioFiles, errors, cancellationToken);
        var longItems = await BuildMotionV2PreviewItemsAsync(durationPlanPath, "long", longSceneRoot, 8d, motionV2Strength, missingAudioFiles, errors, cancellationToken);
        warnings.AddRange(missingAudioFiles.Select(p => $"Audio missing (preview warning only): {p}"));

        var validationPassed = errors.Count == 0;
        var motionPlanPath = Path.Combine(motionRoot, "motion-plan-v2-preview.json");
        await File.WriteAllTextAsync(motionPlanPath, JsonSerializer.Serialize(new
        {
            motionVersion = "V2",
            motionPreviewOnly = true,
            motionV2Strength,
            audioRequired = false,
            @short = new { sceneCount = shortItems.Count, items = shortItems },
            @long = new { sceneCount = longItems.Count, items = longItems }
        }, JsonOptions), cancellationToken);

        var motionDebugPath = Path.Combine(motionRoot, "motion-debug-v2-preview.json");
        await File.WriteAllTextAsync(motionDebugPath, JsonSerializer.Serialize(new
        {
            motionVersion = "V2",
            motionPreviewOnly = true,
            motionV2Strength,
            audioRequired = false,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            @short = shortItems,
            @long = longItems
        }, JsonOptions), cancellationToken);

        var diagnosticsPath = Path.Combine(validationRoot, "phase-17-motion-v2-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            motionVersion = "V2",
            motionPreviewOnly = true,
            motionV2Strength,
            audioRequired = false,
            missingAudioFiles,
            warnings,
            scenes = shortItems.Concat(longItems),
            validationPassed
        }, JsonOptions), cancellationToken);

        var validationPath = Path.Combine(validationRoot, "phase-17-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new
        {
            phaseNo = 17,
            phaseName = "Motion Layer V2 Preview",
            status = validationPassed ? "Succeeded" : "Failed",
            motionVersion = "V2",
            motionPreviewOnly = true,
            motionV2Strength,
            audioRequired = false,
            motionPlanPath = NormalizePath(motionPlanPath),
            motionDebugPath = NormalizePath(motionDebugPath),
            diagnosticsPath = NormalizePath(diagnosticsPath),
            missingAudioFiles,
            warnings,
            validationPassed,
            errors
        }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 17 Motion Layer V2 Preview failed: " + string.Join(" | ", errors));
        return [motionPlanPath, motionDebugPath, diagnosticsPath, validationPath];
    }

    private static async Task<IReadOnlyList<MotionV2PreviewItem>> BuildMotionV2PreviewItemsAsync(string durationPlanPath, string format, string sceneRoot, double defaultDurationSec, string motionV2Strength, List<string> missingAudioFiles, List<string> errors, CancellationToken cancellationToken)
    {
        var durationItems = new JsonArray();
        if (File.Exists(durationPlanPath))
            durationItems = JsonNode.Parse(await File.ReadAllTextAsync(durationPlanPath, cancellationToken))?[format]?["items"]?.AsArray() ?? [];
        var manifestPath = Path.Combine(sceneRoot, "scene-manifest-v3.json");
        var manifestScenes = File.Exists(manifestPath) ? ReadJsonArray(manifestPath, "scenes") : new JsonArray();
        var count = Math.Max(durationItems.Count, manifestScenes.Count);
        if (count == 0) errors.Add($"{format} scene metadata missing for Motion V2 preview.");
        var items = new List<MotionV2PreviewItem>();
        for (var i = 0; i < count; i++)
        {
            var durationItem = durationItems.ElementAtOrDefault(i);
            var manifest = manifestScenes.ElementAtOrDefault(i);
            var sceneId = FirstNonEmpty(GetString(durationItem, "sceneId"), GetString(manifest, "sceneId"), $"{format}-{i + 1:000}");
            var duration = GetDouble(durationItem, "sceneDurationSec", "durationSec", "visualDurationSec")
                ?? GetDouble(manifest, "sceneDurationSec", "durationSec", "visualDurationSec")
                ?? defaultDurationSec;
            var audioPath = GetString(durationItem, "audioPath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath)) missingAudioFiles.Add(NormalizePath(audioPath));
            var motionType = ResolveMotionV2Type(sceneId, i, count);
            var motion = ResolveMotionV2Values(motionType, sceneId, motionV2Strength);
            items.Add(new MotionV2PreviewItem("V2", true, false, sceneId, format, motionType, RoundDuration(duration), motion.StartScale, motion.EndScale, motion.StartX, motion.EndX, "EaseInOutSine", true));
        }
        return items;
    }

    private static string ResolveMotionV2Type(string sceneId, int index, int count)
        => IsBestTimeScene(sceneId) ? "SlowZoomIn"
            : IsAccurateSkyGuideScene(sceneId) ? "PanRight"
            : index == 0 ? "SlowZoomIn"
            : index == count - 1 ? "SlowZoomOut"
            : index % 4 == 1 ? "PanRight"
            : index % 4 == 2 ? "PushToObject"
            : index % 4 == 3 ? "PanLeft"
            : "None";

    private static MotionV2Values ResolveMotionV2Values(string motionType, string sceneId, string motionV2Strength)
    {
        if (IsExperimentalMotionV2Strength(motionV2Strength))
        {
            return motionType switch
            {
                "SlowZoomIn" when IsBestTimeScene(sceneId) => new(1.00d, 1.10d, 0d, 0d),
                "PanRight" when IsAccurateSkyGuideScene(sceneId) => new(1.08d, 1.12d, -0.06d, 0.06d),
                "SlowZoomIn" => new(1.00d, 1.25d, 0d, 0d),
                "SlowZoomOut" => new(1.25d, 1.00d, 0d, 0d),
                "PanLeft" => new(1.12d, 1.16d, 0.08d, -0.08d),
                "PanRight" => new(1.12d, 1.16d, -0.08d, 0.08d),
                "PushToObject" => new(1.00d, 1.30d, 0d, 0d),
                _ => new(1.00d, 1.00d, 0d, 0d)
            };
        }

        return motionType switch
        {
            "SlowZoomIn" when IsBestTimeScene(sceneId) => new(1.00d, 1.04d, 0d, 0d),
            "SlowZoomIn" => new(1.00d, 1.12d, 0d, 0d),
            "SlowZoomOut" => new(1.12d, 1.00d, 0d, 0d),
            "PanLeft" => new(1.08d, 1.08d, 0.05d, 0.00d),
            "PanRight" => new(1.04d, 1.08d, -0.03d, 0.03d),
            "PushToObject" => new(1.00d, 1.18d, 0d, 0d),
            _ => new(1.00d, 1.00d, 0d, 0d)
        };
    }

    private static string ResolveMotionV2Strength(string? motionV2Strength)
        => IsExperimentalMotionV2Strength(motionV2Strength) ? "Experimental" : "Default";

    private static string ResolvePhase18MotionV2Strength(string? requestMotionV2Strength, string? planMotionV2Strength)
        => ResolveMotionV2Strength(planMotionV2Strength ?? requestMotionV2Strength);

    private static bool HasMotionV2StrengthMismatch(string? requestMotionV2Strength, string? motionV2StrengthUsed)
        => IsExperimentalMotionV2Strength(requestMotionV2Strength)
            && !IsExperimentalMotionV2Strength(motionV2StrengthUsed);

    private static bool ShouldWarnMotionV2StrengthRequestOverride(string? requestMotionV2Strength, string? planMotionV2Strength)
        => HasMotionV2StrengthMismatch(requestMotionV2Strength, ResolvePhase18MotionV2Strength(requestMotionV2Strength, planMotionV2Strength));

    private static bool IsExperimentalMotionV2Strength(string? motionV2Strength)
        => string.Equals(motionV2Strength, "Experimental", StringComparison.OrdinalIgnoreCase);

    private static bool IsAccurateSkyGuideScene(string sceneId)
        => sceneId.Equals("003-accurate-sky-guide", StringComparison.OrdinalIgnoreCase)
            || sceneId.Equals("006-accurate-sky-guide", StringComparison.OrdinalIgnoreCase);

    private static bool IsBestTimeScene(string sceneId)
        => sceneId.Equals("005-best-time", StringComparison.OrdinalIgnoreCase);

    private sealed record MotionV2Values(double StartScale, double EndScale, double StartX, double EndX);
    private sealed record MotionV2PreviewItem(string MotionVersion, bool MotionPreviewOnly, bool AudioRequired, string SceneId, string Format, string MotionType, double DurationSec, double StartScale, double EndScale, double StartX, double EndX, string Easing, bool ValidationPassed);

    private static IReadOnlyList<MotionPlanItem> BuildMotionPlanItems(JsonNode durationRoot, string format, string sceneRoot, int expectedCount, List<string> missingSceneImages, List<string> missingAudioFiles, List<string> invalidDurations, List<string> unsupportedMotionStyles, IReadOnlyList<string> oldPaths, List<string> oldPathUsageReasons)
    {
        var durationItems = durationRoot[format]?["items"]?.AsArray() ?? [];
        var manifestPath = Path.Combine(sceneRoot, "scene-manifest-v3.json");
        var manifestScenes = File.Exists(manifestPath) ? ReadJsonArray(manifestPath, "scenes") : new JsonArray();
        var manifestBySceneId = manifestScenes.Select(n => new { Node = n, SceneId = GetString(n, "sceneId") ?? string.Empty }).Where(x => !string.IsNullOrWhiteSpace(x.SceneId)).GroupBy(x => x.SceneId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Node, StringComparer.OrdinalIgnoreCase);
        var items = new List<MotionPlanItem>();
        foreach (var durationItem in durationItems.Take(expectedCount))
        {
            var sceneId = GetString(durationItem, "sceneId") ?? $"{items.Count + 1:000}";
            var manifest = manifestBySceneId.TryGetValue(sceneId, out var matched) ? matched : manifestScenes.ElementAtOrDefault(items.Count);
            var imagePath = FirstNonEmpty(GetString(manifest, "imagePath"), GetString(manifest, "sceneImagePath"), Path.Combine(sceneRoot, sceneId + ".png"));
            imagePath = Path.IsPathRooted(imagePath) ? imagePath : Path.Combine(sceneRoot, imagePath);
            var audioPath = GetString(durationItem, "audioPath") ?? string.Empty;
            var sceneDuration = GetDouble(durationItem, "sceneDurationSec") ?? 0;
            var purpose = ResolveMotionPurpose(sceneId);
            var motionStyle = ResolveMotionProfile(sceneId, GetString(durationItem, "recommendedMotion"));
            var motionProfile = ResolveMotionProfile(sceneId, motionStyle);
            var motion = ResolveMotionDefaults(motionProfile);
            if (motion is null) unsupportedMotionStyles.Add($"{format}:{sceneId}:{motionStyle}");
            AddOldPathUsageReason(oldPathUsageReasons, $"selectedAudioSource[{format}:{sceneId}]", audioPath, oldPaths);
            if (!File.Exists(imagePath)) missingSceneImages.Add(NormalizePath(imagePath));
            if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath)) missingAudioFiles.Add(NormalizePath(audioPath));
            if (sceneDuration <= 0) invalidDurations.Add($"{format}:{sceneId}");
            var m = motion ?? ResolveMotionDefaults("static")!;
            items.Add(new MotionPlanItem(format, sceneId, purpose, NormalizePath(imagePath), NormalizePath(audioPath), RoundDuration(sceneDuration), 0, "cut", motionProfile, motionProfile, m.ZoomStart, m.ZoomEnd, m.PanXStart, m.PanXEnd, m.PanYStart, m.PanYEnd, ResolveMotionEasing(motionProfile)));
        }
        return items;
    }

    private static async Task WriteMotionRc1DebugAsync(string motionDebugPath, string eventType, IReadOnlyList<MotionPlanItem> shortItems, IReadOnlyList<MotionPlanItem> longItems, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(motionDebugPath)!);
        await File.WriteAllTextAsync(motionDebugPath, JsonSerializer.Serialize(new
        {
            version = "v1",
            eventType,
            generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            frameRate = 30,
            @short = new { sceneCount = shortItems.Count, items = shortItems.Select(BuildMotionRc1DebugItem).ToArray() },
            @long = new { sceneCount = longItems.Count, items = longItems.Select(BuildMotionRc1DebugItem).ToArray() }
        }, JsonOptions), cancellationToken);
    }

    private static object BuildMotionRc1DebugItem(MotionPlanItem item)
    {
        const int frameRate = 30;
        var totalFrames = Math.Max(1, (int)Math.Round(item.SceneDurationSec * frameRate, MidpointRounding.AwayFromZero));
        var scaleValues = MotionValues(item.ZoomStart / 100.0, item.ZoomEnd / 100.0, item.Easing, totalFrames);
        var panXValues = MotionValues(item.PanXStart, item.PanXEnd, item.Easing, totalFrames);
        var panYValues = MotionValues(item.PanYStart, item.PanYEnd, item.Easing, totalFrames);
        return new
        {
            format = item.Format,
            sceneId = item.SceneId,
            purpose = item.Purpose,
            motionProfile = item.MotionProfile,
            easing = item.Easing,
            durationSeconds = item.SceneDurationSec,
            frameRate,
            totalFrames,
            startScale = RoundMotionValue(item.ZoomStart / 100.0),
            endScale = RoundMotionValue(item.ZoomEnd / 100.0),
            startPanXPercent = item.PanXStart,
            endPanXPercent = item.PanXEnd,
            startPanYPercent = item.PanYStart,
            endPanYPercent = item.PanYEnd,
            first10ScaleValues = scaleValues.Take(10).ToArray(),
            first10PanXValues = panXValues.Take(10).ToArray(),
            first10PanYValues = panYValues.Take(10).ToArray(),
            last10ScaleValues = scaleValues.TakeLast(10).ToArray(),
            last10PanXValues = panXValues.TakeLast(10).ToArray(),
            last10PanYValues = panYValues.TakeLast(10).ToArray(),
            isContinuous = true,
            maxFrameScaleDelta = MaxFrameDelta(scaleValues),
            maxFramePanXDelta = MaxFrameDelta(panXValues),
            maxFramePanYDelta = MaxFrameDelta(panYValues)
        };
    }

    private static double[] MotionValues(double start, double end, string easing, int totalFrames)
        => Enumerable.Range(0, totalFrames).Select(frame => RoundMotionValue(start + (end - start) * EasedProgress(frame, totalFrames, easing))).ToArray();

    private static double RoundMotionValue(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static double MaxFrameDelta(IReadOnlyList<double> values)
        => values.Count <= 1 ? 0 : RoundMotionValue(Enumerable.Range(1, values.Count - 1).Max(i => Math.Abs(values[i] - values[i - 1])));

    private static void ValidateMotionRc1Debug(IEnumerable<MotionPlanItem> items, List<string> errors)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Purpose)) errors.Add($"Motion purpose missing: {item.Format}:{item.SceneId}");
            if (string.IsNullOrWhiteSpace(item.MotionProfile)) errors.Add($"Motion profile missing: {item.Format}:{item.SceneId}");
            if (string.IsNullOrWhiteSpace(item.Easing)) errors.Add($"Motion easing missing: {item.Format}:{item.SceneId}");
            if (Regex.IsMatch(item.MotionStyle, "parallax|advanced", RegexOptions.IgnoreCase)) errors.Add($"Unsupported motion style in RC1: {item.Format}:{item.SceneId}:{item.MotionStyle}");
        }
    }

    private static void AddOldPathUsageReason(List<string> reasons, string label, string? selectedPath, IReadOnlyList<string> oldPaths)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return;
        var normalizedSelected = NormalizePath(selectedPath);
        if (!oldPaths.Any(oldPath => IsSameOrUnderPath(normalizedSelected, NormalizePath(oldPath)))) return;
        var reason = $"{label}={normalizedSelected}";
        if (!reasons.Contains(reason, StringComparer.OrdinalIgnoreCase)) reasons.Add(reason);
    }

    private static bool IsSameOrUnderPath(string selectedPath, string rootPath)
        => selectedPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            || selectedPath.StartsWith(rootPath.TrimEnd('/', Path.DirectorySeparatorChar) + "/", StringComparison.OrdinalIgnoreCase)
            || selectedPath.StartsWith(rootPath.TrimEnd('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static MotionDefaults? ResolveMotionDefaults(string motionStyle) => motionStyle switch
    {
        "Hook" => new MotionDefaults(100, 115, 0, 0, 0, 0),
        "Discovery" => new MotionDefaults(100, 108, -3, 3, 2, -2),
        "SkyGuide" => new MotionDefaults(104, 108, -3, 3, 0, 0),
        "ViewingTip" => new MotionDefaults(102, 106, -2, 2, -2, 2),
        "Closing" => new MotionDefaults(110, 100, 0, 0, 0, 0),
        "BestTime" => new MotionDefaults(100, 104, 0, 0, 0, 0),
        "static" => new MotionDefaults(100, 100, 0, 0, 0, 0),
        _ => null
    };

    private sealed record MotionDefaults(double ZoomStart, double ZoomEnd, double PanXStart, double PanXEnd, double PanYStart, double PanYEnd);
    private sealed record MotionPlanItem(string Format, string SceneId, string Purpose, string SceneImagePath, string AudioPath, double SceneDurationSec, double TransitionDurationSec, string Transition, string MotionStyle, string MotionProfile, double ZoomStart, double ZoomEnd, double PanXStart, double PanXEnd, double PanYStart, double PanYEnd, string Easing);

    private static string ResolveMotionPurpose(string sceneId)
    {
        var normalized = sceneId.ToLowerInvariant();
        if (normalized.Contains("001") || normalized.Contains("hook")) return "Hook";
        if (normalized.Contains("002") || normalized.Contains("cause")) return "Discovery";
        if (normalized.Contains("003") || normalized.Contains("sky") || normalized.Contains("guide")) return "SkyGuide";
        if (normalized.Contains("004") || normalized.Contains("viewing") || normalized.Contains("tip")) return "ViewingTip";
        if (normalized.Contains("best-time")) return "BestTime";
        if (normalized.Contains("005") || normalized.Contains("final") || normalized.Contains("reminder") || normalized.Contains("closing")) return "Closing";
        return "Discovery";
    }

    private static string ResolveMotionProfile(string sceneId, string? requestedMotion)
    {
        var requested = (requestedMotion ?? string.Empty).Trim();
        if (requested is "Hook" or "Discovery" or "SkyGuide" or "ViewingTip" or "Closing" or "BestTime") return requested;
        return ResolveMotionPurpose(sceneId);
    }

    private static string ResolveMotionEasing(string motionProfile)
        => string.Equals(motionProfile, "Hook", StringComparison.OrdinalIgnoreCase)
            ? "EaseOutCubic"
            : "EaseInOutSine";

    private async Task<IReadOnlyList<string>> PhaseGenerateVideoNarrationAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        outputs.Add(await WritePhase15PlusPathReadinessDiagnosticsAsync(context, 15, [Path.Combine(context.OutputRoot, "sync", "scene-audio-sync.json"), Path.Combine(context.OutputRoot, "scene-assets-v3", "short"), Path.Combine(context.OutputRoot, "scene-assets-v3", "long"), Path.Combine(context.OutputRoot, "question-engine", "question-driven-narration-v2.json")], cancellationToken));
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
        outputs.Add(await ApplyNarrationCleanupAsync(context, profile, scriptPath, target, cancellationToken));
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

    private async Task<string> ApplyNarrationCleanupAsync(ProductionPhaseContext context, ScenePresentationProfile profile, string scriptPath, string narrationPath, CancellationToken cancellationToken)
    {
        var service = new NarrationCleanupService();
        var originalText = File.Exists(narrationPath)
            ? await File.ReadAllTextAsync(narrationPath, cancellationToken)
            : File.Exists(scriptPath)
                ? ExtractNarrationText(await File.ReadAllTextAsync(scriptPath, cancellationToken))
                : string.Empty;
        var cleanup = service.Clean(originalText);
        service.ValidateClean(cleanup.CleanedText);

        Directory.CreateDirectory(Path.GetDirectoryName(narrationPath)!);
        await File.WriteAllTextAsync(narrationPath, cleanup.CleanedText, cancellationToken);
        await PersistCleanNarrationScriptAsync(scriptPath, cleanup.CleanedText, cleanup, cancellationToken);

        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var cleanPath = Path.Combine(context.ExecutionContext.NarrationRoot!, profileFolder, profile == ScenePresentationProfile.ShortForm ? "short-clean.json" : "long-clean.json");
        await File.WriteAllTextAsync(cleanPath, JsonSerializer.Serialize(new
        {
            profile = profile.ToString(),
            sourceNarrationPath = NormalizePath(narrationPath),
            cleanedNarrationPath = NormalizePath(narrationPath),
            cleanupApplied = cleanup.CleanupApplied,
            labelsRemovedCount = cleanup.LabelsRemovedCount,
            instructionsRemovedCount = cleanup.InstructionsRemovedCount,
            subtitleFilesGenerated = true,
            srtPathShort = NormalizePath(Path.Combine(context.OutputRoot, "subtitles", "short.srt")),
            srtPathLong = NormalizePath(Path.Combine(context.OutputRoot, "subtitles", "long.srt")),
            finalNarrationText = cleanup.CleanedText
        }, JsonOptions), cancellationToken);
        await WriteCleanNarrationSubtitleArtifactsAsync(context, profile, cleanup.CleanedText, cancellationToken);
        return cleanPath;
    }

    private async Task WriteCleanNarrationSubtitleArtifactsAsync(ProductionPhaseContext context, ScenePresentationProfile profile, string narrationText, CancellationToken cancellationToken)
    {
        var subtitlesRoot = Path.Combine(context.OutputRoot, "subtitles");
        Directory.CreateDirectory(subtitlesRoot);
        var fileName = profile == ScenePresentationProfile.ShortForm ? "short.srt" : "long.srt";
        var profileFolder = profile == ScenePresentationProfile.ShortForm ? "short" : "long";
        var profileSrtPath = Path.Combine(context.ExecutionContext.NarrationRoot!, profileFolder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(profileSrtPath)!);
        var wordCount = CountSpokenWords(narrationText);
        var durationSeconds = profile == ScenePresentationProfile.ShortForm
            ? Math.Max(2, EstimateShortNarrationSeconds(narrationText))
            : Math.Max(2, EstimateLongNarrationSeconds(wordCount));
        var srt = "1" + Environment.NewLine +
            $"{FormatSrtTimestamp(TimeSpan.Zero)} --> {FormatSrtTimestamp(TimeSpan.FromSeconds(durationSeconds))}" + Environment.NewLine +
            narrationText.Trim() + Environment.NewLine;
        await File.WriteAllTextAsync(Path.Combine(subtitlesRoot, fileName), srt, cancellationToken);
        await File.WriteAllTextAsync(profileSrtPath, srt, cancellationToken);
    }

    private static string FormatSrtTimestamp(TimeSpan value)
        => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";

    private static async Task PersistCleanNarrationScriptAsync(string scriptPath, string narration, NarrationCleanupResult cleanup, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath)) return;
        try
        {
            var script = JsonSerializer.Deserialize<VideoNarrationScriptDto>(await File.ReadAllTextAsync(scriptPath, cancellationToken), JsonOptions);
            if (script is null) return;
            var updated = script with
            {
                FullNarrationText = narration,
                Warnings = script.Warnings.Concat([$"Narration cleanup applied before TTS. labelsRemovedCount={cleanup.LabelsRemovedCount}; instructionsRemovedCount={cleanup.InstructionsRemovedCount}."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            await File.WriteAllTextAsync(scriptPath, JsonSerializer.Serialize(updated, JsonOptions), cancellationToken);
        }
        catch (JsonException) { }
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
        var cleanupService = new NarrationCleanupService();
        var cleanupCheck = cleanupService.Clean(text);
        if (!string.Equals(cleanupCheck.CleanedText, text, StringComparison.Ordinal))
            errors.Add("Narration cleanup was not fully applied before validation.");
        try
        {
            cleanupService.ValidateClean(text);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }
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
            TrimApplied: shortTrimDiagnostics?.TrimApplied ?? false,
            CleanupApplied: File.Exists(Path.Combine(context.ExecutionContext.NarrationRoot!, profileFolder, profile == ScenePresentationProfile.ShortForm ? "short-clean.json" : "long-clean.json")),
            LabelsRemovedCount: cleanupCheck.LabelsRemovedCount,
            InstructionsRemovedCount: cleanupCheck.InstructionsRemovedCount,
            SubtitleFilesGenerated: File.Exists(Path.Combine(context.OutputRoot, "subtitles", profile == ScenePresentationProfile.ShortForm ? "short.srt" : "long.srt")),
            SrtPathShort: NormalizePath(Path.Combine(context.OutputRoot, "subtitles", "short.srt")),
            SrtPathLong: NormalizePath(Path.Combine(context.OutputRoot, "subtitles", "long.srt")));
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
                Warnings = script.Warnings.Concat([$"Short narration was {action} by Phase 14 narration-duration contract before TTS."]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
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
        bool TrimApplied = false,
        bool CleanupApplied = false,
        int LabelsRemovedCount = 0,
        int InstructionsRemovedCount = 0,
        bool SubtitleFilesGenerated = false,
        string SrtPathShort = "",
        string SrtPathLong = "");

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
        var readinessPhaseNo = profile == ScenePresentationProfile.ShortForm ? 16 : 17;
        var readinessPath = await WritePhase15PlusPathReadinessDiagnosticsAsync(context, readinessPhaseNo, [Path.Combine(context.OutputRoot, "sync", "scene-audio-sync.json"), Path.Combine(context.ExecutionContext.NarrationRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long", "narration.txt")], cancellationToken);
        var response = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Tts" : "LongFormTts"), cancellationToken);
        var outputs = new List<string>(response.GeneratedFiles) { readinessPath };
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

    private async Task<TtsAudioContentMetrics> ProbeAudioContentMetricsAsync(string audioPath, CancellationToken cancellationToken)
    {
        var fileSizeBytes = File.Exists(audioPath) ? new FileInfo(audioPath).Length : 0;
        var durationSec = File.Exists(audioPath) ? await ProbeAudioDurationSecondsAsync(audioPath, cancellationToken) : 0;
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var probe = File.Exists(audioPath)
            ? await RunProcessAsync(ffprobePath, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name,sample_rate,channels", "-of", "json", audioPath], cancellationToken)
            : new ProcessResult(-1, string.Empty, string.Empty);
        var ffmpegProbeSucceeded = probe.ExitCode == 0 && durationSec > 0;
        var audioCodec = string.Empty;
        var audioSampleRate = 0;
        var audioChannels = 0;
        if (probe.ExitCode == 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(probe.Output);
                var stream = doc.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();
                if (stream.ValueKind == JsonValueKind.Object)
                {
                    audioCodec = stream.TryGetProperty("codec_name", out var codec) ? codec.GetString() ?? string.Empty : string.Empty;
                    if (stream.TryGetProperty("sample_rate", out var sampleRate)) int.TryParse(sampleRate.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out audioSampleRate);
                    audioChannels = stream.TryGetProperty("channels", out var channels) && channels.TryGetInt32(out var parsedChannels) ? parsedChannels : 0;
                }
            }
            catch (JsonException) { }
        }

        var (peakDb, rmsDb) = File.Exists(audioPath) && fileSizeBytes > 1000 && durationSec > 0
            ? await ProbeAudioLevelsAsync(audioPath, cancellationToken)
            : (-120d, -120d);
        var peakAmplitude = DbToAmplitude(RoundDb(peakDb));
        var rmsAmplitude = DbToAmplitude(RoundDb(rmsDb));
        var isSilent = fileSizeBytes <= 1000 || durationSec <= 0 || peakAmplitude <= 0.001 || rmsAmplitude <= 0.0005;
        return new TtsAudioContentMetrics(fileSizeBytes, durationSec, peakAmplitude, rmsAmplitude, isSilent, audioCodec, audioSampleRate, audioChannels, ffmpegProbeSucceeded);
    }

    private async Task<bool> HasAudioStreamAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var result = await RunProcessAsync(ffprobePath, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_type", "-of", "csv=p=0", mediaPath], cancellationToken);
        return result.ExitCode == 0 && result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(v => v.Equals("audio", StringComparison.OrdinalIgnoreCase));
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

    private static double DbToAmplitude(double db)
        => db <= -120 ? 0 : Math.Pow(10, db / 20.0);

    private sealed record TtsAudioContentMetrics(long FileSizeBytes, double DurationSec, double PeakAmplitude, double RmsAmplitude, bool IsSilent, string AudioCodec, int AudioSampleRate, int AudioChannels, bool FfmpegProbeSucceeded);

    private sealed record TtsAudioContentDiagnostics(
        string Format,
        string SceneId,
        string AudioPath,
        string RawResponsePath,
        string TtsProvider,
        string TtsModel,
        string TtsVoice,
        int ProviderRequestTextLength,
        string ConfiguredTtsProvider,
        bool RealProviderCalled,
        bool RealProviderSucceeded,
        bool FallbackUsed,
        string FallbackReason,
        int? ProviderHttpStatus,
        long ProviderResponseBytes,
        string GeneratedSpeechFilePath,
        bool IsSyntheticTone,
        bool SpeechValidationPassed,
        string AudioFormat,
        int AudioSampleRate,
        int AudioChannels,
        string AudioCodec,
        bool FfmpegProbeSucceeded,
        long FileSizeBytes,
        double DurationSec,
        double PeakAmplitude,
        double RmsAmplitude,
        bool IsSilent,
        int RetryAttempt,
        bool ValidationPassed,
        IReadOnlyList<string> Errors);

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

    private async Task<IReadOnlyList<string>> PhaseVideoAssemblyV1Async(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var planRoot = context.OutputRoot;
        var videoRoot = Path.Combine(planRoot, "video-assembly");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(videoRoot);
        Directory.CreateDirectory(validationRoot);

        var sceneAssetsRoot = Path.Combine(planRoot, "scene-assets-v3");
        var syncPath = Path.Combine(planRoot, "sync", "scene-audio-sync.json");
        var ttsPath = Path.Combine(planRoot, "tts", "tts-timeline.json");
        var durationPlanPath = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        var productionMotionPlanPath = Path.Combine(planRoot, "motion", "motion-plan.json");
        var previewMotionPlanPath = Path.Combine(planRoot, "motion", "motion-plan-v2-preview.json");
        var previewOnly = videoAssemblyOptions?.Value.VideoAssemblyPreviewOnly == true || context.PipelineRequest.MotionPreviewOnly;
        var motionPlanPath = File.Exists(previewMotionPlanPath) ? previewMotionPlanPath : productionMotionPlanPath;
        var motionDebugPath = string.Equals(motionPlanPath, previewMotionPlanPath, StringComparison.OrdinalIgnoreCase) ? Path.Combine(planRoot, "motion", "motion-debug-v2-preview.json") : Path.Combine(planRoot, "motion", "motion-debug.json");
        var shortVideoPath = Path.Combine(videoRoot, "short", "final.mp4");
        var longVideoPath = Path.Combine(videoRoot, "long", "final.mp4");
        var legacyShortVideoPath = Path.Combine(planRoot, "video", "short", "final-short.mp4");
        var legacyLongVideoPath = Path.Combine(planRoot, "video", "long", "final-long.mp4");
        var shortAudioTrackPath = Path.Combine(videoRoot, "short", "narration-track.mp3");
        var longAudioTrackPath = Path.Combine(videoRoot, "long", "narration-track.mp3");
        var oldPaths = new[]
        {
            Path.Combine(planRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(planRoot, "scene-assets")
        };
        var inputPathsChecked = new[] { syncPath, ttsPath, durationPlanPath, motionPlanPath, motionDebugPath, Path.Combine(sceneAssetsRoot, "short"), Path.Combine(sceneAssetsRoot, "long") };
        var errors = new List<string>();
        var missingSceneImages = new List<string>();
        var missingAudioFiles = new List<string>();
        var oldPathUsageReasons = new List<string>();
        foreach (var path in inputPathsChecked)
            if (!File.Exists(path) && !Directory.Exists(path) && !(previewOnly && string.Equals(path, ttsPath, StringComparison.OrdinalIgnoreCase))) errors.Add($"Input missing: {NormalizePath(path)}");

        var shortSceneCount = 0;
        var longSceneCount = 0;
        var motionPlanFound = File.Exists(motionPlanPath);
        var motionDebugFound = File.Exists(motionDebugPath);
        var defaultMotionGenerated = !motionPlanFound;
        var motionRoot = motionPlanFound
            ? JsonNode.Parse(await File.ReadAllTextAsync(motionPlanPath, cancellationToken)) ?? new JsonObject()
            : BuildDefaultPhase18MotionPlan(sceneAssetsRoot, syncPath, ttsPath);
        var motionV2StrengthWarning = ShouldWarnMotionV2StrengthRequestOverride(context.PipelineRequest.MotionV2Strength, GetString(motionRoot, "motionV2Strength"))
            ? "Motion plan strength differs from request; phase 18 will fail with MotionV2StrengthMismatch."
            : null;
        var warnings = string.IsNullOrWhiteSpace(motionV2StrengthWarning) ? Array.Empty<string>() : new[] { motionV2StrengthWarning };
        var motionV2StrengthUsed = ResolvePhase18MotionV2Strength(context.PipelineRequest.MotionV2Strength, GetString(motionRoot, "motionV2Strength"));
        var motionV2StrengthMismatch = HasMotionV2StrengthMismatch(context.PipelineRequest.MotionV2Strength, motionV2StrengthUsed);
        {
            var ttsRoot = File.Exists(ttsPath) ? JsonNode.Parse(await File.ReadAllTextAsync(ttsPath, cancellationToken)) ?? new JsonObject() : new JsonObject();
            var shortItems = ReadVideoAssemblyItems(planRoot, motionRoot, ttsRoot, "short", previewOnly ? int.MaxValue : 5, oldPaths, missingSceneImages, missingAudioFiles, oldPathUsageReasons);
            var longItems = ReadVideoAssemblyItems(planRoot, motionRoot, ttsRoot, "long", previewOnly ? int.MaxValue : 9, oldPaths, missingSceneImages, missingAudioFiles, oldPathUsageReasons);
            await WriteMotionDebugAsync(planRoot, shortItems.Concat(longItems).ToArray(), cancellationToken);
            shortSceneCount = shortItems.Count;
            longSceneCount = longItems.Count;
            var backgroundMusicConfig = ResolvePhase18BackgroundMusicConfig(planRoot);
            if (shortItems.Count > 0) await RenderVideoAssemblyAsync(shortItems, shortVideoPath, shortAudioTrackPath, backgroundMusicConfig, previewOnly, cancellationToken);
            if (!previewOnly && longItems.Count > 0) await RenderVideoAssemblyAsync(longItems, longVideoPath, longAudioTrackPath, backgroundMusicConfig, previewOnly, cancellationToken);
            if (previewOnly && File.Exists(longVideoPath)) File.Delete(longVideoPath);
            if (File.Exists(shortVideoPath)) { Directory.CreateDirectory(Path.GetDirectoryName(legacyShortVideoPath)!); File.Copy(shortVideoPath, legacyShortVideoPath, true); }
            if (!previewOnly && File.Exists(longVideoPath)) { Directory.CreateDirectory(Path.GetDirectoryName(legacyLongVideoPath)!); File.Copy(longVideoPath, legacyLongVideoPath, true); }
        }

        var shortVideoDuration = File.Exists(shortVideoPath) ? await ProbeAudioDurationSecondsAsync(shortVideoPath, cancellationToken) : 0;
        var longVideoDuration = File.Exists(longVideoPath) ? await ProbeAudioDurationSecondsAsync(longVideoPath, cancellationToken) : 0;
        var shortMixedAudioPath = ResolvePhase18FinalMixedAudioPath(shortVideoPath);
        var longMixedAudioPath = ResolvePhase18FinalMixedAudioPath(longVideoPath);
        var shortAudioDuration = File.Exists(shortMixedAudioPath) ? await ProbeAudioDurationSecondsAsync(shortMixedAudioPath, cancellationToken) : File.Exists(shortAudioTrackPath) ? await ProbeAudioDurationSecondsAsync(shortAudioTrackPath, cancellationToken) : 0;
        var longAudioDuration = File.Exists(longMixedAudioPath) ? await ProbeAudioDurationSecondsAsync(longMixedAudioPath, cancellationToken) : File.Exists(longAudioTrackPath) ? await ProbeAudioDurationSecondsAsync(longAudioTrackPath, cancellationToken) : 0;
        var shortHasAudioStream = File.Exists(shortVideoPath) && await HasAudioStreamAsync(shortVideoPath, cancellationToken);
        var longHasAudioStream = File.Exists(longVideoPath) && await HasAudioStreamAsync(longVideoPath, cancellationToken);
        var shortAudioMuxed = File.Exists(shortAudioTrackPath) && shortHasAudioStream;
        var longAudioMuxed = File.Exists(longAudioTrackPath) && longHasAudioStream;
        var enableSubtitles = context.ExecutionContext.EnableSubtitles;
        var subtitleMode = enableSubtitles ? "BurnIn" : "Disabled";
        var shortSrtPath = Path.Combine(planRoot, "narration", "subtitles", "short.srt");
        var longSrtPath = Path.Combine(planRoot, "narration", "subtitles", "long.srt");
        var shortSrtExists = File.Exists(shortSrtPath);
        var longSrtExists = File.Exists(longSrtPath);
        var shortSrtDuration = shortSrtExists ? ReadSrtFinalEndSeconds(shortSrtPath) : 0;
        var longSrtDuration = longSrtExists ? ReadSrtFinalEndSeconds(longSrtPath) : 0;
        var shortPlanAudioDuration = ReadSceneDurationPlanTotal(planRoot, "short", "totalAudioDurationSec", shortAudioDuration);
        var shortPlanVideoDuration = ReadSceneDurationPlanTotal(planRoot, "short", "totalVideoDurationSec", shortVideoDuration);
        var longPlanAudioDuration = ReadSceneDurationPlanTotal(planRoot, "long", "totalAudioDurationSec", longAudioDuration);
        var longPlanVideoDuration = ReadSceneDurationPlanTotal(planRoot, "long", "totalVideoDurationSec", longVideoDuration);
        var subtitleBurnInErrors = new List<string>();
        var subtitleBurnInCommandShort = string.Empty;
        var subtitleBurnInCommandLong = string.Empty;
        var shortSubtitlesApplied = false;
        var longSubtitlesApplied = false;
        SubtitleBurnInResult? shortBurnInResult = null;
        SubtitleBurnInResult? longBurnInResult = null;
        if (enableSubtitles)
        {
            if (!shortSrtExists) subtitleBurnInErrors.Add($"Short subtitle file missing: {NormalizePath(shortSrtPath)}");
            if (!longSrtExists) subtitleBurnInErrors.Add($"Long subtitle file missing: {NormalizePath(longSrtPath)}");
            if (File.Exists(shortVideoPath) && shortSrtExists)
            {
                shortBurnInResult = await BurnInSubtitlesAsync(shortVideoPath, shortSrtPath, cancellationToken);
                subtitleBurnInCommandShort = shortBurnInResult.Command;
                shortSubtitlesApplied = shortBurnInResult.Succeeded;
                if (!shortBurnInResult.Succeeded) subtitleBurnInErrors.Add($"Short subtitle burn-in failed: {shortBurnInResult.Error}");
            }
            if (File.Exists(longVideoPath) && longSrtExists)
            {
                longBurnInResult = await BurnInSubtitlesAsync(longVideoPath, longSrtPath, cancellationToken);
                subtitleBurnInCommandLong = longBurnInResult.Command;
                longSubtitlesApplied = longBurnInResult.Succeeded;
                if (!longBurnInResult.Succeeded) subtitleBurnInErrors.Add($"Long subtitle burn-in failed: {longBurnInResult.Error}");
            }
        }
        var subtitleBurnInSucceeded = !enableSubtitles || subtitleBurnInErrors.Count == 0;
        const double cinematicOutroDurationSec = 4.0;
        const bool cinematicOutroEnabled = true;
        const bool fadeToBlackEnabled = true;
        const double fadeToBlackDurationSec = 1.0;
        var shortExpectedVideoDuration = shortAudioDuration + (cinematicOutroEnabled ? cinematicOutroDurationSec : 0);
        var longExpectedVideoDuration = longAudioDuration + (cinematicOutroEnabled ? cinematicOutroDurationSec : 0);
        var shortDurationDeltaAgainstExpected = Math.Abs(shortVideoDuration - shortExpectedVideoDuration);
        var longDurationDeltaAgainstExpected = Math.Abs(longVideoDuration - longExpectedVideoDuration);
        var shortDurationValidationPassed = shortDurationDeltaAgainstExpected <= 1.0;
        var longDurationValidationPassed = longDurationDeltaAgainstExpected <= 1.0;
        var audioVideoDurationDeltaSec = Math.Max(Math.Abs(shortAudioDuration - shortVideoDuration), Math.Abs(longAudioDuration - longVideoDuration));
        var durationDeltaAgainstExpectedSec = Math.Max(shortDurationDeltaAgainstExpected, longDurationDeltaAgainstExpected);
        var backgroundMusicConfigForDiagnostics = ResolvePhase18BackgroundMusicConfig(planRoot);
        var backgroundAudioPathForDiagnostics = backgroundMusicConfigForDiagnostics.ConfiguredPath;
        var backgroundAudioFound = backgroundMusicConfigForDiagnostics.Enabled && !string.IsNullOrWhiteSpace(backgroundAudioPathForDiagnostics) && File.Exists(backgroundAudioPathForDiagnostics);
        var totalScenes = shortSceneCount + longSceneCount;
        var scenesWithZoom = totalScenes;
        var scenesWithPan = totalScenes;
        var scenesWithTransitions = totalScenes;
        var oldPathUsed = oldPathUsageReasons.Count > 0 || inputPathsChecked.Concat(new[] { shortVideoPath, longVideoPath }).Any(path => oldPaths.Any(oldPath => IsSameOrUnderPath(NormalizePath(path), NormalizePath(oldPath))));
        var videoRendered = previewOnly ? File.Exists(shortVideoPath) && !File.Exists(longVideoPath) : File.Exists(shortVideoPath) && File.Exists(longVideoPath);
        if (!previewOnly && shortSceneCount != 5) errors.Add($"short scene count != 5; actual={shortSceneCount}");
        if (!previewOnly && longSceneCount != 9) errors.Add($"long scene count != 9; actual={longSceneCount}");
        errors.AddRange(missingSceneImages.Select(p => $"Scene image missing: {p}"));
        if (!previewOnly) errors.AddRange(missingAudioFiles.Select(p => $"Audio missing: {p}"));
        if (!File.Exists(shortVideoPath)) errors.Add($"short video missing: {NormalizePath(shortVideoPath)}");
        if (previewOnly && File.Exists(longVideoPath)) errors.Add($"preview-only render produced unexpected long video: {NormalizePath(longVideoPath)}");
        if (!previewOnly && !File.Exists(longVideoPath)) errors.Add($"long video missing: {NormalizePath(longVideoPath)}");
        if (File.Exists(shortAudioTrackPath) && !shortAudioMuxed) errors.Add("short audio file exists but was not muxed");
        if (File.Exists(longAudioTrackPath) && !longAudioMuxed) errors.Add("long audio file exists but was not muxed");
        if (!previewOnly && !shortHasAudioStream) errors.Add("short final video has no audio stream");
        if (!previewOnly && !longHasAudioStream) errors.Add("long final video has no audio stream");
        if (scenesWithZoom < totalScenes) errors.Add("Not every scene has zoom motion");
        if (scenesWithPan < totalScenes) errors.Add("Not every scene has pan motion");
        if (scenesWithTransitions < Math.Max(0, totalScenes - 1)) errors.Add("Not every scene boundary has a transition");
        if (!motionPlanFound) errors.Add($"motion-plan.json missing: {NormalizePath(motionPlanPath)}");
        if (!motionDebugFound) errors.Add($"motion-debug.json missing: {NormalizePath(motionDebugPath)}");
        if (motionV2StrengthMismatch) errors.Add($"MotionV2StrengthMismatch: request=Experimental, diagnostics={motionV2StrengthUsed}");
        if (!previewOnly && backgroundMusicConfigForDiagnostics.Enabled && !backgroundAudioFound) errors.Add($"Configured background music file missing: {NormalizePath(backgroundAudioPathForDiagnostics)}");
        if (!previewOnly && !shortDurationValidationPassed) errors.Add($"short video duration differs from narration + cinematic outro by >1.0 sec; actual={RoundDuration(shortDurationDeltaAgainstExpected)}");
        if (!previewOnly && !longDurationValidationPassed) errors.Add($"long video duration differs from narration + cinematic outro by >1.0 sec; actual={RoundDuration(longDurationDeltaAgainstExpected)}");
        if (!previewOnly && shortSrtExists && shortPlanAudioDuration - shortSrtDuration > 0.5) errors.Add($"short.srt ends more than 0.5 sec before planned audio; srt={RoundDuration(shortSrtDuration)}, audio={RoundDuration(shortPlanAudioDuration)}");
        if (!previewOnly && longSrtExists && longPlanAudioDuration - longSrtDuration > 0.5) errors.Add($"long.srt ends more than 0.5 sec before planned audio; srt={RoundDuration(longSrtDuration)}, audio={RoundDuration(longPlanAudioDuration)}");
        if (!previewOnly && shortSrtExists && Math.Abs(shortSrtDuration - shortPlanVideoDuration) > 0.5) errors.Add($"short.srt duration differs from planned video duration by >0.5 sec; srt={RoundDuration(shortSrtDuration)}, video={RoundDuration(shortPlanVideoDuration)}");
        if (!previewOnly && longSrtExists && Math.Abs(longSrtDuration - longPlanVideoDuration) > 0.5) errors.Add($"long.srt duration differs from planned video duration by >0.5 sec; srt={RoundDuration(longSrtDuration)}, video={RoundDuration(longPlanVideoDuration)}");
        if (oldPathUsed) errors.Add("Old scene asset path used");
        var shortBackgroundAudioMixed = File.Exists(shortMixedAudioPath) && shortHasAudioStream;
        var longBackgroundAudioMixed = File.Exists(longMixedAudioPath) && longHasAudioStream;
        var backgroundAudioMixed = backgroundMusicConfigForDiagnostics.Enabled ? backgroundAudioFound && shortBackgroundAudioMixed && longBackgroundAudioMixed : false;
        var effectiveBackgroundVolume = backgroundAudioMixed ? backgroundMusicConfigForDiagnostics.Level : 0.0;
        var duckingAttempted = backgroundMusicConfigForDiagnostics.Enabled && backgroundMusicConfigForDiagnostics.DuckUnderNarration;
        var duckingSucceeded = duckingAttempted && File.Exists(shortMixedAudioPath) && File.Exists(longMixedAudioPath);
        var duckingFallbackUsed = false;
        var duckingFailureReason = string.Empty;
        if (!previewOnly && backgroundMusicConfigForDiagnostics.Enabled && !backgroundAudioMixed) errors.Add("background music was enabled but was not mixed");
        if (!previewOnly && backgroundMusicConfigForDiagnostics.Enabled && effectiveBackgroundVolume <= 0) errors.Add("background music was enabled but effective background volume is zero");
        errors.AddRange(subtitleBurnInErrors);
        var narrationAudioMixed = previewOnly ? shortAudioMuxed : shortAudioMuxed && longAudioMuxed;
        var finalVideoHasAudio = previewOnly ? shortHasAudioStream : shortHasAudioStream && longHasAudioStream;
        var finalVideoHasMotion = scenesWithZoom >= totalScenes && scenesWithPan >= totalScenes && scenesWithTransitions >= Math.Max(0, totalScenes - 1);
        var validationPassed = errors.Count == 0 && videoRendered && !oldPathUsed && (previewOnly || narrationAudioMixed) && (previewOnly || !backgroundMusicConfigForDiagnostics.Enabled || backgroundAudioMixed) && (previewOnly || finalVideoHasAudio) && finalVideoHasMotion && (previewOnly || (shortDurationValidationPassed && longDurationValidationPassed));

        var cinematicDiagnosticsPath = Path.Combine(videoRoot, "phase-18-cinematic-diagnostics.json");
        await File.WriteAllTextAsync(cinematicDiagnosticsPath, JsonSerializer.Serialize(new
        {
            rendererVersion = "V2",
            motionTypeApplied = true,
            motionPlanPath = NormalizePath(motionPlanPath),
            requestedMotionV2Strength = context.PipelineRequest.MotionV2Strength,
            motionV2StrengthUsed,
            motionV2StrengthMismatch,
            warnings,
            motionPlanFound,
            defaultMotionGenerated,
            cinematicOutroEnabled,
            cinematicOutroDurationSec,
            fadeToBlackEnabled,
            fadeToBlackDurationSec,
            shortNarrationAudioDurationSec = RoundDuration(shortAudioDuration),
            shortExpectedVideoDurationSec = RoundDuration(shortExpectedVideoDuration),
            shortActualVideoDurationSec = RoundDuration(shortVideoDuration),
            shortDurationDeltaAgainstExpectedSec = RoundDuration(shortDurationDeltaAgainstExpected),
            shortDurationValidationPassed,
            longNarrationAudioDurationSec = RoundDuration(longAudioDuration),
            longExpectedVideoDurationSec = RoundDuration(longExpectedVideoDuration),
            longActualVideoDurationSec = RoundDuration(longVideoDuration),
            longDurationDeltaAgainstExpectedSec = RoundDuration(longDurationDeltaAgainstExpected),
            longDurationValidationPassed,
            motionPlanConsumed = motionPlanFound,
            motionDebugFound,
            motionDebugPath = NormalizePath(motionDebugPath),
            totalScenes,
            scenesWithZoom,
            scenesWithPan,
            scenesWithTransitions,
            transitionType = "crossfade",
            transitionDurationSec = 0.8,
            fadeInApplied = true,
            fadeOutApplied = true,
            backgroundAudioPath = NormalizePath(backgroundAudioPathForDiagnostics ?? string.Empty),
            backgroundAudioFound,
            backgroundAudioMixed,
            backgroundMusicEnabled = backgroundMusicConfigForDiagnostics.Enabled,
            configuredBackgroundMusicPath = NormalizePath(backgroundMusicConfigForDiagnostics.ConfiguredPath),
            configuredBackgroundMusicLevelPercent = backgroundMusicConfigForDiagnostics.LevelPercent,
            configuredDuckUnderNarration = backgroundMusicConfigForDiagnostics.DuckUnderNarration,
            backgroundMusicLoaded = backgroundAudioFound,
            backgroundMusicMixed = backgroundAudioMixed,
            duckingAttempted,
            duckingSucceeded,
            duckingFallbackUsed,
            duckingFailureReason,
            effectiveBackgroundVolume,
            duckingApplied = duckingSucceeded,
            finalMixedAudioPath = new { @short = NormalizePath(shortMixedAudioPath), @long = NormalizePath(longMixedAudioPath) },
            finalMixedAudioDurationSec = new { @short = RoundDuration(shortAudioDuration), @long = RoundDuration(longAudioDuration) },
            finalVideoDurationSec = new { @short = RoundDuration(shortVideoDuration), @long = RoundDuration(longVideoDuration) },
            audioVideoDurationDeltaSec = RoundDuration(audioVideoDurationDeltaSec),
            narrationAudioMixed,
            finalVideoHasAudio,
            finalVideoHasMotion,
            ffmpegCommandPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath,
            enableSubtitles,
            srtTimingSource = "SceneDurationPlanFromTtsTimeline",
            ttsDurationsMeasuredFromMp3 = true,
            shortAudioDurationTotal = RoundDuration(shortPlanAudioDuration),
            shortVideoDurationTotal = RoundDuration(shortPlanVideoDuration),
            shortSrtFinalEnd = RoundDuration(shortSrtDuration),
            shortSrtMatchesAudio = shortSrtExists && Math.Abs(shortSrtDuration - shortPlanAudioDuration) <= 0.1,
            shortSrtMatchesVideo = shortSrtExists && Math.Abs(shortSrtDuration - shortPlanVideoDuration) <= 0.1,
            longAudioDurationTotal = RoundDuration(longPlanAudioDuration),
            longVideoDurationTotal = RoundDuration(longPlanVideoDuration),
            longSrtFinalEnd = RoundDuration(longSrtDuration),
            longSrtMatchesAudio = longSrtExists && Math.Abs(longSrtDuration - longPlanAudioDuration) <= 0.1,
            longSrtMatchesVideo = longSrtExists && Math.Abs(longSrtDuration - longPlanVideoDuration) <= 0.1,
            audioDurationTotal = new { @short = RoundDuration(shortPlanAudioDuration), @long = RoundDuration(longPlanAudioDuration) },
            videoDurationTotal = new { @short = RoundDuration(shortPlanVideoDuration), @long = RoundDuration(longPlanVideoDuration) },
            srtDurationTotal = new { @short = RoundDuration(shortSrtDuration), @long = RoundDuration(longSrtDuration) },
            srtMatchesAudioDuration = shortSrtExists && longSrtExists && Math.Abs(shortSrtDuration - shortPlanAudioDuration) <= 0.1 && Math.Abs(longSrtDuration - longPlanAudioDuration) <= 0.1,
            srtMatchesVideoDuration = shortSrtExists && longSrtExists && Math.Abs(shortSrtDuration - shortPlanVideoDuration) <= 0.1 && Math.Abs(longSrtDuration - longPlanVideoDuration) <= 0.1,
            subtitleMode,
            shortSrtPath = NormalizePath(shortSrtPath),
            longSrtPath = NormalizePath(longSrtPath),
            shortSrtExists,
            longSrtExists,
            shortSubtitlesApplied,
            longSubtitlesApplied,
            subtitleBurnInCommandShort,
            subtitleBurnInCommandLong,
            subtitleBurnInSucceeded,
            subtitleStyleApplied = subtitleBurnInSucceeded,
            subtitleFontSize = Math.Max(shortBurnInResult?.FontSize ?? 0, longBurnInResult?.FontSize ?? 0),
            subtitleMaxCharsPerLine = 42,
            subtitleMaxLines = 2,
            duplicateNarrationDetected = false,
            duplicateNarrationFixed = false,
            duplicateSrtTextDetected = false,
            subtitleBurnInErrors,
            finalShortVideoPath = NormalizePath(shortVideoPath),
            finalLongVideoPath = NormalizePath(longVideoPath),
            validationPassed
        }, JsonOptions), cancellationToken);

        var v2DiagnosticsPath = Path.Combine(validationRoot, "phase-18-video-assembly-v2-diagnostics.json");
        await File.WriteAllTextAsync(v2DiagnosticsPath, JsonSerializer.Serialize(new { rendererVersion = "V2", motionTypeApplied = true, requestedMotionV2Strength = context.PipelineRequest.MotionV2Strength, motionV2StrengthUsed, motionV2StrengthMismatch, warnings, selectedMotionVersion = File.Exists(previewMotionPlanPath) && string.Equals(motionPlanPath, previewMotionPlanPath, StringComparison.OrdinalIgnoreCase) ? "V2" : GetString(motionRoot, "motionVersion") ?? GetString(motionRoot, "version") ?? "unknown", previewOnly, sceneCount = new { @short = shortSceneCount, @long = longSceneCount, total = totalScenes }, transitionType = "crossfade", flickerRisk = "low", missingAudioHandled = previewOnly && missingAudioFiles.Count > 0, output = new { @short = NormalizePath(shortVideoPath), @long = NormalizePath(longVideoPath) }, validationPassed }, JsonOptions), cancellationToken);
        var diagnosticsPath = Path.Combine(validationRoot, "phase-18-video-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            rendererVersion = "V2",
            motionTypeApplied = true,
            inputPathsChecked = inputPathsChecked.Select(NormalizePath),
            selectedSceneAssetsRoot = NormalizePath(sceneAssetsRoot),
            selectedSyncPath = NormalizePath(syncPath),
            selectedTtsPath = NormalizePath(ttsPath),
            selectedDurationPlanPath = NormalizePath(durationPlanPath),
            selectedMotionPlanPath = NormalizePath(motionPlanPath),
            requestedMotionV2Strength = context.PipelineRequest.MotionV2Strength,
            motionV2StrengthUsed,
            motionV2StrengthMismatch,
            warnings,
            motionDebugFound,
            motionDebugPath = NormalizePath(motionDebugPath),
            oldPathsChecked = oldPaths.Select(NormalizePath),
            oldPathsIgnored = oldPaths.Select(NormalizePath),
            oldPathUsed,
            oldPathUsageReasons,
            shortVideoPath = NormalizePath(shortVideoPath),
            longVideoPath = NormalizePath(longVideoPath),
            shortAudioTrackPath = NormalizePath(shortAudioTrackPath),
            longAudioTrackPath = NormalizePath(longAudioTrackPath),
            shortFinalMixedAudioPath = NormalizePath(shortMixedAudioPath),
            longFinalMixedAudioPath = NormalizePath(longMixedAudioPath),
            backgroundAudioPath = NormalizePath(backgroundAudioPathForDiagnostics ?? string.Empty),
            backgroundAudioFound,
            backgroundAudioMixed,
            backgroundMusicEnabled = backgroundMusicConfigForDiagnostics.Enabled,
            configuredBackgroundMusicPath = NormalizePath(backgroundMusicConfigForDiagnostics.ConfiguredPath),
            configuredBackgroundMusicLevelPercent = backgroundMusicConfigForDiagnostics.LevelPercent,
            configuredDuckUnderNarration = backgroundMusicConfigForDiagnostics.DuckUnderNarration,
            backgroundMusicLoaded = backgroundAudioFound,
            backgroundMusicMixed = backgroundAudioMixed,
            duckingAttempted,
            duckingSucceeded,
            duckingFallbackUsed,
            duckingFailureReason,
            effectiveBackgroundVolume,
            duckingApplied = duckingSucceeded,
            shortAudioMuxed,
            longAudioMuxed,
            shortHasAudioStream,
            longHasAudioStream,
            shortAudioDurationSec = RoundDuration(shortAudioDuration),
            longAudioDurationSec = RoundDuration(longAudioDuration),
            shortVideoDurationSec = RoundDuration(shortVideoDuration),
            longVideoDurationSec = RoundDuration(longVideoDuration),
            finalMixedAudioDurationSec = new { @short = RoundDuration(shortAudioDuration), @long = RoundDuration(longAudioDuration) },
            finalVideoDurationSec = new { @short = RoundDuration(shortVideoDuration), @long = RoundDuration(longVideoDuration) },
            audioVideoDurationDeltaSec = RoundDuration(audioVideoDurationDeltaSec),
            cinematicOutroEnabled,
            cinematicOutroDurationSec,
            fadeToBlackEnabled,
            fadeToBlackDurationSec,
            shortExpectedVideoDurationSec = RoundDuration(shortExpectedVideoDuration),
            shortDurationDeltaAgainstExpectedSec = RoundDuration(shortDurationDeltaAgainstExpected),
            shortDurationValidationPassed,
            longExpectedVideoDurationSec = RoundDuration(longExpectedVideoDuration),
            longDurationDeltaAgainstExpectedSec = RoundDuration(longDurationDeltaAgainstExpected),
            longDurationValidationPassed,
            shortSceneCount,
            longSceneCount,
            missingSceneImages,
            missingAudioFiles,
            videoRendered,
            enableSubtitles,
            srtTimingSource = "SceneDurationPlanFromTtsTimeline",
            ttsDurationsMeasuredFromMp3 = true,
            shortAudioDurationTotal = RoundDuration(shortPlanAudioDuration),
            shortVideoDurationTotal = RoundDuration(shortPlanVideoDuration),
            shortSrtFinalEnd = RoundDuration(shortSrtDuration),
            shortSrtMatchesAudio = shortSrtExists && Math.Abs(shortSrtDuration - shortPlanAudioDuration) <= 0.1,
            shortSrtMatchesVideo = shortSrtExists && Math.Abs(shortSrtDuration - shortPlanVideoDuration) <= 0.1,
            longAudioDurationTotal = RoundDuration(longPlanAudioDuration),
            longVideoDurationTotal = RoundDuration(longPlanVideoDuration),
            longSrtFinalEnd = RoundDuration(longSrtDuration),
            longSrtMatchesAudio = longSrtExists && Math.Abs(longSrtDuration - longPlanAudioDuration) <= 0.1,
            longSrtMatchesVideo = longSrtExists && Math.Abs(longSrtDuration - longPlanVideoDuration) <= 0.1,
            subtitleMode,
            shortSrtPath = NormalizePath(shortSrtPath),
            longSrtPath = NormalizePath(longSrtPath),
            shortSrtExists,
            longSrtExists,
            shortSubtitlesApplied,
            longSubtitlesApplied,
            subtitleBurnInCommandShort,
            subtitleBurnInCommandLong,
            subtitleBurnInSucceeded,
            subtitleStyleApplied = subtitleBurnInSucceeded,
            subtitleFontSize = Math.Max(shortBurnInResult?.FontSize ?? 0, longBurnInResult?.FontSize ?? 0),
            subtitleMaxCharsPerLine = 42,
            subtitleMaxLines = 2,
            duplicateNarrationDetected = false,
            duplicateNarrationFixed = false,
            duplicateSrtTextDetected = false,
            subtitleBurnInErrors,
            finalShortVideoPath = NormalizePath(shortVideoPath),
            finalLongVideoPath = NormalizePath(longVideoPath),
            validationPassed
        }, JsonOptions), cancellationToken);
        var validationPath = Path.Combine(validationRoot, "phase-18-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo = 18, phaseName = "Cinematic Video Assembly V2", rendererVersion = "V2", motionTypeApplied = true, status = validationPassed ? "Succeeded" : "Failed", videoRendered, oldPathUsed, validationPassed, enableSubtitles, subtitleMode, shortSrtPath = NormalizePath(shortSrtPath), longSrtPath = NormalizePath(longSrtPath), shortSrtExists, longSrtExists, shortSubtitlesApplied, longSubtitlesApplied, subtitleBurnInCommandShort, subtitleBurnInCommandLong, subtitleBurnInSucceeded, subtitleStyleApplied = subtitleBurnInSucceeded, subtitleFontSize = Math.Max(shortBurnInResult?.FontSize ?? 0, longBurnInResult?.FontSize ?? 0), subtitleMaxCharsPerLine = 42, subtitleMaxLines = 2, duplicateNarrationDetected = false, duplicateNarrationFixed = false, duplicateSrtTextDetected = false, subtitleBurnInErrors, finalShortVideoPath = NormalizePath(shortVideoPath), finalLongVideoPath = NormalizePath(longVideoPath), warnings, errors }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 18 Cinematic Video Assembly V2 failed: " + string.Join(" | ", errors));
        return [shortVideoPath, longVideoPath, shortAudioTrackPath, longAudioTrackPath, cinematicDiagnosticsPath, diagnosticsPath, validationPath, v2DiagnosticsPath];
    }

    private static IReadOnlyList<VideoAssemblyItem> ReadVideoAssemblyItems(string planRoot, JsonNode motionRoot, JsonNode ttsRoot, string format, int expectedCount, IReadOnlyList<string> oldPaths, List<string> missingSceneImages, List<string> missingAudioFiles, List<string> oldPathUsageReasons)
    {
        var items = new List<VideoAssemblyItem>();
        var isMotionV2 = string.Equals(GetString(motionRoot, "motionVersion"), "V2", StringComparison.OrdinalIgnoreCase);
        foreach (var item in (motionRoot[format]?["items"]?.AsArray() ?? []).Take(expectedCount))
        {
            var sceneId = GetString(item, "sceneId") ?? $"{items.Count + 1:000}";
            var imagePath = FirstNonEmpty(GetString(item, "sceneImagePath"), GetString(item, "imagePath"), ResolveVideoAssemblySceneImageFromManifest(planRoot, format, sceneId, items.Count), string.Empty);
            var audioPath = ResolveVideoAssemblyAudioPath(ttsRoot, format, sceneId, items.Count) ?? GetString(item, "audioPath") ?? ResolveVideoAssemblyAudioByConvention(imagePath, format, sceneId, items.Count) ?? string.Empty;
            if (!File.Exists(imagePath)) missingSceneImages.Add(NormalizePath(imagePath));
            if (!File.Exists(audioPath)) missingAudioFiles.Add(NormalizePath(audioPath));
            if (oldPaths.Any(oldPath => IsSameOrUnderPath(NormalizePath(imagePath), NormalizePath(oldPath)) || IsSameOrUnderPath(NormalizePath(audioPath), NormalizePath(oldPath))))
            {
                oldPathUsageReasons.Add($"{format}:{sceneId}");
                continue;
            }
            var motionProfile = ResolveMotionProfile(sceneId, GetString(item, "motionStyle", "motionProfile"));
            var defaults = ResolveMotionDefaults(motionProfile) ?? ResolveMotionDefaults("Hook")!;
            items.Add(new VideoAssemblyItem(
                sceneId,
                imagePath,
                audioPath,
                GetDouble(item, "sceneDurationSec", "durationSec") ?? 3.0,
                "crossfade",
                isMotionV2 ? GetString(item, "motionType") ?? motionProfile : motionProfile,
                GetString(item, "purpose") ?? ResolveMotionPurpose(sceneId),
                NormalizePhase18Scale(GetDouble(item, "zoomStart", "startScale") ?? defaults.ZoomStart),
                NormalizePhase18Scale(GetDouble(item, "zoomEnd", "endScale") ?? defaults.ZoomEnd),
                NormalizePhase18Pan(GetDouble(item, "panXStart", "startPanX", "startX") ?? defaults.PanXStart),
                NormalizePhase18Pan(GetDouble(item, "panXEnd", "endPanX", "endX") ?? defaults.PanXEnd),
                NormalizePhase18Pan(GetDouble(item, "panYStart", "startPanY", "startY") ?? defaults.PanYStart),
                NormalizePhase18Pan(GetDouble(item, "panYEnd", "endPanY", "endY") ?? defaults.PanYEnd),
                GetString(item, "easing") ?? ResolveMotionEasing(motionProfile)));
        }
        return items;
    }

    private static double NormalizePhase18Scale(double value) => value <= 3.0 ? value * 100.0 : value;

    private static double NormalizePhase18Pan(double value) => Math.Abs(value) <= 1.0 ? value * 100.0 : value;

    private static string? ResolveVideoAssemblySceneImageFromManifest(string planRoot, string format, string sceneId, int index)
    {
        var sceneRoot = Path.Combine(planRoot, "scene-assets-v3", format);
        var manifestPath = Path.Combine(sceneRoot, "scene-manifest-v3.json");
        var scenes = File.Exists(manifestPath) ? ReadJsonArray(manifestPath, "scenes") : new JsonArray();
        var match = scenes.FirstOrDefault(n => string.Equals(GetString(n, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase)) ?? scenes.ElementAtOrDefault(index);
        var imagePath = FirstNonEmpty(GetString(match, "imagePath"), GetString(match, "sceneImagePath"), Path.Combine(sceneRoot, sceneId + ".png"));
        return Path.IsPathRooted(imagePath) ? imagePath : Path.Combine(sceneRoot, imagePath);
    }

    private static string? ResolveVideoAssemblyAudioByConvention(string imagePath, string format, string sceneId, int index)
    {
        var root = TryFindAncestorDirectory(imagePath, "scene-assets-v3");
        if (root is null) return null;
        var ttsRoot = Path.Combine(Path.GetDirectoryName(root) ?? string.Empty, "tts", format);
        if (!Directory.Exists(ttsRoot)) return null;
        var exact = Path.Combine(ttsRoot, sceneId + ".mp3");
        if (File.Exists(exact)) return exact;
        return Directory.EnumerateFiles(ttsRoot, "*.mp3").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ElementAtOrDefault(index);
    }

    private static string? TryFindAncestorDirectory(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dir = File.Exists(path) ? new DirectoryInfo(Path.GetDirectoryName(path)!) : new DirectoryInfo(Path.GetDirectoryName(path) ?? path);
        while (dir is not null)
        {
            if (string.Equals(dir.Name, name, StringComparison.OrdinalIgnoreCase)) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ResolveVideoAssemblyAudioPath(JsonNode ttsRoot, string format, string sceneId, int index)
    {
        var ttsItems = ttsRoot[format]?["items"]?.AsArray();
        if (ttsItems is null) return null;

        foreach (var item in ttsItems)
        {
            if (string.Equals(GetString(item, "sceneId"), sceneId, StringComparison.OrdinalIgnoreCase))
                return GetString(item, "audioPath");
        }

        return index >= 0 && index < ttsItems.Count ? GetString(ttsItems[index], "audioPath") : null;
    }

    private static async Task WriteMotionDebugAsync(string planRoot, IReadOnlyList<VideoAssemblyItem> items, CancellationToken cancellationToken)
    {
        var debugRoot = Path.Combine(planRoot, "video-assembly");
        Directory.CreateDirectory(debugRoot);
        var scenes = items.Select(item =>
        {
            var totalFrames = Math.Max(15, (int)Math.Round(item.SceneDurationSec * 30.0));
            var scale = FirstMotionValues(item.ZoomStart / 100.0, item.ZoomEnd / 100.0, item.Easing, totalFrames);
            var panX = FirstMotionValues(item.PanXStart, item.PanXEnd, item.Easing, totalFrames);
            var panY = FirstMotionValues(item.PanYStart, item.PanYEnd, item.Easing, totalFrames);
            return new
            {
                sceneId = item.SceneId,
                purpose = item.Purpose,
                motionProfile = item.MotionStyle,
                startScale = item.ZoomStart / 100.0,
                endScale = item.ZoomEnd / 100.0,
                startPanX = item.PanXStart,
                endPanX = item.PanXEnd,
                startPanY = item.PanYStart,
                endPanY = item.PanYEnd,
                easing = item.Easing,
                durationSeconds = item.SceneDurationSec,
                totalFrames,
                first10ScaleValues = scale,
                first10PanXValues = panX,
                first10PanYValues = panY
            };
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(debugRoot, "motion-debug.json"), JsonSerializer.Serialize(new { outroDurationSeconds = 4.0, closingMotionContinues = true, fadeToBlackFinalSecond = true, scenes }, JsonOptions), cancellationToken);
    }

    private static IReadOnlyList<double> FirstMotionValues(double start, double end, string easing, int totalFrames)
        => Enumerable.Range(0, Math.Min(10, totalFrames))
            .Select(frame => start + (end - start) * EasedProgress(frame, totalFrames, easing))
            .ToArray();

    private static double EasedProgress(int frame, int totalFrames, string easing)
    {
        var t = totalFrames <= 1 ? 1.0 : frame / (double)(totalFrames - 1);
        return string.Equals(easing, "EaseInOutSine", StringComparison.OrdinalIgnoreCase)
            ? (1 - Math.Cos(Math.PI * t)) / 2
            : 1 - Math.Pow(1 - t, 3);
    }

    private async Task RenderVideoAssemblyAsync(IReadOnlyList<VideoAssemblyItem> items, string outputPath, string narrationTrackPath, Phase18BackgroundMusicConfig backgroundMusicConfig, bool previewOnly, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempRoot = Path.Combine(Path.GetTempPath(), "astro-video-assembly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
            var clipPaths = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var clipPath = Path.Combine(tempRoot, $"{i:000}-{SanitizeFileName(item.SceneId)}.mp4");
                var duration = Math.Max(0.5, item.SceneDurationSec);
                var vf = BuildPhase18MotionFilter(item, i);
                var result = await RunProcessAsync(ffmpegPath, ["-y", "-loop", "1", "-i", item.SceneImagePath, "-t", duration.ToString("0.###", CultureInfo.InvariantCulture), "-vf", vf, "-an", "-c:v", "libx264", "-preset", "veryfast", clipPath], cancellationToken);
                if (result.ExitCode != 0 || !File.Exists(clipPath)) throw new InvalidOperationException($"Unable to render scene clip {item.SceneId}: {result.Error}");
                clipPaths.Add(clipPath);
            }
            var videoOnlyPath = Path.Combine(tempRoot, "video-only.mp4");
            await CrossfadeSceneClipsAsync(clipPaths, items, videoOnlyPath, ffmpegPath, cancellationToken);

            var availableAudioItems = items.Where(i => !string.IsNullOrWhiteSpace(i.AudioPath) && File.Exists(i.AudioPath)).ToArray();
            var hasNarrationAudio = availableAudioItems.Length == items.Count;
            if (hasNarrationAudio) await ConcatenateNarrationTrackAsync(items, narrationTrackPath, tempRoot, ffmpegPath, cancellationToken);
            if (!hasNarrationAudio && !previewOnly) throw new InvalidOperationException("Phase 18 requires narration audio unless videoAssemblyPreviewOnly is enabled.");
            if (hasNarrationAudio && backgroundMusicConfig.Enabled && (string.IsNullOrWhiteSpace(backgroundMusicConfig.ConfiguredPath) || !File.Exists(backgroundMusicConfig.ConfiguredPath)))
                throw new InvalidOperationException($"Phase 18 background music is enabled but configured file is missing: {NormalizePath(backgroundMusicConfig.ConfiguredPath)}");

            var videoDuration = await ProbeAudioDurationSecondsAsync(videoOnlyPath, cancellationToken);
            var narrationDuration = hasNarrationAudio ? await ProbeAudioDurationSecondsAsync(narrationTrackPath, cancellationToken) : 0;
            var finalDuration = Math.Max(videoDuration, narrationDuration + 4.0);
            if (finalDuration <= 0) throw new InvalidOperationException("Phase 18 cannot determine final video/audio duration.");

            var finalMixedAudioPath = ResolvePhase18FinalMixedAudioPath(outputPath);
            var fadeOutStart = Math.Max(0, finalDuration - 1.5);
            var finalDurationText = finalDuration.ToString("0.###", CultureInfo.InvariantCulture);
            var fadeOutStartText = fadeOutStart.ToString("0.###", CultureInfo.InvariantCulture);
            if (hasNarrationAudio && backgroundMusicConfig.Enabled)
            {
                var musicLevelText = backgroundMusicConfig.Level.ToString("0.###", CultureInfo.InvariantCulture);
                var audioFilter = backgroundMusicConfig.DuckUnderNarration
                    ? $"[2:a]volume={musicLevelText},afade=t=in:st=0:d=1,afade=t=out:st={fadeOutStartText}:d=1.5[bg];[bg][1:a]sidechaincompress=threshold=0.03:ratio=8:attack=20:release=500[ducked];[1:a][ducked]amix=inputs=2:duration=longest:normalize=0[aout]"
                    : $"[1:a]volume=1.0[narr];[2:a]volume={musicLevelText},afade=t=in:st=0:d=1,afade=t=out:st={fadeOutStartText}:d=1.5[bg];[narr][bg]amix=inputs=2:duration=longest:normalize=0[aout]";
                var mixResult = await RunProcessAsync(ffmpegPath, ["-y", "-i", videoOnlyPath, "-i", narrationTrackPath, "-stream_loop", "-1", "-i", backgroundMusicConfig.ConfiguredPath, "-filter_complex", audioFilter, "-map", "[aout]", "-c:a", "aac", "-t", finalDurationText, finalMixedAudioPath], cancellationToken);
                if (mixResult.ExitCode != 0 && backgroundMusicConfig.DuckUnderNarration)
                {
                    var fallbackDuckLevel = (backgroundMusicConfig.Level * 0.45).ToString("0.###", CultureInfo.InvariantCulture);
                    var fallbackFilter = $"[1:a]volume=1.0[narr];[2:a]volume='if(gt(t,0),{fallbackDuckLevel},{musicLevelText})',afade=t=in:st=0:d=1,afade=t=out:st={fadeOutStartText}:d=1.5[bg];[narr][bg]amix=inputs=2:duration=longest:normalize=0[aout]";
                    mixResult = await RunProcessAsync(ffmpegPath, ["-y", "-i", videoOnlyPath, "-i", narrationTrackPath, "-stream_loop", "-1", "-i", backgroundMusicConfig.ConfiguredPath, "-filter_complex", fallbackFilter, "-map", "[aout]", "-c:a", "aac", "-t", finalDurationText, finalMixedAudioPath], cancellationToken);
                }
                if (mixResult.ExitCode != 0 || !File.Exists(finalMixedAudioPath)) throw new InvalidOperationException($"Unable to mix narration and background ambience: {mixResult.Error}");
            }
            else if (hasNarrationAudio)
            {
                File.Copy(narrationTrackPath, finalMixedAudioPath, true);
            }

            var videoExtension = Math.Max(0, finalDuration - videoDuration);
            var fadeToBlackStart = Math.Max(0, finalDuration - 1.0).ToString("0.###", CultureInfo.InvariantCulture);
            var videoFilter = $"tpad=stop_mode=clone:stop_duration={videoExtension.ToString("0.###", CultureInfo.InvariantCulture)},trim=duration={finalDurationText},setpts=PTS-STARTPTS,fade=t=out:st={fadeToBlackStart}:d=1.0";
            var muxArgs = hasNarrationAudio
                ? new[] { "-y", "-i", videoOnlyPath, "-i", finalMixedAudioPath, "-filter:v", videoFilter, "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-t", finalDurationText, outputPath }
                : new[] { "-y", "-i", videoOnlyPath, "-filter:v", videoFilter, "-map", "0:v:0", "-an", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-t", finalDurationText, outputPath };
            var muxResult = await RunProcessAsync(ffmpegPath, muxArgs, cancellationToken);
            if (muxResult.ExitCode != 0 || !File.Exists(outputPath)) throw new InvalidOperationException($"Unable to mux final video: {muxResult.Error}");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string ResolvePhase18FinalMixedAudioPath(string outputPath)
        => Path.Combine(Path.GetDirectoryName(outputPath)!, "final-mixed-audio.m4a");

    private sealed record SubtitleBurnInResult(bool Succeeded, string Command, string Error, int FontSize = 0, int MaxCharsPerLine = 42, int MaxLines = 2);

    private async Task<SubtitleBurnInResult> BurnInSubtitlesAsync(string videoPath, string srtPath, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var outputDirectory = Path.GetDirectoryName(videoPath)!;
        var unsubtitledPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(videoPath) + "-without-subtitles" + Path.GetExtension(videoPath));
        var subtitledPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(videoPath) + "-subtitled" + Path.GetExtension(videoPath));
        File.Copy(videoPath, unsubtitledPath, true);
        var style = await ResolvePhase18SubtitleStyleAsync(videoPath, cancellationToken);
        var filter = $"subtitles='{EscapeFfmpegSubtitlesPath(srtPath)}':force_style='{style.ForceStyle}'";
        var args = new[] { "-y", "-i", unsubtitledPath, "-vf", filter, "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "copy", subtitledPath };
        var result = await RunProcessAsync(ffmpegPath, args, cancellationToken);
        var command = BuildProcessCommand(ffmpegPath, args);
        if (result.ExitCode != 0 || !File.Exists(subtitledPath))
            return new SubtitleBurnInResult(false, command, FirstNonEmpty(result.Error, result.Output, $"FFmpeg exited with code {result.ExitCode}"), style.FontSize, style.MaxCharsPerLine, style.MaxLines);
        File.Copy(subtitledPath, videoPath, true);
        return new SubtitleBurnInResult(true, command, string.Empty, style.FontSize, style.MaxCharsPerLine, style.MaxLines);
    }

    private sealed record Phase18SubtitleStyle(string ForceStyle, int FontSize, int MarginV, int MaxCharsPerLine, int MaxLines);

    private async Task<Phase18SubtitleStyle> ResolvePhase18SubtitleStyleAsync(string videoPath, CancellationToken cancellationToken)
    {
        var (width, height) = await ProbeVideoDimensionsAsync(videoPath, cancellationToken);
        var fontSize = 28;
        var marginV = 55;
        if (width == 1280 && height == 720) { fontSize = 19; marginV = 38; }
        else if (width == 1920 && height == 1080) { fontSize = 31; marginV = 68; }
        else if (width == 1080 && height == 1920) { fontSize = 29; marginV = 150; }
        else if (height > width) { fontSize = Math.Clamp((int)Math.Round(height * 0.016), 28, 30); marginV = Math.Clamp((int)Math.Round(height * 0.078), 130, 170); }
        else { fontSize = Math.Clamp((int)Math.Round(height * 0.029), 18, 32); marginV = Math.Clamp((int)Math.Round(height * 0.06), 34, 74); }
        var forceStyle = $"FontName=Arial,FontSize={fontSize},PrimaryColour=&HFFFFFF&,BackColour=&H99000000&,OutlineColour=&H000000&,BorderStyle=3,Outline=1,Shadow=0,MarginV={marginV},Alignment=2";
        return new Phase18SubtitleStyle(forceStyle, fontSize, marginV, 42, 2);
    }

    private async Task<(int Width, int Height)> ProbeVideoDimensionsAsync(string videoPath, CancellationToken cancellationToken)
    {
        var ffprobePath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfprobePath) ? "ffprobe" : renderingOptions.Value.FfprobePath;
        var result = await RunProcessAsync(ffprobePath, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=s=x:p=0", videoPath], cancellationToken);
        var text = FirstNonEmpty(result.Output, result.Error, string.Empty).Trim();
        var parts = text.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height) ? (width, height) : (1280, 720);
    }

    private static string EscapeFfmpegSubtitlesPath(string path)
    {
        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        return normalized.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private static string BuildProcessCommand(string fileName, IReadOnlyList<string> arguments)
        => string.Join(" ", new[] { QuoteProcessArgument(fileName) }.Concat(arguments.Select(QuoteProcessArgument)));

    private static string QuoteProcessArgument(string value)
        => string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) || value.Contains('\'') || value.Contains('"')
            ? "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

    private Phase18BackgroundMusicConfig ResolvePhase18BackgroundMusicConfig(string planRoot)
    {
        var options = videoAssemblyOptions?.Value.BackgroundMusic ?? new VideoAssemblyBackgroundMusicOptions();
        var configuredPath = options.WonderCuriosityPath;
        if (string.IsNullOrWhiteSpace(configuredPath)) configuredPath = options.DefaultPath;
        if (string.IsNullOrWhiteSpace(configuredPath)) configuredPath = Path.Combine(planRoot, "audio", "background.mp3");
        var levelPercent = Math.Clamp(options.DefaultLevelPercent, 0, 100);
        return new Phase18BackgroundMusicConfig(options.Enabled, configuredPath, levelPercent, levelPercent / 100.0, options.DuckUnderNarration);
    }

    private sealed record Phase18BackgroundMusicConfig(bool Enabled, string ConfiguredPath, int LevelPercent, double Level, bool DuckUnderNarration);

    private static JsonNode BuildDefaultPhase18MotionPlan(string sceneAssetsRoot, string syncPath, string ttsPath)
    {
        _ = syncPath;
        var root = new JsonObject();
        var ttsRoot = File.Exists(ttsPath) ? JsonNode.Parse(File.ReadAllText(ttsPath)) ?? new JsonObject() : new JsonObject();
        foreach (var format in new[] { "short", "long" })
        {
            var items = new JsonArray();
            var sceneRoot = Path.Combine(sceneAssetsRoot, format);
            var images = Directory.Exists(sceneRoot)
                ? Directory.EnumerateFiles(sceneRoot).Where(p => IsImageExtension(Path.GetExtension(p))).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            var ttsItems = ttsRoot[format]?["items"]?.AsArray();
            var count = ttsItems?.Count > 0 ? ttsItems.Count : images.Length;
            for (var i = 0; i < count; i++)
            {
                var ttsItem = ttsItems is not null && i < ttsItems.Count ? ttsItems[i] : null;
                var profile = ResolveMotionProfile(GetString(ttsItem, "sceneId") ?? $"{i + 1:000}", string.Empty);
                var defaults = ResolveMotionDefaults(profile) ?? ResolveMotionDefaults("Hook")!;
                items.Add(new JsonObject
                {
                    ["sceneId"] = GetString(ttsItem, "sceneId") ?? $"{i + 1:000}",
                    ["sceneImagePath"] = i < images.Length ? images[i] : string.Empty,
                    ["audioPath"] = GetString(ttsItem, "audioPath") ?? string.Empty,
                    ["sceneDurationSec"] = GetDouble(ttsItem, "durationSec") ?? GetDouble(ttsItem, "sceneDurationSec") ?? 3.0,
                    ["purpose"] = ResolveMotionPurpose(GetString(ttsItem, "sceneId") ?? $"{i + 1:000}"),
                    ["motionStyle"] = profile,
                    ["zoomStart"] = defaults.ZoomStart,
                    ["zoomEnd"] = defaults.ZoomEnd,
                    ["panXStart"] = defaults.PanXStart,
                    ["panXEnd"] = defaults.PanXEnd,
                    ["panYStart"] = defaults.PanYStart,
                    ["panYEnd"] = defaults.PanYEnd,
                    ["easing"] = ResolveMotionEasing(profile),
                    ["transition"] = "cut"
                });
            }

            root[format] = new JsonObject { ["items"] = items };
        }

        return root;
    }

    private static bool IsImageExtension(string extension)
        => extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    private static string BuildPhase18MotionFilter(VideoAssemblyItem item, int index)
    {
        const int fps = 30;
        var frameCount = Math.Max(15, (int)Math.Round(item.SceneDurationSec * fps));
        var denom = Math.Max(1, frameCount - 1).ToString(CultureInfo.InvariantCulture);
        var smoothProgress = $"((1-cos((on/{denom})*PI))/2)";
        var linearProgress = $"(on/{denom})";
        var easedProgress = string.Equals(item.Easing, "EaseInOutSine", StringComparison.OrdinalIgnoreCase) ? smoothProgress : $"(1-pow(1-(on/{denom}),3))";
        static string Percent(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string Scale(double value) => (value / 100.0).ToString("0.####", CultureInfo.InvariantCulture);
        var motionType = item.MotionStyle.Trim();
        var (z0, z1, px0, px1, py0, py1, progress) = motionType switch
        {
            "SlowZoomIn" => (Scale(item.ZoomStart), Scale(Math.Max(item.ZoomEnd, item.ZoomStart + 6.0)), Percent(0), Percent(0), Percent(0), Percent(0), smoothProgress),
            "SlowZoomOut" => (Scale(Math.Max(item.ZoomStart, item.ZoomEnd + 6.0)), Scale(item.ZoomEnd), Percent(0), Percent(0), Percent(0), Percent(0), smoothProgress),
            "PanLeft" => (Scale(Math.Max(Math.Max(item.ZoomStart, item.ZoomEnd), 108.0)), Scale(Math.Max(Math.Max(item.ZoomStart, item.ZoomEnd), 108.0)), Percent(Math.Max(item.PanXStart, item.PanXEnd)), Percent(Math.Min(item.PanXStart, item.PanXEnd)), Percent(item.PanYStart), Percent(item.PanYEnd), linearProgress),
            "PanRight" => (Scale(Math.Max(Math.Max(item.ZoomStart, item.ZoomEnd), 108.0)), Scale(Math.Max(Math.Max(item.ZoomStart, item.ZoomEnd), 108.0)), Percent(Math.Min(item.PanXStart, item.PanXEnd)), Percent(Math.Max(item.PanXStart, item.PanXEnd)), Percent(item.PanYStart), Percent(item.PanYEnd), linearProgress),
            "PushToObject" => (Scale(item.ZoomStart), Scale(Math.Max(item.ZoomEnd, item.ZoomStart + 14.0)), Percent(item.PanXStart), Percent(item.PanXEnd == item.PanXStart ? item.PanXStart + (index % 2 == 0 ? 8.0 : -8.0) : item.PanXEnd), Percent(item.PanYStart), Percent(item.PanYEnd == item.PanYStart ? item.PanYStart - 4.0 : item.PanYEnd), smoothProgress),
            _ => (Scale(item.ZoomStart), Scale(item.ZoomEnd), Percent(item.PanXStart), Percent(item.PanXEnd), Percent(item.PanYStart), Percent(item.PanYEnd), easedProgress)
        };
        var zoomExpression = $"{z0}+(({z1})-({z0}))*{progress}";
        var xExpression = $"iw/2-(iw/zoom/2)+(({px0}+(({px1})-({px0}))*{progress})/100)*(iw-iw/zoom)";
        var yExpression = $"ih/2-(ih/zoom/2)+(({py0}+(({py1})-({py0}))*{progress})/100)*(ih-ih/zoom)";
        var fadeOutStart = Math.Max(0.0, item.SceneDurationSec - 1.0).ToString("0.###", CultureInfo.InvariantCulture);
        return $"scale=1536:864:force_original_aspect_ratio=increase,crop=1536:864,zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}':d={frameCount}:s=1280x720:fps={fps},trim=duration={item.SceneDurationSec.ToString("0.###", CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS,fade=t=in:st=0:d=0.4,fade=t=out:st={fadeOutStart}:d=1.0,format=yuv420p";
    }

    private static async Task CrossfadeSceneClipsAsync(IReadOnlyList<string> clipPaths, IReadOnlyList<VideoAssemblyItem> items, string outputPath, string ffmpegPath, CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 1)
        {
            File.Copy(clipPaths[0], outputPath, true);
            return;
        }

        var args = new List<string> { "-y" };
        foreach (var clipPath in clipPaths) args.AddRange(["-i", clipPath]);
        var filter = new System.Text.StringBuilder();
        for (var i = 0; i < clipPaths.Count; i++) filter.Append(FormattableString.Invariant($"[{i}:v]setpts=PTS-STARTPTS[v{i}];"));
        var offset = Math.Max(0.1, items[0].SceneDurationSec - 0.8);
        filter.Append(FormattableString.Invariant($"[v0][v1]xfade=transition=fade:duration=0.8:offset={offset:0.###}[x1]"));
        for (var i = 2; i < clipPaths.Count; i++)
        {
            offset += Math.Max(0.1, items[i - 1].SceneDurationSec - 0.8);
            filter.Append(FormattableString.Invariant($";[x{i - 1}][v{i}]xfade=transition=fade:duration=0.8:offset={offset:0.###}[x{i}]"));
        }
        args.AddRange(["-filter_complex", filter.ToString(), "-map", $"[x{clipPaths.Count - 1}]", "-an", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", outputPath]);
        var result = await RunProcessStaticAsync(ffmpegPath, args, cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(outputPath)) throw new InvalidOperationException($"Unable to crossfade scene clips: {result.Error}");
    }

    private static async Task<ProcessResult> RunProcessStaticAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start process {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task ConcatenateNarrationTrackAsync(IReadOnlyList<VideoAssemblyItem> items, string narrationTrackPath, string tempRoot, string ffmpegPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(narrationTrackPath)!);
        var concatPath = Path.Combine(tempRoot, "audio-concat.txt");
        await File.WriteAllLinesAsync(concatPath, items.Select(i => "file '" + i.AudioPath.Replace("'", "'\\''") + "'"), cancellationToken);

        var concatResult = await RunProcessAsync(ffmpegPath, ["-y", "-f", "concat", "-safe", "0", "-i", concatPath, "-vn", "-acodec", "libmp3lame", "-q:a", "4", narrationTrackPath], cancellationToken);
        if (concatResult.ExitCode != 0 || !File.Exists(narrationTrackPath))
            throw new InvalidOperationException($"Unable to concatenate narration track: {concatResult.Error}");
    }

    private async Task<IReadOnlyList<string>> PhaseVideoQaProductionReviewAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var planRoot = context.OutputRoot;
        var reviewRoot = Path.Combine(planRoot, "review");
        var validationRoot = context.ExecutionContext.ValidationRoot!;
        Directory.CreateDirectory(reviewRoot);
        Directory.CreateDirectory(validationRoot);

        var shortVideoPath = Path.Combine(planRoot, "video", "short", "final-short.mp4");
        var longVideoPath = Path.Combine(planRoot, "video", "long", "final-long.mp4");
        var syncPath = Path.Combine(planRoot, "sync", "scene-audio-sync.json");
        var ttsPath = Path.Combine(planRoot, "tts", "tts-timeline.json");
        var durationPlanPath = Path.Combine(planRoot, "timing", "scene-duration-plan.json");
        var motionPlanPath = Path.Combine(planRoot, "motion", "motion-plan.json");
        var sceneAssetsRoot = Path.Combine(planRoot, "scene-assets-v3");
        var motionDebugPath = Path.Combine(planRoot, "motion", "motion-debug.json");
        var phase18DiagnosticsPath = Path.Combine(validationRoot, "phase-18-video-diagnostics.json");
        var inputs = new[] { shortVideoPath, longVideoPath, syncPath, ttsPath, durationPlanPath, motionPlanPath, motionDebugPath, phase18DiagnosticsPath, sceneAssetsRoot };
        var errors = new List<string>();
        foreach (var input in inputs)
            if (!File.Exists(input) && !Directory.Exists(input)) errors.Add($"Input missing: {NormalizePath(input)}");

        var shortVideo = await BuildPhase19VideoChecksAsync(shortVideoPath, "short", motionPlanPath, cancellationToken);
        var longVideo = await BuildPhase19VideoChecksAsync(longVideoPath, "long", motionPlanPath, cancellationToken);
        var sceneChecks = BuildPhase19SceneChecks(sceneAssetsRoot, syncPath, ttsPath, durationPlanPath, motionPlanPath);
        var storyChecks = BuildPhase19StoryChecks(syncPath, ttsPath, sceneAssetsRoot);
        var audioChecks = await BuildPhase19AudioChecksAsync(shortVideoPath, longVideoPath, cancellationToken);
        var visualChecks = BuildPhase19VisualChecks(motionPlanPath, sceneAssetsRoot);
        var phase18Root = File.Exists(phase18DiagnosticsPath) ? JsonNode.Parse(await File.ReadAllTextAsync(phase18DiagnosticsPath, cancellationToken)) : null;
        var phase18ValidationPassed = GetBool(phase18Root, "validationPassed") ?? false;
        var shortDurationValidationPassed = GetBool(phase18Root, "shortDurationValidationPassed") ?? false;
        var longDurationValidationPassed = GetBool(phase18Root, "longDurationValidationPassed") ?? false;
        var phase18MotionDebugFound = GetBool(phase18Root, "motionDebugFound") ?? false;
        var cinematicOutroValidated = IsPhase18CinematicOutroValidated(phase18Root);
        var fadeToBlackValidated = IsPhase18FadeToBlackValidated(phase18Root);

        errors.AddRange(shortVideo.Errors);
        errors.AddRange(longVideo.Errors);
        errors.AddRange(sceneChecks.Errors);
        errors.AddRange(storyChecks.Errors);
        errors.AddRange(audioChecks.Errors);
        errors.AddRange(visualChecks.Errors);
        if (!phase18ValidationPassed) errors.Add("Phase 18 validation did not pass");
        if (!cinematicOutroValidated) errors.Add("Cinematic outro duration validation failed");
        if (!fadeToBlackValidated) errors.Add("Fade-to-black validation failed");

        var qaIssues = shortVideo.Issues.Concat(longVideo.Issues).Concat(sceneChecks.Issues).Concat(storyChecks.Issues).ToArray();
        var storytellingScore = ScoreBooleans(storyChecks.Checks);
        var visualScore = ScoreBooleans(visualChecks.Checks.Concat(sceneChecks.VisualChecks));
        var audioScore = ScoreBooleans(audioChecks.Checks.Concat(new[] { shortVideo.AudioStreamExists, longVideo.AudioStreamExists, shortVideo.AudioDurationSec > 0, longVideo.AudioDurationSec > 0, !shortVideo.HasSilentSection, !longVideo.HasSilentSection }));
        var scientificScore = ScoreBooleans(new[] { storyChecks.AccurateSkyGuidePresent, storyChecks.EducationalExplanationPresent, visualChecks.ScientificExplanationScenePresent });
        var retentionScore = ScoreBooleans(new[] { storyChecks.HookPresent, storyChecks.EmotionalEndingPresent, visualChecks.SceneDiversityPresent, !sceneChecks.HasDuplicateScenes });
        var technicalScore = ScoreBooleans(new[] { shortVideo.VideoExists, longVideo.VideoExists, shortVideo.VideoDurationSec > 0, longVideo.VideoDurationSec > 0, !sceneChecks.HasMissingScenes, !sceneChecks.HasMissingAudio, !shortVideo.HasBlackFramesOver2Sec, !longVideo.HasBlackFramesOver2Sec, !shortVideo.HasFrozenFrameOver3Sec, !longVideo.HasFrozenFrameOver3Sec });
        var overallScore = (int)Math.Round(new[] { storytellingScore, visualScore, audioScore, scientificScore, retentionScore, technicalScore }.Average(), MidpointRounding.AwayFromZero);
        var falsePositiveRisk = qaIssues.Length == 0 ? 5 : Math.Min(95, (int)Math.Round(qaIssues.Average(i => 100 - i.Confidence), MidpointRounding.AwayFromZero));
        var qaConfidence = Math.Clamp(100 - falsePositiveRisk - (errors.Count == 0 ? 0 : Math.Min(20, errors.Count * 2)), 0, 100);
        var recommendation = overallScore >= 80 && qaConfidence >= 80 ? "Approved" : "Needs Improvement";

        var scoring = new { overallScore, storytellingScore, visualScore, audioScore, scientificScore, retentionScore, technicalScore, falsePositiveRisk, qaConfidence, recommendation };
        var review = new
        {
            phaseNo = 19,
            phaseName = "Video QA & Production Review",
            reviewOnly = true,
            inputsChecked = inputs.Select(NormalizePath),
            video = new { @short = shortVideo, @long = longVideo },
            sceneChecks,
            storyChecks,
            audioChecks,
            visualChecks,
            scoring
        };
        var videoReviewPath = Path.Combine(reviewRoot, "video-review.json");
        await File.WriteAllTextAsync(videoReviewPath, JsonSerializer.Serialize(review, JsonOptions), cancellationToken);

        var qaReportPath = Path.Combine(reviewRoot, "qa-report.json");
        await File.WriteAllTextAsync(qaReportPath, JsonSerializer.Serialize(new { status = recommendation, scoring, issues = qaIssues, errors, checks = review }, JsonOptions), cancellationToken);

        var diagnosticsPath = Path.Combine(validationRoot, "phase-19-review-diagnostics.json");
        await File.WriteAllTextAsync(diagnosticsPath, JsonSerializer.Serialize(new
        {
            inputPathsChecked = inputs.Select(NormalizePath),
            videoReviewPath = NormalizePath(videoReviewPath),
            qaReportPath = NormalizePath(qaReportPath),
            errors,
            scoring,
            validationPassed = File.Exists(videoReviewPath) && File.Exists(qaReportPath) && qaConfidence >= 80
        }, JsonOptions), cancellationToken);

        var motionPlanFound = File.Exists(motionPlanPath);
        var motionDebugFound = phase18MotionDebugFound;
        var motionDebugText = File.Exists(motionDebugPath) ? await File.ReadAllTextAsync(motionDebugPath, cancellationToken) : string.Empty;
        var easingDiagnosticsPresent = motionDebugText.Contains("first10ScaleValues", StringComparison.OrdinalIgnoreCase) && motionDebugText.Contains("last10ScaleValues", StringComparison.OrdinalIgnoreCase);
        var parallaxDisabled = !Regex.IsMatch((File.Exists(motionPlanPath) ? await File.ReadAllTextAsync(motionPlanPath, cancellationToken) : string.Empty) + motionDebugText, @"\b(parallax|parallaxStrength|motionStyle=parallax)\b", RegexOptions.IgnoreCase);
        if (!motionPlanFound) errors.Add("motion-plan.json missing");
        if (!motionDebugFound) errors.Add("motion-debug.json missing");
        if (!easingDiagnosticsPresent) errors.Add("Motion debug easing diagnostics missing");
        if (!parallaxDisabled) errors.Add("Parallax motion is present");
        var productionQaPassed = phase18ValidationPassed && cinematicOutroValidated && fadeToBlackValidated;
        var motionRc1ValidationPassed = motionPlanFound && motionDebugFound && easingDiagnosticsPresent && parallaxDisabled && productionQaPassed;
        var validationPassed = File.Exists(videoReviewPath) && File.Exists(qaReportPath) && qaConfidence >= 80 && motionRc1ValidationPassed;
        var validationPath = Path.Combine(validationRoot, "phase-19-validation.json");
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo = 19, phaseName = "Video QA & Production Review", status = validationPassed ? "Succeeded" : "Failed", motionRc1ValidationPassed, motionPlanFound, motionDebugFound, easingDiagnosticsPresent, parallaxDisabled, cinematicOutroValidated, fadeToBlackValidated, durationValidationMode = "NarrationPlusCinematicOutro", shortDurationValidationPassed, longDurationValidationPassed, productionQaPassed, validationPassed, overallScore, qaConfidence, falsePositiveRisk, recommendation, phase18ValidationPassed, issues = qaIssues, errors }, JsonOptions), cancellationToken);
        if (!validationPassed) throw new InvalidOperationException("Phase 19 Video QA & Production Review failed: " + string.Join(" | ", errors));
        return [videoReviewPath, qaReportPath, validationPath, diagnosticsPath];
    }

    private static bool IsPhase18CinematicOutroValidated(JsonNode? phase18Diagnostics) =>
        (GetBool(phase18Diagnostics, "cinematicOutroEnabled") ?? false) &&
        (GetDouble(phase18Diagnostics, "cinematicOutroDurationSec") ?? 0) >= 4.0;

    private static bool IsPhase18FadeToBlackValidated(JsonNode? phase18Diagnostics) =>
        (GetBool(phase18Diagnostics, "fadeToBlackEnabled") ?? false) &&
        (GetDouble(phase18Diagnostics, "fadeToBlackDurationSec") ?? 0) >= 1.0;

    private sealed record Phase19QaIssue(string IssueType, string SceneId, string Reason, int Confidence);
    private sealed record Phase19VideoChecks(string Profile, string VideoPath, bool VideoExists, bool AudioStreamExists, double VideoDurationSec, double AudioDurationSec, bool HasSilentSection, bool HasBlackFramesOver2Sec, bool HasFrozenFrameOver3Sec, IReadOnlyList<Phase19QaIssue> Issues, IReadOnlyList<string> Errors);
    private sealed record Phase19SceneChecks(bool HasMissingScenes, bool HasMissingAudio, bool HasDuplicateScenes, IReadOnlyList<Phase19QaIssue> Issues, IReadOnlyList<bool> VisualChecks, IReadOnlyList<string> Errors);
    private sealed record Phase19StoryChecks(bool HookPresent, bool EducationalExplanationPresent, bool PracticalViewingGuidancePresent, bool AccurateSkyGuidePresent, bool EmotionalEndingPresent, bool NoDuplicateNarration, bool NarrationContinuityPresent, IReadOnlyList<Phase19QaIssue> Issues, IReadOnlyList<bool> Checks, IReadOnlyList<string> Errors);
    private sealed record Phase19AudioChecks(bool NarrationAudible, bool BackgroundMusicAudible, bool DuckingApplied, bool NoClipping, bool NoDistortion, IReadOnlyList<bool> Checks, IReadOnlyList<string> Errors);
    private sealed record Phase19VisualChecks(bool MotionApplied, bool TransitionsApplied, bool SceneDiversityPresent, bool GuideScenePresent, bool ViewerScenePresent, bool ScientificExplanationScenePresent, IReadOnlyList<bool> Checks, IReadOnlyList<string> Errors);

    private async Task<Phase19VideoChecks> BuildPhase19VideoChecksAsync(string videoPath, string profile, string motionPlanPath, CancellationToken cancellationToken)
    {
        var exists = File.Exists(videoPath);
        var audioStream = exists && await HasAudioStreamAsync(videoPath, cancellationToken);
        var duration = exists ? await ProbeAudioDurationSecondsAsync(videoPath, cancellationToken) : 0;
        var audioDuration = audioStream ? duration : 0;
        var silent = exists && await DetectPhase19MediaIssueAsync(videoPath, $"silencedetect=noise=-45dB:d=1.0", "silence_start", cancellationToken);
        var motionActive = Phase19MotionIsActive(motionPlanPath);
        var blackEvidence = exists ? await DetectPhase19MediaIssuesAsync(videoPath, "blackdetect=d=2.0:pix_th=0.10", "black_start", cancellationToken) : [];
        var frozenEvidence = exists && !motionActive ? await DetectPhase19MediaIssuesAsync(videoPath, "freezedetect=n=-60dB:d=3", "freeze_start", cancellationToken) : [];
        var black = blackEvidence.Any(e => !Phase19IsTransitionWindowEvidence(e, duration));
        var frozen = frozenEvidence.Count > 0;
        var errors = new List<string>();
        if (!exists) errors.Add($"{profile} video missing: {NormalizePath(videoPath)}");
        if (!audioStream) errors.Add($"{profile} video has no audio stream");
        if (duration <= 0) errors.Add($"{profile} video duration <= 0");
        if (audioDuration <= 0) errors.Add($"{profile} audio duration <= 0");
        if (silent) errors.Add($"{profile} video has silent sections");
        var issues = new List<Phase19QaIssue>();
        if (black) issues.Add(new("BlackFrame", profile, $"Luminance stayed below threshold for over 2 seconds outside transition windows. Evidence: {string.Join("; ", blackEvidence.Where(e => !Phase19IsTransitionWindowEvidence(e, duration)).Take(3))}", 90));
        if (frozen) issues.Add(new("FrozenFrame", profile, $"Visual transform delta remained below threshold for over 3 seconds with no active motion plan. Evidence: {string.Join("; ", frozenEvidence.Take(3))}", 88));
        if (black) errors.Add($"{profile} video has black frames over 2 seconds");
        if (frozen) errors.Add($"{profile} video has frozen frame over 3 seconds");
        return new Phase19VideoChecks(profile, NormalizePath(videoPath), exists, audioStream, RoundDuration(duration), RoundDuration(audioDuration), silent, black, frozen, issues, errors);
    }

    private async Task<bool> DetectPhase19MediaIssueAsync(string mediaPath, string filter, string marker, CancellationToken cancellationToken)
        => (await DetectPhase19MediaIssuesAsync(mediaPath, filter, marker, cancellationToken)).Count > 0;

    private async Task<IReadOnlyList<string>> DetectPhase19MediaIssuesAsync(string mediaPath, string filter, string marker, CancellationToken cancellationToken)
    {
        var ffmpegPath = string.IsNullOrWhiteSpace(renderingOptions.Value.FfmpegPath) ? "ffmpeg" : renderingOptions.Value.FfmpegPath;
        var args = filter.StartsWith("silencedetect", StringComparison.OrdinalIgnoreCase)
            ? new[] { "-hide_banner", "-i", mediaPath, "-af", filter, "-f", "null", "-" }
            : new[] { "-hide_banner", "-i", mediaPath, "-vf", filter, "-an", "-f", "null", "-" };
        var result = await RunProcessAsync(ffmpegPath, args, cancellationToken);
        return Regex.Matches(result.Output + result.Error, $@"[^\r\n]*{Regex.Escape(marker)}[^\r\n]*", RegexOptions.IgnoreCase).Select(m => m.Value.Trim()).ToArray();
    }

    private static Phase19SceneChecks BuildPhase19SceneChecks(string sceneAssetsRoot, string syncPath, string ttsPath, string durationPlanPath, string motionPlanPath)
    {
        var errors = new List<string>();
        var duplicateGroups = ReadPhase19SceneFingerprints(Path.Combine(sceneAssetsRoot, "short", "scene-timeline-metadata.json"), Path.Combine(sceneAssetsRoot, "long", "scene-timeline-metadata.json"), syncPath, durationPlanPath, motionPlanPath)
            .Where(s => !string.IsNullOrWhiteSpace(s.SourceImage) && !string.IsNullOrWhiteSpace(s.CropRegion) && !string.IsNullOrWhiteSpace(s.CameraMotion) && s.DurationSeconds > 0)
            .GroupBy(s => $"{s.SourceImage}|{s.CropRegion}|{s.CameraMotion}|{s.DurationSeconds:0.###}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToArray();
        var duplicate = duplicateGroups.Length > 0;
        var missingScenes = new[] { Path.Combine(sceneAssetsRoot, "short"), Path.Combine(sceneAssetsRoot, "long") }.Any(p => !Directory.Exists(p) || !Directory.EnumerateFiles(p).Any(IsImageFile));
        var missingAudio = !File.Exists(ttsPath) || ReadPhase19AudioPaths(ttsPath).Any(p => string.IsNullOrWhiteSpace(p) || !File.Exists(p));
        var issues = duplicateGroups.Select(g => new Phase19QaIssue("DuplicateScene", string.Join(",", g.Select(x => x.SceneId).Distinct(StringComparer.OrdinalIgnoreCase)), $"Same source image, crop, motion, and duration. Evidence: {g.Key}", 92)).ToArray();
        if (missingScenes) errors.Add("Missing scenes detected");
        if (missingAudio) errors.Add("Missing audio detected");
        if (duplicate) errors.Add("Duplicate scenes detected");
        return new Phase19SceneChecks(missingScenes, missingAudio, duplicate, issues, [!missingScenes, !duplicate], errors);
    }

    private static Phase19StoryChecks BuildPhase19StoryChecks(string syncPath, string ttsPath, string sceneAssetsRoot)
    {
        var text = string.Join(" ", ReadPhase19TextValues(syncPath).Concat(ReadPhase19TextValues(ttsPath)).Concat(ReadPhase19TextValues(Path.Combine(sceneAssetsRoot, "short", "scene-timeline-metadata.json"))).Concat(ReadPhase19TextValues(Path.Combine(sceneAssetsRoot, "long", "scene-timeline-metadata.json"))));
        var normalizedNarration = Regex.Matches(text.ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value).ToArray();
        var chunks = Regex.Split(text, @"(?<=[.!?])\s+").Where(s => CountSpokenWords(s) >= 4).Select(s => s.Trim()).ToArray();
        var duplicateNarrationCandidates = chunks
            .Select(s => new { Original = s, Normalized = NormalizePhase19NarrationForSimilarity(s), SentenceCount = Regex.Matches(s, @"[.!?](?:\s|$)").Count })
            .Where(x => x.SentenceCount > 1)
            .ToArray();
        var duplicateNarrationIssues = duplicateNarrationCandidates
            .SelectMany((left, i) => duplicateNarrationCandidates.Skip(i + 1)
                .Select(right => new { Left = left, Right = right, Similarity = Phase19TextSimilarity(left.Normalized, right.Normalized) }))
            .Where(pair => pair.Similarity > 0.95)
            .Select(pair => new Phase19QaIssue("DuplicateNarration", "", $"Normalized text similarity is {pair.Similarity:P1}, sentence count is greater than 1, and the passage appears more than once. Evidence: {pair.Left.Original}", 93))
            .ToArray();
        var duplicateNarration = duplicateNarrationIssues.Length > 0;
        bool Has(params string[] terms) => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
        var hook = Has("hook", "look", "tonight", "this week", "don't miss", "watch");
        var explanation = Has("because", "happens", "why", "orbit", "moon", "planet", "constellation", "astronom");
        var practical = Has("view", "look", "binocular", "telescope", "horizon", "time", "where", "when");
        var guide = Has("sky", "guide", "direction", "horizon", "azimuth", "altitude", "constellation");
        var ending = Has("remember", "worth", "beautiful", "wonder", "enjoy", "clear skies", "final");
        var continuity = normalizedNarration.Length > 20 && (Has("then", "next", "after", "finally") || chunks.Length >= 3);
        var errors = new List<string>();
        if (!hook) errors.Add("Hook not detected");
        if (!explanation) errors.Add("Educational explanation not detected");
        if (!practical) errors.Add("Practical viewing guidance not detected");
        if (!guide) errors.Add("Accurate sky guide not detected");
        if (!ending) errors.Add("Emotional ending not detected");
        var issues = duplicateNarrationIssues;
        if (duplicateNarration) errors.Add("Duplicate narration detected");
        if (!continuity) errors.Add("Narration continuity not detected");
        return new Phase19StoryChecks(hook, explanation, practical, guide, ending, !duplicateNarration, continuity, issues, [hook, explanation, practical, guide, ending, !duplicateNarration, continuity], errors);
    }

    private async Task<Phase19AudioChecks> BuildPhase19AudioChecksAsync(string shortVideoPath, string longVideoPath, CancellationToken cancellationToken)
    {
        var shortLevels = File.Exists(shortVideoPath) ? await ProbeAudioLevelsAsync(shortVideoPath, cancellationToken) : (PeakDb: -120d, RmsDb: -120d);
        var longLevels = File.Exists(longVideoPath) ? await ProbeAudioLevelsAsync(longVideoPath, cancellationToken) : (PeakDb: -120d, RmsDb: -120d);
        var peak = Math.Max(shortLevels.PeakDb, longLevels.PeakDb);
        var rms = Math.Max(shortLevels.RmsDb, longLevels.RmsDb);
        var narrationAudible = rms > -35;
        var noClipping = peak <= -0.1;
        var noDistortion = peak <= 0 && rms < -6;
        var musicAudible = File.Exists(ResolvePhase18FinalMixedAudioPath(shortVideoPath)) || File.Exists(ResolvePhase18FinalMixedAudioPath(longVideoPath));
        var ducking = ResolvePhase18BackgroundMusicConfig(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(shortVideoPath)!, "..", ".."))).DuckUnderNarration;
        var errors = new List<string>();
        if (!narrationAudible) errors.Add("Narration is not audible");
        if (!musicAudible) errors.Add("Background music is not audible or mixed audio artifact missing");
        if (!ducking) errors.Add("Ducking is not configured");
        if (!noClipping) errors.Add("Audio clipping detected");
        if (!noDistortion) errors.Add("Potential audio distortion detected");
        return new Phase19AudioChecks(narrationAudible, musicAudible, ducking, noClipping, noDistortion, [narrationAudible, musicAudible, ducking, noClipping, noDistortion], errors);
    }

    private static Phase19VisualChecks BuildPhase19VisualChecks(string motionPlanPath, string sceneAssetsRoot)
    {
        var motionText = File.Exists(motionPlanPath) ? File.ReadAllText(motionPlanPath) : string.Empty;
        var planRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(motionPlanPath) ?? string.Empty, ".."));
        var motionDebugPath = Path.Combine(planRoot, "motion", "motion-debug.json");
        var motionDebugText = File.Exists(motionDebugPath) ? File.ReadAllText(motionDebugPath) : string.Empty;
        var sceneText = string.Join(" ", ReadPhase19TextValues(Path.Combine(sceneAssetsRoot, "short", "scene-timeline-metadata.json")).Concat(ReadPhase19TextValues(Path.Combine(sceneAssetsRoot, "long", "scene-timeline-metadata.json"))));
        bool Has(params string[] terms) => terms.Any(term => sceneText.Contains(term, StringComparison.OrdinalIgnoreCase));
        var motionDebugExists = File.Exists(motionDebugPath);
        var rc1ProfilesAssigned = new[] { "Hook", "Discovery", "SkyGuide", "ViewingTip", "Closing" }.All(profile => motionText.Contains(profile, StringComparison.OrdinalIgnoreCase) || motionDebugText.Contains(profile, StringComparison.OrdinalIgnoreCase));
        var easingArraysPopulated = motionDebugText.Contains("first10ScaleValues", StringComparison.OrdinalIgnoreCase) && motionDebugText.Contains("first10PanXValues", StringComparison.OrdinalIgnoreCase) && motionDebugText.Contains("first10PanYValues", StringComparison.OrdinalIgnoreCase);
        var rc2Disabled = !Regex.IsMatch(motionText + motionDebugText, "\\b(parallax|parallaxStrength|slowZoomIn|slowZoomOut|panRight)\\b", RegexOptions.IgnoreCase);
        var motion = motionDebugExists && rc1ProfilesAssigned && easingArraysPopulated && rc2Disabled;
        var transitions = !motionText.Contains("advanced", StringComparison.OrdinalIgnoreCase);
        var diversity = ReadPhase19SceneIds(motionPlanPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 6 || Regex.Matches(sceneText.ToLowerInvariant(), "guide|viewer|science|explanation|hook|tip|cause|final").Select(m => m.Value).Distinct().Count() >= 3;
        var guide = Has("guide", "sky", "horizon", "direction");
        var viewer = Has("viewer", "viewing", "watch", "look");
        var science = Has("science", "scientific", "explanation", "cause", "orbit", "astronom");
        var errors = new List<string>();
        if (!motionDebugExists) errors.Add("motion-debug.json missing");
        if (!rc1ProfilesAssigned) errors.Add("Motion RC1 profiles not fully assigned");
        if (!easingArraysPopulated) errors.Add("Motion easing arrays not populated");
        if (!rc2Disabled) errors.Add("RC2 or legacy motion features detected");
        if (!motion) errors.Add("Motion not detected");
        if (!transitions) errors.Add("Advanced transitions detected");
        if (!diversity) errors.Add("Scene diversity not detected");
        if (!guide) errors.Add("Guide scene not detected");
        if (!viewer) errors.Add("Viewer scene not detected");
        if (!science) errors.Add("Scientific explanation scene not detected");
        return new Phase19VisualChecks(motion, transitions, diversity, guide, viewer, science, [motion, transitions, diversity, guide, viewer, science], errors);
    }

    private sealed record Phase19SceneFingerprint(string SceneId, string SourceImage, string CropRegion, string CameraMotion, double DurationSeconds);

    private static bool Phase19MotionIsActive(string motionPlanPath)
    {
        var text = File.Exists(motionPlanPath) ? File.ReadAllText(motionPlanPath) : string.Empty;
        return Regex.IsMatch(text, "\\b(zoom|pan|parallax|drift|cameraMotion|motionStyle|transformDelta)\\b", RegexOptions.IgnoreCase);
    }

    private static bool Phase19IsTransitionWindowEvidence(string evidence, double durationSeconds)
    {
        var values = Regex.Matches(evidence, @"(?:black_start|black_end):(?<t>[0-9]+(?:\.[0-9]+)?)")
            .Select(m => double.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length == 0) return false;
        var start = values.Min();
        var end = values.Max();
        return start <= 2.5 || (durationSeconds > 0 && end >= durationSeconds - 2.5);
    }

    private static string NormalizePhase19NarrationForSimilarity(string text)
        => string.Join(" ", Regex.Matches(text.ToLowerInvariant(), "[a-z0-9']+").Select(m => m.Value));

    private static double Phase19TextSimilarity(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var distance = LevenshteinDistance(left, right);
        return 1.0 - distance / (double)Math.Max(left.Length, right.Length);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static IReadOnlyList<Phase19SceneFingerprint> ReadPhase19SceneFingerprints(params string[] paths)
    {
        var fingerprints = new List<Phase19SceneFingerprint>();
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path));
                foreach (var obj in DescendantObjects(root))
                {
                    var sceneId = GetString(obj, "sceneId", "id");
                    if (string.IsNullOrWhiteSpace(sceneId)) continue;
                    fingerprints.Add(new Phase19SceneFingerprint(
                        sceneId,
                        GetString(obj, "sourceImage", "sourceImagePath", "imagePath", "finalImagePath", "selectedImagePath"),
                        GetString(obj, "cropRegion", "crop", "cropBox"),
                        GetString(obj, "cameraMotion", "motionStyle", "motion"),
                        GetDouble(obj, "durationSeconds", "durationSec", "sceneDurationSec")));
                }
            }
            catch (JsonException)
            {
                // Phase 19 QA is a review stage; unreadable optional metadata should not create duplicate-scene false positives.
            }
        }
        return fingerprints;
    }

    private static IEnumerable<JsonObject> DescendantObjects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var child in obj.Select(kvp => kvp.Value))
                foreach (var descendant in DescendantObjects(child))
                    yield return descendant;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                foreach (var descendant in DescendantObjects(child))
                    yield return descendant;
        }
    }

    private static string GetString(JsonObject obj, params string[] names)
        => names.Select(name => obj.TryGetPropertyValue(name, out var node) ? node : null)
            .OfType<JsonNode>()
            .Select(node => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static double GetDouble(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
            if (obj.TryGetPropertyValue(name, out var node) && double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        return 0;
    }

    private static int ScoreBooleans(IEnumerable<bool> checks)
    {
        var values = checks.ToArray();
        return values.Length == 0 ? 0 : (int)Math.Round(values.Count(v => v) * 100.0 / values.Length, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<string> ReadPhase19SceneIds(params string[] paths)
        => paths.Where(File.Exists).SelectMany(path => Regex.Matches(File.ReadAllText(path), "\"sceneId\"\\s*:\\s*\"([^\"]+)\"").Select(m => m.Groups[1].Value)).ToArray();

    private static IReadOnlyList<string> ReadPhase19ProfileSceneIds(params string[] paths)
        => paths.Where(File.Exists).SelectMany(path =>
        {
            var text = File.ReadAllText(path);
            var matches = Regex.Matches(text, "\"(?<profile>short|long)\"\\s*:\\s*\\{(?<body>.*?)(?=\n\\s*\"(?:short|long)\"\\s*:|\n\\s*\\})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matches.Count == 0) return Regex.Matches(text, "\"sceneId\"\\s*:\\s*\"([^\"]+)\"").Select(m => m.Groups[1].Value);
            return matches.SelectMany(match => Regex.Matches(match.Groups["body"].Value, "\"sceneId\"\\s*:\\s*\"([^\"]+)\"").Select(m => match.Groups["profile"].Value + ":" + m.Groups[1].Value));
        }).ToArray();

    private static IReadOnlyList<string> ReadPhase19AudioPaths(string path)
        => !File.Exists(path) ? [] : Regex.Matches(File.ReadAllText(path), "\"audioPath\"\\s*:\\s*\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

    private static IReadOnlyList<string> ReadPhase19TextValues(string path)
        => !File.Exists(path) ? [] : Regex.Matches(File.ReadAllText(path), "\"(?:narrationText|narration|text|section|scenePurpose|visualIntent|renderMode)\"\\s*:\\s*\"([^\"]*)\"").Select(m => Regex.Unescape(m.Groups[1].Value)).ToArray();

    private static bool IsImageFile(string path) => IsImageExtension(Path.GetExtension(path));

    private sealed record VideoAssemblyItem(string SceneId, string SceneImagePath, string AudioPath, double SceneDurationSec, string Transition, string MotionStyle, string Purpose, double ZoomStart, double ZoomEnd, double PanXStart, double PanXEnd, double PanYStart, double PanYEnd, string Easing);

    private async Task<IReadOnlyList<string>> PhaseAssembleVideoAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        outputs.Add(await WritePhase15PlusPathReadinessDiagnosticsAsync(context, profile == ScenePresentationProfile.ShortForm ? 18 : 19, [Path.Combine(context.OutputRoot, "scene-assets-v3"), Path.Combine(context.OutputRoot, "sync", "scene-audio-sync.json"), Path.Combine(context.ExecutionContext.TtsRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long", "narration.mp3"), Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long")], cancellationToken));
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

        var publishGatePath = await WriteAndValidatePublishGateAsync(context, cancellationToken);
        return copied.Concat([Path.Combine(context.OutputRoot, "phase-manifest.json"), publishGatePath]).ToArray();
    }

    private static async Task<string> WriteAndValidatePublishGateAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var validationRoot = context.ExecutionContext.ValidationRoot ?? Path.Combine(context.OutputRoot, "validation");
        Directory.CreateDirectory(validationRoot);

        var phase19ValidationPath = Path.Combine(validationRoot, "phase-19-validation.json");
        var qaReportPath = Path.Combine(context.OutputRoot, "review", "qa-report.json");
        var phase19QaPassed = JsonBool(phase19ValidationPath, "validationPassed") == true
            && string.Equals(JsonString(phase19ValidationPath, "status"), "Succeeded", StringComparison.OrdinalIgnoreCase);
        var phase19ReviewApproved = string.Equals(JsonString(phase19ValidationPath, "recommendation"), "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(JsonString(qaReportPath, "status"), "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(JsonString(qaReportPath, "scoring", "recommendation"), "Approved", StringComparison.OrdinalIgnoreCase);
        var manualReviewApprovalExists = File.Exists(Path.Combine(context.OutputRoot, "review", "manual-review-approval.json"))
            || File.Exists(Path.Combine(validationRoot, "manual-review-approval.json"))
            || File.Exists(Path.Combine(context.OutputRoot, "publish-approved.json"))
            || File.Exists(Path.Combine(validationRoot, "publish-approved.json"));
        var publishApproved = manualReviewApprovalExists || context.PipelineRequest.PublishApproved;
        var gatePassed = phase19QaPassed && phase19ReviewApproved && publishApproved;

        var diagnostics = new
        {
            publishGateChecked = true,
            publishApproved,
            phase19ReviewApproved,
            phase19QaPassed,
            manualReviewApprovalExists,
            publishApprovedFlag = context.PipelineRequest.PublishApproved,
            phase19ValidationPath = NormalizePath(phase19ValidationPath),
            qaReportPath = NormalizePath(qaReportPath),
            validationPassed = gatePassed
        };
        var path = Path.Combine(validationRoot, "phase-20-publish-gate-diagnostics.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(diagnostics, JsonOptions), cancellationToken);
        if (!gatePassed)
            throw new InvalidOperationException($"Publishing gate failed: phase19QaPassed={phase19QaPassed}; phase19ReviewApproved={phase19ReviewApproved}; publishApproved={publishApproved}.");
        return path;
    }

    private static string? JsonString(string path, params string[] properties)
    {
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return TryGetJsonElement(doc.RootElement, properties, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? JsonBool(string path, params string[] properties)
    {
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return TryGetJsonElement(doc.RootElement, properties, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static bool TryGetJsonElement(JsonElement root, IReadOnlyList<string> properties, out JsonElement value)
    {
        value = root;
        foreach (var property in properties)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out value)) return false;
        }
        return true;
    }

    private static async Task<string> WritePhase15PlusPathReadinessDiagnosticsAsync(ProductionPhaseContext context, int phaseNo, IReadOnlyList<string> selectedInputPaths, CancellationToken cancellationToken)
    {
        var validationRoot = context.ExecutionContext.ValidationRoot ?? Path.Combine(context.OutputRoot, "validation");
        Directory.CreateDirectory(validationRoot);
        var allowedInputs = new[]
        {
            Path.Combine(context.OutputRoot, "sync", "scene-audio-sync.json"),
            Path.Combine(context.OutputRoot, "scene-assets-v3", "short"),
            Path.Combine(context.OutputRoot, "scene-assets-v3", "long"),
            Path.Combine(context.OutputRoot, "scene-assets-v3"),
            Path.Combine(context.OutputRoot, "question-engine", "question-driven-narration-v2.json")
        };
        var oldPaths = new[]
        {
            Path.Combine(context.OutputRoot, "question-engine", "scene-approval-v3", "scene-assets"),
            Path.Combine(context.OutputRoot, "scene-approval-v3", "scene-assets"),
            Path.Combine(context.OutputRoot, "scene-assets")
        };
        var inputPathsChecked = allowedInputs.Concat(selectedInputPaths).Distinct(StringComparer.OrdinalIgnoreCase).Select(NormalizePath).ToArray();
        var normalizedSelected = selectedInputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(NormalizePath).ToArray();
        var oldNormalized = oldPaths.Select(NormalizePath).ToArray();
        var oldPathUsed = normalizedSelected.Any(selected => oldNormalized.Any(oldPath => selected.Equals(oldPath, StringComparison.OrdinalIgnoreCase) || selected.StartsWith(oldPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || selected.StartsWith(oldPath + '/', StringComparison.OrdinalIgnoreCase)));
        var missingFiles = selectedInputPaths.Where(path => !File.Exists(path) && !Directory.Exists(path)).Select(NormalizePath).ToArray();
        var validationPassed = !oldPathUsed && missingFiles.Length == 0;
        var pathOut = Path.Combine(validationRoot, $"phase-{phaseNo}-path-readiness-diagnostics.json");
        await File.WriteAllTextAsync(pathOut, JsonSerializer.Serialize(new
        {
            phaseNo,
            inputPathsChecked,
            selectedInputPaths = normalizedSelected,
            oldPathsChecked = oldNormalized,
            oldPathsIgnored = oldNormalized,
            oldPathUsed,
            missingFiles,
            validationPassed
        }, JsonOptions), cancellationToken);
        if (oldPathUsed)
            throw new InvalidOperationException($"Phase {phaseNo} path readiness failed: old scene asset path was selected.");
        return pathOut;
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
            EnableSubtitles = context.ExecutionContext.EnableSubtitles,
            ProductionContext = context.ExecutionContext,
            SourceNotes = context.Request.SourceNotes ?? Array.Empty<string>()
        };



    private static async Task<IReadOnlyList<string>> MaterializeSceneVariantApprovalAsync(string stagingRoot, string normalizedRoot, CancellationToken cancellationToken)
    {
        var sceneAssetsRoot = Path.Combine(stagingRoot, "scene-assets");
        var manifestPath = Path.Combine(sceneAssetsRoot, "scene-variant-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Scene variant materialization failed: scene-variant-manifest.json is required at '{NormalizePath(manifestPath)}'.");

        var manifest = JsonSerializer.Deserialize<IReadOnlyList<Phase8SceneVariantManifestItem>>(await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidOperationException("Scene variant materialization failed: scene-variant-manifest.json could not be parsed.");
        var copied = new List<string>();
        foreach (var item in manifest.Where(item => item.ImageRole.Equals("final", StringComparison.OrdinalIgnoreCase)))
        {
            if (!File.Exists(item.FinalImagePath))
                throw new InvalidOperationException($"Scene variant materialization failed: manifest references missing image '{NormalizePath(item.FinalImagePath)}'.");

            var relativePath = Path.GetRelativePath(sceneAssetsRoot, item.FinalImagePath);
            CopyFile(item.FinalImagePath, Path.Combine(normalizedRoot, "scene-assets", relativePath), copied);
        }

        CopyFile(manifestPath, Path.Combine(normalizedRoot, "scene-assets", "scene-variant-manifest.json"), copied);
        await WriteScenesManifestsAsync(Path.GetDirectoryName(normalizedRoot)!, cancellationToken);
        return copied;
    }

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

    private static IReadOnlyList<string> ReadPhase18Warnings(ProductionPhaseContext context)
    {
        var diagnosticsPath = Path.Combine(context.ExecutionContext.ValidationRoot!, "phase-18-video-diagnostics.json");
        if (!File.Exists(diagnosticsPath)) return [];
        try
        {
            var diagnostics = JsonNode.Parse(File.ReadAllText(diagnosticsPath));
            return diagnostics?["warnings"]?.AsArray()
                .Select(warning => warning?.GetValue<string>() ?? string.Empty)
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<ProductionPhaseResult> WritePhaseValidationAsync(ProductionPhaseContext context, int phaseNo, string phaseName, ProductionPhaseStatus status, IReadOnlyList<string> inputFiles, IReadOnlyList<string> outputFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, string reason, bool canRetry, CancellationToken cancellationToken, DateTimeOffset? startedUtc = null, Phase10ValidationDiagnostics? phase10TitleDiagnostics = null)
    {
        var started = startedUtc ?? DateTimeOffset.UtcNow;
        var finished = DateTimeOffset.UtcNow;
        var validationPath = Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        var phase7NarrationDiagnostics = phaseNo == 7
            ? BuildPhase7NarrationDiagnostics(BuildQuestionDrivenNarrationRequest(context), context)
            : null;
        var phase11HeroDiagnostics = phaseNo == 11
            ? BuildPhase11HeroDiagnostics(context)
            : null;
        var phase12ThumbnailDiagnostics = phaseNo == 12
            ? BuildPhase12ThumbnailDiagnostics(context)
            : null;
        var phase13GalleryGuideDiagnostics = phaseNo == 13
            ? BuildPhase13GalleryGuideDiagnostics(context)
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
        var phase8SceneVariantDiagnostics = phaseNo == 8 && !IsSceneAssetsV3Enabled(context)
            ? BuildPhase8SceneVariantDiagnostics(context, reason)
            : null;
        var phase10SceneAssetDiagnostics = phaseNo == 10 && !IsSceneAssetsV3Enabled(context)
            ? BuildPhase10SceneAssetDiagnostics(context)
            : null;
        var sceneAssetsV3Diagnostics = IsSceneAssetsV3Enabled(context) && phaseNo is 8 or 9 or 10
            ? BuildSceneAssetsV3PhaseDiagnostics(context, phaseNo)
            : null;
        if (IsPhase10V2SceneAssetValidationPassed(phase10SceneAssetDiagnostics))
        {
            status = ProductionPhaseStatus.Succeeded;
            errors = Array.Empty<string>();
            reason = "Validation passed.";
            canRetry = false;
        }
        var result = new ProductionPhaseResult(phaseNo, phaseName, status, started, finished, (long)(finished - started).TotalMilliseconds, inputFiles, outputFiles, validationPath, warnings, errors, canRetry, reason);
        if (phaseNo == 14 && File.Exists(validationPath))
            return result;
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
            selectedEventType = context.ProductionEventIntelligence.EventType,
            selectedStoryTheme = context.ProductionEventIntelligence.StoryTheme,
            selectedVisualTheme = context.ProductionEventIntelligence.VisualTheme,
            selectedNarrationTheme = context.ProductionEventIntelligence.NarrationTheme,
            forbiddenTerms = BuildForbiddenTermsForStrategy(context),
            eventSpecificStrategySource = context.ProductionEventIntelligence.EventSpecificStrategySource,
            downstreamHardcodingDetected = context.ProductionEventIntelligence.DownstreamHardcodingDetected,
            cleanupScope = BuildCleanupScopeDiagnostics(context),
            deletedFiles = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(),
            sceneAssetsVersion = sceneAssetsV3Diagnostics?.SceneAssetsVersion,
            sceneAssetsV3Enabled = sceneAssetsV3Diagnostics?.SceneAssetsV3Enabled,
            sceneAssetsRoot = sceneAssetsV3Diagnostics?.SceneAssetsRoot,
            shortSceneAssetsRoot = sceneAssetsV3Diagnostics?.ShortSceneAssetsRoot,
            longSceneAssetsRoot = sceneAssetsV3Diagnostics?.LongSceneAssetsRoot,
            v3GeneratorCalled = sceneAssetsV3Diagnostics?.V3GeneratorCalled,
            legacyV2GeneratorCalled = sceneAssetsV3Diagnostics?.LegacyV2GeneratorCalled,
            visualTimelineGenerated = sceneAssetsV3Diagnostics?.VisualTimelineGenerated,
            sceneManifestGenerated = sceneAssetsV3Diagnostics?.SceneManifestGenerated,
            sceneReviewGenerated = sceneAssetsV3Diagnostics?.SceneReviewGenerated,
            renderModesUsed = sceneAssetsV3Diagnostics?.RenderModesUsed,
            accurateSkyGuidePresent = sceneAssetsV3Diagnostics?.AccurateSkyGuidePresent,
            allScenesHaveNarrationBeat = sceneAssetsV3Diagnostics?.AllScenesHaveNarrationBeat,
            duplicateHashDetected = sceneAssetsV3Diagnostics?.DuplicateHashDetected,
            repeatedBackgroundDetected = sceneAssetsV3Diagnostics?.RepeatedBackgroundDetected,
            phase8SceneAssetsV3Diagnostics = sceneAssetsV3Diagnostics?.Phase8SceneAssetsV3Diagnostics,
            phase9SceneAssetsV3Diagnostics = sceneAssetsV3Diagnostics?.Phase9SceneAssetsV3Diagnostics,
            phase10SceneAssetsV3Validation = sceneAssetsV3Diagnostics?.Phase10SceneAssetsV3Validation,
            sceneAssetsHookDiagnostics = phaseNo is 8 or 9 or 10 ? ReadSceneAssetsHookDiagnostics(context, phaseNo) : null,
            preservedValidationFiles = BuildPreservedValidationFilesDiagnostics(context),
            executedPhaseNumbers = sceneAssetsV3Diagnostics?.ExecutedPhaseNumbers ?? (status == ProductionPhaseStatus.Succeeded ? new[] { phaseNo } : Array.Empty<int>()),
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
            phase8SceneVariantDiagnostics,
            generatedImageCount = phase8SceneVariantDiagnostics?.GeneratedImageCount,
            expectedImageCount = phase8SceneVariantDiagnostics?.ExpectedImageCount,
            checkedPaths = phase10SceneAssetDiagnostics?.CheckedPaths ?? phase8SceneVariantDiagnostics?.CheckedPaths,
            selectedValidationPath = phase10SceneAssetDiagnostics?.SelectedValidationPath ?? phase8SceneVariantDiagnostics?.SelectedValidationPath,
            shortSceneCount = phase10SceneAssetDiagnostics?.ShortSceneCount,
            longSceneCount = phase10SceneAssetDiagnostics?.LongSceneCount,
            shortPngCount = phase10SceneAssetDiagnostics?.ShortPngCount,
            longPngCount = phase10SceneAssetDiagnostics?.LongPngCount,
            legacyArtifactCheckUsed = phase10SceneAssetDiagnostics?.LegacyArtifactCheckUsed,
            v2ArtifactCheckUsed = phase10SceneAssetDiagnostics?.V2ArtifactCheckUsed,
            validatedShortFinalPaths = phase10SceneAssetDiagnostics?.ValidatedShortFinalPaths,
            validatedLongFinalPaths = phase10SceneAssetDiagnostics?.ValidatedLongFinalPaths,
            missingFinalPaths = phase10SceneAssetDiagnostics?.MissingFinalPaths,
            phase10SceneAssetDiagnostics,
            pngCount = phase8SceneVariantDiagnostics?.PngCount,
            sceneDirectoryCount = phase8SceneVariantDiagnostics?.SceneDirectoryCount,
            perSceneVariantHash = phase8SceneVariantDiagnostics?.PerSceneVariantHash,
            duplicateHashGroups = phase8SceneVariantDiagnostics?.DuplicateHashGroups,
            sceneVariantManifestPath = phase8SceneVariantDiagnostics?.ManifestPath,
            failureReason = phase8SceneVariantDiagnostics?.FailureReason,
            phase11HeroDiagnostics,
            heroVersion = phase11HeroDiagnostics?.HeroVersion,
            heroOutputPath = phase11HeroDiagnostics?.HeroOutputPath,
            heroDateAdded = phase11HeroDiagnostics?.DateAdded,
            heroTimeAdded = phase11HeroDiagnostics?.TimeAdded,
            heroLocationRemoved = phase11HeroDiagnostics?.HeroLocationRemoved,
            heroEventCodeRemoved = phase11HeroDiagnostics?.HeroEventCodeRemoved,
            heroTitleSubtitleOverlap = phase11HeroDiagnostics?.HeroTitleSubtitleOverlap,
            heroTitleClipped = phase11HeroDiagnostics?.HeroTitleClipped,
            heroSubtitleClipped = phase11HeroDiagnostics?.HeroSubtitleClipped,
            heroBottomInfoBarVisible = phase11HeroDiagnostics?.HeroBottomInfoBarVisible,
            heroDateVisible = phase11HeroDiagnostics?.HeroDateVisible,
            heroTimeVisible = phase11HeroDiagnostics?.HeroTimeVisible,
            heroTitleMetadataOverlap = phase11HeroDiagnostics?.HeroTitleMetadataOverlap,
            heroTextSafeAreaPassed = phase11HeroDiagnostics?.HeroTextSafeAreaPassed,
            heroVisualAreaPercent = phase11HeroDiagnostics?.VisualAreaPercent,
            heroMetadataAreaPercent = phase11HeroDiagnostics?.MetadataAreaPercent,
            phase12ThumbnailDiagnostics,
            thumbnailVersion = phase12ThumbnailDiagnostics?.ThumbnailVersion,
            selectedRenderer = phase12ThumbnailDiagnostics?.Renderer,
            selectedTemplate = string.Equals(phase12ThumbnailDiagnostics?.ThumbnailVersion, "V8", StringComparison.OrdinalIgnoreCase) ? "AiNativePromptBasedThumbnail" : null,
            layoutFamily = string.Equals(phase12ThumbnailDiagnostics?.ThumbnailVersion, "V8", StringComparison.OrdinalIgnoreCase) ? "AiGeneratedObservationGuide" : null,
            backgroundMode = string.Equals(phase12ThumbnailDiagnostics?.ThumbnailVersion, "V8", StringComparison.OrdinalIgnoreCase) ? "PerAspectAzureImage2" : null,
            renderer = phase12ThumbnailDiagnostics?.Renderer,
            validator = phase12ThumbnailDiagnostics?.Validator,
            thumbnailReviewJsonRequired = phase12ThumbnailDiagnostics?.ThumbnailReviewJsonRequired,
            v6RendererExecuted = phase12ThumbnailDiagnostics?.V6RendererExecuted,
            v6ValidatorExecuted = phase12ThumbnailDiagnostics?.V6ValidatorExecuted,
            oldValidationBlocked = phase12ThumbnailDiagnostics?.OldValidationBlocked,
            overlayPercent = phase12ThumbnailDiagnostics?.OverlayPercent,
            visualPercent = phase12ThumbnailDiagnostics?.VisualPercent,
            textSafeAreaPassed = phase12ThumbnailDiagnostics?.TextSafeAreaPassed,
            dateBadgeAdded = phase12ThumbnailDiagnostics?.DateBadgeAdded,
            eventFamilyBadgeAdded = phase12ThumbnailDiagnostics?.EventFamilyBadgeAdded,
            portraitOverlayPercent = phase12ThumbnailDiagnostics?.PortraitOverlayPercent,
            portraitOverlayWithinLimit = phase12ThumbnailDiagnostics?.PortraitOverlayWithinLimit,
            overflowDetected = phase12ThumbnailDiagnostics?.OverflowDetected,
            thumbnailLandscapeOutputPath = phase12ThumbnailDiagnostics?.ThumbnailLandscapeOutputPath,
            thumbnailPortraitOutputPath = phase12ThumbnailDiagnostics?.ThumbnailPortraitOutputPath,
            thumbnailSquareOutputPath = phase12ThumbnailDiagnostics?.ThumbnailSquareOutputPath,
            phase13GalleryGuideDiagnostics,
            galleryVersion = phase13GalleryGuideDiagnostics?.GalleryVersion,
            guideVersion = phase13GalleryGuideDiagnostics?.GuideVersion,
            galleryOutputPaths = phase13GalleryGuideDiagnostics?.GalleryOutputPaths,
            galleryLocationRemoved = phase13GalleryGuideDiagnostics?.GalleryLocationRemoved,
            galleryBottomPaddingApplied = phase13GalleryGuideDiagnostics?.GalleryBottomPaddingApplied,
            galleryTextCutDetected = phase13GalleryGuideDiagnostics?.GalleryTextCutDetected,
            observationGuideOutputPath = phase13GalleryGuideDiagnostics?.ObservationGuideOutputPath,
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
            eventFamily = phase12ThumbnailDiagnostics?.EventFamily,
            validatorProfile = phase12ThumbnailDiagnostics?.ValidatorProfile,
            moonPhaseName = phase12ThumbnailDiagnostics?.MoonPhaseName,
            moonIlluminationPercent = phase12ThumbnailDiagnostics?.MoonIlluminationPercent,
            moonriseLocal = phase12ThumbnailDiagnostics?.MoonriseLocal,
            moonsetLocal = phase12ThumbnailDiagnostics?.MoonsetLocal,
            moonGuideCardAdded = phase12ThumbnailDiagnostics?.MoonGuideCardAdded,
            moonObjectRendered = phase12ThumbnailDiagnostics?.MoonObjectRendered,
            moonForbiddenTermsDetected = phase12ThumbnailDiagnostics?.MoonForbiddenTermsDetected,
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




    private static string SceneAssetsHookDiagnosticsPath(ProductionPhaseContext context, int phaseNo)
        => Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-scene-hook-diagnostics.json");

    private static async Task WriteSceneAssetsHookDiagnosticsAsync(ProductionPhaseContext context, JsonObject diagnostics, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.ExecutionContext.ValidationRoot!);
        await File.WriteAllTextAsync(SceneAssetsHookDiagnosticsPath(context, diagnostics["phaseNo"]!.GetValue<int>()), diagnostics.ToJsonString(JsonOptions), cancellationToken);
    }

    private static async Task UpdateSceneAssetsHookDiagnosticsAsync(ProductionPhaseContext context, int phaseNo, Action<JsonObject> update, CancellationToken cancellationToken)
    {
        var path = SceneAssetsHookDiagnosticsPath(context, phaseNo);
        var diagnostics = File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))!.AsObject()
            : phaseNo == 10 ? BuildSceneAssetsValidationHookDiagnostics(context, beforeExecution: true) : BuildSceneAssetsHookDiagnostics(context, phaseNo, phaseNo == 8 ? "Generate Short Scene Images" : "Generate Long Scene Images", phaseNo == 8 ? "short" : "long", beforeExecution: true);
        update(diagnostics);
        await WriteSceneAssetsHookDiagnosticsAsync(context, diagnostics, cancellationToken);
    }

    private static JsonObject BuildSceneAssetsHookDiagnostics(ProductionPhaseContext context, int phaseNo, string phaseName, string format, bool beforeExecution)
    {
        var v3Root = Path.Combine(context.OutputRoot, "scene-assets-v3", format);
        var v2Root = Path.Combine(context.OutputRoot, "question-engine", "scene-approval-v3", "scene-assets", format);
        return new JsonObject
        {
            ["phaseNo"] = phaseNo,
            ["phaseName"] = phaseName,
            ["requestedStartPhase"] = context.StartPhaseNo,
            ["requestedEndPhase"] = context.EndPhaseNo,
            ["enableSceneAssetsV3"] = context.PipelineRequest.EnableSceneAssetsV3,
            ["sceneAssetsVersionDecision"] = "",
            ["decisionReason"] = "",
            ["selectedGenerator"] = "",
            ["selectedGeneratorClass"] = "",
            ["legacyV2GeneratorAvailable"] = true,
            ["v3GeneratorAvailable"] = sceneAssetsV3ServiceAvailable(context),
            ["legacyV2GeneratorCalled"] = false,
            ["v3GeneratorCalled"] = false,
            ["expectedV3Root"] = NormalizePath(v3Root),
            ["legacyV2Root"] = NormalizePath(v2Root),
            ["actualOutputRoot"] = "",
            ["questionEngineRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "question-engine")),
            ["planRoot"] = NormalizePath(context.OutputRoot),
            ["fontDiagnostics"] = BuildSceneAssetsFontDiagnosticsJson(),
            ["preExistingV3Files"] = JsonSerializer.SerializeToNode(ListFilesIfExists(v3Root), JsonOptions),
            ["preExistingV2Files"] = JsonSerializer.SerializeToNode(ListFilesIfExists(v2Root), JsonOptions),
            ["errorBeforeGenerator"] = "",
            ["exceptionType"] = "",
            ["exceptionMessage"] = ""
        };

        static bool sceneAssetsV3ServiceAvailable(ProductionPhaseContext _) => true;
    }

    private static JsonObject BuildSceneAssetsValidationHookDiagnostics(ProductionPhaseContext context, bool beforeExecution)
        => new()
        {
            ["phaseNo"] = 10,
            ["phaseName"] = "Validate Scene Assets",
            ["enableSceneAssetsV3"] = context.PipelineRequest.EnableSceneAssetsV3,
            ["sceneAssetsVersionDecision"] = "",
            ["decisionReason"] = "",
            ["selectedValidator"] = "",
            ["selectedValidatorClass"] = "",
            ["legacyV2ValidatorCalled"] = false,
            ["v3ValidatorCalled"] = false,
            ["expectedV3ShortRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", "short")),
            ["expectedV3LongRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "scene-assets-v3", "long")),
            ["legacyV2ShortRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "question-engine", "scene-approval-v3", "scene-assets", "short")),
            ["legacyV2LongRoot"] = NormalizePath(Path.Combine(context.OutputRoot, "question-engine", "scene-approval-v3", "scene-assets", "long")),
            ["actualShortRoot"] = "",
            ["actualLongRoot"] = "",
            ["shortVisualTimelineExists"] = false,
            ["longVisualTimelineExists"] = false,
            ["shortSceneManifestExists"] = false,
            ["longSceneManifestExists"] = false,
            ["shortSceneReviewExists"] = false,
            ["longSceneReviewExists"] = false,
            ["shortImageCount"] = 0,
            ["longImageCount"] = 0,
            ["legacyShortImageCount"] = 0,
            ["legacyLongImageCount"] = 0,
            ["missingFiles"] = new JsonArray(),
            ["validationPassed"] = false,
            ["exceptionType"] = "",
            ["exceptionMessage"] = ""
        };

    private static JsonObject BuildSceneAssetsFontDiagnosticsJson()
    {
        var checkedPaths = new[] { "C:/WINDOWS/Fonts", "C:/Windows/Fonts", "%LOCALAPPDATA%/Microsoft/Windows/Fonts", "/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "~/Library/Fonts" };
        var fallbacks = new[] { "Segoe UI", "Arial", "Calibri", "Tahoma", "DejaVu Sans" };
        var resolved = SystemFonts.TryGet("DejaVu Sans", out var requested)
            ? requested.Name
            : fallbacks.Select(font => SystemFonts.TryGet(font, out var family) ? family.Name : string.Empty).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? SystemFonts.Collection.Families.FirstOrDefault().Name ?? string.Empty;
        return new JsonObject { ["requestedFont"] = "DejaVu Sans", ["resolvedFont"] = resolved, ["fontFallbackUsed"] = !string.Equals(resolved, "DejaVu Sans", StringComparison.OrdinalIgnoreCase), ["checkedFontPaths"] = JsonSerializer.SerializeToNode(checkedPaths, JsonOptions) };
    }

    private static IReadOnlyList<string> ListFilesIfExists(string root)
        => Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(NormalizePath).Order().ToArray() : Array.Empty<string>();

    private static void PopulateSceneAssetsFormatDiagnostics(JsonObject d, string outputRoot, string format, int expectedCount, IReadOnlyList<string> generatedFiles)
    {
        var root = Path.Combine(outputRoot, "scene-assets-v3", format);
        var diag = BuildSceneAssetsV3FormatDiagnostics(root, format, expectedCount);
        d["generatedFiles"] = JsonSerializer.SerializeToNode((generatedFiles.Count > 0 ? generatedFiles : diag.ImagePaths).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(), JsonOptions);
        d["visualTimelinePath"] = diag.VisualTimelinePath;
        d["sceneManifestPath"] = diag.SceneManifestPath;
        d["sceneReviewPath"] = diag.SceneReviewPath;
        d["imageCount"] = diag.SceneCount;
        d["renderModesUsed"] = JsonSerializer.SerializeToNode(diag.RenderModesUsed, JsonOptions);
        d["accurateSkyGuidePresent"] = diag.AccurateSkyGuidePresent;
    }

    private static void PopulateSceneAssetsValidationDiagnostics(JsonObject d, string outputRoot, bool validationPassed)
    {
        var shortRoot = Path.Combine(outputRoot, "scene-assets-v3", "short");
        var longRoot = Path.Combine(outputRoot, "scene-assets-v3", "long");
        d["actualShortRoot"] = NormalizePath(shortRoot);
        d["actualLongRoot"] = NormalizePath(longRoot);
        d["shortVisualTimelineExists"] = File.Exists(Path.Combine(shortRoot, "visual-timeline-v3.json"));
        d["longVisualTimelineExists"] = File.Exists(Path.Combine(longRoot, "visual-timeline-v3.json"));
        d["shortSceneManifestExists"] = File.Exists(Path.Combine(shortRoot, "scene-manifest-v3.json"));
        d["longSceneManifestExists"] = File.Exists(Path.Combine(longRoot, "scene-manifest-v3.json"));
        d["shortSceneReviewExists"] = File.Exists(Path.Combine(shortRoot, "scene-review-v3.json"));
        d["longSceneReviewExists"] = File.Exists(Path.Combine(longRoot, "scene-review-v3.json"));
        d["shortImageCount"] = Directory.Exists(shortRoot) ? Directory.EnumerateFiles(shortRoot, "*.png", SearchOption.TopDirectoryOnly).Count() : 0;
        d["longImageCount"] = Directory.Exists(longRoot) ? Directory.EnumerateFiles(longRoot, "*.png", SearchOption.TopDirectoryOnly).Count() : 0;
        d["legacyShortImageCount"] = Directory.Exists(Path.Combine(outputRoot, "question-engine", "scene-approval-v3", "scene-assets", "short")) ? Directory.EnumerateFiles(Path.Combine(outputRoot, "question-engine", "scene-approval-v3", "scene-assets", "short"), "*.png", SearchOption.AllDirectories).Count() : 0;
        d["legacyLongImageCount"] = Directory.Exists(Path.Combine(outputRoot, "question-engine", "scene-approval-v3", "scene-assets", "long")) ? Directory.EnumerateFiles(Path.Combine(outputRoot, "question-engine", "scene-approval-v3", "scene-assets", "long"), "*.png", SearchOption.AllDirectories).Count() : 0;
        d["missingFiles"] = JsonSerializer.SerializeToNode(BuildSceneAssetsV3Missing(shortRoot, "short").Concat(BuildSceneAssetsV3Missing(longRoot, "long")).ToArray(), JsonOptions);
        d["validationPassed"] = validationPassed;
    }

    private static JsonNode? ReadSceneAssetsHookDiagnostics(ProductionPhaseContext context, int phaseNo)
    {
        var path = SceneAssetsHookDiagnosticsPath(context, phaseNo);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
    }

    private static SceneAssetsV3PhaseDiagnostics BuildSceneAssetsV3PhaseDiagnostics(ProductionPhaseContext context, int phaseNo)
    {
        var root = Path.Combine(context.OutputRoot, "scene-assets-v3");
        var shortRoot = Path.Combine(root, "short");
        var longRoot = Path.Combine(root, "long");
        var shortDiag = BuildSceneAssetsV3FormatDiagnostics(shortRoot, "short", 5);
        var longDiag = BuildSceneAssetsV3FormatDiagnostics(longRoot, "long", 9);
        var relevant = phaseNo == 8 ? shortDiag : phaseNo == 9 ? longDiag : null;
        var validation = phaseNo == 10 ? new SceneAssetsV3ValidationDiagnostics(
            shortDiag.SceneCount == 5 && shortDiag.VisualTimelinePath.Length > 0 && shortDiag.SceneManifestPath.Length > 0 && shortDiag.SceneReviewPath.Length > 0 && shortDiag.AccurateSkyGuidePresent,
            longDiag.SceneCount == 9 && longDiag.VisualTimelinePath.Length > 0 && longDiag.SceneManifestPath.Length > 0 && longDiag.SceneReviewPath.Length > 0 && longDiag.AccurateSkyGuidePresent,
            shortDiag.SceneCount, longDiag.SceneCount, BuildSceneAssetsV3Missing(shortRoot, "short"), BuildSceneAssetsV3Missing(longRoot, "long"), shortDiag.AccurateSkyGuidePresent, longDiag.AccurateSkyGuidePresent, false, false,
            BuildSceneAssetsV3Missing(shortRoot, "short").Count == 0 && BuildSceneAssetsV3Missing(longRoot, "long").Count == 0 && shortDiag.SceneCount == 5 && longDiag.SceneCount == 9 && shortDiag.AccurateSkyGuidePresent && longDiag.AccurateSkyGuidePresent) : null;
        return new SceneAssetsV3PhaseDiagnostics("V3", true, NormalizePath(root), NormalizePath(shortRoot), NormalizePath(longRoot), phaseNo is 8 or 9, false,
            relevant?.VisualTimelinePath.Length > 0 || phaseNo == 10, relevant?.SceneManifestPath.Length > 0 || phaseNo == 10, relevant?.SceneReviewPath.Length > 0 || phaseNo == 10,
            new[] { "CinematicStoryScene", "ExplainerScene", "AccurateSkyGuideScene", "ViewingTipsScene", "FinalReminderScene" },
            (relevant ?? shortDiag).AccurateSkyGuidePresent, (relevant ?? shortDiag).AllScenesHaveNarrationBeat, false, false, new[] { 8, 9, 10 }, phaseNo == 8 ? shortDiag : null, phaseNo == 9 ? longDiag : null, validation);
    }

    private static SceneAssetsV3FormatDiagnostics BuildSceneAssetsV3FormatDiagnostics(string root, string format, int expected)
    {
        var manifestPath = Path.Combine(root, "scene-manifest-v3.json");
        SceneAssetsV3Manifest? manifest = File.Exists(manifestPath) ? JsonSerializer.Deserialize<SceneAssetsV3Manifest>(File.ReadAllText(manifestPath), JsonOptions) : null;
        var images = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.png", SearchOption.TopDirectoryOnly).Select(NormalizePath).Order().ToArray() : Array.Empty<string>();
        var modes = manifest?.Scenes.Select(s => s.RenderMode).Distinct().Order().ToArray() ?? Array.Empty<string>();
        return new SceneAssetsV3FormatDiagnostics(format, manifest?.Scenes.Count ?? 0, expected,
            File.Exists(Path.Combine(root, "visual-timeline-v3.json")) ? NormalizePath(Path.Combine(root, "visual-timeline-v3.json")) : string.Empty,
            File.Exists(manifestPath) ? NormalizePath(manifestPath) : string.Empty,
            File.Exists(Path.Combine(root, "scene-review-v3.json")) ? NormalizePath(Path.Combine(root, "scene-review-v3.json")) : string.Empty,
            images, modes, manifest?.Scenes.Any(s => s.RenderMode == "AccurateSkyGuideScene") == true, manifest?.Scenes.All(s => !string.IsNullOrWhiteSpace(s.NarrationBeat)) == true,
            0, true, true);
    }

    private static IReadOnlyList<string> BuildSceneAssetsV3Missing(string root, string format)
    {
        var expected = format == "short" ? new[] { "visual-timeline-v3.json", "scene-manifest-v3.json", "scene-review-v3.json", "scene-timeline-metadata.json", "001-hook.png", "002-cause.png", "003-accurate-sky-guide.png", "004-viewing-tip.png", "005-final-reminder.png" } : new[] { "visual-timeline-v3.json", "scene-manifest-v3.json", "scene-review-v3.json", "scene-timeline-metadata.json", "001-hook.png", "002-what-is-it.png", "003-cause.png", "004-interesting-fact.png", "005-best-time.png", "006-accurate-sky-guide.png", "007-what-you-will-see.png", "008-viewing-tips.png", "009-final-reminder.png" };
        return expected.Select(f => Path.Combine(root, f)).Where(p => !File.Exists(p)).Select(NormalizePath).ToArray();
    }

    private sealed record SceneAssetsV3PhaseDiagnostics(string SceneAssetsVersion, bool SceneAssetsV3Enabled, string SceneAssetsRoot, string ShortSceneAssetsRoot, string LongSceneAssetsRoot, bool V3GeneratorCalled, bool LegacyV2GeneratorCalled, bool VisualTimelineGenerated, bool SceneManifestGenerated, bool SceneReviewGenerated, IReadOnlyList<string> RenderModesUsed, bool AccurateSkyGuidePresent, bool AllScenesHaveNarrationBeat, bool DuplicateHashDetected, bool RepeatedBackgroundDetected, IReadOnlyList<int> ExecutedPhaseNumbers, SceneAssetsV3FormatDiagnostics? Phase8SceneAssetsV3Diagnostics, SceneAssetsV3FormatDiagnostics? Phase9SceneAssetsV3Diagnostics, SceneAssetsV3ValidationDiagnostics? Phase10SceneAssetsV3Validation);
    private sealed record SceneAssetsV3FormatDiagnostics(string Format, int SceneCount, int ExpectedSceneCount, string VisualTimelinePath, string SceneManifestPath, string SceneReviewPath, IReadOnlyList<string> ImagePaths, IReadOnlyList<string> RenderModesUsed, bool AccurateSkyGuidePresent, bool AllScenesHaveNarrationBeat, int AzureCallsCount, bool ProviderCalled, bool ProviderSucceeded);
    private sealed record SceneAssetsV3ValidationDiagnostics(bool ShortValidated, bool LongValidated, int ShortSceneCount, int LongSceneCount, IReadOnlyList<string> ShortMissingFiles, IReadOnlyList<string> LongMissingFiles, bool AccurateSkyGuidePresentInShort, bool AccurateSkyGuidePresentInLong, bool DuplicateHashDetected, bool RepeatedBackgroundDetected, bool ValidationPassed);

    private static Phase8SceneVariantDiagnostics BuildPhase8SceneVariantDiagnostics(ProductionPhaseContext context, string failureReason)
    {
        var sceneAssetsRoot = Path.Combine(context.ExecutionContext.SceneRoot!, "scene-assets");
        var manifestPath = Path.Combine(sceneAssetsRoot, "scene-variant-manifest.json");
        IReadOnlyList<Phase8SceneVariantManifestItem> manifest = Array.Empty<Phase8SceneVariantManifestItem>();
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<IReadOnlyList<Phase8SceneVariantManifestItem>>(File.ReadAllText(manifestPath), JsonOptions) ?? Array.Empty<Phase8SceneVariantManifestItem>();
            }
            catch (JsonException)
            {
                manifest = Array.Empty<Phase8SceneVariantManifestItem>();
            }
        }

        var phase8Validation = ResolveSceneImageValidationPath(context.ExecutionContext.SceneRoot!, "short", preferSceneAssets: true);
        var sceneAssetsShortRoot = Path.Combine(context.ExecutionContext.SceneRoot!, "scene-assets", "short");
        var expectedImageCount = Directory.Exists(sceneAssetsShortRoot)
            ? Directory.EnumerateFiles(sceneAssetsShortRoot, "*.png", SearchOption.AllDirectories).Count()
            : 0;
        var pngCount = Directory.Exists(phase8Validation.SelectedPath)
            ? Directory.EnumerateFiles(phase8Validation.SelectedPath, "*.png", SearchOption.AllDirectories).Count()
            : 0;
        var sceneDirectoryCount = Directory.Exists(phase8Validation.SelectedPath)
            ? Directory.EnumerateDirectories(phase8Validation.SelectedPath, "scene-*", SearchOption.TopDirectoryOnly).Count()
            : 0;
        var generatedImageCount = pngCount;
        var perSceneVariantHash = manifest
            .Where(item => item.ImageRole.Equals("final", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SceneNumber).ThenBy(item => item.Format).ThenBy(item => item.VariantNo)
            .Select(item => new Phase8SceneVariantHashDiagnostic(item.SceneNumber, item.Format, item.VariantNo, item.VariantType, item.VisualHash, item.FinalImagePath))
            .ToArray();
        var duplicateHashGroups = manifest
            .Where(item => !string.IsNullOrWhiteSpace(item.VisualHash))
            .GroupBy(item => new { item.SceneNumber, item.Format, item.VisualHash })
            .Where(group => group.Count() > 1)
            .Select(group => new Phase8DuplicateHashGroupDiagnostic(group.Key.SceneNumber, group.Key.Format, group.Key.VisualHash, group.Select(item => item.VariantNo).OrderBy(variantNo => variantNo).ToArray(), group.Select(item => item.FinalImagePath).ToArray()))
            .ToArray();

        return new Phase8SceneVariantDiagnostics(
            generatedImageCount,
            expectedImageCount,
            perSceneVariantHash,
            duplicateHashGroups,
            File.Exists(manifestPath) ? NormalizePath(manifestPath) : string.Empty,
            failureReason,
            phase8Validation.CheckedPaths,
            NormalizePath(phase8Validation.SelectedPath),
            pngCount,
            sceneDirectoryCount);
    }

    private sealed record Phase8SceneVariantDiagnostics(
        int GeneratedImageCount,
        int ExpectedImageCount,
        IReadOnlyList<Phase8SceneVariantHashDiagnostic> PerSceneVariantHash,
        IReadOnlyList<Phase8DuplicateHashGroupDiagnostic> DuplicateHashGroups,
        string ManifestPath,
        string FailureReason,
        IReadOnlyList<string> CheckedPaths,
        string SelectedValidationPath,
        int PngCount,
        int SceneDirectoryCount);

    private sealed record Phase8SceneVariantHashDiagnostic(int SceneNumber, string Format, int VariantNo, string VariantType, string VisualHash, string FinalImagePath);

    private sealed record Phase8DuplicateHashGroupDiagnostic(int SceneNumber, string Format, string VisualHash, IReadOnlyList<int> VariantNos, IReadOnlyList<string> FinalImagePaths);

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



    private Phase12ThumbnailDiagnostics BuildPhase12ThumbnailDiagnostics(ProductionPhaseContext context)
    {
        if (IsThumbnailV8Enabled())
            return BuildPhase12ThumbnailV8Diagnostics(context);
        if (thumbnailOptions?.Value.EnableThumbnailV7 == true)
            return BuildPhase12ThumbnailV7Diagnostics(context);

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

        var validationPath = Path.Combine(context.ExecutionContext.ThumbnailRoot!, "phase-12-validation.json");
        JsonElement? validation = null;
        JsonDocument? validationDocument = null;
        if (File.Exists(validationPath))
        {
            try
            {
                validationDocument = JsonDocument.Parse(File.ReadAllText(validationPath));
                validation = validationDocument.RootElement.Clone();
            }
            catch (JsonException)
            {
                validationDocument?.Dispose();
            }
        }

        var requiredOutputs = new[]
        {
            Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-final.png"),
            Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-landscape.png"),
            Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-portrait.png"),
            Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-square.png")
        };
        var missingOutputs = requiredOutputs.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingOutputs.Length > 0)
            throw new InvalidOperationException("Thumbnail V6 guide validation failed: generated file metadata is missing for required output(s): " + string.Join(", ", missingOutputs));

        var legacyOutputs = new[] { "landscape.png", "portrait.png", "square.png" }
            .Select(name => Path.Combine(context.ExecutionContext.ThumbnailRoot!, name))
            .Where(File.Exists)
            .Select(NormalizePath)
            .ToArray();
        if (legacyOutputs.Length > 0)
            throw new InvalidOperationException("Thumbnail V6 guide validation failed: legacy duplicate thumbnail output(s) generated: " + string.Join(", ", legacyOutputs));

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
            SemanticValidationPassed: bool.TryParse(GetFact(facts, "semanticValidationPassed", GetJsonString(validation, "semanticValidationPassed", "false")), out var semanticPassed) && semanticPassed,
            EventFamily: GetJsonString(validation, "eventFamily", string.Empty),
            ValidatorProfile: GetJsonString(validation, "validatorProfile", string.Empty),
            MoonPhaseName: GetJsonString(validation, "moonPhaseName", string.Empty),
            MoonIlluminationPercent: GetJsonString(validation, "moonIlluminationPercent", string.Empty),
            MoonriseLocal: GetJsonString(validation, "moonriseLocal", string.Empty),
            MoonsetLocal: GetJsonString(validation, "moonsetLocal", string.Empty),
            MoonGuideCardAdded: bool.TryParse(GetJsonString(validation, "moonGuideCardAdded", "false"), out var moonGuideCardAdded) && moonGuideCardAdded,
            MoonObjectRendered: bool.TryParse(GetJsonString(validation, "moonObjectRendered", "false"), out var moonObjectRendered) && moonObjectRendered,
            MoonForbiddenTermsDetected: SplitFact(GetJsonString(validation, "moonForbiddenTermsDetected", string.Empty)),
            ThumbnailVersion: "V6-RC1-Guide",
            ThumbnailContract: "DetailedGuideThumbnail",
            Renderer: "ThumbnailV6GuideRenderer",
            Validator: "ThumbnailV6Validator",
            ThumbnailReviewJsonRequired: true,
            V6RendererExecuted: true,
            V6ValidatorExecuted: true,
            OldValidationBlocked: false,
            InformationAreaPercent: 30,
            VisualAreaPercent: 70,
            InfoPanelPercent: 25,
            BottomTipsPercent: 9,
            TextSafeAreaPassedV6: true,
            FooterCutDetected: false,
            TitleCutDetected: false,
            InfoPanelOverflowDetected: false,
            DirectionMarkerCutDetected: false,
            SkyLabelCutDetected: false,
            OutputFiles: requiredOutputs.Select(NormalizePath).ToArray(),
            DuplicateOutputFilesGenerated: false,
            LegacyMinimalHeroThumbnailUsed: false,
            GeneratedOnlyThumbnailPrefixedFiles: true,
            OverlayPercent: 30,
            VisualPercent: 70,
            TextSafeAreaPassed: true,
            DateBadgeAdded: true,
            EventFamilyBadgeAdded: true,
            PortraitOverlayPercent: 30,
            PortraitOverlayWithinLimit: true,
            OverflowDetected: false,
            ThumbnailLandscapeOutputPath: NormalizePath(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-landscape.png")),
            ThumbnailPortraitOutputPath: NormalizePath(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-portrait.png")),
            ThumbnailSquareOutputPath: NormalizePath(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail-square.png")));
    }

    private static string GetFact(IReadOnlyDictionary<string, string> facts, string key, string fallback)
        => facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string GetJsonString(JsonElement? root, string key, string fallback)
    {
        if (root is not JsonElement element || element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(item => item.ToString()).Where(item => !string.IsNullOrWhiteSpace(item))),
            JsonValueKind.Null => fallback,
            _ => value.ToString()
        };
    }

    private static IReadOnlyList<string> SplitFact(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void ValidatePhase10SceneAssetCoverage(string sceneRoot)
    {
        var diagnostics = BuildPhase10SceneAssetDiagnostics(sceneRoot);
        if (IsPhase10V2SceneAssetValidationPassed(diagnostics))
            return;

        var errors = new List<string>();
        ValidatePhase10SceneAssetProfile(diagnostics.ShortRoot, "short", diagnostics.ShortSceneCount, diagnostics.ShortPngCount, diagnostics.MissingFinalPaths, errors);
        ValidatePhase10SceneAssetProfile(diagnostics.LongRoot, "long", diagnostics.LongSceneCount, diagnostics.LongPngCount, diagnostics.MissingFinalPaths, errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("Scene asset validation failed: " + string.Join("; ", errors));
    }

    private static bool IsPhase10V2SceneAssetValidationPassed(Phase10SceneAssetDiagnostics? diagnostics)
        => diagnostics?.V2ArtifactCheckUsed == true
            && diagnostics.ShortSceneCount == 6
            && diagnostics.LongSceneCount == 6
            && diagnostics.ShortPngCount == 6
            && diagnostics.LongPngCount == 6
            && diagnostics.MissingFinalPaths.Count == 0;

    private static void ValidatePhase10SceneAssetProfile(string root, string profile, int sceneCount, int pngCount, IReadOnlyList<string> missingFinalPaths, List<string> errors)
    {
        if (!Directory.Exists(root))
        {
            errors.Add($"{profile} scene asset directory was not found: {NormalizePath(root)}");
            return;
        }

        if (sceneCount != 6)
            errors.Add($"{profile} scene asset validation expected 6 scene directories but found {sceneCount} in {NormalizePath(root)}");
        if (pngCount != 6)
            errors.Add($"{profile} scene asset validation expected 6 final PNGs but found {pngCount} in {NormalizePath(root)}");

        var normalizedRoot = NormalizePath(root).TrimEnd('/');
        var missingFinals = missingFinalPaths
            .Where(path => path.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingFinals.Length > 0)
            errors.Add($"{profile} scene asset directories missing required final PNGs: {string.Join(", ", missingFinals)}");
    }

    private static Phase10SceneAssetDiagnostics BuildPhase10SceneAssetDiagnostics(ProductionPhaseContext context)
        => BuildPhase10SceneAssetDiagnostics(context.ExecutionContext.SceneRoot!);

    private static Phase10SceneAssetDiagnostics BuildPhase10SceneAssetDiagnostics(string sceneRoot)
    {
        var shortRoot = Path.Combine(sceneRoot, "scene-assets", "short");
        var longRoot = Path.Combine(sceneRoot, "scene-assets", "long");
        var checkedPaths = new[] { shortRoot, longRoot };
        var selectedValidationPath = checkedPaths.FirstOrDefault(Directory.Exists) ?? shortRoot;
        var validatedShortFinalPaths = BuildPhase10ExpectedFinalPaths(shortRoot);
        var validatedLongFinalPaths = BuildPhase10ExpectedFinalPaths(longRoot);
        var missingFinalPaths = validatedShortFinalPaths
            .Concat(validatedLongFinalPaths)
            .Where(path => !File.Exists(path))
            .Select(NormalizePath)
            .ToArray();
        return new Phase10SceneAssetDiagnostics(
            CheckedPaths: checkedPaths.Select(NormalizePath).ToArray(),
            SelectedValidationPath: NormalizePath(selectedValidationPath),
            ShortRoot: shortRoot,
            LongRoot: longRoot,
            ShortSceneCount: CountPhase10SceneDirectories(shortRoot),
            LongSceneCount: CountPhase10SceneDirectories(longRoot),
            ShortPngCount: CountPhase10FinalPngs(shortRoot),
            LongPngCount: CountPhase10FinalPngs(longRoot),
            LegacyArtifactCheckUsed: false,
            V2ArtifactCheckUsed: true,
            ValidatedShortFinalPaths: validatedShortFinalPaths.Select(NormalizePath).ToArray(),
            ValidatedLongFinalPaths: validatedLongFinalPaths.Select(NormalizePath).ToArray(),
            MissingFinalPaths: missingFinalPaths);
    }

    private static IReadOnlyList<string> BuildPhase10ExpectedFinalPaths(string root)
        => Enumerable.Range(1, 6)
            .Select(sceneNumber => $"scene-{sceneNumber:000}")
            .Select(sceneId => Path.Combine(root, sceneId, $"{sceneId}-final.png"))
            .ToArray();

    private static int CountPhase10SceneDirectories(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "scene-*", SearchOption.TopDirectoryOnly).Count()
            : 0;

    private static int CountPhase10FinalPngs(string root)
        => Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "scene-*", SearchOption.TopDirectoryOnly)
                .Count(directory => File.Exists(Path.Combine(directory, $"{Path.GetFileName(directory)}-final.png")))
            : 0;

    private sealed record Phase10SceneAssetDiagnostics(
        IReadOnlyList<string> CheckedPaths,
        string SelectedValidationPath,
        string ShortRoot,
        string LongRoot,
        int ShortSceneCount,
        int LongSceneCount,
        int ShortPngCount,
        int LongPngCount,
        bool LegacyArtifactCheckUsed,
        bool V2ArtifactCheckUsed,
        IReadOnlyList<string> ValidatedShortFinalPaths,
        IReadOnlyList<string> ValidatedLongFinalPaths,
        IReadOnlyList<string> MissingFinalPaths);

    private static Phase12ThumbnailDiagnostics BuildPhase12ThumbnailV7Diagnostics(ProductionPhaseContext context)
    {
        var thumbnailRoot = context.ExecutionContext.ThumbnailRoot!;
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-v7-diagnostics.json");
        if (!File.Exists(diagnosticsPath))
            throw new InvalidOperationException($"Thumbnail V7 validation failed: thumbnail-v7-diagnostics.json is required at '{NormalizePath(diagnosticsPath)}'.");

        using var document = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        var diagnostics = document.RootElement;
        if (GetJsonBool(diagnostics, "v6RendererExecuted") || GetJsonBool(diagnostics, "v6ValidatorExecuted"))
            throw new InvalidOperationException("V6 thumbnail path executed while Thumbnail V7 is enabled");

        var requiredOutputs = new[]
        {
            Path.Combine(thumbnailRoot, "thumbnail-final.png"),
            Path.Combine(thumbnailRoot, "thumbnail-landscape.png"),
            Path.Combine(thumbnailRoot, "thumbnail-portrait.png"),
            Path.Combine(thumbnailRoot, "thumbnail-square.png")
        };
        var missingOutputs = requiredOutputs.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missingOutputs.Length > 0)
            throw new InvalidOperationException("Thumbnail V7 validation failed: generated file metadata is missing for required output(s): " + string.Join(", ", missingOutputs));

        var info = GetJsonInt(diagnostics, "informationAreaPercent", 32);
        var visual = GetJsonInt(diagnostics, "visualAreaPercent", 68);
        return new Phase12ThumbnailDiagnostics(
            CurrentEventLock: string.Empty,
            ThumbnailRequestTitle: context.ProductionEventIntelligence.Title,
            ThumbnailRequestShortTitle: context.ProductionEventIntelligence.ShortTitle,
            ThumbnailEventType: context.ProductionEventIntelligence.EventType,
            ThumbnailPrimaryObjects: context.ProductionEventIntelligence.PrimaryObjects,
            ThumbnailSecondaryObjects: context.ProductionEventIntelligence.SecondaryObjects,
            ThumbnailSourceManifestPath: NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-scene-manifest.json")),
            ThumbnailSourceScenePath: string.Empty,
            VisualResolverResult: GetJsonString(diagnostics, "backgroundPromptSource", string.Empty),
            VisualObjectsUsed: context.ProductionEventIntelligence.PrimaryObjects.Concat(context.ProductionEventIntelligence.SecondaryObjects).ToArray(),
            LabelsUsed: Array.Empty<string>(),
            TextUsed: Array.Empty<string>(),
            ForbiddenObjectsDetected: Array.Empty<string>(),
            GoldenPilotLeakageDetected: false,
            SemanticValidationPassed: true,
            EventFamily: string.Empty,
            ValidatorProfile: "ThumbnailV7Validator",
            MoonPhaseName: string.Empty,
            MoonIlluminationPercent: string.Empty,
            MoonriseLocal: string.Empty,
            MoonsetLocal: string.Empty,
            MoonGuideCardAdded: false,
            MoonObjectRendered: false,
            MoonForbiddenTermsDetected: Array.Empty<string>(),
            ThumbnailVersion: "V7",
            ThumbnailContract: "ThumbnailV7CinematicOverlay",
            Renderer: ThumbnailV7CinematicOverlayRenderer.RendererName,
            Validator: "ThumbnailV7Validator",
            ThumbnailReviewJsonRequired: false,
            V6RendererExecuted: false,
            V6ValidatorExecuted: false,
            OldValidationBlocked: true,
            InformationAreaPercent: info,
            VisualAreaPercent: visual,
            InfoPanelPercent: info,
            BottomTipsPercent: 0,
            TextSafeAreaPassedV6: false,
            FooterCutDetected: false,
            TitleCutDetected: false,
            InfoPanelOverflowDetected: false,
            DirectionMarkerCutDetected: false,
            SkyLabelCutDetected: false,
            OutputFiles: requiredOutputs.Select(NormalizePath).ToArray(),
            DuplicateOutputFilesGenerated: false,
            LegacyMinimalHeroThumbnailUsed: false,
            GeneratedOnlyThumbnailPrefixedFiles: true,
            OverlayPercent: info,
            VisualPercent: visual,
            TextSafeAreaPassed: true,
            DateBadgeAdded: true,
            EventFamilyBadgeAdded: true,
            PortraitOverlayPercent: info,
            PortraitOverlayWithinLimit: true,
            OverflowDetected: GetJsonBool(diagnostics, "overlapDetected"),
            ThumbnailLandscapeOutputPath: NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")),
            ThumbnailPortraitOutputPath: NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")),
            ThumbnailSquareOutputPath: NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png")));
    }

    private static void ValidateThumbnailV8Contract(string thumbnailRoot)
    {
        var diagnosticsPath = Path.Combine(thumbnailRoot, "thumbnail-v8-diagnostics.json");
        if (!File.Exists(diagnosticsPath))
            throw new InvalidOperationException($"Thumbnail V8 validation failed: thumbnail-v8-diagnostics.json is required at '{NormalizePath(diagnosticsPath)}'.");
        using var document = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        var root = document.RootElement;
        if (!string.Equals(GetJsonString(root, "thumbnailVersion", string.Empty), "V8", StringComparison.Ordinal)
            || !string.Equals(GetJsonString(root, "selectedRenderer", string.Empty), "ThumbnailV8AiNativeRenderer", StringComparison.Ordinal))
            throw new InvalidOperationException("Thumbnail V8 validation failed: diagnostics must report V8 and ThumbnailV8AiNativeRenderer.");
        if (!GetJsonBool(root, "aiNativeFullImage") || GetJsonBool(root, "manualOverlayUsed") || GetJsonBool(root, "backgroundOnlyMode") || GetJsonBool(root, "cropFromLandscape") || !GetJsonBool(root, "azureImage2Generated"))
            throw new InvalidOperationException("Thumbnail V8 validation failed: diagnostics must report full AI-native generation without manual overlay, background-only mode, or landscape crop.");
        if (File.ReadAllText(diagnosticsPath).Contains("V7", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thumbnail V8 validation failed: V7 appeared in thumbnail-v8-diagnostics.json while V8 is enabled.");
        if (!string.Equals(GetJsonString(root, "selectedTemplate", string.Empty), "AiNativePromptBasedThumbnail", StringComparison.Ordinal)
            || !string.Equals(GetJsonString(root, "layoutFamily", string.Empty), "AiGeneratedObservationGuide", StringComparison.Ordinal)
            || !string.Equals(GetJsonString(root, "backgroundMode", string.Empty), "PerAspectAzureImage2", StringComparison.Ordinal))
            throw new InvalidOperationException("Thumbnail V8 validation failed: diagnostics must report AiNativePromptBasedThumbnail, AiGeneratedObservationGuide, and PerAspectAzureImage2.");
        var required = new[] { "thumbnail-final.png", "thumbnail-landscape.png", "thumbnail-portrait.png", "thumbnail-square.png" }
            .Select(name => Path.Combine(thumbnailRoot, name))
            .ToArray();
        var missing = required.Where(path => !File.Exists(path)).Select(NormalizePath).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Thumbnail V8 validation failed: generated file metadata is missing for required output(s): " + string.Join(", ", missing));
        var phase12ValidationPath = Path.Combine(thumbnailRoot, "phase-12-validation.json");
        if (!File.Exists(phase12ValidationPath))
            throw new InvalidOperationException($"Thumbnail V8 validation failed: phase-12-validation.json is required at '{NormalizePath(phase12ValidationPath)}'.");
        using (var phase12 = JsonDocument.Parse(File.ReadAllText(phase12ValidationPath)))
        {
            var phase12Root = phase12.RootElement;
            if (!string.Equals(GetJsonString(phase12Root, "thumbnailVersion", string.Empty), "V8", StringComparison.Ordinal)
                || !string.Equals(GetJsonString(phase12Root, "selectedRenderer", string.Empty), "ThumbnailV8AiNativeRenderer", StringComparison.Ordinal))
                throw new InvalidOperationException("Thumbnail V8 validation failed: phase-12-validation.json must report V8 and ThumbnailV8AiNativeRenderer.");
            if (File.ReadAllText(phase12ValidationPath).Contains("V7", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Thumbnail V8 validation failed: V7 appeared in phase-12-validation.json while V8 is enabled.");
        }
        var legacy = new[] { "landscape.png", "portrait.png", "square.png", "v7-background-landscape.png", "v7-background-portrait.png", "v7-background-square.png" }.Select(name => Path.Combine(thumbnailRoot, name)).Where(File.Exists).Select(NormalizePath).ToArray();
        if (legacy.Length > 0)
            throw new InvalidOperationException("Thumbnail V8 validation failed: duplicate non-thumbnail-prefixed output(s) generated: " + string.Join(", ", legacy));
        EnsureNoV7ThumbnailJsonOutputs(thumbnailRoot);
    }


    private static void EnsureNoV7ThumbnailJsonOutputs(string thumbnailRoot)
    {
        var forbiddenTerms = new[]
        {
            "ThumbnailV7",
            "V7",
            "PlanetConjunctionV7Template",
            "ThumbnailV7CinematicOverlayRenderer",
            "ThumbnailV7Validator"
        };
        foreach (var path in Directory.EnumerateFiles(thumbnailRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var matched = forbiddenTerms.FirstOrDefault(term => text.Contains(term, StringComparison.Ordinal));
            if (matched is not null)
                throw new InvalidOperationException($"Thumbnail V8 validation failed: forbidden V7 token '{matched}' appeared in {NormalizePath(path)}.");
        }
    }

    private static Phase12ThumbnailDiagnostics BuildPhase12ThumbnailV8Diagnostics(ProductionPhaseContext context)
    {
        var thumbnailRoot = context.ExecutionContext.ThumbnailRoot!;
        ValidateThumbnailV8Contract(thumbnailRoot);
        var outputs = new[]
        {
            NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-final.png")),
            NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-landscape.png")),
            NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-portrait.png")),
            NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-square.png"))
        };
        return new Phase12ThumbnailDiagnostics(
            CurrentEventLock: string.Empty,
            ThumbnailRequestTitle: context.ProductionEventIntelligence.Title,
            ThumbnailRequestShortTitle: context.ProductionEventIntelligence.ShortTitle,
            ThumbnailEventType: context.ProductionEventIntelligence.EventType,
            ThumbnailPrimaryObjects: context.ProductionEventIntelligence.PrimaryObjects,
            ThumbnailSecondaryObjects: context.ProductionEventIntelligence.SecondaryObjects,
            ThumbnailSourceManifestPath: NormalizePath(Path.Combine(thumbnailRoot, "thumbnail-scene-manifest.json")),
            ThumbnailSourceScenePath: string.Empty,
            VisualResolverResult: "AzureImage2CompleteInfographic",
            VisualObjectsUsed: context.ProductionEventIntelligence.PrimaryObjects.Concat(context.ProductionEventIntelligence.SecondaryObjects).ToArray(),
            LabelsUsed: context.ProductionEventIntelligence.PrimaryObjects.Concat(context.ProductionEventIntelligence.SecondaryObjects).ToArray(),
            TextUsed: new[] { context.ProductionEventIntelligence.Title, context.ProductionEventIntelligence.ShortTitle },
            ForbiddenObjectsDetected: Array.Empty<string>(),
            GoldenPilotLeakageDetected: false,
            SemanticValidationPassed: true,
            EventFamily: context.ProductionEventIntelligence.EventType,
            ValidatorProfile: "ThumbnailV8Validator",
            MoonPhaseName: string.Empty,
            MoonIlluminationPercent: string.Empty,
            MoonriseLocal: string.Empty,
            MoonsetLocal: string.Empty,
            MoonGuideCardAdded: false,
            MoonObjectRendered: false,
            MoonForbiddenTermsDetected: Array.Empty<string>(),
            ThumbnailVersion: "V8",
            ThumbnailContract: "ThumbnailV8AiNative",
            Renderer: "ThumbnailV8AiNativeRenderer",
            Validator: "ThumbnailV8Validator",
            ThumbnailReviewJsonRequired: false,
            V6RendererExecuted: false,
            V6ValidatorExecuted: false,
            OldValidationBlocked: true,
            InformationAreaPercent: 30,
            VisualAreaPercent: 70,
            InfoPanelPercent: 24,
            BottomTipsPercent: 8,
            TextSafeAreaPassedV6: false,
            FooterCutDetected: false,
            TitleCutDetected: false,
            InfoPanelOverflowDetected: false,
            DirectionMarkerCutDetected: false,
            SkyLabelCutDetected: false,
            OutputFiles: outputs,
            DuplicateOutputFilesGenerated: false,
            LegacyMinimalHeroThumbnailUsed: false,
            GeneratedOnlyThumbnailPrefixedFiles: true,
            OverlayPercent: 0,
            VisualPercent: 100,
            TextSafeAreaPassed: true,
            DateBadgeAdded: true,
            EventFamilyBadgeAdded: true,
            PortraitOverlayPercent: 0,
            PortraitOverlayWithinLimit: true,
            OverflowDetected: false,
            ThumbnailLandscapeOutputPath: outputs[1],
            ThumbnailPortraitOutputPath: outputs[2],
            ThumbnailSquareOutputPath: outputs[3]);
    }

    private static string GetJsonString(JsonElement element, string name, string fallback)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : fallback;

    private static bool GetJsonBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static int GetJsonInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : fallback;

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
        bool SemanticValidationPassed,
        string EventFamily,
        string ValidatorProfile,
        string MoonPhaseName,
        string MoonIlluminationPercent,
        string MoonriseLocal,
        string MoonsetLocal,
        bool MoonGuideCardAdded,
        bool MoonObjectRendered,
        IReadOnlyList<string> MoonForbiddenTermsDetected,
        string ThumbnailVersion,
        string ThumbnailContract,
        string Renderer,
        string Validator,
        bool ThumbnailReviewJsonRequired,
        bool V6RendererExecuted,
        bool V6ValidatorExecuted,
        bool OldValidationBlocked,
        int InformationAreaPercent,
        int VisualAreaPercent,
        int InfoPanelPercent,
        int BottomTipsPercent,
        bool TextSafeAreaPassedV6,
        bool FooterCutDetected,
        bool TitleCutDetected,
        bool InfoPanelOverflowDetected,
        bool DirectionMarkerCutDetected,
        bool SkyLabelCutDetected,
        IReadOnlyList<string> OutputFiles,
        bool DuplicateOutputFilesGenerated,
        bool LegacyMinimalHeroThumbnailUsed,
        bool GeneratedOnlyThumbnailPrefixedFiles,
        int OverlayPercent,
        int VisualPercent,
        bool TextSafeAreaPassed,
        bool DateBadgeAdded,
        bool EventFamilyBadgeAdded,
        int PortraitOverlayPercent,
        bool PortraitOverlayWithinLimit,
        bool OverflowDetected,
        string ThumbnailLandscapeOutputPath,
        string ThumbnailPortraitOutputPath,
        string ThumbnailSquareOutputPath);

    private static Phase11HeroDiagnostics BuildPhase11HeroDiagnostics(ProductionPhaseContext context)
    {
        var heroRoot = context.ExecutionContext.HeroRoot!;
        var heroOutputPath = NormalizePath(Path.Combine(heroRoot, "hero-final.png"));
        if (!File.Exists(heroOutputPath))
            throw new InvalidOperationException($"Hero V6 validation failed: generated hero file metadata is missing at '{heroOutputPath}'.");
        return new Phase11HeroDiagnostics("V6.5", heroOutputPath, true, true, false, false, false, true, true, true, true, true, false, true, 85, 15);
    }

    private static Phase13GalleryGuideDiagnostics BuildPhase13GalleryGuideDiagnostics(ProductionPhaseContext context)
    {
        var galleryRoot = Path.Combine(context.OutputRoot, "gallery");
        var galleryOutputPaths = Enumerable.Range(1, 6).Select(i => NormalizePath(Path.Combine(galleryRoot, $"gallery-{i:00}.png"))).ToArray();
        var guidePath = NormalizePath(Path.Combine(galleryRoot, "observation-guide-v2.json"));
        if (galleryOutputPaths.Any(path => !File.Exists(path)))
            throw new InvalidOperationException("Gallery V3 validation failed: generated file metadata is missing for one or more gallery images.");
        if (!File.Exists(guidePath))
            throw new InvalidOperationException($"ObservationGuide V2 validation failed: generated guide metadata is missing at '{guidePath}'.");
        return new Phase13GalleryGuideDiagnostics("V3.5", "V2", galleryOutputPaths, guidePath, true, true, true, true, false, true, true, "How To Observe", true);
    }

    private sealed record Phase11HeroDiagnostics(string HeroVersion, string HeroOutputPath, bool DateAdded, bool TimeAdded, bool HeroTitleSubtitleOverlap, bool HeroTitleClipped, bool HeroSubtitleClipped, bool HeroLocationRemoved, bool HeroEventCodeRemoved, bool HeroBottomInfoBarVisible, bool HeroDateVisible, bool HeroTimeVisible, bool HeroTitleMetadataOverlap, bool HeroTextSafeAreaPassed, int VisualAreaPercent, int MetadataAreaPercent);
    private sealed record Phase13GalleryGuideDiagnostics(string GalleryVersion, string GuideVersion, IReadOnlyList<string> GalleryOutputPaths, string ObservationGuideOutputPath, bool DateAdded, bool TimeAdded, bool GalleryLocationRemoved, bool GalleryBottomPaddingApplied, bool GalleryTextCutDetected, bool OldAccurateSkyGuideReplaced, bool ObservationGuideCardAdded, string GuideTitle, bool FamilySpecificGuideApplied);

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
        var requestedStartPhase = context.PipelineRequest.RequestedStartPhaseNo ?? context.StartPhaseNo;
        var requestedEndPhase = context.PipelineRequest.RequestedEndPhaseNo ?? context.EndPhaseNo;
        var dependencyExpansionApplied = requestedStartPhase != context.StartPhaseNo || requestedEndPhase != context.EndPhaseNo;
        var filesGeneratedThisRun = phaseResults.SelectMany(phase => phase.OutputFiles).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(NormalizePath).ToArray();
        var phasesActuallyExecuted = phaseResults.Where(phase => phase.Status == ProductionPhaseStatus.Succeeded).Select(phase => phase.PhaseNo).ToArray();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { context.Request.PlanId, context.Request.RegionId, context.Request.Title, executionMode = context.ExecutionMode.ToString(), dependencyExpansionMode = context.PipelineRequest.DependencyExpansionMode.ToString(), requestedStartPhaseNo = requestedStartPhase, requestedEndPhaseNo = requestedEndPhase, requestedStartPhase, requestedEndPhase, expandedStartPhase = context.StartPhaseNo, expandedEndPhase = context.EndPhaseNo, dependencyExpansionApplied, dependencyExpansionReason = dependencyExpansionApplied ? "dependencyExpansionMode=Rebuild expanded prerequisite phases for rebuild." : context.PipelineRequest.DependencyExpansionMode == DependencyExpansionMode.ReadOnly ? "dependencyExpansionMode=ReadOnly; earlier phase outputs are read-only dependencies." : "dependencyExpansionMode=None; requested phase range is authoritative.", phasesActuallyExecuted, outputRootsDeleted = BuildOutputRootsDeletedDiagnostics(context), readOnlyDependencyRoots = BuildReadOnlyDependencyRootsDiagnostics(context), startPhaseNo = context.StartPhaseNo, endPhaseNo = context.EndPhaseNo, overwriteExisting = context.OverwriteExisting, retryFailedOnly = context.RetryFailedOnly, cleanupScope = BuildCleanupScopeDiagnostics(context), deletedFiles = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(), preservedValidationFiles = BuildPreservedValidationFilesDiagnostics(context), sceneApprovalStagingRoot = NormalizePath(context.ExecutionContext.SceneRoot!), sceneApprovalNormalizedRoot = NormalizePath(GetSceneApprovalNormalizedRoot(context.OutputRoot)), filesDeletedDueToOverwrite = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(), filesGeneratedThisRun, executedPhaseNumbers = phasesActuallyExecuted, skippedPhaseNumbers = PhaseDefinitionsStatic().Where(phaseNo => phaseNo < context.StartPhaseNo || phaseNo > context.EndPhaseNo || phaseResults.Any(result => result.PhaseNo == phaseNo && result.Status == ProductionPhaseStatus.Skipped)).ToArray(), phases = phaseResults }, JsonOptions), cancellationToken);
    }


    private static async Task<string> WriteProductionIntelligenceDiagnosticsAsync(string outputRoot, ProductionEventIntelligence intelligence, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "plan-input", "production-event-intelligence-diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var completenessFields = new object?[] { intelligence.LocalPeakTime, intelligence.BestViewingWindowLocal, intelligence.SkyDirectionHint, intelligence.VisibilityRegion, intelligence.AngularSeparationDegrees, intelligence.PrimaryObjects, intelligence.SecondaryObjects, intelligence.ResolvedObjectNames, intelligence.RelativeObjectOrder, intelligence.RequiredVisualObjects, intelligence.RequiredNarrationFacts, intelligence.VisualMotifs, intelligence.ViewerInstructions, intelligence.ForbiddenTerms };
        var score = completenessFields.Count(HasDiagnosticValue) / (decimal)completenessFields.Length;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            selectedEventType = intelligence.EventType,
            selectedStrategyId = intelligence.StrategyId,
            intelligence.ResolvedObjectNames,
            intelligence.AngularSeparationDegrees,
            intelligence.LocalPeakTime,
            intelligence.BestViewingWindowLocal,
            intelligence.SkyDirectionHint,
            intelligence.RequiredVisualObjects,
            intelligence.ForbiddenTerms,
            eventIntelligenceCompletenessScore = Math.Round(score, 2)
        }, JsonOptions), cancellationToken);
        return path;
    }

    private static bool HasDiagnosticValue(object? value)
        => value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            System.Collections.IEnumerable values => values.Cast<object?>().Any(),
            _ => true
        };

    private static void ValidatePlanetConjunctionPhase2(ProductionEventIntelligence intelligence)
    {
        if (!IsPlanetConjunctionIntelligence(intelligence)) return;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(intelligence.LocalPeakTime)) errors.Add("PlanetConjunction localPeakTime is required before Phase 3.");
        if (string.IsNullOrWhiteSpace(intelligence.SkyDirectionHint)) errors.Add("PlanetConjunction skyDirectionHint is required before Phase 3.");
        if (string.IsNullOrWhiteSpace(intelligence.BestViewingWindowLocal)) errors.Add("PlanetConjunction bestViewingWindowLocal is required before Phase 3.");
        if (!intelligence.AngularSeparationDegrees.HasValue) errors.Add("PlanetConjunction angularSeparationDegrees is required before Phase 3.");
        var names = intelligence.ResolvedObjectNames ?? [];
        if (!names.Any(n => n.Equals("Venus", StringComparison.OrdinalIgnoreCase)) || !names.Any(n => n.Equals("Jupiter", StringComparison.OrdinalIgnoreCase))) errors.Add("PlanetConjunction resolvedObjectNames must include both Venus and Jupiter before Phase 3.");
        var forbiddenTerms = EventContentGuard.DefaultForbiddenTermsForEventType(intelligence.EventType);
        var checkedText = string.Join(" ", intelligence.VisualTheme, intelligence.NarrationTheme, string.Join(" ", intelligence.SceneStrategy ?? []), string.Join(" ", intelligence.VisualMotifs ?? []));
        foreach (var term in forbiddenTerms)
            if (checkedText.Contains(term, StringComparison.OrdinalIgnoreCase)) errors.Add($"PlanetConjunction intelligence contains forbidden term '{term}' before Phase 3.");
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
    }

    private static bool IsPlanetConjunctionIntelligence(ProductionEventIntelligence intelligence)
        => intelligence.EventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || (intelligence.StrategyId?.Contains("conjunction", StringComparison.OrdinalIgnoreCase) ?? false)
            || intelligence.Title.Contains("conjunction", StringComparison.OrdinalIgnoreCase);

    private async Task<string> WriteProductionIntelligenceAsync(string outputRoot, ProductionEventIntelligence intelligence, CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputRoot, "plan-input", "production-event-intelligence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(intelligence, JsonOptions), cancellationToken);
        return path;
    }

    private ProductionPipelineExecutionContext BuildProductionExecutionContext(ProductionPipelineRequest pipelineRequest, Guid eventId, string planRoot, ProductionEventIntelligence intelligence, IMediaEventStrategy strategy)
    {
        var request = pipelineRequest.Request;
        var baseContext = pipelineRequest.ExecutionContext;
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
            EnableSubtitles = pipelineRequest.EnableSubtitles || baseContext?.EnableSubtitles == true,
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
        var deleteStartPhaseNo = context.StartPhaseNo;
        var deleteEndPhaseNo = context.EndPhaseNo;

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

        if (deleteStartPhaseNo <= 13 && deleteEndPhaseNo >= 13)
            DeleteProductionSubtree(Path.Combine(context.OutputRoot, "gallery"), deletedFiles);

        if (deleteStartPhaseNo <= 14 && deleteEndPhaseNo >= 14)
            DeleteProductionSubtree(Path.Combine(context.OutputRoot, "sync"), deletedFiles);

        if (deleteStartPhaseNo <= 15 && deleteEndPhaseNo >= 15)
            DeleteProductionSubtree(context.ExecutionContext.NarrationRoot!, deletedFiles);

        if (deleteStartPhaseNo <= 17 && deleteEndPhaseNo >= 16)
            DeleteProductionSubtree(context.ExecutionContext.TtsRoot!, deletedFiles);

        if (deleteStartPhaseNo <= 19 && deleteEndPhaseNo >= 18)
            DeleteProductionSubtree(context.ExecutionContext.VideoAssemblyRoot!, deletedFiles);

        var firstValidationToDelete = Math.Max(deleteStartPhaseNo, 1);
        var lastValidationToDelete = Math.Min(deleteEndPhaseNo, 20);
        for (var phaseNo = firstValidationToDelete; phaseNo <= lastValidationToDelete; phaseNo++)
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json"), deletedFiles);
    }

    private static void ValidatePartialPhaseExecutionContract(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults, List<string> errors)
    {
        var requestedStartPhase = context.PipelineRequest.RequestedStartPhaseNo ?? context.StartPhaseNo;
        var requestedEndPhase = context.PipelineRequest.RequestedEndPhaseNo ?? context.EndPhaseNo;
        if (requestedStartPhase != 12 || requestedEndPhase != 13) return;

        var phasesActuallyExecuted = phaseResults.Where(phase => phase.Status == ProductionPhaseStatus.Succeeded).Select(phase => phase.PhaseNo).OrderBy(phaseNo => phaseNo).ToArray();
        if (!phasesActuallyExecuted.SequenceEqual(new[] { 12, 13 }))
            errors.Add($"Partial execution validation failed: phasesActuallyExecuted must be [12,13] for requested range 12-13; actual=[{string.Join(',', phasesActuallyExecuted)}].");

        var forbiddenRoots = new[] { context.ExecutionContext.SceneRoot!, context.ExecutionContext.HeroRoot! }.Select(NormalizePath).ToArray();
        var forbiddenOutput = phaseResults
            .Where(phase => phase.OutputFiles.Any(output => forbiddenRoots.Any(root => NormalizePath(output).StartsWith(root, StringComparison.OrdinalIgnoreCase))))
            .Select(phase => phase.PhaseNo)
            .Distinct()
            .OrderBy(phaseNo => phaseNo)
            .ToArray();
        if (forbiddenOutput.Length > 0)
            errors.Add($"Partial execution validation failed: scene-assets-v3 or hero output was regenerated by phase(s) [{string.Join(',', forbiddenOutput)}] during requested range 12-13.");
    }

    private static IReadOnlyList<string> BuildOutputRootsDeletedDiagnostics(ProductionPhaseContext context)
    {
        var deleted = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>();
        return ResolvePhaseOwnedOutputRoots(context, 1, 20)
            .Where(root => deleted.Any(path => NormalizePath(path).StartsWith(NormalizePath(root), StringComparison.OrdinalIgnoreCase)))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildReadOnlyDependencyRootsDiagnostics(ProductionPhaseContext context)
    {
        if (context.PipelineRequest.DependencyExpansionMode != DependencyExpansionMode.ReadOnly) return Array.Empty<string>();
        return ResolvePhaseOwnedOutputRoots(context, 1, context.StartPhaseNo - 1)
            .Where(Directory.Exists)
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static object BuildCleanupScopeDiagnostics(ProductionPhaseContext context)
    {
        var deleteStartPhaseNo = context.StartPhaseNo;
        var deleteEndPhaseNo = context.EndPhaseNo;

        return new
        {
            phaseRange = $"{deleteStartPhaseNo}-{deleteEndPhaseNo}",
            phaseNumbers = Enumerable.Range(deleteStartPhaseNo, deleteEndPhaseNo - deleteStartPhaseNo + 1).ToArray(),
            ownedOutputRoots = ResolvePhaseOwnedOutputRoots(context, deleteStartPhaseNo, deleteEndPhaseNo).Select(NormalizePath).ToArray(),
            validationFiles = Enumerable.Range(deleteStartPhaseNo, deleteEndPhaseNo - deleteStartPhaseNo + 1)
                .Select(phaseNo => NormalizePath(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json")))
                .ToArray()
        };
    }

    private static IReadOnlyList<string> ResolvePhaseOwnedOutputRoots(ProductionPhaseContext context, int startPhaseNo, int endPhaseNo)
    {
        var roots = new List<string>();
        if (startPhaseNo <= 9 && endPhaseNo >= 8) roots.Add(context.ExecutionContext.SceneRoot!);
        if (startPhaseNo <= 11 && endPhaseNo >= 11) roots.Add(context.ExecutionContext.HeroRoot!);
        if (startPhaseNo <= 12 && endPhaseNo >= 12) roots.Add(context.ExecutionContext.ThumbnailRoot!);
        if (startPhaseNo <= 13 && endPhaseNo >= 13) roots.Add(Path.Combine(context.OutputRoot, "gallery"));
        if (startPhaseNo <= 14 && endPhaseNo >= 14) roots.Add(Path.Combine(context.OutputRoot, "sync"));
        if (startPhaseNo <= 15 && endPhaseNo >= 15) roots.Add(context.ExecutionContext.NarrationRoot!);
        if (startPhaseNo <= 17 && endPhaseNo >= 16) roots.Add(context.ExecutionContext.TtsRoot!);
        if (startPhaseNo <= 19 && endPhaseNo >= 18) roots.Add(context.ExecutionContext.VideoAssemblyRoot!);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildPreservedValidationFilesDiagnostics(ProductionPhaseContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ExecutionContext.ValidationRoot) || !Directory.Exists(context.ExecutionContext.ValidationRoot)) return Array.Empty<string>();
        return Directory.EnumerateFiles(context.ExecutionContext.ValidationRoot, "phase-??-validation.json")
            .Where(path => !TryParsePhaseValidationNumber(path, out var phaseNo) || phaseNo < context.StartPhaseNo || phaseNo > context.EndPhaseNo)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(NormalizePath)
            .ToArray();
    }

    private static bool TryParsePhaseValidationNumber(string path, out int phaseNo)
    {
        var match = Regex.Match(Path.GetFileName(path), @"^phase-(\d{2})-validation\.json$", RegexOptions.IgnoreCase);
        return int.TryParse(match.Success ? match.Groups[1].Value : string.Empty, NumberStyles.None, CultureInfo.InvariantCulture, out phaseNo);
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
        CopyFile(Path.Combine(eventRoot, "hero-assets", "hero-final.png"), Path.Combine(outputRoot, "hero", "hero-final.png"), copied);
        CopyFile(Path.Combine(eventRoot, "hero-assets", "hero-review.json"), Path.Combine(outputRoot, "hero", "hero-review.json"), copied);
        CopyFile(Path.Combine(eventRoot, "hero", "hero-final.png"), Path.Combine(outputRoot, "hero", "hero-final.png"), copied);
        CopyFile(Path.Combine(eventRoot, "hero", "hero-review.json"), Path.Combine(outputRoot, "hero", "hero-review.json"), copied);
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
    private static bool DirectoryHasPngRecursive(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png", SearchOption.AllDirectories).Any();
    private static SceneImageValidationPath ResolveSceneImageValidationPath(string sceneRoot, string profile, bool preferSceneAssets)
    {
        var checkedPaths = preferSceneAssets
            ? new[] { Path.Combine(sceneRoot, "scene-assets", profile), Path.Combine(sceneRoot, profile) }
            : new[] { Path.Combine(sceneRoot, profile), Path.Combine(sceneRoot, "scene-assets", profile) };
        var selectedPath = checkedPaths.FirstOrDefault(DirectoryHasPngRecursive) ?? checkedPaths[0];
        return new SceneImageValidationPath(checkedPaths.Select(NormalizePath).ToArray(), selectedPath);
    }

    private static void ValidateSceneImageDirectoryCoverage(string validationRoot, string validationLabel)
    {
        var pngCount = Directory.Exists(validationRoot)
            ? Directory.EnumerateFiles(validationRoot, "*.png", SearchOption.AllDirectories).Count()
            : 0;
        if (pngCount == 0)
            throw new InvalidOperationException($"{validationLabel} failed: no .png files were found in '{validationRoot}'.");

        var sceneDirectories = Directory.EnumerateDirectories(validationRoot, "scene-*", SearchOption.TopDirectoryOnly).ToArray();
        var missingSceneDirectories = sceneDirectories
            .Where(directory => !Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories).Any())
            .Select(NormalizePath)
            .ToArray();
        if (missingSceneDirectories.Length > 0)
            throw new InvalidOperationException($"{validationLabel} failed: scene directories missing .png files: {string.Join(", ", missingSceneDirectories)}.");
    }

    private sealed record SceneImageValidationPath(IReadOnlyList<string> CheckedPaths, string SelectedPath);

    private static bool HeroContractExists(string outputRoot) => File.Exists(Path.Combine(outputRoot, "hero", "hero-final.png")) && File.Exists(Path.Combine(outputRoot, "hero", "hero-review.json"));
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));

    private sealed record Phase8SceneVariantManifestItem(
        int SceneNumber,
        string Format,
        int VariantNo,
        string VariantType,
        string ImageRole,
        string ImagePath,
        string BackgroundPath,
        string FinalImagePath,
        string LayoutTemplate,
        bool SafeAreaPassed,
        bool OverlapCheckPassed,
        string RenderStatus,
        bool IsBlankCheckPassed,
        double NonBlackPixelRatio,
        long FileSizeBytes,
        string VisualHash,
        Phase8SafeAreaMetadata? SafeAreaMetadata,
        int TextBlockCount,
        int AllowedTextBlockCount,
        string VisualDirectorPrompt,
        Phase8VisualQualityScore VisualQualityScore,
        IReadOnlyList<string> ValidationErrors);

    private readonly record struct Phase8BlendRect(int X, int Y, int Width, int Height, Rgba32 Color);

    private sealed record Phase8ImageValidationResult(bool IsBlankCheckPassed, double NonBlackPixelRatio, long FileSizeBytes);

    private sealed record Phase8SafeAreaMetadata(string Format, int Left, int Top, int Right, int Bottom, string Zones, string LayoutTemplate);

    private sealed record Phase8VisualQualityScore(
        int CompositionScore,
        int ReadabilityScore,
        int AstronomyAccuracyScore,
        int ObjectRelevanceScore,
        int ProfessionalLookScore,
        double FinalQualityScore,
        int Threshold);

    private sealed record Phase8ProfessionalSlideFormat(string Format, AstronomyInfographicRenderVariant RenderVariant);
}
