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
                new BatchGenerateFromPlansSelectedPlan(Guid.NewGuid(), "Moon and Jupiter Close Approach", "PlanetaryConjunction", "Short", "US", "en", DateTimeOffset.Parse("2026-07-09T12:00:00Z"), "Ready", "Ready", 1, null)
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
            new BatchGenerateFromPlansSelectedPlan(Guid.NewGuid(), "Moon and Jupiter Close Approach", "PlanetaryConjunction", "Short", "US", "en", DateTimeOffset.Parse("2026-07-09T12:00:00Z"), "Ready", "Ready", 1, null)
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

public sealed class Rc2NarrationV5OrchestrationTests
{
    [Fact]
    public async Task Phase8_RangeRequest_RunsNarrationV5AndAddsOutputsToResponseAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "rc2-narration-v5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "editorial"));
        Directory.CreateDirectory(Path.Combine(root, "creative"));

        await File.WriteAllTextAsync(Path.Combine(root, "editorial", "editorial-contract.json"), """
        {
          "language": "en",
          "requiredNarrationFacts": [
            { "name": "bestViewingWindowLocal", "value": "Before dawn" },
            { "name": "skyDirectionHint", "value": "Eastern sky" }
          ],
          "prohibitedPhrases": ["once in a lifetime"],
          "preferredPhrases": ["look east"]
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(root, "creative", "creative-storyboard.json"), """
        {
          "orchestrationVersion": "RC2",
          "language": "en",
          "storyArc": "Hook → Observation → Takeaway",
          "scenes": [
            { "sceneId": "s1", "sceneOrder": 1, "scenePurpose": "Hook", "keyMessage": "A bright pairing opens the morning sky." },
            { "sceneId": "s2", "sceneOrder": 2, "scenePurpose": "Observation", "keyMessage": "Use the supported viewing window and direction." }
          ]
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(root, "phase-manifest.json"), """
        {
          "filesGeneratedThisRun": [],
          "executedPhaseNumbers": [],
          "phasesActuallyExecuted": [],
          "phases": []
        }
        """);

        var request = new BatchGenerateFromPlansRequest(2026, "US", StartPhaseNo: 1, EndPhaseNo: 8);
        var response = await new Rc2ContentPlanningBatchOrchestrator(
            new StubBatchGenerationService(BuildBaseResponse(root)),
            new Rc2PipelinePhaseRegistry(),
            new SceneIntentBuilder(NullLogger<SceneIntentBuilder>.Instance),
            new CreativeStoryboardBuilder(NullLogger<CreativeStoryboardBuilder>.Instance),
            new NarrationGeneratorV5(NullLogger<NarrationGeneratorV5>.Instance),
            NullLogger<Rc2ContentPlanningBatchOrchestrator>.Instance)
            .GenerateFromPlansAsync(request, CancellationToken.None);

        var expectedFiles = new[]
        {
            Path.Combine(root, "narration-v5", "narration-plan.json"),
            Path.Combine(root, "narration-v5", "narration-briefs.json"),
            Path.Combine(root, "narration-v5", "prompt-preview.md"),
            Path.Combine(root, "narration-v5", "prompt-diagnostics.json"),
            Path.Combine(root, "narration-v5", "llm-request.json"),
            Path.Combine(root, "narration-v5", "narration.json"),
            Path.Combine(root, "narration-v5", "narration-diagnostics.json")
        };

        Assert.All(expectedFiles, path => Assert.True(File.Exists(path), path));
        var phase8 = Assert.Single(response.Steps.OfType<ProductionPhaseResult>(), phase => phase.PhaseNo == 8);
        Assert.Equal("Narration Generator V5", phase8.PhaseName);
        Assert.All(expectedFiles, path => Assert.Contains(path, phase8.OutputFiles));

        var execution = Assert.Single(response.Results!.OfType<ContentPlanProductionExecutionResult>());
        Assert.All(expectedFiles, path => Assert.Contains(path, execution.GeneratedFiles));
        Assert.Contains(execution.PhaseResults!, phase => phase.PhaseNo == 8 && phase.PhaseName == "Narration Generator V5");

        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "narration-diagnostics.json")));
        Assert.Equal("Narration Generator V5", diagnostics.RootElement.GetProperty("phaseName").GetString());
        Assert.Equal("RC2", diagnostics.RootElement.GetProperty("orchestrationVersion").GetString());
        Assert.Equal("AstroPulse-NarrationValidator-v2", diagnostics.RootElement.GetProperty("validationVersion").GetString());
        Assert.Equal(2, diagnostics.RootElement.GetProperty("sceneCount").GetInt32());
        Assert.All(diagnostics.RootElement.GetProperty("inputs").EnumerateArray(), input => Assert.True(input.GetProperty("exists").GetBoolean()));
        Assert.All(diagnostics.RootElement.GetProperty("outputsCreated").EnumerateArray(), output => Assert.True(output.GetProperty("exists").GetBoolean()));
        Assert.True(diagnostics.RootElement.GetProperty("requiredFactCoverage").TryGetProperty("bestViewingWindowLocal", out _));
        Assert.True(diagnostics.RootElement.GetProperty("narrativeDirectorExecuted").GetBoolean());
        Assert.Equal(2, diagnostics.RootElement.GetProperty("narrationBriefCount").GetInt32());
        Assert.True(diagnostics.RootElement.TryGetProperty("factsDistributedByScene", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("repeatedFactWarnings", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("narrationNaturalnessWarnings", out _));
        Assert.True(diagnostics.RootElement.GetProperty("promptComposerExecuted").GetBoolean());
        Assert.True(diagnostics.RootElement.GetProperty("llmRequestCreated").GetBoolean());
        Assert.True(diagnostics.RootElement.GetProperty("llmGenerationExecuted").GetBoolean());
        Assert.True(diagnostics.RootElement.TryGetProperty("scientificAccuracyScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("editorialQualityScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("naturalnessScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("observationGuidanceScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("flowScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("overallDocumentaryScore", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("engineeringLeakageViolations", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("warnings", out _));
        Assert.True(diagnostics.RootElement.TryGetProperty("errors", out _));

        var promptPreview = await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "prompt-preview.md"));
        Assert.Contains("## 1. Your Role", promptPreview);
        Assert.Contains("## 8. Output Contract", promptPreview);
        Assert.DoesNotContain("Available facts", promptPreview);

        using var llmRequest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "llm-request.json")));
        Assert.Equal("AstroPulse-NarrationLlmRequest-v1", llmRequest.RootElement.GetProperty("requestVersion").GetString());
        Assert.Equal("NarrationStudio", llmRequest.RootElement.GetProperty("component").GetString());

        using var briefs = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "narration-briefs.json")));
        Assert.Equal(2, briefs.RootElement.GetProperty("briefs").GetArrayLength());
        Assert.True(briefs.RootElement.GetProperty("briefs")[1].TryGetProperty("generationInstructions", out _));

        using var narration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "narration-v5", "narration.json")));
        var fullNarrationText = narration.RootElement.GetProperty("fullNarrationText").GetString()!;
        Assert.DoesNotContain("Verified details", fullNarrationText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Before dawn", fullNarrationText);
        Assert.Contains("Eastern sky", fullNarrationText);
        Assert.Contains("open horizon", fullNarrationText);
        Assert.Contains("Until next time, keep looking up.", fullNarrationText);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "phase-manifest.json")));
        Assert.Contains(manifest.RootElement.GetProperty("phases").EnumerateArray(), phase => phase.GetProperty("phaseNo").GetInt32() == 8 && phase.GetProperty("phaseName").GetString() == "Narration Generator V5");
        Assert.Contains(manifest.RootElement.GetProperty("filesGeneratedThisRun").EnumerateArray(), file => file.GetString()!.EndsWith("narration-v5/narration.json", StringComparison.OrdinalIgnoreCase));
    }

    private static BatchGenerateFromPlansResponse BuildBaseResponse(string root)
    {
        var planId = Guid.NewGuid();
        var productionRequest = new ContentPlanProductionPipelineRequest(
            PlanId: planId,
            Category: "DailySkyGuide",
            Title: "Moon and Jupiter",
            ShortTitle: "Moon and Jupiter",
            EventType: "PlanetaryConjunction",
            RegionId: "US",
            Language: "en",
            PrimaryObjects: ["Moon", "Jupiter"],
            SecondaryObjects: [],
            StartUtc: null,
            PeakUtc: null,
            EndUtc: null,
            ScheduledUtc: null,
            SourceExternalEventId: null,
            PlannedFormat: "Short",
            RequestedOutputs: [],
            VisibilityScore: null,
            RarityScore: null,
            AudienceInterestScore: null,
            ContentOpportunityScore: null,
            VerificationStatus: null,
            VerificationSource: null,
            ContentStrategy: null,
            LocalPeakTime: null,
            SkyDirectionHint: null,
            VisibilityRegion: null,
            MoonInterference: null,
            BestViewingWindowLocal: null,
            RadiantVisibilityNote: null,
            MoonIlluminationPercent: null,
            RecommendedPublishWindow: null,
            RecommendedContentTypes: [],
            Warnings: [],
            SourceNotes: []);
        var execution = new ContentPlanProductionExecutionResult(
            Success: true,
            DryRun: false,
            UseProductionPipeline: true,
            UsedPlaceholderVisuals: false,
            SelectedPlanCount: 1,
            PlanId: planId,
            Title: "Moon and Jupiter",
            OutputRoot: root,
            QuestionEngineCompleted: true,
            ShortScenesGenerated: false,
            LongScenesGenerated: false,
            HeroGenerated: false,
            ThumbnailsGenerated: false,
            ShortNarrationGenerated: false,
            LongNarrationGenerated: false,
            ShortTtsGenerated: false,
            LongTtsGenerated: false,
            ShortVideoGenerated: false,
            LongVideoGenerated: false,
            FinalShortVideoPath: string.Empty,
            FinalLongVideoPath: string.Empty,
            ProductionPipelineRequest: productionRequest,
            PlannedProductionSteps: [],
            GeneratedFiles: [],
            Warnings: [],
            Errors: [],
            PhaseResults: []);
        return new BatchGenerateFromPlansResponse(true, false, 1, 1, 1,
        [
            new BatchGenerateFromPlansSelectedPlan(planId, "Moon and Jupiter", "DailySkyGuide", "Short", "US", "en", DateTimeOffset.Parse("2026-07-09T12:00:00Z"), "Ready", "Ready", 1, null)
        ], [], [], [], Results: [execution], UseProductionPipeline: true, Title: "Moon and Jupiter", OutputRoot: root);
    }

    private sealed class StubBatchGenerationService(BatchGenerateFromPlansResponse response) : IContentPlanBatchGenerationService
    {
        public Task<BatchGenerateFromPlansResponse> GenerateFromPlansAsync(BatchGenerateFromPlansRequest request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
