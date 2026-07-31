using System.Net;
using System.Net.Http.Json;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2CertifiedApiIntegrationTests
{
    [Fact]
    public async Task rc2_api_executes_certified_phase1_to_phase4()
    {
        var executionId = Guid.NewGuid();
        var endpoint = new CertifiedEndpoint(executionId);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IRc2ContentPlanningBatchOrchestrator>(endpoint);
        builder.Services.AddControllers().AddApplicationPart(typeof(ContentPlanningRc2Controller).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new BatchGenerateFromPlansRequest(2026, "US", "en", DryRun: false,
            UseProductionPipeline: true, StartPhaseNo: 1, EndPhaseNo: 4, PlanId: executionId,
            ExecutionMode: ContentPlanExecutionMode.Normal);

        var firstHttp = await client.PostAsJsonAsync("/api/content-planning/rc2/batch-generate-from-plans", request);
        Assert.Equal(HttpStatusCode.OK, firstHttp.StatusCode);
        var first = await firstHttp.Content.ReadFromJsonAsync<BatchGenerateFromPlansResponse>();
        var status = Assert.IsType<Rc2CertifiedExecutionStatus>(first!.Rc2CertifiedExecution);
        Assert.Equal(executionId.ToString("D"), status.ExecutionId);
        Assert.Equal([1, 2, 3, 4], status.Phases.Select(x => x.PhaseNo));
        Assert.All(status.Phases, phase => Assert.Equal("Succeeded", phase.Status));
        Assert.Equal("DocumentaryBlueprintPhase4IntegrationService", status.Phase4Publication.IntegrationService);
        Assert.True(status.Phase4Publication.PhysicalAuthorityExists);
        Assert.True(status.Phase4Publication.CommittedStateValidationPassed);
        Assert.True(status.PublicationCommitted);
        Assert.Equal("Valid", status.ValidationStatus);
        Assert.Equal(12, status.LongSceneCount);
        Assert.Equal(4, status.ShortSceneCount);
        Assert.Equal(endpoint.AggregateChecksum, status.AggregateChecksum);
        Assert.False(status.Phase4Publication.LegacyAuthorityProduced);
        Assert.True(status.CommittedStateValidationPassed);
        Assert.False(status.LegacyAuthorityProduced);
        Assert.Equal("DocumentaryBlueprintPhase4IntegrationService", status.PipelineIntegrationService);
        Assert.Equal("PublishedDocumentaryBlueprintAggregate", status.DownstreamAuthorityType);

        var before = endpoint.UpstreamChecksums.ToArray();
        var rerun = request with { StartPhaseNo = 4, ExecutionMode = ContentPlanExecutionMode.RerunPhase };
        var second = await (await client.PostAsJsonAsync("/api/content-planning/rc2/batch-generate-from-plans", rerun))
            .Content.ReadFromJsonAsync<BatchGenerateFromPlansResponse>();
        Assert.True(second!.Rc2CertifiedExecution!.AlreadyPublished);
        Assert.Equal(before, endpoint.UpstreamChecksums);
    }

    private sealed class CertifiedEndpoint(Guid executionId) : IRc2ContentPlanningBatchOrchestrator
    {
        private int calls;
        public string AggregateChecksum { get; } = new('a', 64);
        public IReadOnlyList<string> UpstreamChecksums { get; } = [new('1', 64), new('2', 64), new('3', 64)];

        public Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
        {
            calls++;
            var phases = Enumerable.Range(1, 4).Select(number => new Rc2CertifiedPhaseStatus(number,
                number == 4 ? "Documentary Blueprint" : $"Certified Phase {number}", "Succeeded",
                number == 4 ? (calls > 1 ? "P4PUB_ALREADY_PUBLISHED" : "P4INT_COMPLETED") : null)).ToArray();
            var certified = new Rc2CertifiedExecutionStatus(executionId.ToString("D"), phases,
                new("DocumentaryBlueprintPhase4IntegrationService", "Succeeded", true, true, false),
                "orion-gold-aggregate", AggregateChecksum, 12, 4, 720, 120, "Valid", true, calls > 1,
                ["04-blueprint/documentary-blueprint.json", "phase-manifest.json", "phase-04-validation.json"],
                CommittedStateValidationPassed: true,
                LegacyAuthorityProduced: false,
                PipelineIntegrationService: "DocumentaryBlueprintPhase4IntegrationService",
                DownstreamAuthorityType: "PublishedDocumentaryBlueprintAggregate");
            return Task.FromResult(new BatchGenerateFromPlansResponse(true, false, 0, 1, 1, [], [], [], [],
                PlanId: executionId, SelectedPlanId: executionId, OutputRoot: "/executions/" + executionId.ToString("D"),
                LastCompletedPhaseNo: 4, UseProductionPipeline: true, Rc2CertifiedExecution: certified));
        }
    }
}
