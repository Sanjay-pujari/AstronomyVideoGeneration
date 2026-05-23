using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2FinalMediaTests
{
    [Fact]
    public async Task RenderFinalMediaEndpoint_Exists()
    {
        var app = WebApplication.CreateBuilder();
        app.WebHost.UseTestServer();
        app.Services.AddSingleton<IWeeklySkyForecastFinalMediaOrchestrator, FakeFinalMediaOrchestrator>();
        app.Services.AddSingleton<IWeeklySkyForecastTimelineCompositionOrchestrator, FakeTimelineOrchestrator>();
        app.Services.AddSingleton<IContentPlanningService, FakePlanningService>();
        var web = app.Build();

        web.MapPost("/api/content-planning/weekly-skyforecast-v2/render-final-media", async (WeeklySkyForecastV2RenderScenesRequest request, IWeeklySkyForecastFinalMediaOrchestrator finalMediaOrchestrator, IWeeklySkyForecastTimelineCompositionOrchestrator timelineOrchestrator, IContentPlanningService planning, CancellationToken ct) =>
        {
            var planId = request.ContentGenerationPlanId ?? (await planning.GeneratePlanAsync(new GenerateContentPlanRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc.UtcDateTime), ct)).ContentGenerationPlanId;
            var runId = request.PipelineRunId ?? planId;
            var intelligenceRequest = new WeeklySkyForecastV2IntelligenceRequest(request.ContentCategoryCode, request.Language, request.RegionId, request.RegionName, request.ScheduledUtc, request.WeekStartDate, request.Diagnostics, runId, planId);
            var timeline = await timelineOrchestrator.RunAsync(intelligenceRequest, planId, ct);
            var media = await finalMediaOrchestrator.RunAsync(intelligenceRequest, planId, ct);
            return Results.Ok(new { timelineCompositionPackage = timeline, finalMediaPackage = media });
        });
        await web.StartAsync();

        var response = await web.GetTestClient().PostAsJsonAsync("/api/content-planning/weekly-skyforecast-v2/render-final-media", new WeeklySkyForecastV2RenderScenesRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FinalMediaOrchestrator_Renders_FinalAssets_WithoutPublishing()
    {
        var orchestrator = new WeeklySkyForecastFinalMediaOrchestrator(new FakeIntelligenceServiceFor6C(), new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceServiceFor6C(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>()), new WeeklySkyForecastTimelineCompositionOrchestrator(new FakeIntelligenceServiceFor6C(), new WeeklySkyForecastSceneRenderingOrchestrator(new FakeIntelligenceServiceFor6C(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeeklySkyForecastSceneRenderingOrchestrator>())), new FakeSpeechSynthesisService());
        var request = new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow, Diagnostics: true);

        var result = await orchestrator.RunAsync(request, null, CancellationToken.None);
        result.FinalMediaValidation.ReadyForHumanReview.Should().BeTrue();
        result.FinalMediaValidation.ReadyForPublishing.Should().BeFalse();
        result.ThumbnailFinalResult.ReusedFromPhase6B.Should().BeTrue();
        File.Exists(result.LongFormFinalVideo.OutputPath).Should().BeTrue();
        File.Exists(result.FinalAudioMixResult.FinalMixedAudioPath).Should().BeTrue();
    }

    private sealed class FakeFinalMediaOrchestrator : IWeeklySkyForecastFinalMediaOrchestrator
    {
        public Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken)
            => Task.FromResult(new FinalMediaPackage(new FinalLongFormVideoResult("out.mp4",110,"1920x1080",30,"Rendered",[],[]), new NarrationAudioResult("n.wav","en","auto",110,"Rendered",[],[]), new BackgroundMusicResult(null,"NoMusic",0,"Rendered",[],[]), new FinalAudioMixResult("mix.wav",110,"Rendered",[],[]), [], new ThumbnailFinalResult("thumb.jpg","Rendered",true,[],[]), new SubtitleResult("a.srt","a.vtt","Planned",false,[],[]), new FinalMediaValidation(true,true,true,true,true,true,true,true,true,true,false,true,true,[],[]), new FinalMediaFreezeStatus(true,true,[],[],[])));
    }

    private sealed class FakeTimelineOrchestrator : IWeeklySkyForecastTimelineCompositionOrchestrator
    {
        public Task<TimelineCompositionPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken) => Task.FromResult(new TimelineCompositionPackage(new LongFormTimelineResult("draft.mp4",110,"Composed",true,true,[],[]),[],[],new NarrationSyncResult(true,"",110,110,false,"Planned",[],[],[]),new AudioCompositionPlan("a","b","c",false,false,false,"Planned"),[],new TimelineCompositionValidation(true,true,110,110,true,true,true,true,true,true,true,false,[],[]),new TimelineCompositionFreezeStatus(true,true,[],[],[])));
    }

    private sealed class FakePlanningService : IContentPlanningService
    {
        public Task<GenerateContentPlanResponse> GeneratePlanAsync(GenerateContentPlanRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new GenerateContentPlanResponse(Guid.NewGuid(), "Planned", null, null));

        public Task<ContentGenerationPlan> GenerateDailyPlanAsync(string contentCategoryCode, string language, string regionId, DateTimeOffset scheduledUtc, string? primaryCelestialObjectCode, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<ContentGenerationPlan>> GetPendingPlansAsync(string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DailySkyGuideContext> BuildDailySkyGuideContextPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AstronomyVisibilityResult> BuildAstronomyVisibilityPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StellariumSceneCapturePlan> BuildStellariumScenePlanPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PipelineBuildResult> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PrepareManualRunResponse?> PrepareManualRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> MarkPlanAsInProgressAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> MarkPlanAsCompletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> MarkPlanAsFailedAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPipelineExecution?> CompleteExecutionAsync(Guid executionId, CompleteContentPlanningExecutionRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPipelineExecution?> FailExecutionAsync(Guid executionId, FailContentPlanningExecutionRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<ContentPipelineExecution>> GetExecutionsAsync(string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ContentPipelineExecution?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeSpeechSynthesisService : ISpeechSynthesisService
    {
        public Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "narration.mp3");
            File.WriteAllText(path, script);
            return Task.FromResult(path);
        }
    }
}
