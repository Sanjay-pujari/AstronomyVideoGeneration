using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddMediaFactorySecureConfiguration(builder.Environment);

var telemetryOptions = new TelemetryOptions();
builder.Configuration.GetSection(TelemetryOptions.SectionName).Bind(telemetryOptions);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
if (!string.IsNullOrWhiteSpace(telemetryOptions.ApplicationInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

const string DevelopmentCorsPolicy = "DevelopmentCorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevelopmentCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
                builder.Environment.IsDevelopment()
                && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("127.0.0.1", StringComparison.Ordinal)))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddMediaFactory(builder.Configuration);

var app = builder.Build();

app.Logger.LogInformation("Starting Astronomy.MediaFactory.Api in {Environment}", app.Environment.EnvironmentName);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(DevelopmentCorsPolicy);

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { service = "Astronomy.MediaFactory.Api", status = "ok" }));
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (ctx, _) => await ctx.Response.WriteAsJsonAsync(new { status = "live" })
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (ctx, report) =>
    {
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
        };

        await ctx.Response.WriteAsJsonAsync(payload);
    }
});

app.MapPost("/api/assets/celestial/extract-pack", async (ICelestialAssetPackExtractor extractor, CancellationToken ct) =>
{
    var report = await extractor.ExtractAsync(ct);
    return Results.Ok(report);
});

app.MapGet("/api/events/upcoming", async (int? days, string? regionId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var upcoming = await events.GetUpcomingAsync(days, ct);
    return Results.Ok(string.IsNullOrWhiteSpace(regionId) ? upcoming : upcoming.Where(e => e.GlobalVisibility || e.RegionId == regionId || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase))).ToArray());
});
app.MapPost("/api/alerts/subscribe", async (AlertSubscribeRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        var created = await alerts.SubscribeAsync(request, ct);
        return Results.Created($"/api/alerts/preferences/{created.SubscriberId}", created);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/alerts/preferences/{subscriberId:guid}", async (Guid subscriberId, ISkyAlertService alerts, CancellationToken ct) =>
{
    var item = await alerts.GetPreferencesAsync(subscriberId, ct);
    return item is null ? Results.NotFound(new { message = "Alert subscriber was not found." }) : Results.Ok(item);
});
app.MapPut("/api/alerts/preferences/{subscriberId:guid}", async (Guid subscriberId, AlertPreferenceUpdateRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        var item = await alerts.UpdatePreferencesAsync(subscriberId, request, ct);
        return item is null ? Results.NotFound(new { message = "Alert subscriber was not found." }) : Results.Ok(item);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/alerts/upcoming", async (string? regionId, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await alerts.GetUpcomingAsync(regionId, ct));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapPost("/api/alerts/test", async (AlertTestRequest request, ISkyAlertService alerts, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await alerts.CreateTestAlertAsync(request, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});
app.MapPost("/api/alerts/unsubscribe/{subscriberId:guid}", async (Guid subscriberId, ISkyAlertService alerts, CancellationToken ct) =>
    await alerts.UnsubscribeAsync(subscriberId, ct) ? Results.Ok(new { subscriberId, isActive = false }) : Results.NotFound(new { message = "Alert subscriber was not found." }));

app.MapGet("/api/events/top", async (int? days, string? regionId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var top = await events.GetTopAsync(days, ct);
    return Results.Ok(string.IsNullOrWhiteSpace(regionId) ? top : top.Where(e => e.GlobalVisibility || e.RegionId == regionId || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase))).ToArray());
});
app.MapGet("/api/events/{eventId}", async (string eventId, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
{
    var item = await events.GetByIdAsync(eventId, ct);
    return item is null ? Results.NotFound(new { message = $"Astronomy event '{eventId}' was not found." }) : Results.Ok(item);
});
app.MapPost("/api/events/refresh", async (int? days, IAstronomyEventDiscoveryService events, CancellationToken ct) =>
    Results.Ok(await events.RefreshAsync(days, ct)));

app.MapGet("/api/pipelines/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAsync(20, ct)));
app.MapGet("/api/pipelines/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.MapGet("/api/scripts/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentScriptsAsync(20, ct)));
app.MapGet("/api/scheduler/status", async (IPipelineSchedulerService scheduler, CancellationToken ct) => Results.Ok(await scheduler.GetStatusAsync(ct)));
app.MapGet("/api/scheduler/event-plan", async (string regionId, DateOnly date, IPipelineSchedulerService scheduler, CancellationToken ct) =>
    Results.Ok(await scheduler.GetEventPlanAsync(regionId, date, ct)));
app.MapGet("/api/regions", async (IPipelineSchedulerService scheduler, CancellationToken ct) => Results.Ok(await scheduler.GetRegionsAsync(ct)));
app.MapPost("/api/regions/{regionId}/run-now", async (string regionId, bool? force, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var result = await scheduler.RunRegionNowAsync(regionId, force ?? false, ct);
    return result.Status == "NotFound" ? Results.NotFound(new { message = result.Reason }) : Results.Ok(result);
});
app.MapPost("/api/regions/{regionId}/enable", async (string regionId, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.EnableRegionAsync(regionId, ct);
    return updated ? Results.Ok(new { regionId, enabled = true }) : Results.NotFound(new { message = $"Region '{regionId}' was not found." });
});
app.MapPost("/api/regions/{regionId}/disable", async (string regionId, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.DisableRegionAsync(regionId, ct);
    return updated ? Results.Ok(new { regionId, enabled = false }) : Results.NotFound(new { message = $"Region '{regionId}' was not found." });
});
app.MapGet("/api/tokenhealth", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckAllAsync(ct)));
app.MapGet("/api/tokenhealth/youtube", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckYouTubeAsync(ct)));
app.MapGet("/api/tokenhealth/meta", async (ITokenHealthService tokenHealth, CancellationToken ct) => Results.Ok(await tokenHealth.CheckMetaAsync(ct)));
app.MapPost("/api/scheduler/run-now/{scheduleName}", async (string scheduleName, bool? force, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var result = await scheduler.RunNowAsync(scheduleName, force ?? false, ct);
    return result.Status == "NotFound" ? Results.NotFound(new { message = result.Reason }) : Results.Ok(result);
});
app.MapPost("/api/scheduler/enable/{scheduleName}", async (string scheduleName, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.EnableScheduleAsync(scheduleName, ct);
    return updated ? Results.Ok(new { scheduleName, enabled = true }) : Results.NotFound(new { message = $"Schedule '{scheduleName}' was not found." });
});
app.MapPost("/api/scheduler/disable/{scheduleName}", async (string scheduleName, IPipelineSchedulerService scheduler, CancellationToken ct) =>
{
    var updated = await scheduler.DisableScheduleAsync(scheduleName, ct);
    return updated ? Results.Ok(new { scheduleName, enabled = false }) : Results.NotFound(new { message = $"Schedule '{scheduleName}' was not found." });
});

app.MapPost("/api/events/{eventId}/generate", async (string eventId, string? regionId, ContentType? contentType, RunPipelineRequest request, IAstronomyEventDiscoveryService events, IPipelineRepository repository, PipelineOrchestrator orchestrator, CancellationToken ct) =>
{
    var astronomyEvent = await events.GetByIdAsync(eventId, ct);
    if (astronomyEvent is null)
        return Results.NotFound(new { message = $"Astronomy event '{eventId}' was not found." });

    var duplicateRegionId = string.IsNullOrWhiteSpace(regionId) ? (string.IsNullOrWhiteSpace(request.RegionId) ? request.LocationName : request.RegionId) : regionId;
    var requestedContentType = contentType ?? ContentType.SpecialEventGuide;
    var statuses = new[] { PipelineRunStatus.Queued, PipelineRunStatus.Running, PipelineRunStatus.Succeeded, PipelineRunStatus.CompletedWithPublishErrors };
    if (await repository.HasSpecialEventRunAsync(eventId, request.Date, duplicateRegionId, requestedContentType, statuses, ct))
        return Results.Conflict(new { message = "Special event video already exists for event/date/region/contentType.", eventId, targetDate = request.Date, regionId = duplicateRegionId, contentType = requestedContentType });

    var specialRequest = request with
    {
        ContentType = requestedContentType,
        RegionId = duplicateRegionId,
        EventId = astronomyEvent.EventId,
        EventType = astronomyEvent.EventType,
        EventTitle = astronomyEvent.Title,
        EventDescription = astronomyEvent.Description,
        UseTopicPlanner = false
    };
    var result = await orchestrator.RunAsync(specialRequest, ct);
    return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Special event guide completed."));
});
app.MapGet("/api/events/generated", async (int? take, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetGeneratedSpecialEventRunsAsync(Math.Clamp(take ?? 50, 1, 200), ct)));

app.MapPost("/api/pipelines/run", async (RunPipelineRequest request, PipelineOrchestrator orchestrator, IPipelineRecoveryService recoveryService, ILogger<Program> logger, CancellationToken ct) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["contentType"] = request.ContentType,
        ["runDate"] = request.Date,
        ["regionId"] = request.RegionId ?? "",
        ["publishToYouTube"] = request.PublishToYouTube
    });
    var result = await orchestrator.RunAsync(request, ct);
    if (result.Status is PipelineRunStatus.PublishFailed or PipelineRunStatus.CompletedWithPublishErrors)
    {
        var status = await recoveryService.GetStatusAsync(result.Id, ct);
        var failedStages = status?.Stages
            .Where(s => s.Status.Equals(PersistentStageStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                && (s.StageName.Equals(PipelineStageNames.YouTubeLongPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.YouTubeShortPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.FacebookReelPublished, StringComparison.OrdinalIgnoreCase)
                    || s.StageName.Equals(PipelineStageNames.InstagramReelPublished, StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.StageName)
            .ToArray() ?? [];

        return Results.Ok(new RunPipelineExecutionResponse(
            result.Id,
            result.Status,
            "Succeeded",
            "Failed",
            failedStages,
            $"/api/pipeline/resume/{result.Id}",
            $"/api/pipeline/retry-publish/{result.Id}?platform=youtube",
            "Generation completed, but one or more publish stages failed."));
    }

    return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Completed."));
});

app.MapGet("/api/pipeline/status/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? includeInternal, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.GetStatusAsync(pipelineRunId, ct, includeInternal ?? false);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});

app.MapGet("/api/pipeline/{runId:guid}/thumbnail-publish-status", async (Guid runId, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(runId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {runId} was not found." });
    }

    var outputDirectory = run.OutputFolder;
    if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
    {
        return Results.NotFound(new { message = $"Pipeline run {runId} output folder was not found.", outputDirectory });
    }

    async Task<JsonNode?> ReadJsonAsync(string fileName)
    {
        var path = Path.Combine(outputDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonNode.Parse(await File.ReadAllTextAsync(path, ct));
    }

    var report = await ReadJsonAsync("platform-thumbnail-resolution-report.json");
    var youtubeLong = await ReadJsonAsync("youtube-publish-result-long.json");
    var youtubeShort = await ReadJsonAsync("youtube-publish-result-short.json");
    var facebook = await ReadJsonAsync("facebook-reel-publish-result.json");
    var instagram = await ReadJsonAsync("instagram-reel-publish-result.json");
    var facebookThumb = await ReadJsonAsync("facebook-thumbnail-upload-diagnostics.json");
    var instagramThumb = await ReadJsonAsync("instagram-thumbnail-upload-diagnostics.json");
    var youtubeThumb = await ReadJsonAsync("youtube-thumbnail-upload-diagnostics.json");

    return Results.Ok(new
    {
        runId,
        localGeneratedThumbnails = new
        {
            longPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-long.jpg"),
            shortPath = Path.Combine(outputDirectory, "thumbnails", "thumbnail-short.jpg")
        },
        perPlatformResolution = report,
        publishResults = new { youtubeLong, youtubeShort, facebook, instagram },
        thumbnailDiagnostics = new { youtube = youtubeThumb, facebook = facebookThumb, instagram = instagramThumb }
    });
});
app.MapPost("/api/pipeline/resume/{pipelineRunId:guid}", async (Guid pipelineRunId, string? forceStage, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.ResumeAsync(pipelineRunId, forceStage, ct);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});
app.MapPost("/api/pipeline/retry-publish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? platform, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.RetryPublishAsync(pipelineRunId, platform ?? "all", ct);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
});
app.MapPost("/api/youtubepublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IContentPublishService publishService, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });
    }

    var results = await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct);
    return results.Count == 0 ? Results.BadRequest(new { message = "No publishing result was produced." }) : Results.Ok(results);
});
app.MapPost("/api/metapublish/{pipelineRunId:guid}", async (Guid pipelineRunId, string? asset, IMetaPublishService publishService, IPipelineRepository repository, CancellationToken ct) =>
{
    var run = await repository.GetAsync(pipelineRunId, ct);
    if (run is null)
    {
        return Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." });
    }

    if (run.Status != PipelineRunStatus.Succeeded)
    {
        return Results.BadRequest(new { message = $"Pipeline run {pipelineRunId} is not completed." });
    }

    var results = await publishService.PublishForPipelineRunAsync(pipelineRunId, asset ?? "all", ct);
    return results.Count == 0 ? Results.BadRequest(new { message = "No Meta publishing result was produced." }) : Results.Ok(results);
});
app.MapPost("/api/jobs/enqueue", async (EnqueuePipelineJobRequest request, IPipelineJobQueue queue, CancellationToken ct) =>
{
    try
    {
        var job = await queue.EnqueueAsync(request, ct);
        return Results.Ok(job);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
});
app.MapGet("/api/jobs/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentJobsAsync(50, ct)));
app.MapGet("/api/jobs/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetJobAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/platform-publications/recent", async (int? take, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentPlatformPublicationRecordsAsync(take ?? 20, ct)));
app.MapGet("/api/platform-publications/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetPlatformPublicationRecordAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.MapGet("/api/platform-publications/by-short/{shortId:guid}", async (Guid shortId, IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetPlatformPublicationRecordsByShortIdAsync(shortId, ct)));

app.MapGet("/api/ops/dashboard", async (IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetDashboardAsync(ct)));
app.MapGet("/api/ops/runs", async (DateOnly? date, string? status, IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetRunsAsync(date, status ?? "all", ct)));
app.MapGet("/api/ops/run/{pipelineRunId:guid}", async (Guid pipelineRunId, IOpsDashboardService dashboardService, CancellationToken ct) =>
{
    var run = await dashboardService.GetRunAsync(pipelineRunId, ct);
    return run is null ? Results.NotFound() : Results.Ok(run);
});
app.MapGet("/api/ops/failures", async (int? days, IOpsDashboardService dashboardService, CancellationToken ct) => Results.Ok(await dashboardService.GetFailuresAsync(days ?? 7, ct)));
app.MapGet("/api/ops/summary", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetSummaryAsync(ct)));
app.MapGet("/api/ops/pipelines/recent", async (int? take, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetRecentPipelinesAsync(take ?? 20, ct)));
app.MapGet("/api/ops/pipelines/{id:guid}/stages", async (Guid id, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetPipelineStagesAsync(id, ct)));
app.MapGet("/api/ops/failures/recent", async (int? take, IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetRecentFailuresAsync(take ?? 20, ct)));
app.MapGet("/api/ops/jobs/summary", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetJobSummaryAsync(ct)));

app.MapPost("/api/ops/runs/{id:guid}/replay", async (Guid id, ReplayPipelineRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.ReplayRunAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/retry-publish", async (Guid id, RetryPublishRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RetryPublishAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/retry-archive", async (Guid id, RetryArchiveRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RetryArchiveAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/regenerate-shorts", async (Guid id, RegenerateShortsRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RegenerateShortsAsync(id, request, ct)));
app.MapPost("/api/ops/runs/{id:guid}/rerun-metadata", async (Guid id, RerunMetadataOptimizationRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RerunMetadataOptimizationAsync(id, request, ct)));
app.MapPost("/api/ops/jobs/{id:guid}/requeue", async (Guid id, RequeueJobRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RequeueJobAsync(id, request, ct)));
app.MapPost("/api/ops/jobs/recover-stale", async (RecoverStaleJobsRequest request, IRunOperationsService ops, CancellationToken ct) =>
    await ExecuteOpsAsync(() => ops.RecoverStaleJobsAsync(request, ct)));
app.MapPost("/api/ops/maintenance/cleanup", async (CleanupMaintenanceRequest request, IMaintenanceService maintenanceService, CancellationToken ct) =>
    await ExecuteOpsAsync(() => maintenanceService.CleanupAsync(request, ct)));

app.MapGet("/api/topics/recommended", async (DateOnly? date, ContentType? contentType, string? locationName, string? timeZone, ITopicSelectionService topicSelectionService, CancellationToken ct) =>
{
    var request = new TopicSelectionRequest
    {
        Date = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
        ContentType = contentType,
        LocationName = string.IsNullOrWhiteSpace(locationName) ? "Udaipur, India" : locationName,
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "Asia/Kolkata" : timeZone,
        MaxCandidates = 8
    };

    var plan = await topicSelectionService.BuildPlanAsync(request, ct);
    return Results.Ok(plan);
});
app.MapPost("/api/topics/plan", async (TopicSelectionRequest request, ITopicSelectionService topicSelectionService, CancellationToken ct) =>
    Results.Ok(await topicSelectionService.BuildPlanAsync(request, ct)));
app.MapPost("/api/prompts/feedback-preview", async (PromptFeedbackRequest request, IPromptFeedbackService promptFeedbackService, CancellationToken ct) =>
    Results.Ok(await promptFeedbackService.BuildContextAsync(request, ct)));
app.MapGet("/api/experiments/recent", async (int? take, IContentExperimentService experimentService, CancellationToken ct) =>
    Results.Ok(await experimentService.GetRecentExperimentsAsync(take ?? 20, ct)));
app.MapGet("/api/experiments/{id:guid}", async (Guid id, IContentExperimentService experimentService, CancellationToken ct) =>
{
    var experiment = await experimentService.GetExperimentAsync(id, ct);
    return experiment is null ? Results.NotFound() : Results.Ok(experiment);
});
app.MapGet("/api/experiments/top-performing", async (int? take, IContentExperimentService experimentService, CancellationToken ct) =>
    Results.Ok(await experimentService.GetTopPerformingExperimentsAsync(take ?? 10, ct)));

app.MapGet("/api/analytics/recent", async (int? days, string? platform, string? location, string? contentType, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPlatformContentAnalyticsAsync(new PlatformAnalyticsQuery(days ?? 14, platform, location, contentType, 100), ct)));
app.MapGet("/api/analytics/platform/{platform}", async (string platform, int? days, string? location, string? contentType, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetPlatformContentAnalyticsAsync(new PlatformAnalyticsQuery(days ?? 14, platform, location, contentType, 100), ct)));
app.MapGet("/api/analytics/run/{pipelineRunId:guid}", async (Guid pipelineRunId, IPipelineRepository repository, CancellationToken ct) =>
{
    var items = await repository.GetPlatformContentAnalyticsByRunAsync(pipelineRunId, ct);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
});
app.MapPost("/api/analytics/collect-now", async (Guid? pipelineRunId, IAnalyticsCollectionService collectionService, CancellationToken ct) =>
{
    if (pipelineRunId.HasValue)
        await collectionService.CollectForPipelineRunAsync(pipelineRunId.Value, ct);
    else
        await collectionService.CollectRecentAnalyticsAsync(ct);
    return Results.Accepted();
});
app.MapGet("/api/analytics/dashboard", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.BuildDashboardAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/top-content", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetTopContentAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/insights", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetInsightsAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/platform-summary", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetPlatformSummaryAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));
app.MapGet("/api/analytics/content-performance", async (int? days, string? platform, string? contentType, string? location, int? limit, IAnalyticsIntelligenceService analytics, CancellationToken ct) =>
    Results.Ok(await analytics.GetContentPerformanceAsync(BuildAnalyticsIntelligenceRequest(days, platform, contentType, location, limit), ct)));


app.MapGet("/api/optimization/plan", async (string location, string? platform, IOptimizationService optimizationService, CancellationToken ct) =>
    Results.Ok(await optimizationService.BuildPlanAsync(location, platform ?? "YouTube", ct)));
app.MapPost("/api/optimization/apply-preview", async (OptimizationApplyPreviewRequest request, IOptimizationService optimizationService, CancellationToken ct) =>
{
    var plan = request.Plan ?? await optimizationService.BuildPlanAsync(request.Request.LocationName, request.Platform, ct);
    var result = await optimizationService.ApplyPlanAsync(request.Request, plan, ct);
    var changed = GetChangedFields(request.Request, result);
    return Results.Ok(new OptimizationApplyResult { OriginalRequest = request.Request, ResultRequest = result, Plan = plan, ChangedFields = changed, Mode = "Preview" });
});

app.MapGet("/api/ai-optimization/recommendations", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GetRecommendationsAsync(ct)));
app.MapPost("/api/ai-optimization/generate-now", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GenerateNowAsync(ct)));
app.MapGet("/api/ai-optimization/pending-approval", async (IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.GetPendingApprovalAsync(ct)));
app.MapPost("/api/ai-optimization/apply-approved", async Task<IResult> (AIOptimizationApplyRequest request, IAIOptimizationService service, CancellationToken ct) =>
{
    var result = await service.ApplyApprovedAsync(request, ct);
    return result.Applied ? Results.Ok(result) : Results.BadRequest(result);
});
app.MapPost("/api/ai-optimization/reject", async (AIOptimizationApplyRequest request, IAIOptimizationService service, CancellationToken ct) =>
    Results.Ok(await service.RejectAsync(request, ct)));

app.MapGet("/api/analytics/summary", async (int? days, IPipelineRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetAnalyticsDashboardSummaryAsync(days ?? 14, ct)));
app.MapGet("/api/analytics/top-performing", async (int? topN, IAnalyticsAggregationService aggregationService, CancellationToken ct) =>
{
    var take = topN.GetValueOrDefault(10);
    var summary = await aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, take, ct);
    return Results.Ok(summary);
});
app.MapGet("/api/analytics/youtube/{videoId}", async (string videoId, IPipelineRepository repository, CancellationToken ct) =>
{
    var items = await repository.GetAnalyticsByVideoIdAsync(videoId, ct);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
});

app.MapPost("/api/assets/celestial/refresh", async (ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
    Results.Ok(await ingestion.RefreshAsync(ct)));
app.MapGet("/api/assets/celestial/status", async (ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
    Results.Ok(await ingestion.GetStatusAsync(ct)));
app.MapGet("/api/assets/celestial/{objectKey}", async (string objectKey, ICelestialAssetIngestionService ingestion, CancellationToken ct) =>
{
    var status = await ingestion.GetObjectAsync(objectKey, ct);
    return status is null ? Results.NotFound(new { message = $"Celestial object '{objectKey}' is not configured." }) : Results.Ok(status);
});

app.Run();


static AnalyticsIntelligenceRequest BuildAnalyticsIntelligenceRequest(int? days, string? platform, string? contentType, string? location, int? limit)
    => new(days ?? 14, platform, contentType, location, limit ?? 10);

static async Task<IResult> ExecuteOpsAsync<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
}

static IReadOnlyCollection<string> GetChangedFields(RunPipelineRequest original, RunPipelineRequest result)
{
    var changed = new List<string>();
    if (original.Date != result.Date) changed.Add(nameof(RunPipelineRequest.Date));
    if (original.ContentType != result.ContentType) changed.Add(nameof(RunPipelineRequest.ContentType));
    if (original.LocationName != result.LocationName) changed.Add(nameof(RunPipelineRequest.LocationName));
    if (original.TimeZone != result.TimeZone) changed.Add(nameof(RunPipelineRequest.TimeZone));
    if (original.PublishToYouTube != result.PublishToYouTube) changed.Add(nameof(RunPipelineRequest.PublishToYouTube));
    if (original.UseTopicPlanner != result.UseTopicPlanner) changed.Add(nameof(RunPipelineRequest.UseTopicPlanner));
    if (original.Latitude != result.Latitude) changed.Add(nameof(RunPipelineRequest.Latitude));
    if (original.Longitude != result.Longitude) changed.Add(nameof(RunPipelineRequest.Longitude));
    if (original.OverrideTimezone != result.OverrideTimezone) changed.Add(nameof(RunPipelineRequest.OverrideTimezone));
    if (original.OverrideLocationName != result.OverrideLocationName) changed.Add(nameof(RunPipelineRequest.OverrideLocationName));
    if (original.TargetDate != result.TargetDate) changed.Add(nameof(RunPipelineRequest.TargetDate));
    if (original.RegionId != result.RegionId) changed.Add(nameof(RunPipelineRequest.RegionId));
    return changed;
}
