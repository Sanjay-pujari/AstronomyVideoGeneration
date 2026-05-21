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

public sealed class StellariumCaptureEndpointSafetyTests
{
    [Fact]
    public async Task Endpoint_InvokesExecutorOnly()
    {
        var planning = new FakePlanning();
        var executor = new FakeExecutor();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IContentPlanningService>(planning);
        builder.Services.AddSingleton<IStellariumImageCaptureExecutor>(executor);
        var app = builder.Build();
        app.MapPost("/api/content-planning/plans/{id:guid}/capture-stellarium-scenes", async (Guid id, StellariumCaptureExecutionApiRequest apiRequest, IContentPlanningService p, IStellariumImageCaptureExecutor e, CancellationToken ct) =>
        {
            var plan = await p.GetPlanByIdAsync(id, ct);
            var scenePlan = await p.BuildStellariumScenePlanPreviewAsync(id, ct);
            return Results.Ok(await e.CaptureAsync(scenePlan, new StellariumCaptureExecutionRequest(id, apiRequest.DryRun, apiRequest.OverwriteExisting), ct));
        });

        await app.StartAsync();
        var client = app.GetTestClient();
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/content-planning/plans/{id}/capture-stellarium-scenes", new { dryRun = true, overwriteExisting = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, planning.GetPlanByIdCalls);
        Assert.Equal(1, planning.ScenePreviewCalls);
        Assert.Equal(1, executor.Calls);
        await app.StopAsync();
    }

    private sealed class FakeExecutor : IStellariumImageCaptureExecutor
    {
        public int Calls;
        public Task<StellariumCaptureExecutionResponse> CaptureAsync(StellariumSceneCapturePlan scenePlan, StellariumCaptureExecutionRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new StellariumCaptureExecutionResponse(request.ContentGenerationPlanId, true, 0, 0, "", [], [], null));
        }
    }

    private sealed class FakePlanning : IContentPlanningService
    {
        public int GetPlanByIdCalls;
        public int ScenePreviewCalls;
        public Task<GenerateContentPlanResponse> GeneratePlanAsync(GenerateContentPlanRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentGenerationPlan> GenerateDailyPlanAsync(string contentCategoryCode, string language, string regionId, DateTime? scheduledUtc, string? primaryCelestialObjectCode, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContentGenerationPlan>> GetPendingPlansAsync(string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken) { GetPlanByIdCalls++; return Task.FromResult<ContentGenerationPlan?>(new ContentGenerationPlan{Id=id,Status="Planned",ContentCategoryCode="DailySkyGuide"}); }
        public Task<ContentPlanningPipelineRequestPreview> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DailySkyGuideContext> BuildDailySkyGuideContextPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AstronomyVisibilityResult> BuildAstronomyVisibilityPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StellariumSceneCapturePlan> BuildStellariumScenePlanPreviewAsync(Guid id, CancellationToken cancellationToken) { ScenePreviewCalls++; return Task.FromResult(new StellariumSceneCapturePlan(id,"DailySkyGuide","us","x",0,0,"UTC",DateOnly.FromDateTime(DateTime.UtcNow),[],[])); }
        public Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPlanningExecution?> PrepareManualRunAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPlanningExecution?> CompleteExecutionAsync(Guid contentPipelineExecutionId, CompleteContentPlanningExecutionRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPlanningExecution?> FailExecutionAsync(Guid contentPipelineExecutionId, FailContentPlanningExecutionRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContentPlanningExecution>> GetExecutionsAsync(string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPlanningExecution?> GetExecutionByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
