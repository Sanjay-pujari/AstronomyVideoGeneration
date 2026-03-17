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
    public async Task OpsEndpoints_ReturnExpectedShapes()
    {
        var svc = new FakeMonitoringService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IPipelineMonitoringService>(svc);
        builder.Services.AddHealthChecks();
        var app = builder.Build();

        app.MapHealthChecks("/health/live");
        app.MapGet("/api/ops/summary", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetSummaryAsync(ct)));
        app.MapGet("/api/ops/failures/recent", async (IPipelineMonitoringService monitoringService, CancellationToken ct) => Results.Ok(await monitoringService.GetRecentFailuresAsync(20, ct)));

        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ops/summary")).StatusCode);

        var summary = await client.GetFromJsonAsync<PipelineOpsSummary>("/api/ops/summary");
        Assert.NotNull(summary);
        Assert.Equal(3, summary!.TotalRuns);

        var failures = await client.GetFromJsonAsync<RecentFailuresSnapshot>("/api/ops/failures/recent");
        Assert.NotNull(failures);

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
}
