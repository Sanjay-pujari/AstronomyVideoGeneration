using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ProductionPipelineExecutionService(
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
    ILogger<ProductionPipelineExecutionService> logger) : IProductionPipelineExecutionService, IProductionPhaseRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
        => RunAsync(request, cancellationToken);

    public async Task<ProductionPipelineExecutionResult> RunAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);

        var productionRequest = request.Request;
        var eventId = request.AstronomyEventIntelligenceId.ToString("D");
        var warnings = new List<string>(productionRequest.Warnings);
        var errors = new List<string>();
        var generatedFiles = new List<string>();
        var outputRoot = request.OutputRoot;
        var productionIntelligence = intelligenceAdapter.Normalize(request);
        var strategy = strategyResolver.Resolve(productionIntelligence.EventType, productionIntelligence.Title);
        var executionContext = BuildProductionExecutionContext(request.ExecutionContext, productionRequest, request.AstronomyEventIntelligenceId, outputRoot, productionIntelligence, strategy);
        var startPhaseNo = Math.Clamp(request.StartPhaseNo ?? 1, 1, 19);
        var endPhaseNo = Math.Clamp(request.EndPhaseNo ?? 19, startPhaseNo, 19);
        var phaseResults = new List<ProductionPhaseResult>();
        var deletedFilesDueToOverwrite = new List<string>();

        if (request.OverwriteExisting && startPhaseNo <= 1)
            ClearProductionOutputRoot(outputRoot);

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(executionContext.ValidationRoot!);

        var context = new ProductionPhaseContext(request, productionRequest, request.AstronomyEventIntelligenceId, eventId, outputRoot, executionContext, productionIntelligence, strategy, request.DryRun, request.OverwriteExisting, startPhaseNo, endPhaseNo, request.RetryFailedOnly, deletedFilesDueToOverwrite);
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
            if (request.RetryFailedOnly && PreviousPhaseSucceeded(context, phase.No))
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
        var success = errors.Count == 0 && (!phaseResults.Any() || phaseResults.All(p => p.Status is ProductionPhaseStatus.Succeeded or ProductionPhaseStatus.Skipped));
        return BuildResult(success, false, outputRoot, File.Exists(Path.Combine(outputRoot, "question-engine", "question-answer-set.json")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long")), File.Exists(Path.Combine(outputRoot, "hero", "hero.png")) || File.Exists(Path.Combine(outputRoot, "hero", "hero-landscape.png")), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "short", "video-narration-script.json")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "long", "video-long-narration-script.json")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.mp3")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "short", "video-tts-audio.mp3")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.mp3")) || File.Exists(Path.Combine(outputRoot, "video-assembly", "long", "video-long-tts-audio.mp3")), File.Exists(shortVideo), File.Exists(longVideo), File.Exists(shortVideo) ? shortVideo : string.Empty, File.Exists(longVideo) ? longVideo : string.Empty, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), phaseResults);
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

    private async Task<ProductionPhaseResult> ExecutePhaseAsync(ProductionPhaseContext context, int phaseNo, string phaseName, Func<ProductionPhaseContext, CancellationToken, Task<IReadOnlyList<string>>> action, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var outputs = (await action(context, cancellationToken)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = outputs.Where(p => !File.Exists(p) && !Directory.Exists(p)).Select(p => $"Expected output was not found: {p}").ToArray();
            return await WritePhaseValidationAsync(context, phaseNo, phaseName, missing.Length == 0 ? ProductionPhaseStatus.Succeeded : ProductionPhaseStatus.Failed, [], outputs, [], missing, missing.Length == 0 ? "Validation passed." : "Validation failed: required output missing.", missing.Length > 0, cancellationToken, started);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return await WritePhaseValidationAsync(context, phaseNo, phaseName, ProductionPhaseStatus.Failed, [], [], [], [ex.Message], ex.Message, true, cancellationToken, started);
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
        var response = await sceneIntentEnricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(context.EventId, context.Request.RegionId, context.Request.Language, DryRun: false, OverwriteExisting: context.OverwriteExisting, ProductionContext: context.ExecutionContext), cancellationToken);
        return response.GeneratedFiles;
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateNarrationPlanAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await narrationGenerator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, context.ExecutionContext), cancellationToken);
        var outputs = new List<string>(response.GeneratedFiles);
        Directory.CreateDirectory(context.ExecutionContext.SceneRoot!);
        CopyFile(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json"), Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration.json"), outputs);
        CopyFile(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json"), Path.Combine(context.ExecutionContext.SceneRoot!, "question-driven-narration-review.json"), outputs);
        return outputs;
    }

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
        var validation = await qualityValidator.ValidateBeforeVideoAssemblyAsync(context.ProductionEventIntelligence, context.OutputRoot, cancellationToken);
        if (!validation.IsValid) throw new InvalidOperationException("Scene asset validation failed: " + string.Join("; ", validation.Errors));
        var materialized = await MaterializeSceneApprovalAsync(context.ExecutionContext.SceneRoot!, GetSceneApprovalNormalizedRoot(context.OutputRoot), cancellationToken);
        return materialized.Concat([Path.Combine(context.ExecutionContext.SceneRoot!, "short"), Path.Combine(context.ExecutionContext.SceneRoot!, "long")]).ToArray();
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateHeroAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await heroEngine.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(context.EventId, context.Request.RegionId, context.Request.Language, false, context.OverwriteExisting, HeroAssetGenerationPhase.Full, context.ExecutionContext), cancellationToken);
        return response.GeneratedFiles;
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateThumbnailsAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        var response = await thumbnailEngine.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest { EventId = context.EventId, RegionId = context.Request.RegionId, Language = context.Request.Language, Phase = "Images", DryRun = false, OverwriteExisting = context.OverwriteExisting, ThumbnailStyle = "ScrollStopping", ThumbnailVisualStyle = "PhotoCinematic", ProductionContext = context.ExecutionContext }, cancellationToken);
        return response.GeneratedFiles;
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateVideoNarrationAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        var intelligence = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Intelligence" : "LongFormIntelligence"), cancellationToken);
        outputs.AddRange(intelligence.GeneratedFiles);
        var script = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Script" : "LongFormScript"), cancellationToken);
        outputs.AddRange(script.GeneratedFiles);
        var scriptPath = profile == ScenePresentationProfile.ShortForm ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-narration-script.json") : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-narration-script.json");
        var target = Path.Combine(context.ExecutionContext.NarrationRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long", "narration.txt");
        CopyFile(scriptPath, target, outputs, jsonNarrationToText: true);
        return outputs;
    }

    private async Task<IReadOnlyList<string>> PhaseGenerateTtsAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var response = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, profile == ScenePresentationProfile.ShortForm ? "Tts" : "LongFormTts"), cancellationToken);
        var outputs = new List<string>(response.GeneratedFiles);
        var source = profile == ScenePresentationProfile.ShortForm ? Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "short", "video-tts-audio.mp3") : Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "long", "video-long-tts-audio.mp3");
        var target = Path.Combine(context.ExecutionContext.TtsRoot!, profile == ScenePresentationProfile.ShortForm ? "short" : "long", "narration.mp3");
        CopyFile(source, target, outputs);
        return outputs;
    }

    private async Task<IReadOnlyList<string>> PhaseAssembleVideoAsync(ProductionPhaseContext context, ScenePresentationProfile profile, CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        foreach (var phase in new[] { profile == ScenePresentationProfile.ShortForm ? "Assembly" : "LongFormAssembly", profile == ScenePresentationProfile.ShortForm ? "Render" : "LongFormRender" })
        {
            var response = await videoAssemblyEngine.GenerateVideoAssemblyAsync(BuildVideoRequest(context, profile, phase), cancellationToken);
            outputs.AddRange(response.GeneratedFiles);
        }
        return outputs;
    }

    private async Task<IReadOnlyList<string>> PhaseFinalValidationAsync(ProductionPhaseContext context, CancellationToken cancellationToken)
    {
        await WriteScenesManifestsAsync(context.OutputRoot, cancellationToken);
        var copied = await MaterializePlanFolderAsync(context.Request, context.EventId, context.OutputRoot, [], cancellationToken);
        var validation = await qualityValidator.ValidateFinalOutputAsync(context.ProductionEventIntelligence, context.OutputRoot, cancellationToken);
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
            AllowSyntheticSilentTts = true,
            ShortForm = new VideoAssemblyFormRequest { Enabled = profile == ScenePresentationProfile.ShortForm, Platform = "YouTubeShort", ScenePresentationProfile = ScenePresentationProfile.ShortForm, TargetDurationSeconds = 60, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 12, DuckMusicUnderNarration = true },
            LongForm = new VideoAssemblyFormRequest { Enabled = profile == ScenePresentationProfile.LongForm, Platform = "YouTubeLong", ScenePresentationProfile = ScenePresentationProfile.LongForm, TargetDurationSeconds = 360, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 10, DuckMusicUnderNarration = true },
            ScenePresentationProfile = profile,
            BackgroundMusic = true,
            MusicMood = "WonderCuriosity",
            MusicLevelPercent = profile == ScenePresentationProfile.ShortForm ? 12 : 10,
            DuckMusicUnderNarration = true,
            ProductionContext = context.ExecutionContext
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

    private static IEnumerable<string> BuildForbiddenTermsForStrategy(ProductionPhaseContext context)
    {
        var intelligence = context.ProductionEventIntelligence;
        var terms = new List<string>();
        terms.AddRange(intelligence.ForbiddenTerms);
        terms.AddRange(intelligence.ForbiddenObjectNames ?? []);
        if (IsGeminidsOrMeteorStrategy(context))
            terms.AddRange(["Venus", "Jupiter", "conjunction", "look west", "after sunset", "7:23 PM IST"]);
        return terms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsGeminidsOrMeteorStrategy(ProductionPhaseContext context)
        => (context.ProductionEventIntelligence.Title ?? string.Empty).Contains("Geminid", StringComparison.OrdinalIgnoreCase)
            || (context.ProductionEventIntelligence.EventType ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || (context.Request.EventType ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || (context.MediaEventStrategy.EventType ?? string.Empty).Contains("meteor", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle)) return false;
        var escaped = Regex.Escape(needle.Trim());
        escaped = Regex.Replace(escaped, @"\s+", @"\s+");
        return Regex.IsMatch(haystack, $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    private async Task<ProductionPhaseResult> WritePhaseValidationAsync(ProductionPhaseContext context, int phaseNo, string phaseName, ProductionPhaseStatus status, IReadOnlyList<string> inputFiles, IReadOnlyList<string> outputFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, string reason, bool canRetry, CancellationToken cancellationToken, DateTimeOffset? startedUtc = null)
    {
        var started = startedUtc ?? DateTimeOffset.UtcNow;
        var finished = DateTimeOffset.UtcNow;
        var validationPath = Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(validationPath)!);
        var result = new ProductionPhaseResult(phaseNo, phaseName, status, started, finished, (long)(finished - started).TotalMilliseconds, inputFiles, outputFiles, validationPath, warnings, errors, canRetry);
        await File.WriteAllTextAsync(validationPath, JsonSerializer.Serialize(new { phaseNo, phaseName, status = status.ToString(), startedUtc = started, finishedUtc = finished, durationMs = result.DurationMs, sceneApprovalStagingRoot = NormalizePath(context.ExecutionContext.SceneRoot!), sceneApprovalNormalizedRoot = NormalizePath(GetSceneApprovalNormalizedRoot(context.OutputRoot)), filesDeletedDueToOverwrite = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(), filesGeneratedThisRun = outputFiles, inputFiles, outputFiles, warnings, errors, reason, canRetry }, JsonOptions), cancellationToken);
        return result;
    }

    private static async Task WritePhaseManifestAsync(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults, CancellationToken cancellationToken)
    {
        var path = Path.Combine(context.OutputRoot, "phase-manifest.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { context.Request.PlanId, context.Request.RegionId, context.Request.Title, startPhaseNo = context.StartPhaseNo, endPhaseNo = context.EndPhaseNo, overwriteExisting = context.OverwriteExisting, retryFailedOnly = context.RetryFailedOnly, sceneApprovalStagingRoot = NormalizePath(context.ExecutionContext.SceneRoot!), sceneApprovalNormalizedRoot = NormalizePath(GetSceneApprovalNormalizedRoot(context.OutputRoot)), filesDeletedDueToOverwrite = context.DeletedFilesDueToOverwrite ?? Array.Empty<string>(), filesGeneratedThisRun = phaseResults.SelectMany(phase => phase.OutputFiles).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(NormalizePath).ToArray(), phases = phaseResults }, JsonOptions), cancellationToken);
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
            if (string.Equals(Path.GetFileName(directory), "plan-input", StringComparison.OrdinalIgnoreCase)) continue;
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
        if (context.StartPhaseNo <= 7 && context.EndPhaseNo >= 7)
        {
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration.json"), deletedFiles);
            DeleteFileIfExists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-narration-review.json"), deletedFiles);
        }

        if (context.StartPhaseNo <= 10 && context.EndPhaseNo >= 7)
        {
            DeleteProductionSubtree(context.ExecutionContext.SceneRoot!, deletedFiles);
            DeleteProductionSubtree(Path.Combine(context.ExecutionContext.QuestionRoot!, "scene-approval-v3"), deletedFiles);
            DeleteProductionSubtree(GetSceneApprovalNormalizedRoot(context.OutputRoot), deletedFiles);
        }

        var firstValidationToDelete = Math.Max(context.StartPhaseNo, 7);
        var lastValidationToDelete = Math.Min(context.EndPhaseNo, 10);
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

    private static ProductionPipelineExecutionResult BuildResult(bool success, bool dryRun, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool heroGenerated, bool thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool shortVideoGenerated, bool longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<ProductionPhaseResult>? phaseResults = null)
        => new(success, dryRun, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), phaseResults);

    private static string GetSceneApprovalNormalizedRoot(string outputRoot) => Path.Combine(outputRoot, "scene-approval-v3");
    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
