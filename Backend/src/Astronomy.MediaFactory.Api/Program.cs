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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddMediaFactory(builder.Configuration);

var app = builder.Build();

app.Logger.LogInformation("Starting Astronomy.MediaFactory.Api in {Environment}", app.Environment.EnvironmentName);

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
app.MapGet("/api/analytics/recent", async (IPipelineRepository repository, CancellationToken ct) => Results.Ok(await repository.GetRecentAnalyticsAsync(50, ct)));
app.MapGet("/api/analytics/top-performing", async (int? topN, IAnalyticsAggregationService aggregationService, CancellationToken ct) =>
{
    var take = topN.GetValueOrDefault(10);
    var summary = await aggregationService.BuildSummaryAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, take, ct);
    return Results.Ok(summary);
});
app.MapGet("/api/analytics/{videoId}", async (string videoId, IPipelineRepository repository, CancellationToken ct) =>
{
    var items = await repository.GetAnalyticsByVideoIdAsync(videoId, ct);
    return items.Count == 0 ? Results.NotFound() : Results.Ok(items);
});

app.Run();

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
