using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class QuestionDrivenNarrationGeneratorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_DryRunReturnsValidNarrationWithoutWritingFiles()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan());
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.InRange(result.TotalEstimatedDurationSeconds, 45, 70);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(BuildPlanPath(workingDirectory, "question-driven-narration.json")));
        Assert.False(File.Exists(BuildPlanPath(workingDirectory, "question-driven-narration-review.json")));
        Assert.Equal(AstronomyQuestionTypes.What, result.Narration.Scenes.First().QuestionType);
        Assert.Equal(AstronomyQuestionTypes.Action, result.Narration.Scenes.Last().QuestionType);
        Assert.All(result.Narration.Scenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.NarrationText));
            Assert.False(string.IsNullOrWhiteSpace(scene.CaptionText));
            Assert.True(scene.EstimatedDurationSeconds > 0);
            Assert.True(scene.CaptionText.Length < scene.NarrationText.Length);
            Assert.NotEqual(Normalize(scene.SourceAnswer), Normalize(scene.NarrationText));
        });
        Assert.Equal(result.Narration.Scenes.Count, result.Narration.Scenes.Select(scene => scene.NarrationText).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_WritesNarrationAndReviewWhenDryRunIsFalse()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan());
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.GeneratedFiles.Count);
        Assert.All(result.GeneratedFiles, path => Assert.True(File.Exists(path)));

        var narrationJson = await File.ReadAllTextAsync(BuildPlanPath(workingDirectory, "question-driven-narration.json"));
        using var narrationDocument = JsonDocument.Parse(narrationJson);
        Assert.Equal(6, narrationDocument.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("Venus and Jupiter shine close tonight.", narrationDocument.RootElement.GetProperty("scenes")[0].GetProperty("captionText").GetString());

        var reviewJson = await File.ReadAllTextAsync(BuildPlanPath(workingDirectory, "question-driven-narration-review.json"));
        using var reviewDocument = JsonDocument.Parse(reviewJson);
        Assert.True(reviewDocument.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(result.TotalEstimatedDurationSeconds, reviewDocument.RootElement.GetProperty("totalEstimatedDurationSeconds").GetInt32());
    }

    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_RejectsNonGoldenPilotRequests()
    {
        var generator = CreateGenerator(CreateWorkingDirectory());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            EventId,
            "OTHER-REGION",
            "en"), CancellationToken.None));

        Assert.Contains("Approved enriched question-driven scene plan was not found", ex.Message);
    }


    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedProductionPlanRequest()
    {
        const string geminidsEventId = "e60aa11f-ad8c-440f-ad49-2079a435f8c1";
        var planId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
        var intelligenceId = Guid.Parse(geminidsEventId);
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan(geminidsEventId));
        var generator = CreateGenerator(workingDirectory);
        var context = new ProductionPipelineExecutionContext(
            UseProductionPipeline: true,
            ContentGenerationPlanId: planId,
            AstronomyEventIntelligenceId: intelligenceId,
            SourceExternalEventId: "meteor-shower-geminids-2026",
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            ContentGenerationPlanStatus: "Planned",
            ContentGenerationPlanPlanStatus: "Approved",
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: true,
            VerificationStatus: "Approximate",
            ContentStrategy: "LocalViewingGuide",
            RegionId: RegionId,
            Language: "en",
            RequestedOutputs: ["ShortVideo", "LongVideo"],
            Category: "RareEventAlert",
            PlannedFormat: "ShortAndLongVideo");

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            geminidsEventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            ProductionContext: context), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(geminidsEventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
    }


    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedRareEventVideoPlanWithProductionStatusAndNoExternalEventId()
    {
        const string geminidsEventId = "e60aa11f-ad8c-440f-ad49-2079a435f8c1";
        var planId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
        var intelligenceId = Guid.Parse(geminidsEventId);
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan(geminidsEventId));
        var generator = CreateGenerator(workingDirectory);
        var context = new ProductionPipelineExecutionContext(
            UseProductionPipeline: true,
            ContentGenerationPlanId: planId,
            AstronomyEventIntelligenceId: intelligenceId,
            SourceExternalEventId: null,
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            ContentGenerationPlanStatus: "ProductionRunning",
            ContentGenerationPlanPlanStatus: "ProductionRunning",
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: true,
            VerificationStatus: "Verified",
            ContentStrategy: "LocalViewingGuide",
            RegionId: RegionId,
            Language: "en",
            RequestedOutputs: ["LongVideo"],
            Category: "RareEventAlert",
            PlannedFormat: "LongVideo");

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            geminidsEventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            ProductionContext: context), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(geminidsEventId, result.EventId);
    }


    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_ProductionMeteorShowerUsesStrategyIntelligenceInsteadOfStalePilotPlan()
    {
        const string geminidsEventId = "e60aa11f-ad8c-440f-ad49-2079a435f8c1";
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan(geminidsEventId));
        var generator = CreateGenerator(workingDirectory);
        var context = BuildMeteorProductionContext(Guid.Parse(geminidsEventId));

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            geminidsEventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: true,
            ProductionContext: context), CancellationToken.None);

        Assert.True(result.IsValid);
        var combined = string.Join(" ", result.Narration.Scenes.Select(scene => $"{scene.SourceAnswer} {scene.ViewerTakeaway} {scene.NarrationText} {scene.CaptionText}"));
        Assert.Contains("Geminids Meteor Shower", combined);
        Assert.Contains("2026-12-14 00:00–05:00 IST", combined);
        Assert.Contains("east to overhead after 10 PM", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low moon interference", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dark sky", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No telescope", combined);
        Assert.DoesNotContain("Venus", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jupiter", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("after sunset", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("look west", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7:23 PM IST", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_AllowsDbApprovedProductionPlanWhenCategoryIsNotRareEventAlert()
    {
        const string geminidsEventId = "e60aa11f-ad8c-440f-ad49-2079a435f8c1";
        var planId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
        var intelligenceId = Guid.Parse(geminidsEventId);
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan(geminidsEventId));
        var generator = CreateGenerator(workingDirectory);
        var context = new ProductionPipelineExecutionContext(
            UseProductionPipeline: true,
            ContentGenerationPlanId: planId,
            AstronomyEventIntelligenceId: intelligenceId,
            SourceExternalEventId: "daily-sky",
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: true,
            VerificationStatus: "Verified",
            ContentStrategy: "LocalViewingGuide",
            RegionId: RegionId,
            Language: "en",
            RequestedOutputs: ["ShortVideo"],
            Category: "DailySkyGuide",
            PlannedFormat: "ShortVideo");

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            geminidsEventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false,
            ProductionContext: context), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(geminidsEventId, result.EventId);
    }


    [Fact]
    public async Task GenerateQuestionDrivenNarrationAsync_AllowsNamedFullMoonProductionPlanAndUsesMoonIntelligence()
    {
        const string fullMoonEventId = "5b84d088-7102-4d5f-b0c0-8cea19091ea6";
        var workingDirectory = CreateWorkingDirectory();
        await WriteEnrichedQuestionDrivenScenePlanAsync(workingDirectory, BuildEnrichedPlan(fullMoonEventId));
        var generator = CreateGenerator(workingDirectory);
        var context = BuildNamedFullMoonProductionContext(Guid.Parse(fullMoonEventId));

        var result = await generator.GenerateQuestionDrivenNarrationAsync(new QuestionDrivenNarrationRequest(
            fullMoonEventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: true,
            ProductionContext: context,
            PlanId: context.ContentGenerationPlanId,
            EventType: "NamedFullMoon",
            Title: "Snow Moon Full Moon",
            ShortTitle: "Snow Moon",
            PrimaryObjects: ["Moon"],
            SecondaryObjects: [],
            LocalPeakTime: "2026-02-02 04:39 IST",
            SkyDirectionHint: "eastern sky after sunset and high overhead near midnight",
            BestViewingWindowLocal: "2026-02-01 evening to 2026-02-02 pre-dawn IST",
            StrategyId: "NamedFullMoon",
            SourceOfEventId: "AstronomyEventIntelligenceId"), CancellationToken.None);

        Assert.True(result.IsValid);
        var combined = string.Join(" ", result.Narration.Scenes.Select(scene => $"{scene.SourceAnswer} {scene.ViewerTakeaway} {scene.NarrationText} {scene.CaptionText}"));
        Assert.Contains("Snow Moon Full Moon", combined);
        Assert.Contains("Moon", combined);
        Assert.DoesNotContain("Venus", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Jupiter", combined, StringComparison.OrdinalIgnoreCase);
    }


    private static ProductionPipelineExecutionContext BuildMeteorProductionContext(Guid intelligenceId)
    {
        var planId = Guid.Parse("2af19a66-3777-47c7-8672-6e9d6245ac1c");
        var intelligence = new ProductionEventIntelligence(
            Domain: "Astronomy",
            EventType: "MeteorShower",
            Title: "Geminids Meteor Shower",
            ShortTitle: "Geminids",
            EventDate: DateTimeOffset.Parse("2026-12-13T18:30:00Z"),
            PeakUtc: DateTimeOffset.Parse("2026-12-13T18:30:00Z"),
            LocalPeakTime: "2026-12-14 00:00 IST",
            BestViewingWindowLocal: "2026-12-14 00:00–05:00 IST",
            SkyDirectionHint: "east to overhead after 10 PM",
            VisibilityRegion: RegionId,
            PrimaryObjects: ["Geminids Meteor Shower"],
            SecondaryObjects: [],
            ViewingQuality: "Excellent",
            MoonInterference: "low moon interference",
            MoonIlluminationPercent: 20,
            ScientificContext: "Earth crosses debris that produces bright meteor streaks from the shower radiant across the whole sky.",
            ViewerInstructions: ["No telescope needed", "Choose a dark sky", "Watch meteor streaks across the whole sky"],
            VisualMotifs: ["meteor streaks", "radiant", "whole sky", "dark sky"],
            SceneStrategy: [],
            QualityWarnings: [],
            ForbiddenTerms: ["Venus", "Jupiter", "conjunction", "after sunset", "look west", "7:23 PM IST"],
            StrategyId: "MeteorShower",
            ResolvedObjectNames: ["Geminids Meteor Shower"],
            ForbiddenObjectNames: ["Venus", "Jupiter"],
            RequiredVisualObjects: ["meteor streaks", "radiant", "whole sky"],
            RequiredNarrationFacts: ["2026-12-14 00:00–05:00 IST", "east to overhead after 10 PM", "low moon interference", "dark sky", "no telescope needed"],
            PreferredViewingWindow: "2026-12-14 00:00–05:00 IST");

        return new ProductionPipelineExecutionContext(
            UseProductionPipeline: true,
            ContentGenerationPlanId: planId,
            AstronomyEventIntelligenceId: intelligenceId,
            SourceExternalEventId: "meteor-shower-geminids-2026",
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            ContentGenerationPlanStatus: "ProductionRunning",
            ContentGenerationPlanPlanStatus: "ProductionRunning",
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: true,
            VerificationStatus: "Verified",
            ContentStrategy: "LocalViewingGuide",
            RegionId: RegionId,
            Language: "en",
            RequestedOutputs: ["ShortVideo", "LongVideo"],
            Category: "RareEventAlert",
            PlannedFormat: "ShortAndLongVideo",
            EventType: "MeteorShower",
            ProductionEventIntelligence: intelligence,
            MediaEventStrategy: new MeteorShowerStrategy());
    }

    private static ProductionPipelineExecutionContext BuildNamedFullMoonProductionContext(Guid intelligenceId)
    {
        var planId = Guid.Parse("1e3f4ab7-65d0-46d9-9a86-a68a3cb7d979");
        var intelligence = new ProductionEventIntelligence(
            Domain: "Astronomy",
            EventType: "NamedFullMoon",
            Title: "Snow Moon Full Moon",
            ShortTitle: "Snow Moon",
            EventDate: DateTimeOffset.Parse("2026-02-01T23:09:00Z"),
            PeakUtc: DateTimeOffset.Parse("2026-02-01T23:09:00Z"),
            LocalPeakTime: "2026-02-02 04:39 IST",
            BestViewingWindowLocal: "2026-02-01 evening to 2026-02-02 pre-dawn IST",
            SkyDirectionHint: "eastern sky after sunset and high overhead near midnight",
            VisibilityRegion: RegionId,
            PrimaryObjects: ["Moon"],
            SecondaryObjects: [],
            ViewingQuality: "Good",
            MoonInterference: "full Moon is the event target",
            MoonIlluminationPercent: 100,
            ScientificContext: "The Snow Moon is February's named full Moon and appears bright through the night.",
            ViewerInstructions: ["Watch the Moon rise", "Use binoculars for lunar texture", "Choose a clear eastern view"],
            VisualMotifs: ["full Moon", "lunar disc", "moonlit sky"],
            SceneStrategy: [],
            QualityWarnings: [],
            ForbiddenTerms: ["Venus", "Jupiter", "planet pairing", "meteor shower"],
            StrategyId: "NamedFullMoon",
            ResolvedObjectNames: ["Moon"],
            ForbiddenObjectNames: ["Venus", "Jupiter"],
            RequiredVisualObjects: ["Moon", "full Moon", "lunar disc"],
            RequiredNarrationFacts: ["Snow Moon", "Moon", "2026-02-02 04:39 IST"],
            PreferredViewingWindow: "2026-02-01 evening to 2026-02-02 pre-dawn IST");

        return new ProductionPipelineExecutionContext(
            UseProductionPipeline: true,
            ContentGenerationPlanId: planId,
            AstronomyEventIntelligenceId: intelligenceId,
            SourceExternalEventId: "snow-moon-full-moon-2026",
            IsDbApprovedPlanExecution: true,
            ContentGenerationPlanExists: true,
            ContentGenerationPlanStatus: "ProductionRunning",
            ContentGenerationPlanPlanStatus: "ProductionRunning",
            AstronomyEventIntelligenceExists: true,
            AutoGenerateAllowed: false,
            VerificationStatus: "Verified",
            ContentStrategy: "LocalViewingGuide",
            RegionId: RegionId,
            Language: "en",
            RequestedOutputs: ["ShortVideo", "LongVideo"],
            Category: "FullMoon",
            PlannedFormat: "ShortAndLongVideo",
            EventType: "NamedFullMoon",
            ProductionEventIntelligence: intelligence,
            MediaEventStrategy: new NamedFullMoonStrategy());
    }


    private static QuestionDrivenNarrationGenerator CreateGenerator(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<QuestionDrivenNarrationGenerator>.Instance);

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan(string? eventId = null) => new(
        eventId ?? EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter will appear close together in Udaipur’s evening sky."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look toward the western sky, about one-third above the horizon."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "Best viewing is around 7:23 PM IST, shortly after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find bright Venus first, then look slightly nearby for Jupiter."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "If skies are clear, step outside after sunset and enjoy the view.")
        ],
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static EnrichedQuestionSceneDto BuildScene(int sceneNumber, string questionType, string scenePurpose, string viewerQuestion, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            sourceAnswer,
            "CasualSkyWatcher",
            "Beginner",
            $"Viewer takeaway for {questionType}.",
            $"Narration intent for {questionType}.",
            $"Visual intent for {questionType}.",
            $"Image prompt intent for {questionType}.",
            $"Overlay intent for {questionType}.",
            $"Accessibility intent for {questionType}.",
            true);

    private static async Task WriteEnrichedQuestionDrivenScenePlanAsync(string workingDirectory, EnrichedQuestionScenePlanDto plan)
    {
        var path = BuildPlanPath(workingDirectory, "question-driven-scene-plan.enriched.json", plan.EventId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions));
    }

    private static string BuildPlanPath(string workingDirectory, string fileName, string? eventId = null)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", eventId ?? EventId, "question-engine", fileName);

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-driven-narration-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Normalize(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
