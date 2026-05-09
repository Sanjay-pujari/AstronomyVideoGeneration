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

app.MapGet("/api/pipelines/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAsync(20, ct)));
app.MapGet("/api/pipelines/{id:guid}", async (Guid id, IPipelineRepository repository, CancellationToken ct) =>
{
    var item = await repository.GetAsync(id, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
app.MapGet("/api/scripts/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentScriptsAsync(20, ct)));
app.MapGet("/api/scheduler/status", async (IPipelineSchedulerService scheduler, CancellationToken ct) => Results.Ok(await scheduler.GetStatusAsync(ct)));
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
app.MapPost("/api/pipelines/run", async (RunPipelineRequest request, PipelineOrchestrator orchestrator, ILogger<Program> logger, CancellationToken ct) =>
{
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["contentType"] = request.ContentType,
        ["runDate"] = request.Date,
        ["publishToYouTube"] = request.PublishToYouTube
    });
    var result = await orchestrator.RunAsync(request, ct);
    return Results.Ok(new RunPipelineResponse(result.Id, result.Status, "Completed."));
});

app.MapGet("/api/pipeline/status/{pipelineRunId:guid}", async (Guid pipelineRunId, bool? includeInternal, IPipelineRecoveryService recoveryService, CancellationToken ct) =>
{
    var status = await recoveryService.GetStatusAsync(pipelineRunId, ct, includeInternal ?? false);
    return status is null ? Results.NotFound(new { message = $"Pipeline run {pipelineRunId} was not found." }) : Results.Ok(status);
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
    return changed;
}
