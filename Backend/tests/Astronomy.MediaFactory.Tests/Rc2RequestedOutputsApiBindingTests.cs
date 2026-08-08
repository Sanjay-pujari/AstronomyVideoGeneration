using System.Net;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Api.Controllers;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2RequestedOutputsApiBindingTests
{
    private static readonly Guid PlanId = Guid.Parse("baa5af31-4ba9-4d1d-8ef3-0796210a9ed2");
    private static readonly string[] PersistedOutputs = ["ShortVideo", "LongVideo", "Thumbnail"];

    [Fact]
    public async Task Actual_rc2_route_binds_HeroAsset_and_echoes_manual_override()
    {
        var capture = new CapturingOrchestrator();
        await using var app = await StartApiAsync(capture);
        var json = $$"""
        {
          "year": 2026,
          "regionId": "GLOBAL",
          "language": "en",
          "dryRun": false,
          "useProductionPipeline": true,
          "planId": "{{PlanId:D}}",
          "startPhaseNo": 11,
          "endPhaseNo": 11,
          "overwriteExisting": true,
          "dependencyExpansionMode": "ReadOnly",
          "requestedOutputsOverride": ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]
        }
        """;

        var response = await app.GetTestClient().PostAsync(
            "/api/content-planning/rc2/batch-generate-from-plans",
            new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"], capture.Bound!.RequestedOutputsOverride);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("ManualOverride", root.GetProperty("requestedOutputsSource").GetString());
        AssertJsonArray(root, "requestedOutputsBeforeOverride", PersistedOutputs);
        AssertJsonArray(root, "requestedOutputsOverride", ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);
        AssertJsonArray(root, "requestedOutputsAfterResolution", ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);
        AssertJsonArray(root, "manualRequestedOutputsOverrideReceived", ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);
        AssertJsonArray(root.GetProperty("productionPipelineRequest"), "requestedOutputs", ["ShortVideo", "LongVideo", "Thumbnail", "HeroAsset"]);
    }

    [Fact]
    public async Task Actual_rc2_route_without_override_uses_persisted_outputs_and_omits_HeroAsset()
    {
        var capture = new CapturingOrchestrator();
        await using var app = await StartApiAsync(capture);
        var json = $$"""{"year":2026,"regionId":"GLOBAL","language":"en","dryRun":false,"useProductionPipeline":true,"planId":"{{PlanId:D}}","startPhaseNo":11,"endPhaseNo":11,"overwriteExisting":true,"dependencyExpansionMode":"ReadOnly"}""";

        var response = await app.GetTestClient().PostAsync(
            "/api/content-planning/rc2/batch-generate-from-plans",
            new StringContent(json, Encoding.UTF8, "application/json"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(capture.Bound!.RequestedOutputsOverride);
        Assert.Equal("PersistedPlan", root.GetProperty("requestedOutputsSource").GetString());
        AssertJsonArray(root, "requestedOutputsOverride", []);
        AssertJsonArray(root, "requestedOutputsAfterResolution", PersistedOutputs);
        AssertJsonArray(root, "manualRequestedOutputsOverrideReceived", []);
        AssertJsonArray(root.GetProperty("productionPipelineRequest"), "requestedOutputs", PersistedOutputs);
    }

    private static async Task<WebApplication> StartApiAsync(CapturingOrchestrator capture)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IRc2ContentPlanningBatchOrchestrator>(capture);
        builder.Services.AddControllers().AddApplicationPart(typeof(ContentPlanningRc2Controller).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static void AssertJsonArray(JsonElement owner, string name, IReadOnlyList<string> expected) =>
        Assert.Equal(expected, owner.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToArray());

    private sealed class CapturingOrchestrator : IRc2ContentPlanningBatchOrchestrator
    {
        public BatchGenerateFromPlansRequest? Bound { get; private set; }

        public Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
        {
            Bound = request;
            var resolution = ContentPlanProductionExecutionService.ResolveRequestedOutputs(PersistedOutputs, request.RequestedOutputsOverride);
            return Task.FromResult(new BatchGenerateFromPlansResponse(
                true, request.DryRun, 0, 1, 1, [], [], [], [],
                UseProductionPipeline: true,
                ProductionPipelineRequest: new { requestedOutputs = resolution.AfterResolution },
                RequestedOutputsSource: resolution.Source,
                RequestedOutputsBeforeOverride: resolution.BeforeOverride,
                RequestedOutputsOverride: resolution.Override,
                RequestedOutputsAfterResolution: resolution.AfterResolution));
        }
    }
}
