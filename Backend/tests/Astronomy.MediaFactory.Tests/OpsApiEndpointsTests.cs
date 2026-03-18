using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class OpsApiEndpointsTests
{
    [Fact]
    public async Task OpsEndpoints_ReturnExpectedShapes_And_ValidationErrors()
    {
        var monitoringService = new FakeMonitoringService();
        var runOps = new FakeRunOperationsService();
        var maintenance = new FakeMaintenanceService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineMonitoringService>(monitoringService);
        builder.Services.AddSingleton<IRunOperationsService>(runOps);
        builder.Services.AddSingleton<IMaintenanceService>(maintenance);
        builder.Services.AddHealthChecks();
        var app = builder.Build();

        app.MapHealthChecks("/health/live");
        app.MapGet("/api/ops/summary", async (IPipelineMonitoringService svc, CancellationToken ct) => Results.Ok(await svc.GetSummaryAsync(ct)));
        app.MapGet("/api/ops/failures/recent", async (IPipelineMonitoringService svc, CancellationToken ct) => Results.Ok(await svc.GetRecentFailuresAsync(20, ct)));
        app.MapPost("/api/ops/runs/{id:guid}/retry-publish", async (Guid id, RetryPublishRequest request, IRunOperationsService svc, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await svc.RetryPublishAsync(id, request, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
        app.MapPost("/api/ops/maintenance/cleanup", async (CleanupMaintenanceRequest request, IMaintenanceService svc, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await svc.CleanupAsync(request, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ops/summary")).StatusCode);

        var summary = await client.GetFromJsonAsync<PipelineOpsSummary>("/api/ops/summary");
        Assert.NotNull(summary);
        Assert.Equal(3, summary!.TotalRuns);

        var failures = await client.GetFromJsonAsync<RecentFailuresSnapshot>("/api/ops/failures/recent");
        Assert.NotNull(failures);

        var invalidResponse = await client.PostAsJsonAsync($"/api/ops/runs/{Guid.NewGuid()}/retry-publish", new RetryPublishRequest("", PublishToYouTube: true));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var cleanupResponse = await client.PostAsJsonAsync("/api/ops/maintenance/cleanup", new CleanupMaintenanceRequest("manual", DeleteWorkingFiles: false, DeleteDbRecords: false, DeleteAnalytics: false));
        Assert.Equal(HttpStatusCode.BadRequest, cleanupResponse.StatusCode);

        await app.StopAsync();
    }

    private sealed class FakeMonitoringService : IPipelineMonitoringService
    {
        public Task<PipelineOpsSummary> GetSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult(new PipelineOpsSummary(3, 2, 1, 1200, "Rendering", [], new QueueHealthSnapshot(1, 0, 0, 0), []));
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentPipelinesAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task<IReadOnlyCollection<PipelineStageExecution>> GetPipelineStagesAsync(Guid pipelineRunId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<PipelineStageExecution>>([]);
        public Task<RecentFailuresSnapshot> GetRecentFailuresAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult(new RecentFailuresSnapshot([], [], [], []));
        public Task<JobOpsSummary> GetJobSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult(new JobOpsSummary(1, 1, 0, 0, 0, 0));
    }

    private sealed class FakeRunOperationsService : IRunOperationsService
    {
        public Task<OpsActionResult> ReplayRunAsync(Guid runId, ReplayPipelineRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<OpsActionResult> RetryPublishAsync(Guid runId, RetryPublishRequest request, CancellationToken cancellationToken)
        {
            var validation = RecoveryRequestValidator.Validate(request);
            if (validation is not null)
                throw new InvalidOperationException(validation);
            return Task.FromResult(new OpsActionResult(Guid.NewGuid(), "ok"));
        }
        public Task<OpsActionResult> RetryArchiveAsync(Guid runId, RetryArchiveRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<OpsActionResult> RegenerateShortsAsync(Guid runId, RegenerateShortsRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<OpsActionResult> RerunMetadataOptimizationAsync(Guid runId, RerunMetadataOptimizationRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<OpsActionResult> RequeueJobAsync(Guid jobId, RequeueJobRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<StaleJobRecoverySummary> RecoverStaleJobsAsync(RecoverStaleJobsRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private sealed class FakeMaintenanceService : IMaintenanceService
    {
        public Task<MaintenanceCleanupSummary> CleanupAsync(CleanupMaintenanceRequest request, CancellationToken cancellationToken)
        {
            var validation = RecoveryRequestValidator.Validate(request);
            if (validation is not null)
                throw new InvalidOperationException(validation);
            return Task.FromResult(new MaintenanceCleanupSummary(Guid.NewGuid(), 0, 0, 0, 0, []));
        }
    }
}
