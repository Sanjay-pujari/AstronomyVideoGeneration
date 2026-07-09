using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public sealed class Rc2StoryIntelligenceTests
{
    [Fact]
    public async Task Phase6_BuildsStoryGraphAndMultiSceneIntents()
    {
        var root = Path.Combine(Path.GetTempPath(), "rc2-story-intelligence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plan-input"));
        Directory.CreateDirectory(Path.Combine(root, "question-engine"));

        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "content-plan-production-request.json"), """
        {
          "title": "Moon and Jupiter Close Approach",
          "eventType": "PlanetaryConjunction",
          "timeZone": "UTC",
          "startUtc": "2026-07-10T01:00:00Z",
          "peakUtc": "2026-07-10T03:00:00Z",
          "endUtc": "2026-07-10T05:00:00Z",
          "scheduledUtc": "2026-07-09T12:00:00Z",
          "primaryObjects": ["Moon", "Jupiter"],
          "secondaryObjects": ["Aldebaran"],
          "angularSeparationDegrees": "2.5",
          "bestViewingWindowLocal": "Before dawn",
          "skyDirectionHint": "Eastern sky",
          "moonInterference": "Low",
          "moonIlluminationPercent": "24",
          "visibilityRegion": "United States"
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "production-event-intelligence.json"), "{} ");
        await File.WriteAllTextAsync(Path.Combine(root, "question-engine", "question-answer-set.json"), "{ \"answers\": [] }");
        await File.WriteAllTextAsync(Path.Combine(root, "question-engine", "question-driven-scene-plan.json"), """
        {
          "scenes": [
            { "sceneId": "s1", "sourceQuestionId": "q1", "keyQuestion": "Why look up now?", "keyMessage": "A close pairing sets the hook." },
            { "sceneId": "s2", "sourceQuestionId": "q2", "scenePurpose": "Discovery", "keyQuestion": "What is happening?", "keyMessage": "The Moon passes Jupiter." },
            { "sceneId": "s3", "sourceQuestionId": "q3", "scenePurpose": "Editorial", "keyQuestion": "Why does it happen?", "keyMessage": "Apparent sky motion creates the pairing." },
            { "sceneId": "s4", "sourceQuestionId": "q4", "scenePurpose": "Observation", "keyQuestion": "Where should viewers look?", "keyMessage": "Use the supported viewing metadata." }
          ]
        }
        """);

        var request = new BatchGenerateFromPlansRequest(2026, "US", StartPhaseNo: 1, EndPhaseNo: 6);
        var response = new BatchGenerateFromPlansResponse(
            Success: true,
            DryRun: true,
            RequestedTitleCount: 1,
            SelectedPlanCount: 1,
            MaxPlans: 1,
            SelectedPlans:
            [
                new BatchGenerateFromPlansSelectedPlan(Guid.NewGuid(), "Moon and Jupiter Close Approach", "PlanetaryConjunction", "Short", "US", "en", DateTimeOffset.Parse("2026-07-09T12:00:00Z"), "Ready", "Ready", 1)
            ],
            Steps: [],
            Warnings: [],
            Errors: [],
            Title: "Moon and Jupiter Close Approach",
            OutputRoot: root);

        var result = await new SceneIntentBuilder(NullLogger<SceneIntentBuilder>.Instance).BuildAndWriteDiagnosticsAsync(request, response, CancellationToken.None);

        Assert.Contains(result.GeneratedFiles, path => path.EndsWith(Path.Combine("editorial", "story-graph.json"), StringComparison.OrdinalIgnoreCase));
        using var storyGraph = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "editorial", "story-graph.json")));
        Assert.Equal("AstroPulse-StoryGraph-v1", storyGraph.RootElement.GetProperty("storyGraphVersion").GetString());
        Assert.Equal(4, storyGraph.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("Hook", storyGraph.RootElement.GetProperty("scenes")[0].GetProperty("scenePurpose").GetString());
        Assert.Equal("Discovery", storyGraph.RootElement.GetProperty("scenes")[1].GetProperty("scenePurpose").GetString());
        Assert.Equal("Science", storyGraph.RootElement.GetProperty("scenes")[2].GetProperty("scenePurpose").GetString());
        Assert.Equal("Observation", storyGraph.RootElement.GetProperty("scenes")[3].GetProperty("scenePurpose").GetString());

        using var sceneIntents = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "editorial", "scene-intents.json")));
        Assert.Equal(4, sceneIntents.RootElement.GetArrayLength());
        Assert.DoesNotContain(sceneIntents.RootElement.EnumerateArray(), scene => scene.GetProperty("scenePurpose").GetString() == "Editorial");

        using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "editorial", "editorial-contract.json")));
        Assert.True(contract.RootElement.TryGetProperty("storyGraph", out var contractStoryGraph));
        Assert.Equal(4, contractStoryGraph.GetProperty("sceneCount").GetInt32());

        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "editorial", "editorial-diagnostics.json")));
        Assert.True(diagnostics.RootElement.GetProperty("storyGraphCreated").GetBoolean());
        Assert.Equal(4, diagnostics.RootElement.GetProperty("storySceneCount").GetInt32());
        Assert.Equal(4, diagnostics.RootElement.GetProperty("sceneIntentCount").GetInt32());
        Assert.Contains(diagnostics.RootElement.GetProperty("subPhases").EnumerateArray(), phase => phase.GetString() == "6.2A Story Graph Builder");
    }
}

public sealed class Rc2CreativeStoryboardTests
{
    [Fact]
    public async Task Phase7_BuildsCreativeStoryboardAndDiagnosticsFromEditorialArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "rc2-creative-storyboard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "plan-input"));
        Directory.CreateDirectory(Path.Combine(root, "question-engine"));

        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "content-plan-production-request.json"), """
        {
          "title": "Moon and Jupiter Close Approach",
          "eventType": "PlanetaryConjunction",
          "timeZone": "UTC",
          "startUtc": "2026-07-10T01:00:00Z",
          "peakUtc": "2026-07-10T03:00:00Z",
          "endUtc": "2026-07-10T05:00:00Z",
          "scheduledUtc": "2026-07-09T12:00:00Z",
          "primaryObjects": ["Moon", "Jupiter"],
          "secondaryObjects": ["Aldebaran"],
          "angularSeparationDegrees": "2.5",
          "bestViewingWindowLocal": "Before dawn",
          "skyDirectionHint": "Eastern sky",
          "moonInterference": "Low",
          "moonIlluminationPercent": "24",
          "visibilityRegion": "United States"
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(root, "plan-input", "production-event-intelligence.json"), "{} ");
        await File.WriteAllTextAsync(Path.Combine(root, "question-engine", "question-answer-set.json"), "{ \"answers\": [] }");
        await File.WriteAllTextAsync(Path.Combine(root, "question-engine", "question-driven-scene-plan.json"), """
        {
          "scenes": [
            { "sceneId": "s1", "keyMessage": "A close pairing sets the hook." },
            { "sceneId": "s2", "scenePurpose": "Discovery", "keyMessage": "The Moon passes Jupiter." },
            { "sceneId": "s3", "scenePurpose": "Science", "keyMessage": "Apparent sky motion creates the pairing." },
            { "sceneId": "s4", "scenePurpose": "Observation", "keyMessage": "Look toward the eastern sky before dawn." }
          ]
        }
        """);

        var request = new BatchGenerateFromPlansRequest(2026, "US", StartPhaseNo: 1, EndPhaseNo: 7);
        var response = new BatchGenerateFromPlansResponse(true, true, 1, 1, 1,
        [
            new BatchGenerateFromPlansSelectedPlan(Guid.NewGuid(), "Moon and Jupiter Close Approach", "PlanetaryConjunction", "Short", "US", "en", DateTimeOffset.Parse("2026-07-09T12:00:00Z"), "Ready", "Ready", 1)
        ], [], [], [], "Moon and Jupiter Close Approach", root);

        await new SceneIntentBuilder(NullLogger<SceneIntentBuilder>.Instance).BuildAndWriteDiagnosticsAsync(request, response, CancellationToken.None);
        var result = await new CreativeStoryboardBuilder(NullLogger<CreativeStoryboardBuilder>.Instance).BuildAndWriteDiagnosticsAsync(request, response, CancellationToken.None);

        Assert.Contains(result.GeneratedFiles, path => path.EndsWith(Path.Combine("creative", "creative-storyboard.json"), StringComparison.OrdinalIgnoreCase));
        using var storyboard = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "creative", "creative-storyboard.json")));
        Assert.Equal("AstroPulse-CreativeStoryboard-v1", storyboard.RootElement.GetProperty("creativeStoryboardVersion").GetString());
        Assert.Equal("RC2", storyboard.RootElement.GetProperty("orchestrationVersion").GetString());
        Assert.Equal(4, storyboard.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("Understand why this event is worth watching.", storyboard.RootElement.GetProperty("scenes")[0].GetProperty("viewerFocus").GetString());
        Assert.Contains(storyboard.RootElement.GetProperty("scenes")[3].GetProperty("visualAccuracyRules").EnumerateArray(), rule => rule.GetString() == "Observation visuals must respect direction and timing metadata when available.");
        Assert.Contains(storyboard.RootElement.GetProperty("scenes")[0].GetProperty("prohibitedVisualChoices").EnumerateArray(), choice => choice.GetString() == "fantasy sky");

        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "creative", "creative-diagnostics.json")));
        Assert.Equal(7, diagnostics.RootElement.GetProperty("phaseNo").GetInt32());
        Assert.Equal("Creative Intelligence Foundation", diagnostics.RootElement.GetProperty("phaseName").GetString());
        Assert.Equal(4, diagnostics.RootElement.GetProperty("creativeSceneCount").GetInt32());
        Assert.Contains(diagnostics.RootElement.GetProperty("subPhases").EnumerateArray(), phase => phase.GetString() == "7.1 Creative Storyboard Builder");
    }
}
