using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] RebuildOutputEntries =
    [
        "question-engine",
        "scene-approval-v3",
        "hero",
        "thumbnails",
        "narration",
        "tts",
        "video-assembly",
        "validation",
        "phase-manifest.json"
    ];

    private static readonly string[] ProductionSteps =
    [
        "Question Engine",
        "Scene Engine Short",
        "Scene Engine Long",
        "Hero Engine",
        "Thumbnail Engine",
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
        var isCompletedPlanRerun = IsProductionCompleted(plan) && executionMode is ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.FullRebuild;
        var startPhaseNo = ResolveStartPhaseNo(request);
        var endPhaseNo = request.EndPhaseNo ?? 19;
        var executionContext = BuildExecutionContext(plan, intelligence, productionRequest);
        var outputRoot = BuildPlanOutputRoot(productionRequest);
        logger.LogInformation("Using Astronomy V1 production pipeline for content plan {PlanId}", plan.Id);
        var warnings = new List<string>(productionRequest.Warnings);
        var errors = new List<string>();
        var generatedFiles = new List<string>();

        if (request.DryRun)
        {
            return BuildResult(true, true, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, [], executionMode, isCompletedPlanRerun, false, null, [], startPhaseNo, endPhaseNo);
        }

        ContentPipelineExecution? execution = null;
        try
        {
            var outputPreparation = PrepareOutputRoot(outputRoot, request, startPhaseNo);
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
                OverwriteExisting: request.OverwriteExisting,
                ExecutionContext: executionContext,
                StartPhaseNo: startPhaseNo,
                EndPhaseNo: endPhaseNo,
                RetryFailedOnly: request.RetryFailedOnly,
                ExecutionMode: executionMode), cancellationToken);
            generatedFiles.AddRange(pipelineResult.GeneratedFiles);
            warnings.AddRange(pipelineResult.Warnings);
            errors.AddRange(pipelineResult.Errors);

            var shortVideo = Path.Combine(outputRoot, "video-assembly", "short", "final-video-short.mp4");
            var longVideo = Path.Combine(outputRoot, "video-assembly", "long", "final-video-long.mp4");
            var shortOk = File.Exists(shortVideo);
            var longOk = File.Exists(longVideo);
            var phase19Succeeded = PhaseSucceeded(pipelineResult.PhaseResults, 19);
            var phaseFailed = pipelineResult.PhaseResults?.Any(p => p.Status == ProductionPhaseStatus.Failed) == true;
            var productionFailed = errors.Count > 0 || !pipelineResult.Success || phaseFailed;
            var productionCompleted = !productionFailed && phase19Succeeded;
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

            return BuildResult(productionCompleted, false, plan, productionRequest, outputRoot, true, DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "short")), DirectoryHasPng(Path.Combine(outputRoot, "scene-approval-v3", "long")), File.Exists(Path.Combine(outputRoot, "hero", "hero.png")), ThumbnailsExist(outputRoot), File.Exists(Path.Combine(outputRoot, "narration", "short", "narration.txt")), File.Exists(Path.Combine(outputRoot, "narration", "long", "narration.txt")), File.Exists(Path.Combine(outputRoot, "tts", "short", "narration.mp3")), File.Exists(Path.Combine(outputRoot, "tts", "long", "narration.mp3")), shortOk, longOk, shortOk ? shortVideo : string.Empty, longOk ? longVideo : string.Empty, generatedFiles, warnings, errors, pipelineResult.PhaseResults ?? [], executionMode, isCompletedPlanRerun, outputPreparation.PreviousOutputArchived, outputPreparation.ArchivePath, outputPreparation.DeletedOutputFolders, startPhaseNo, endPhaseNo, pipelineResult.RequestedOutputCompletion);
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
            return BuildResult(false, false, plan, productionRequest, outputRoot, false, false, false, false, false, false, false, false, false, false, false, string.Empty, string.Empty, generatedFiles, warnings, errors, [], executionMode, isCompletedPlanRerun, false, null, [], startPhaseNo, endPhaseNo);
        }
    }


    private static ProductionPipelineExecutionContext BuildExecutionContext(ContentGenerationPlan plan, AstronomyEventIntelligence intelligence, ContentPlanProductionPipelineRequest productionRequest)
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
            PlannedFormat: productionRequest.PlannedFormat);

    private static bool PhaseSucceeded(IReadOnlyList<ProductionPhaseResult>? phaseResults, int phaseNo)
        => phaseResults?.Any(p => p.PhaseNo == phaseNo && p.Status == ProductionPhaseStatus.Succeeded) == true;

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

    private ContentPlanProductionExecutionResult BuildResult(bool success, bool dryRun, ContentGenerationPlan plan, ContentPlanProductionPipelineRequest productionRequest, string outputRoot, bool questionEngineCompleted, bool shortScenesGenerated, bool longScenesGenerated, bool heroGenerated, bool thumbnailsGenerated, bool shortNarrationGenerated, bool longNarrationGenerated, bool shortTtsGenerated, bool longTtsGenerated, bool shortVideoGenerated, bool longVideoGenerated, string finalShortVideoPath, string finalLongVideoPath, IReadOnlyList<string> generatedFiles, IReadOnlyList<string> warnings, IReadOnlyList<string> errors, IReadOnlyList<ProductionPhaseResult> phaseResults, ContentPlanExecutionMode executionMode, bool completedPlanRerun, bool previousOutputArchived, string? archivePath, IReadOnlyList<string> deletedOutputFolders, int startPhaseNo, int endPhaseNo, IReadOnlyList<RequestedOutputCompletion>? requestedOutputCompletion = null)
    {
        var lastCompletedPhaseNo = phaseResults
            .Where(p => p.Status is ProductionPhaseStatus.Succeeded or ProductionPhaseStatus.Skipped)
            .OrderByDescending(p => p.PhaseNo)
            .Select(p => (int?)p.PhaseNo)
            .FirstOrDefault();
        var lastFailedPhaseNo = phaseResults
            .Where(p => p.Status == ProductionPhaseStatus.Failed)
            .OrderByDescending(p => p.PhaseNo)
            .Select(p => (int?)p.PhaseNo)
            .FirstOrDefault();

        return new(success, dryRun, true, false, 1, plan.Id, plan.Title ?? string.Empty, outputRoot, questionEngineCompleted, shortScenesGenerated, longScenesGenerated, heroGenerated, thumbnailsGenerated, shortNarrationGenerated, longNarrationGenerated, shortTtsGenerated, longTtsGenerated, shortVideoGenerated, longVideoGenerated, finalShortVideoPath, finalLongVideoPath, productionRequest, ProductionSteps, generatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), phaseResults, lastCompletedPhaseNo, lastFailedPhaseNo, executionMode, completedPlanRerun, previousOutputArchived, archivePath, deletedOutputFolders, startPhaseNo, endPhaseNo, requestedOutputCompletion);
    }


    private static bool IsProductionCompleted(ContentGenerationPlan plan)
        => string.Equals(plan.Status, "ProductionCompleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.PlanStatus, "ProductionCompleted", StringComparison.OrdinalIgnoreCase);

    private static int ResolveStartPhaseNo(ContentPlanProductionExecutionRequest request)
    {
        if (request.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs && request.RebuildIntelligence) return 1;
        return request.StartPhaseNo ?? (request.ExecutionMode == ContentPlanExecutionMode.FullRebuild ? 1 : request.ExecutionMode == ContentPlanExecutionMode.RebuildOutputs ? 3 : 1);
    }

    private static OutputPreparationResult PrepareOutputRoot(string outputRoot, ContentPlanProductionExecutionRequest request, int startPhaseNo)
    {
        if (request.ExecutionMode is not (ContentPlanExecutionMode.RebuildOutputs or ContentPlanExecutionMode.FullRebuild))
            return new(false, null, []);

        if (Directory.Exists(outputRoot) && !request.OverwriteExisting)
            throw new InvalidOperationException("Completed plan rerun output cleanup requires overwriteExisting=true.");

        if (!Directory.Exists(outputRoot))
            return new(false, null, []);

        if (request.ArchivePreviousRun)
        {
            var archivePath = ArchivePreviousRun(outputRoot, request.ExecutionMode, request.RebuildIntelligence);
            return new(true, archivePath, []);
        }

        var deleted = request.ExecutionMode == ContentPlanExecutionMode.FullRebuild && startPhaseNo <= 1
            ? DeleteFullPlanOutput(outputRoot)
            : DeleteRebuildOutputs(outputRoot, request.RebuildIntelligence);
        return new(false, null, deleted);
    }

    private static string ArchivePreviousRun(string outputRoot, ContentPlanExecutionMode executionMode, bool rebuildIntelligence)
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

        foreach (var entry in RebuildOutputEntries)
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

    private static IReadOnlyList<string> DeleteRebuildOutputs(string outputRoot, bool rebuildIntelligence)
    {
        var deleted = new List<string>();
        foreach (var entry in RebuildOutputEntries)
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

    private sealed record OutputPreparationResult(bool PreviousOutputArchived, string? ArchivePath, IReadOnlyList<string> DeletedOutputFolders);

    private string BuildPlanOutputRoot(ContentPlanProductionPipelineRequest request)
        => Path.Combine(ResolveWorkingDirectoryRoot(), "plans", Sanitize(request.RegionId), (request.ScheduledUtc?.Year ?? request.PeakUtc?.Year ?? DateTimeOffset.UtcNow.Year).ToString(), request.PlanId.ToString("D"));

    private string ResolveWorkingDirectoryRoot() => string.IsNullOrWhiteSpace(renderingOptions.Value.WorkingDirectory) ? "./media-output" : renderingOptions.Value.WorkingDirectory;
    private static string Sanitize(string value) => string.Join("-", (value ?? "unknown").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static bool DirectoryHasPng(string path) => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.png").Any();
    private static bool ThumbnailsExist(string outputRoot) => File.Exists(Path.Combine(outputRoot, "thumbnails", "landscape.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "square.png")) && File.Exists(Path.Combine(outputRoot, "thumbnails", "portrait.png"));
}
