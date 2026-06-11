using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class QuestionSceneIntentEnricherTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_DryRunReturnsPreviewWithoutWritingFile()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(BuildPlanPath(workingDirectory, "question-driven-scene-plan.enriched.json")));
        Assert.True(result.EnrichedScenePlan.IsValid);
        Assert.Equal("CasualSkyWatcher", result.EnrichedScenePlan.ViewerPersona);
        Assert.Equal("Beginner", result.EnrichedScenePlan.KnowledgeLevel);
        Assert.All(result.EnrichedScenePlan.Scenes, scene =>
        {
            Assert.Equal("CasualSkyWatcher", scene.ViewerPersona);
            Assert.Equal("Beginner", scene.KnowledgeLevel);
        });
        Assert.Equal(AstronomyQuestionTypes.What, result.EnrichedScenePlan.Scenes.First().QuestionType);
        Assert.Equal(AstronomyQuestionTypes.Action, result.EnrichedScenePlan.Scenes.Last().QuestionType);

        var what = result.EnrichedScenePlan.Scenes.First();
        Assert.Equal("OpeningOverview", what.ScenePurpose);
        Assert.Equal("Understand this scene from the approved question answer.", what.ViewerTakeaway);
        Assert.Equal("Turn the approved answer into a clear visual beat without adding unrelated sky objects.", what.NarrationIntent);
        Assert.Contains("base scene plan", what.VisualIntent);
        Assert.Contains(what.SourceAnswer, what.ImagePromptIntent);
        Assert.Equal("Use concise labels from the approved answer only.", what.OverlayIntent);
        Assert.Equal("Muted viewers should understand the approved answer without unrelated fallback content.", what.AccessibilityIntent);
        Assert.Equal("GenericFallback", result.EnrichedScenePlan.Diagnostics?.EnrichmentSource);
        Assert.NotEqual(what.SourceAnswer, what.ViewerTakeaway);
        Assert.NotEqual(what.SourceAnswer, what.NarrationIntent);
        Assert.NotEqual(what.SourceAnswer, what.VisualIntent);
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_WritesEnrichedPlanWhenDryRunIsFalse()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Single(result.GeneratedFiles);
        Assert.True(File.Exists(result.GeneratedFiles.Single()));

        var savedJson = await File.ReadAllTextAsync(result.GeneratedFiles.Single());
        using var document = JsonDocument.Parse(savedJson);
        Assert.True(document.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal("CasualSkyWatcher", document.RootElement.GetProperty("viewerPersona").GetString());
        Assert.Equal("Beginner", document.RootElement.GetProperty("knowledgeLevel").GetString());
        Assert.Equal(6, document.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("CasualSkyWatcher", document.RootElement.GetProperty("scenes")[0].GetProperty("viewerPersona").GetString());
        Assert.Equal("Beginner", document.RootElement.GetProperty("scenes")[0].GetProperty("knowledgeLevel").GetString());
        Assert.Equal("GenericFallback", document.RootElement.GetProperty("diagnostics").GetProperty("enrichmentSource").GetString());
        Assert.Contains("approved answer", document.RootElement.GetProperty("scenes")[1].GetProperty("imagePromptIntent").GetString());
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_AppliesRequestedAudienceContextToRootAndScenes()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            "AstroPhotographyBeginner",
            "Intermediate",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("AstroPhotographyBeginner", result.EnrichedScenePlan.ViewerPersona);
        Assert.Equal("Intermediate", result.EnrichedScenePlan.KnowledgeLevel);
        Assert.All(result.EnrichedScenePlan.Scenes, scene =>
        {
            Assert.Equal("AstroPhotographyBeginner", scene.ViewerPersona);
            Assert.Equal("Intermediate", scene.KnowledgeLevel);
        });
        Assert.Equal(AstronomyQuestionTypes.What, result.EnrichedScenePlan.Scenes.First().QuestionType);
        Assert.Equal(AstronomyQuestionTypes.Action, result.EnrichedScenePlan.Scenes.Last().QuestionType);
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_ReportsValidationWarningsForInvalidAudienceContext()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            "DeepSpaceProfessional",
            "Expert",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.False(result.EnrichedScenePlan.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Contains("Root viewerPersona 'DeepSpaceProfessional' is not supported.", result.Warnings);
        Assert.Contains("Root knowledgeLevel 'Expert' is not supported.", result.Warnings);
        Assert.Contains("Scene 1 viewerPersona 'DeepSpaceProfessional' is not supported.", result.Warnings);
        Assert.Contains("Scene 1 knowledgeLevel 'Expert' is not supported.", result.Warnings);
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_ReportsValidationWarningsForInvalidSceneOrderAndDuplicatePurpose()
    {
        var workingDirectory = CreateWorkingDirectory();
        var invalidPlan = BuildSourcePlan() with
        {
            Scenes =
            [
                BuildScene(1, AstronomyQuestionTypes.Where, "DuplicatePurpose", "Where should I look?", "Look west."),
                BuildScene(2, AstronomyQuestionTypes.What, "DuplicatePurpose", "What is happening?", "Venus and Jupiter are close tonight."),
                BuildScene(3, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Step outside after sunset.")
            ]
        };
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, invalidPlan);
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(EventId, RegionId), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.False(result.EnrichedScenePlan.IsValid);
        Assert.Empty(result.GeneratedFiles);
        Assert.Contains("What must be first.", result.Warnings);
        Assert.Contains("Scene purpose 'DuplicatePurpose' must not be duplicated.", result.Warnings);
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_UsesNamedFullMoonStrategyWithoutPlanetLeakage()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildFullMoonSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            ProductionContext: BuildProductionContext(BuildFullMoonIntelligence())), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("Strategy", result.EnrichedScenePlan.Diagnostics?.EnrichmentSource);
        Assert.Equal("NamedFullMoon", result.EnrichedScenePlan.Diagnostics?.StrategyId);
        var enrichedText = string.Join(" ", result.EnrichedScenePlan.Scenes.SelectMany(scene => new[]
        {
            scene.ViewerTakeaway,
            scene.NarrationIntent,
            scene.VisualIntent,
            scene.ImagePromptIntent,
            scene.OverlayIntent,
            scene.AccessibilityIntent
        }));
        Assert.Contains("Moon", enrichedText);
        Assert.Contains("moonrise", enrichedText.ToLowerInvariant());
        Assert.Contains("eastern", enrichedText.ToLowerInvariant());
        Assert.DoesNotContain("venus", enrichedText.ToLowerInvariant());
        Assert.DoesNotContain("jupiter", enrichedText.ToLowerInvariant());
        Assert.DoesNotContain("planet pairing", enrichedText.ToLowerInvariant());
        Assert.Empty(result.EnrichedScenePlan.Diagnostics?.LeakageTermsFound ?? Array.Empty<string>());
    }

    [Fact]
    public async Task EnrichQuestionScenePlanAsync_UsesActualPlanetPairingObjects()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteQuestionDrivenScenePlanAsync(workingDirectory, BuildSourcePlan());
        var enricher = CreateEnricher(workingDirectory);

        var result = await enricher.EnrichQuestionScenePlanAsync(new QuestionSceneIntentEnrichmentRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            ProductionContext: BuildProductionContext(BuildMarsJupiterIntelligence())), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("Strategy", result.EnrichedScenePlan.Diagnostics?.EnrichmentSource);
        var enrichedText = string.Join(" ", result.EnrichedScenePlan.Scenes.Select(scene => scene.VisualIntent));
        Assert.Contains("Mars", enrichedText);
        Assert.Contains("Jupiter", enrichedText);
        Assert.DoesNotContain("venus", enrichedText.ToLowerInvariant());
    }

    private static QuestionSceneIntentEnricher CreateEnricher(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<QuestionSceneIntentEnricher>.Instance);

    private static QuestionDrivenScenePlanDto BuildSourcePlan() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter will appear close together in Udaipur’s evening sky."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look toward the western sky, about one-third above the horizon."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "Best viewing is around 7:23 PM IST, shortly after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find bright Venus first, then look slightly nearby for Jupiter."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "If skies are clear, step outside after sunset and enjoy the view.")
        ],
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static QuestionDrivenScenePlanDto BuildFullMoonSourcePlan() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Snow Moon Full Moon is a named full moon, when the Moon appears fully illuminated."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look toward the eastern sky with an open horizon for moonrise."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "Best viewing is around moonrise, when the full moon is easy to see."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Use the open horizon first, then follow the bright Moon as it rises higher."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "A named full moon connects a bright lunar view with seasonal culture."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Save the moonrise time and prepare a clear eastern view.")
        ],
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static ProductionPipelineExecutionContext BuildProductionContext(ProductionEventIntelligence intelligence) => new(
        UseProductionPipeline: true,
        ContentGenerationPlanId: Guid.NewGuid(),
        AstronomyEventIntelligenceId: Guid.NewGuid(),
        SourceExternalEventId: EventId,
        IsDbApprovedPlanExecution: true,
        RegionId: RegionId,
        Language: "en",
        EventType: intelligence.EventType,
        ProductionEventIntelligence: intelligence);

    private static ProductionEventIntelligence BuildFullMoonIntelligence() => new(
        Domain: "Astronomy",
        EventType: "NamedFullMoon",
        Title: "Snow Moon Full Moon",
        ShortTitle: "Snow Moon",
        EventDate: DateTimeOffset.Parse("2026-02-01T12:00:00Z"),
        PeakUtc: DateTimeOffset.Parse("2026-02-01T12:00:00Z"),
        LocalPeakTime: "6:11 PM IST",
        BestViewingWindowLocal: "moonrise around 6:11 PM IST",
        SkyDirectionHint: "eastern sky",
        VisibilityRegion: RegionId,
        PrimaryObjects: ["Moon"],
        SecondaryObjects: [],
        ViewingQuality: "Good",
        MoonInterference: null,
        MoonIlluminationPercent: 100,
        ScientificContext: "Named full moon",
        ViewerInstructions: ["Watch moonrise"],
        VisualMotifs: ["Moon", "moonrise", "full moon glow"],
        SceneStrategy: ["NamedFullMoon"],
        QualityWarnings: [],
        ForbiddenTerms: ["Venus", "Jupiter", "planet pairing"],
        StrategyId: "NamedFullMoon",
        ResolvedObjectNames: ["Moon"],
        ForbiddenObjectNames: ["Venus", "Jupiter"],
        RequiredVisualObjects: ["Moon", "moonrise", "eastern sky", "full moon glow"],
        RequiredNarrationFacts: ["localPeakTime", "seasonal name"]);

    private static ProductionEventIntelligence BuildMarsJupiterIntelligence() => new(
        Domain: "Astronomy",
        EventType: "PlanetPairing",
        Title: "Mars and Jupiter Close Pairing",
        ShortTitle: "Mars and Jupiter",
        EventDate: DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
        PeakUtc: DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
        LocalPeakTime: "7:23 PM IST",
        BestViewingWindowLocal: "around 7:23 PM IST",
        SkyDirectionHint: "western sky",
        VisibilityRegion: RegionId,
        PrimaryObjects: ["Mars"],
        SecondaryObjects: ["Jupiter"],
        ViewingQuality: "Good",
        MoonInterference: null,
        MoonIlluminationPercent: null,
        ScientificContext: "Close pairing",
        ViewerInstructions: ["Find Mars", "Look for Jupiter nearby"],
        VisualMotifs: ["Mars", "Jupiter", "close pairing"],
        SceneStrategy: ["PlanetPairing"],
        QualityWarnings: [],
        ForbiddenTerms: ["Venus"],
        StrategyId: "PlanetPairing",
        ResolvedObjectNames: ["Mars", "Jupiter"],
        ForbiddenObjectNames: ["Venus"],
        RequiredVisualObjects: ["Mars", "Jupiter", "close pairing"],
        RequiredNarrationFacts: ["angular separation"],
        AngularSeparationDegrees: 1.63m);

    private static QuestionDrivenSceneDto BuildScene(int sceneNumber, string questionType, string scenePurpose, string viewerQuestion, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            sourceAnswer,
            sourceAnswer,
            $"Visual intent for {questionType}.",
            $"Narration intent for {questionType}.",
            true);

    private static async Task WriteQuestionDrivenScenePlanAsync(string workingDirectory, QuestionDrivenScenePlanDto plan)
    {
        var path = BuildPlanPath(workingDirectory, "question-driven-scene-plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions));
    }

    private static string BuildPlanPath(string workingDirectory, string fileName)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", fileName);

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-scene-intent-enricher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
