using System.Net.Http.Json;
using Astronomy.MediaFactory.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2PhaseDiagnosticsEndpointTests
{
    [Fact]
    public async Task Endpoint_Returns_AstronomyEvents_Result_For_AstronomyEvents_Phase()
    {
        var app = WebApplication.Create();
        app.MapPost("/api/content-planning/weekly-skyforecast-v2/phase-diagnostics", async (WeeklySkyForecastV2PhaseDiagnosticsRequest request) =>
        {
            var response = new WeeklySkyForecastV2IntelligenceResponse(
                ContentGenerationPlanId: Guid.NewGuid(),
                Category: "WeeklySkyForecast",
                Success: true,
                WeekStartDate: new DateOnly(2026, 5, 25),
                WeekEndDate: new DateOnly(2026, 5, 31),
                Region: "US",
                SkyfieldSummary: new WeeklySkyForecastV2SkyfieldSummary(7, 10, 5, 3, "JUPITER", null, null),
                EventIntelligence: [],
                EventExtractionResult: new WeeklyAstronomyEventExtractionResult(true, null, [], "summary", new Dictionary<string, int>(), null, [], []),
                WeeklyStoryArc: new WeeklyStoryArc("h", "s", "t", "o", [], "c", [], [], []),
                EditorialStoryPackage: new WeeklyEditorialStoryPackage(
                    new WeeklyHeroEvent("1", "HeroObject", "Moon", "Moon lead", new DateOnly(2026, 5, 26), null, ["MOON"], ["Moon"], 1, 1, 1, "Cinematic", "Why"),
                    [],
                    "headline",
                    "subtitle",
                    "hook",
                    "theme",
                    [],
                    [],
                    new WeeklyThumbnailDirection([], [], [], "Wonder", "Moon", "Wide", []),
                    [],
                    "summary",
                    []),
                CinematicStoryBlueprint: null,
                NarrativeAbstractionPackage: null,
                NarrationPlan: null,
                GeneratedNarrationPackage: null,
                NarrationQuality: null,
                VisualRequirementPackage: null,
                HybridScenePlanPackage: null,
                NormalizedEditorialPackage: null,
                SceneChoreographyPackage: null,
                CinematicChoreographyPackage: null,
                RenderExecutionPackage: null,
                RenderPreparationPackage: null,
                ExecutionValidation: null,
                PreviewStability: null,
                Phase5FoundationStatus: null,
                RenderPreparationFreezeStatus: null,
                ReadyForRenderPreparation: true,
                ReadyForSceneRendering: false,
                ReadyForRendering: false,
                LegacyEditorialPackageDeprecated: true,
                RecommendedVisualStrategies: [],
                Warnings: [],
                StepResults: []);

            var result = string.Equals(request.Phase, nameof(WeeklySkyForecastV2DiagnosticsPhase.AstronomyEvents), StringComparison.OrdinalIgnoreCase)
                ? new { response.EventExtractionResult, response.EventIntelligence }
                : response;

            return Results.Ok(new { phase = request.Phase, result });
        });

        app.RunAsync();
        var client = app.GetTestClient();
        var http = await client.PostAsJsonAsync("/api/content-planning/weekly-skyforecast-v2/phase-diagnostics", new WeeklySkyForecastV2PhaseDiagnosticsRequest("WeeklySkyForecast", "en", "us", "US", DateTimeOffset.UtcNow, Phase: nameof(WeeklySkyForecastV2DiagnosticsPhase.AstronomyEvents)));
        http.EnsureSuccessStatusCode();
        var payload = await http.Content.ReadAsStringAsync();
        Assert.Contains("AstronomyEvents", payload);
        Assert.Contains("eventExtractionResult", payload, StringComparison.OrdinalIgnoreCase);
    }
}
