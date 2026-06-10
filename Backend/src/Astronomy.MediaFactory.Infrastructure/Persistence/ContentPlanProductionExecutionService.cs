using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class ContentPlanProductionExecutionService(
    MediaFactoryDbContext db,
    IContentPlanProductionRequestMapper mapper,
    IProductionPipelineExecutionService productionPipeline,
    IOptions<RenderingOptions> renderingOptions,
    ILogger<ContentPlanProductionExecutionService> logger) : IContentPlanProductionExecutionService
{
    private static readonly Guid GeminidsPlanId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ProductionSteps =
    [
        "Build Production Pipeline Request",
        "Question Engine",
        "Scene Engine short scenes",
        "Scene Engine long scenes",
        "Hero Engine",
        "Thumbnail Engine",
        "Narration Engine short narration",
        "Narration Engine long narration",
        "TTS Engine short audio",
        "TTS Engine long audio",
        "Video Assembly Engine short final video",
        "Video Assembly Engine long final video"
    ];

    public Task<ContentPlanProductionExecutionResult> ExecuteContentPlanAsync(Guid contentGenerationPlanId, bool dryRun, bool overwriteExisting, CancellationToken cancellationToken)
        => ExecuteContentPlanWithProductionPipelineAsync(new ContentPlanProductionExecutionRequest(contentGenerationPlanId, dryRun, overwriteExisting), cancellationToken);

    public async Task<ContentPlanProductionExecutionResult> ExecuteContentPlanWithProductionPipelineAsync(ContentPlanProductionExecutionRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentGenerationPlanId != GeminidsPlanId)
            throw new ArgumentException("Phase 10A.3 production execution is locked to the Geminids plan only.", nameof(request));

        var plan = await db.ContentGenerationPlans
            .Include(p => p.AstronomyEventIntelligence)!.ThenInclude(e => e!.Objects)
            .FirstOrDefaultAsync(p => p.Id == request.ContentGenerationPlanId, cancellationToken)
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.ContentGenerationPlanId}' was not found.", nameof(request));

        var intelligence = plan.AstronomyEventIntelligence
            ?? throw new ArgumentException($"ContentGenerationPlan '{request.ContentGenerationPlanId}' is not linked to AstronomyEventIntelligence.", nameof(request));

        var productionRequest = mapper.Map(plan, intelligence);
        var outputRoot = BuildPlanOutputRoot(productionRequest);
        var warnings = new List<string>(productionRequest.Warnings);
        var errors = new List<string>();
        var generatedFiles = new List<string>();

        if (request.DryRun)
        {
            return BuildResult(true, true, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors);
        }

        ContentPipelineExecution? execution = null;
        try
        {
            Directory.CreateDirectory(outputRoot);
            await WritePlanInputAsync(outputRoot, plan, intelligence, productionRequest, cancellationToken);

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
                OverwriteExisting: request.OverwriteExisting), cancellationToken);
            generatedFiles.AddRange(pipelineResult.GeneratedFiles);
            warnings.AddRange(pipelineResult.Warnings);
            errors.AddRange(pipelineResult.Errors);

            var shortVideo = Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4");
            var longVideo = Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4");
            var shortOk = File.Exists(shortVideo);
            var longOk = File.Exists(longVideo);
            execution.Status = errors.Count == 0 ? "Completed" : "Failed";
            execution.FinishedUtc = DateTimeOffset.UtcNow;
            execution.ErrorMessage = errors.Count == 0 ? null : string.Join("; ", errors);
            execution.OutputFolder = outputRoot;
            execution.ShortVideoPath = shortOk ? shortVideo : null;
            execution.LongVideoPath = longOk ? longVideo : null;
            execution.ThumbnailLongPath = Path.Combine(outputRoot, "thumbnails", "landscape.png");
            execution.ThumbnailShortPath = Path.Combine(outputRoot, "thumbnails", "portrait.png");
            plan.FinalVideoPath = longOk ? longVideo : shortVideo;
            plan.ThumbnailPath = Path.Combine(outputRoot, "thumbnails", "landscape.png");
            plan.PlanStatus = errors.Count == 0 ? "ProductionCompleted" : "ProductionFailed";
            plan.Status = plan.PlanStatus;
            plan.CompletedUtc = errors.Count == 0 ? DateTimeOffset.UtcNow : null;
            plan.FailureReason = errors.Count == 0 ? null : execution.ErrorMessage;
            await db.SaveChangesAsync(cancellationToken);

            return BuildResult(errors.Count == 0, false, plan, productionRequest, outputRoot, true, DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long")), File.Exists(Path.Combine(outputRoot, "hero", "hero.png")), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.wav")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.wav")), shortOk, longOk, shortOk ? shortVideo : string.Empty, longOk ? longVideo : string.Empty, generatedFiles, warnings, errors);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "DB-plan production pipeline execution failed for plan {PlanId}", request.ContentGenerationPlanId);
            errors.Add(ex.Message);
            if (execution is not null)
            {
                execution.Status = "Failed";
                execution.FinishedUtc = DateTimeOffset.UtcNow;
                execution.ErrorMessage = ex.Message;
                plan.PlanStatus = "ProductionFailed";
                plan.Status = "ProductionFailed";
                plan.FailureReason = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
            }
            return BuildResult(false, false, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors);
        }
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

    private ContentPlanProductionExecutionResult BuildResult(bool success, bool dryRun, ContentGenerationPlan plan, ContentPlanProductionPipelineRequest productionRequest, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool heroGenerated, bool thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool shortVideoGenerated, bool longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        => new(success, dryRun, true, false, 1, plan.Id, plan.Title ?? string.Empty, outputRoot, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, productionRequest, ProductionSteps, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    private string BuildPlanOutputRoot(ContentPlanProductionPipelineRequest request)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "plans", Sanitize(request.RegionId), (request.ScheduledUtc?.Year ?? request.PeakUtc?.Year ?? DateTimeOffset.UtcNow.Year).ToString(), request.PlanId.ToString("D"));

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
