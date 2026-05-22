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
        new(null, category ?? string.Empty, false, false, false, false, false, false, null, null, null, null, null, null, null, [], [], message, null);
}

public sealed class DailySkyGuideProductionPipelineStrategy(
    IContentPlanningService planning,
    PipelineOrchestrator orchestrator,
    IPipelineRepository repository,
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
            var region = schedulerOptions.Value.Regions.Items.FirstOrDefault(x => string.Equals(x.RegionId, request.RegionId, StringComparison.OrdinalIgnoreCase));
            var locationName = region?.DisplayName?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? (!string.IsNullOrWhiteSpace(request.RegionName) ? request.RegionName : request.RegionId);
            runRequest = new RunPipelineRequest(
                scheduledDate,
                ContentType.DailySkyGuide,
                locationName,
                TimeZone: region?.Timezone ?? "Asia/Kolkata",
                PublishToYouTube: false,
                UseTopicPlanner: false,
                Latitude: region?.Latitude,
                Longitude: region?.Longitude,
                RegionId: request.RegionId,
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
        var metadata = ResolveMetadataObject(artifacts.MetadataPath);
        var success = (run.Status is PipelineRunStatus.Succeeded or PipelineRunStatus.SuccessWithWarnings) && File.Exists(artifacts.LongAudioPath ?? string.Empty);

        return Build(success, request.ContentCategoryCode, plan.Id, steps, warnings, artifacts, success ? null : run.FailureReason ?? "One or more production steps failed.", metadata, runRequest);
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

    private static string NormalizeStatus(string status) => status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "Completed" : status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? "Failed" : "Skipped";
    private static object? ResolveMetadataObject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(path)); }
        catch { return new { path }; }
    }

    private static (string? LongAudioPath, string? ShortAudioPath, string? LongVideoPath, string? ShortVideoPath, string? LongThumbnailPath, string? ShortThumbnailPath, string? MetadataPath) ResolveArtifacts(string? outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder)) return (null, null, null, null, null, null, null);
        var files = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories);
        string? pick(params string[] terms) => files.FirstOrDefault(f => terms.All(t => f.Contains(t, StringComparison.OrdinalIgnoreCase)));
        return (pick("narration", ".mp3") ?? pick("long", "audio"), pick("short", "audio"), pick("final", ".mp4") ?? pick("long", "video"), pick("short", "final") ?? pick("short", "video"), pick("thumbnail", "long"), pick("thumbnail", "short"), pick("metadata", ".json"));
    }

    private static CategoryProductionPreviewResponse Build(bool success, string category, Guid? planId, IReadOnlyList<CategoryProductionStepResult> steps, IReadOnlyList<string> warnings, (string? LongAudioPath, string? ShortAudioPath, string? LongVideoPath, string? ShortVideoPath, string? LongThumbnailPath, string? ShortThumbnailPath, string? MetadataPath)? artifacts, string? error, object? metadata=null, RunPipelineRequest? runPipelineRequest = null)
    {
        return new(planId, category, success, false, false, false, false, false, artifacts?.LongAudioPath, artifacts?.ShortAudioPath, artifacts?.LongVideoPath, artifacts?.ShortVideoPath, artifacts?.LongThumbnailPath, artifacts?.ShortThumbnailPath, metadata, steps, warnings, error, runPipelineRequest);
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
