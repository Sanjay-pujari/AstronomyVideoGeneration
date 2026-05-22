using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class CategoryProductionRunner(IEnumerable<ICategoryProductionPipelineStrategy> strategies) : ICategoryProductionRunner
{
    public Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentCategoryCode))
            return Task.FromResult(Failed(request.ContentCategoryCode, "contentCategoryCode is required."));

        var strategy = strategies.FirstOrDefault(s => s.ContentCategoryCode.Equals(request.ContentCategoryCode, StringComparison.OrdinalIgnoreCase));
        if (strategy is null)
            return Task.FromResult(Failed(request.ContentCategoryCode, $"Unsupported content category '{request.ContentCategoryCode}'. Only DailySkyGuide is currently supported."));

        var safeRequest = request with { PublishToYouTube = false, PublishToFacebook = false, PublishToInstagram = false };
        return strategy.RunAsync(safeRequest, cancellationToken);
    }

    private static CategoryProductionPreviewResponse Failed(string? category, string message) =>
        new(null, category ?? string.Empty, false, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null, [], [], message, null);
}

public sealed class DailySkyGuideProductionPipelineStrategy(
    IContentPlanningService planning,
    PipelineOrchestrator orchestrator,
    IPipelineRepository repository,
    IProductionPreviewOutputValidator outputValidator,
    IOptions<SchedulerOptions> schedulerOptions,
    ILogger<DailySkyGuideProductionPipelineStrategy> logger) : ICategoryProductionPipelineStrategy
{
    public string ContentCategoryCode => "DailySkyGuide";

    public async Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<CategoryProductionStepResult>();
        var warnings = new List<string>
        {
            "Publishing is disabled by policy for category production preview.",
            "Publishing and analytics disabled for category production preview."
        };
        logger.LogInformation("Publishing and analytics disabled for category production preview.");
        ContentGenerationPlan? plan = null;
        RunPipelineRequest? runRequest = null;
        PipelineRun? run = null;

        steps.Add(await ExecuteStepAsync("BuildRunPipelineRequest", async () =>
        {
            plan = await planning.GenerateDailyPlanAsync(request.ContentCategoryCode, request.Language, request.RegionId, new DateTimeOffset(request.ScheduledUtc, TimeSpan.Zero), request.PrimaryCelestialObjectCode, cancellationToken);
            var scheduledDate = DateOnly.FromDateTime(request.ScheduledUtc.Date);
            var resolvedRegionId = ResolveRegionId(request, plan);
            var region = schedulerOptions.Value.Regions.Items.FirstOrDefault(x => string.Equals(x.RegionId, resolvedRegionId, StringComparison.OrdinalIgnoreCase));

            double? latitude = region?.Latitude;
            double? longitude = region?.Longitude;
            var timezone = region?.Timezone ?? "Asia/Kolkata";
            var locationName = region?.DisplayName?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? (!string.IsNullOrWhiteSpace(request.RegionName) ? request.RegionName : resolvedRegionId);

            if (region is null && resolvedRegionId.Equals("IN-RJ-UDAIPUR", StringComparison.OrdinalIgnoreCase))
            {
                latitude = 24.5854;
                longitude = 73.7125;
                timezone = "Asia/Kolkata";
                locationName = "Udaipur";
            }
            else if (latitude is null || longitude is null)
            {
                warnings.Add("Latitude/longitude could not be resolved for region.");
            }

            runRequest = new RunPipelineRequest(
                scheduledDate,
                ContentType.DailySkyGuide,
                locationName,
                TimeZone: timezone,
                PublishToYouTube: false,
                UseTopicPlanner: false,
                Latitude: latitude,
                Longitude: longitude,
                RegionId: resolvedRegionId,
                Language: request.Language);
            return ("RunPipelineRequest built.", (string?)null, Array.Empty<string>());
        }));

        if (steps.Any(s => s.Status == "Failed") || runRequest is null || plan is null)
            return Build(false, request.ContentCategoryCode, plan?.Id, steps, warnings, null, "Unable to build pipeline request.");

        steps.Add(await ExecuteStepAsync("ExecuteProductionPipelineParity", async () =>
        {
            run = await orchestrator.RunAsync(runRequest, cancellationToken);
            return ("Pipeline execution completed.", (run.Status is PipelineRunStatus.Succeeded or PipelineRunStatus.SuccessWithWarnings) ? null : run.FailureReason ?? "Pipeline did not complete successfully.", Array.Empty<string>());
        }, allowBusinessFailure: true));

        if (run is null)
            return Build(false, request.ContentCategoryCode, plan.Id, steps, warnings, null, "Pipeline execution did not start.");

        var stageSteps = await BuildStepResultsFromStagesAsync(run.Id, cancellationToken);
        steps.AddRange(stageSteps);
        steps.Add(new CategoryProductionStepResult("PublishingSkipped", "Skipped", DateTime.UtcNow, DateTime.UtcNow, 0, "Publishing disabled by policy.", null, []));

        var artifacts = ResolveArtifacts(run.OutputFolder);
        var validation = await outputValidator.ValidateAsync(run.OutputFolder, artifacts.LongAudioPath, artifacts.LongVideoPath, artifacts.ShortVideoPath, artifacts.LongThumbnailPath, artifacts.ShortThumbnailPath, cancellationToken);
        warnings.AddRange(validation.Errors);
        var metadata = ResolveMetadataObject(artifacts.MetadataPath);
        var success = (run.Status is PipelineRunStatus.Succeeded or PipelineRunStatus.SuccessWithWarnings) && validation.IsValid;
        var diagnostics = BuildDiagnostics(artifacts, validation.ValidationReportPath);
        var summary = BuildExecutionSummary(steps);

        return Build(success, request.ContentCategoryCode, plan.Id, steps, warnings, artifacts, success ? null : run.FailureReason ?? "One or more production steps failed.", metadata, runRequest, diagnostics, summary);
    }


    private static string ResolveRegionId(CategoryProductionPreviewRequest request, ContentGenerationPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.RegionId))
            return plan.RegionId;

        if (!string.IsNullOrWhiteSpace(request.RegionId))
            return request.RegionId;

        return "IN-RJ-UDAIPUR";
    }

    private async Task<IReadOnlyList<CategoryProductionStepResult>> BuildStepResultsFromStagesAsync(Guid pipelineRunId, CancellationToken ct)
    {
        var stages = await repository.GetStageExecutionsAsync(pipelineRunId, ct);
        return stages.OrderBy(s => s.StartedAt).Select(s => new CategoryProductionStepResult(
            s.StageName,
            NormalizeStatus(s.Status),
            s.StartedAt.UtcDateTime,
            (s.FinishedAt ?? s.StartedAt).UtcDateTime,
            s.DurationMs ?? 0,
            s.OutputPath,
            s.ErrorMessage,
            [])).ToList();
    }

    private static string NormalizeStatus(string status)
    {
        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (status.Equals("Skipped", StringComparison.OrdinalIgnoreCase)) return "Skipped";
        return "Completed";
    }
    private static object? ResolveMetadataObject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(path)); }
        catch { return new { path }; }
    }

    private static (string? LongAudioPath, string? ShortAudioPath, IReadOnlyList<string>? ShortAudioSegments, string? ShortAudioManifestPath, string? LongVideoPath, string? ShortVideoPath, string? ShortVideoDiagnosticsPath, string? LongThumbnailPath, string? ShortThumbnailPath, string? MetadataPath, string? RenderManifestPath, string? NarrationContextPath, string? SeoMetadataPath, string? ObservationWindowPath, string? SkyfieldResponsePath) ResolveArtifacts(string? outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder)) return (null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var files = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories);
        string? pick(params string[] terms) => files.FirstOrDefault(f => terms.All(t => f.Contains(t, StringComparison.OrdinalIgnoreCase)));
        var shortPlayableAudio = files.FirstOrDefault(f => f.Contains("short", StringComparison.OrdinalIgnoreCase) && (f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)));
        var shortManifest = pick("shorts", "audio-concat-list.txt");
        var shortSegments = files.Where(f => f.Contains("shorts", StringComparison.OrdinalIgnoreCase) && f.Contains("audio", StringComparison.OrdinalIgnoreCase) && (f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))).OrderBy(f => f).ToList();
        var shortVideo = pick("shorts", "final-short", ".mp4") ?? pick("shorts", "short-video", ".mp4") ?? pick("short", ".mp4");
        return (
            pick("narration", ".mp3") ?? pick("long", "audio"),
            shortPlayableAudio,
            shortSegments.Count > 1 ? shortSegments : null,
            shortManifest,
            pick("final", ".mp4") ?? pick("long", "video"),
            shortVideo,
            pick("short-video-diagnostics.json") ?? pick("final-render-diagnostics.json"),
            pick("thumbnail", "long"),
            pick("thumbnail", "short"),
            pick("metadata", ".json"),
            pick("render-manifest", ".json"),
            pick("narration-context", ".json"),
            pick("seo", "metadata", ".json"),
            pick("observation-window", ".json"),
            pick("skyfield", ".json"));
    }

    private static CategoryProductionPreviewResponse Build(bool success, string category, Guid? planId, IReadOnlyList<CategoryProductionStepResult> steps, IReadOnlyList<string> warnings, (string? LongAudioPath, string? ShortAudioPath, IReadOnlyList<string>? ShortAudioSegments, string? ShortAudioManifestPath, string? LongVideoPath, string? ShortVideoPath, string? ShortVideoDiagnosticsPath, string? LongThumbnailPath, string? ShortThumbnailPath, string? MetadataPath, string? RenderManifestPath, string? NarrationContextPath, string? SeoMetadataPath, string? ObservationWindowPath, string? SkyfieldResponsePath)? artifacts, string? error, object? metadata=null, RunPipelineRequest? runPipelineRequest = null, CategoryProductionPreviewDiagnostics? diagnostics = null, CategoryProductionExecutionSummary? summary = null)
    {
        return new(planId, category, success, false, false, false, false, false, artifacts?.LongAudioPath, artifacts?.ShortAudioPath, artifacts?.LongVideoPath, artifacts?.ShortVideoPath, artifacts?.LongThumbnailPath, artifacts?.ShortThumbnailPath, artifacts?.ShortAudioSegments, diagnostics, summary, metadata, steps, warnings, error, runPipelineRequest);
    }
    private static CategoryProductionPreviewDiagnostics BuildDiagnostics((string? LongAudioPath, string? ShortAudioPath, IReadOnlyList<string>? ShortAudioSegments, string? ShortAudioManifestPath, string? LongVideoPath, string? ShortVideoPath, string? ShortVideoDiagnosticsPath, string? LongThumbnailPath, string? ShortThumbnailPath, string? MetadataPath, string? RenderManifestPath, string? NarrationContextPath, string? SeoMetadataPath, string? ObservationWindowPath, string? SkyfieldResponsePath) artifacts, string? validationReportPath)
        => new(artifacts.ShortAudioManifestPath, artifacts.ShortVideoDiagnosticsPath, artifacts.RenderManifestPath, artifacts.NarrationContextPath, artifacts.SeoMetadataPath, validationReportPath, artifacts.ObservationWindowPath, artifacts.SkyfieldResponsePath);

    private static CategoryProductionExecutionSummary BuildExecutionSummary(IReadOnlyCollection<CategoryProductionStepResult> steps)
    {
        var completed = steps.Count(x => string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var failed = steps.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var skipped = steps.Count(x => string.Equals(x.Status, "Skipped", StringComparison.OrdinalIgnoreCase));
        var duration = steps.Sum(x => x.DurationMs) / 1000d;
        return new(duration, completed > 0, completed > 0, completed > 0, completed > 0, false, false, completed, failed, skipped);
    }

    private async Task<CategoryProductionStepResult> ExecuteStepAsync(string name, Func<Task<(string Message, string? Error, IReadOnlyCollection<string> Warnings)>> action, bool allowBusinessFailure = false)
    {
        var started = DateTime.UtcNow;
        try
        {
            var (message, error, warnings) = await action();
            var finished = DateTime.UtcNow;
            return new(name, error is null ? "Completed" : "Failed", started, finished, (long)(finished - started).TotalMilliseconds, message, error, warnings.ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Category production step {Step} failed", name);
            var finished = DateTime.UtcNow;
            return new(name, "Failed", started, finished, (long)(finished - started).TotalMilliseconds, $"{name} failed.", ex.Message, []);
        }
    }
}
