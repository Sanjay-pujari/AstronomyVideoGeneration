using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelineExecutionServiceTests
{
    [Fact]
    public void FirstNonEmpty_ReturnsEmptyString_WhenAllCandidatesAreMissing()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("FirstNonEmpty", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { new string?[] { null, "", "   " } });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void PhaseGating_NamedFullMoonShortOnly_SkipsLongNarration()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.False(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
    }

    [Fact]
    public void PhaseGating_MeteorShortAndLong_RunsBothNarrationPhases()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo", "LongVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
    }

    [Fact]
    public void PhaseGating_ThumbnailOnly_SkipsVideoAndAssetPhasesNotRequested()
    {
        var context = CreateContext("FutureDomain", ["Thumbnail"]);

        Assert.False(IsPhaseRequired(context, 11));
        Assert.True(IsPhaseRequired(context, 12));
        Assert.False(IsPhaseRequired(context, 13));
        Assert.False(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
        Assert.True(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void RequestedOutputCompletion_ReportsSkippedForUnrequestedLongVideo()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo", "Thumbnail"]);
        var now = DateTimeOffset.UtcNow;
        ProductionPhaseResult[] phaseResults =
        [
            new(12, "Generate Thumbnails", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(13, "Generate Short Narration", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(14, "Generate Long Narration", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(15, "Generate Short TTS", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(16, "Generate Long TTS", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(17, "Assemble Short Video", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(18, "Assemble Long Video", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested")
        ];

        var completion = BuildRequestedOutputCompletion(context, phaseResults);

        Assert.Contains(completion, item => item.OutputType == "ShortVideo" && item.Requested && item.Status == "Succeeded");
        Assert.Contains(completion, item => item.OutputType == "LongVideo" && !item.Requested && item.Status == "Skipped");
        Assert.Contains(completion, item => item.OutputType == "Thumbnail" && item.Requested && item.Status == "Succeeded");
    }


    [Theory]
    [InlineData("MeteorShower", "Perseids Tonight", "MeteorShower")]
    [InlineData("PlanetPairing", "Venus Jupiter Pairing", "PlanetPairing")]
    [InlineData("Comet", "Comet Tonight", "Comet")]
    [InlineData("Eclipse", "Eclipse Tonight", "Eclipse")]
    public void BuildDurationTargetedShortNarration_UsesDynamicFacts_AndTargetsProfileRange(string eventType, string shortTitle, string expectedEventType)
    {
        var context = CreateContext(eventType, ["ShortVideo"], shortTitle);
        var buildMethod = typeof(ProductionPipelineExecutionService).GetMethod("BuildDurationTargetedShortNarration", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);

        var narration = (string)buildMethod!.Invoke(null, [context])!;
        var estimatedSeconds = (double)estimateMethod!.Invoke(null, [narration])!;

        Assert.Contains(expectedEventType, narration);
        Assert.Contains(shortTitle, narration);
        Assert.Contains("western sky", narration);
        Assert.Contains("9 PM", narration);
        Assert.Contains("check clouds", narration, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(estimatedSeconds, 30.0, 40.0);
    }

    [Fact]
    public void TrimLowestPriorityShortNarrationSentences_SelfCorrectsOneWordAndHalfSecondOverflow()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo"], "Perseids Tonight");
        var trimMethod = typeof(ProductionPipelineExecutionService).GetMethod("TrimLowestPriorityShortNarrationSentences", BindingFlags.NonPublic | BindingFlags.Static);
        var countMethod = typeof(ProductionPipelineExecutionService).GetMethod("CountSpokenWords", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        var narration = string.Join(" ", new[]
        {
            "Current MeteorShower Event makes Perseids Tonight worth planning for tonight with family nearby.",
            "Watch near western sky with peak timing around 9 PM and the best viewing window at 9 PM to midnight.",
            "Use a chair, dim your phone, and let your eyes adapt before scanning slowly.",
            "This extra context adds atmosphere, expectation, wonder, patience, comfort, curiosity, and perspective for viewers tonight.",
            "Check clouds, choose a safe open spot, save this viewing window, share it nearby, and step outside safely."
        });

        var preTrimWordCount = (int)countMethod!.Invoke(null, [narration])!;
        var preTrimDuration = (double)estimateMethod!.Invoke(null, [narration])!;
        var trimmed = (string)trimMethod!.Invoke(null, [narration, context])!;
        var postTrimWordCount = (int)countMethod.Invoke(null, [trimmed])!;
        var postTrimDuration = (double)estimateMethod.Invoke(null, [trimmed])!;

        Assert.Equal(80, preTrimWordCount);
        Assert.True(preTrimDuration > 45.0);
        Assert.True(postTrimWordCount <= 79);
        Assert.True(postTrimDuration <= 45.0);
        Assert.DoesNotContain("This extra context adds atmosphere", trimmed);
        Assert.Contains("Perseids Tonight", trimmed);
        Assert.Contains("9 PM to midnight", trimmed);
        Assert.Contains("Check clouds", trimmed);
    }


    [Fact]
    public void BuildPhase6SceneVisualVariants_ReturnsPlanningOnlyMetadataWithoutRendering()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneVisualVariants", BindingFlags.NonPublic | BindingFlags.Static);
        var scene = new EnrichedQuestionSceneDto(
            2,
            "How",
            "ExplainObject",
            "How do I find Mars?",
            "Look west after sunset.",
            "CasualSkyWatcher",
            "Beginner",
            "Mars is low in the west.",
            "Explain where Mars appears.",
            "Show Mars above the western horizon.",
            "Mars over a dim western horizon.",
            "Mars • western horizon",
            "Mars label near the horizon.",
            true);

        var variants = (IReadOnlyList<SceneVisualVariantDto>)method!.Invoke(null, [scene])!;

        Assert.InRange(variants.Count, 3, 5);
        Assert.Equal(["wide_context", "object_focus", "educational_overlay", "cinematic_detail", "transition_or_closing"], variants.Select(v => v.VariantType).ToArray());
        Assert.Equal(Enumerable.Range(1, variants.Count), variants.Select(v => v.VariantNo));
        Assert.All(variants, variant =>
        {
            Assert.False(string.IsNullOrWhiteSpace(variant.Purpose));
            Assert.True(variant.RecommendedDurationSeconds > 0);
            Assert.False(string.IsNullOrWhiteSpace(variant.CameraStyle));
            Assert.False(string.IsNullOrWhiteSpace(variant.CompositionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.MotionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.OverlayHint));
            Assert.Contains("do not render", variant.RendererHint, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("scene-02-", variant.OutputFileNameSuggestion, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EnrichedSceneJson_OmitsVisualVariants_WhenSceneVariantsAreDisabled()
    {
        var scene = new EnrichedQuestionSceneDto(
            1,
            "What",
            "OpeningOverview",
            "What is happening?",
            "The Moon is full.",
            "CasualSkyWatcher",
            "Beginner",
            "The full Moon is visible tonight.",
            "Explain the full Moon timing.",
            "Show the Moon over the horizon.",
            "Full Moon above trees.",
            "Full Moon",
            "Moon label centered.",
            true);

        var json = JsonSerializer.Serialize(scene, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("visualVariants", json);
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DetectsInjectedIntentPhrasesAcrossAllIntentFields()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
        Assert.Equal(6, GetIntDiagnostic(diagnostics, "EnrichedSceneIntentCount"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DoesNotApplyInjectedPhraseDetectionToOtherEventTypes()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Multi-planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Use a scan path from west to east.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingContract_UsesInjectedIntentDiagnosticsInsteadOfLegacyObjectPresence()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        context = context with
        {
            ProductionEventIntelligence = context.ProductionEventIntelligence with
            {
                RequiredVisualObjects = ["planet grouping", "guided scan path"]
            }
        };
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Begin at the western horizon and move upward.");

        await ValidatePhase6EnrichedScenePlanContractAsync(context);

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }

    private static bool IsPhaseRequired(ProductionPhaseContext context, int phaseNo)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("IsPhaseRequiredForRequestedOutputs", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, [context, phaseNo])!;
    }

    private static object BuildPhase6SceneEnrichmentDiagnostics(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneEnrichmentDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        return method!.Invoke(null, [context])!;
    }

    private static async Task ValidatePhase6EnrichedScenePlanContractAsync(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase6EnrichedScenePlanContractAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var task = (Task)method!.Invoke(null, [context, path, CancellationToken.None])!;
        await task;
    }

    private static bool GetBooleanDiagnostic(object diagnostics, string propertyName)
        => (bool)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;

    private static int GetIntDiagnostic(object diagnostics, string propertyName)
        => (int)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;

    private static async Task WriteEnrichedScenePlanAsync(
        ProductionPhaseContext context,
        string viewerTakeaway,
        string narrationIntent,
        string visualIntent,
        string imagePromptIntent,
        string overlayIntent,
        string accessibilityIntent)
    {
        Directory.CreateDirectory(context.ExecutionContext.QuestionRoot!);
        var plan = new EnrichedQuestionScenePlanDto(
            "event-id",
            context.Request.RegionId,
            context.Request.Language,
            "CasualSkyWatcher",
            "Beginner",
            [
                new EnrichedQuestionSceneDto(
                    1,
                    "What",
                    "OpeningOverview",
                    "What should I look for?",
                    "Look for the planets near the horizon.",
                    "CasualSkyWatcher",
                    "Beginner",
                    viewerTakeaway,
                    narrationIntent,
                    visualIntent,
                    imagePromptIntent,
                    overlayIntent,
                    accessibilityIntent,
                    true)
            ],
            true,
            DateTimeOffset.UtcNow,
            new QuestionSceneEnrichmentDiagnostics(
                context.ProductionEventIntelligence.EventType,
                context.ProductionEventIntelligence.RequiredVisualObjects,
                [],
                [],
                [],
                "Test"));
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static IReadOnlyList<RequestedOutputCompletion> BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults)
    {
        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildRequestedOutputCompletion" && m.GetParameters().Length == 2);
        return (IReadOnlyList<RequestedOutputCompletion>)method.Invoke(null, [context, phaseResults])!;
    }

    private static ProductionPhaseContext CreateContext(string eventType, IReadOnlyList<string> requestedOutputs, string? shortTitleOverride = null)
    {
        var planId = Guid.NewGuid();
        var outputRoot = Path.Combine(Path.GetTempPath(), "astro-pulse-phase-gating-tests", planId.ToString("N"));
        var request = new ContentPlanProductionPipelineRequest(
            planId,
            "AstronomyEvent",
            $"Current {eventType} Event",
            shortTitleOverride ?? $"{eventType} Tonight",
            eventType,
            "us",
            "en",
            [eventType == "PlanetPairing" ? "Venus" : "Moon"],
            eventType == "PlanetPairing" ? ["Jupiter"] : [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow,
            null,
            string.Join("+", requestedOutputs),
            requestedOutputs,
            null,
            null,
            null,
            null,
            "Verified",
            "Test",
            "Current event strategy",
            "9 PM",
            "western sky",
            "United States",
            null,
            "9 PM to midnight",
            null,
            null,
            requestedOutputs,
            [],
            []);
        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            eventType,
            request.Title,
            request.ShortTitle,
            request.StartUtc,
            request.PeakUtc,
            request.LocalPeakTime,
            request.BestViewingWindowLocal,
            request.SkyDirectionHint,
            request.VisibilityRegion,
            request.PrimaryObjects,
            request.SecondaryObjects,
            null,
            request.MoonInterference,
            request.MoonIlluminationPercent,
            null,
            [],
            [],
            [],
            [],
            []);
        var executionContext = new ProductionPipelineExecutionContext(
            true,
            planId,
            Guid.NewGuid(),
            null,
            true,
            true,
            "Approved",
            "Approved",
            true,
            true,
            "Verified",
            request.ContentStrategy,
            request.RegionId,
            request.Language,
            request.RequestedOutputs,
            request.Category,
            request.PlannedFormat,
            DateTimeOffset.UtcNow.Year,
            request.EventType,
            Path.Combine(outputRoot, "plan-input"),
            Path.Combine(outputRoot, "question-engine"),
            Path.Combine(outputRoot, "scene-approval-v3"),
            Path.Combine(outputRoot, "hero"),
            Path.Combine(outputRoot, "thumbnails"),
            Path.Combine(outputRoot, "narration"),
            Path.Combine(outputRoot, "tts"),
            Path.Combine(outputRoot, "video-assembly"),
            Path.Combine(outputRoot, "validation"),
            intelligence,
            new GenericAstronomyEventStrategy(),
            null);
        var pipelineRequest = new ProductionPipelineRequest(request, Guid.NewGuid(), outputRoot, false, ExecutionContext: executionContext);
        return new ProductionPhaseContext(pipelineRequest, request, Guid.NewGuid(), Guid.NewGuid().ToString("D"), outputRoot, executionContext, intelligence, new GenericAstronomyEventStrategy(), false, false, 1, 19, false);
    }
}
