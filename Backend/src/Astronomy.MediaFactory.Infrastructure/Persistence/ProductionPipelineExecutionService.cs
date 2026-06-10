using System.Text.Json;
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
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ProductionPipelineExecutionService> logger) : IProductionPipelineExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ProductionPipelineExecutionResult> ExecuteAsync(ProductionPipelineRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);

        var productionRequest = request.Request;
        var eventId = request.AstronomyEventIntelligenceId.ToString("D");
        var warnings = new List<string>();
        var errors = new List<string>();
        var generatedFiles = new List<string>();
        var outputRoot = request.OutputRoot;

        if (request.DryRun)
        {
            return BuildResult(true, true, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors);
        }

        try
        {
            var questionResponse = await questionEngine.GenerateQuestionAnswersAsync(new QuestionAnswerGenerationRequest(
                productionRequest.RegionId,
                PlanIds: [productionRequest.PlanId.ToString("D")],
                MaxEvents: 1,
                Language: productionRequest.Language,
                DryRun: false,
                OverwriteExisting: request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(questionResponse.GeneratedFiles);
            warnings.AddRange(questionResponse.Warnings);

            var scenePlanResponse = await scenePlanner.GenerateQuestionScenePlanAsync(new QuestionScenePlanRequest(productionRequest.RegionId, eventId, productionRequest.Language, false, request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(scenePlanResponse.GeneratedFiles);
            warnings.AddRange(scenePlanResponse.Warnings);

            var enrichmentResponse = await sceneIntentEnricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(eventId, productionRequest.RegionId, productionRequest.Language, DryRun: false, OverwriteExisting: request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(enrichmentResponse.GeneratedFiles);
            warnings.AddRange(enrichmentResponse.Warnings);

            var narrationResponse = await narrationGenerator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(eventId, productionRequest.RegionId, productionRequest.Language, false, request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(narrationResponse.GeneratedFiles);
            warnings.AddRange(narrationResponse.Warnings);

            var sceneResponse = await sceneEngine.GenerateEditorialAstronomyInfographicsAsync(new QuestionDrivenVisualGenerationRequest(eventId, productionRequest.RegionId, productionRequest.Language, false, request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(sceneResponse.GeneratedFiles);
            warnings.AddRange(sceneResponse.Warnings);

            var heroResponse = await heroEngine.GenerateHeroAssetsAsync(new HeroAssetStoryGenerationRequest(eventId, productionRequest.RegionId, productionRequest.Language, false, request.OverwriteExisting, HeroAssetGenerationPhase.Full), cancellationToken);
            generatedFiles.AddRange(heroResponse.GeneratedFiles);
            warnings.AddRange(heroResponse.Warnings);

            var thumbnailResponse = await thumbnailEngine.GenerateThumbnailAssetsAsync(new ThumbnailAssetGenerationRequest
            {
                EventId = eventId,
                RegionId = productionRequest.RegionId,
                Language = productionRequest.Language,
                Phase = "Images",
                DryRun = false,
                OverwriteExisting = request.OverwriteExisting,
                ThumbnailStyle = "ScrollStopping",
                ThumbnailVisualStyle = "PhotoCinematic"
            }, cancellationToken);
            generatedFiles.AddRange(thumbnailResponse.GeneratedFiles);
            if (thumbnailResponse.Warnings is not null) warnings.AddRange(thumbnailResponse.Warnings);

            var assemblyResponse = await videoAssemblyEngine.GenerateVideoAssemblyAsync(new VideoAssemblyGenerationRequest
            {
                EventId = eventId,
                RegionId = productionRequest.RegionId,
                Language = productionRequest.Language,
                Platform = "YouTubeShort",
                Phase = "FullPipeline",
                DryRun = false,
                OverwriteExisting = request.OverwriteExisting,
                OutputMode = "Production",
                ShortForm = new VideoAssemblyFormRequest { Enabled = true, Platform = "YouTubeShort", ScenePresentationProfile = ScenePresentationProfile.ShortForm, TargetDurationSeconds = 60, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 12, DuckMusicUnderNarration = true },
                LongForm = new VideoAssemblyFormRequest { Enabled = true, Platform = "YouTubeLong", ScenePresentationProfile = ScenePresentationProfile.LongForm, TargetDurationSeconds = 360, BackgroundMusic = true, MusicMood = "WonderCuriosity", MusicLevelPercent = 10, DuckMusicUnderNarration = true }
            }, cancellationToken);
            generatedFiles.AddRange(assemblyResponse.GeneratedFiles);

            var copied = await MaterializePlanFolderAsync(productionRequest, eventId, outputRoot, generatedFiles, cancellationToken);
            generatedFiles.AddRange(copied);

            var shortVideo = Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4");
            var longVideo = Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4");
            var shortOk = File.Exists(shortVideo);
            var longOk = File.Exists(longVideo);
            if (!shortOk) errors.Add("Short final video was not generated in the DB-plan production folder.");
            if (!longOk) errors.Add("Long final video was not generated in the DB-plan production folder.");

            return BuildResult(errors.Count == 0, false, outputRoot, true, DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long")), File.Exists(Path.Combine(outputRoot, "hero", "hero.png")), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.wav")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.wav")), shortOk, longOk, shortOk ? shortVideo : string.Empty, longOk ? longVideo : string.Empty, generatedFiles, warnings, errors);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Astronomy V1 production pipeline execution failed for plan {PlanId}", productionRequest.PlanId);
            errors.Add(ex.Message);
            return BuildResult(false, false, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors);
        }
    }

    private async Task<IReadOnlyList<string>> MaterializePlanFolderAsync(ContentPlanProductionPipelineRequest request, string eventId, string outputRoot, IReadOnlyList<string> generatedFiles, CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        var eventRoot = Path.Combine(ResolveWorkingDirectoryRoot(), "assets", Sanitize(request.RegionId), "events", eventId);
        CopyFile(Path.Combine(eventRoot, "question-engine", "question-answer-set.json"), Path.Combine(outputRoot, "question-engine", "questions.json"), copied);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "short"), Path.Combine(outputRoot, "scene-approval-v3", "short"), copied, renameFinalScenes: true);
        CopyDirectoryFiles(Path.Combine(eventRoot, "question-engine", "scene-approval-v3", "long"), Path.Combine(outputRoot, "scene-approval-v3", "long"), copied, renameFinalScenes: true);
        CopyFile(Path.Combine(eventRoot, "hero-assets", "hero-landscape.png"), Path.Combine(outputRoot, "hero", "hero.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-landscape.png"), Path.Combine(outputRoot, "thumbnails", "landscape.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-square.png"), Path.Combine(outputRoot, "thumbnails", "square.png"), copied);
        CopyFile(Path.Combine(eventRoot, "thumbnail-assets", "thumbnail-portrait.png"), Path.Combine(outputRoot, "thumbnails", "portrait.png"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "video-narration-script.json"), Path.Combine(outputRoot, "narration", "short", "narration.txt"), copied, jsonNarrationToText: true);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "video-long-narration-script.json"), Path.Combine(outputRoot, "narration", "long", "narration.txt"), copied, jsonNarrationToText: true);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "short", "video-tts-audio.mp3"), Path.Combine(outputRoot, "tts", "short", "narration.wav"), copied);
        CopyFile(Path.Combine(eventRoot, "video-assembly", "long", "video-long-tts-audio.mp3"), Path.Combine(outputRoot, "tts", "long", "narration.wav"), copied);
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

    private static ProductionPipelineExecutionResult BuildResult(bool success, bool dryRun, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool heroGenerated, bool thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool shortVideoGenerated, bool longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        => new(success, dryRun, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
