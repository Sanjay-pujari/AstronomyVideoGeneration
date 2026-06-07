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
        Assert.Equal(AstronomyQuestionTypes.What, result.EnrichedScenePlan.Scenes.First().QuestionType);
        Assert.Equal(AstronomyQuestionTypes.Action, result.EnrichedScenePlan.Scenes.Last().QuestionType);

        var what = result.EnrichedScenePlan.Scenes.First();
        Assert.Equal("OpeningOverview", what.ScenePurpose);
        Assert.Equal("Understand what sky event is happening.", what.ViewerTakeaway);
        Assert.Equal("Create curiosity and introduce the event in a warm, simple way.", what.NarrationIntent);
        Assert.Equal("Show a hero astronomy scene with Venus and Jupiter clearly emphasized.", what.VisualIntent);
        Assert.Equal("Generate a cinematic western evening sky background suitable for a hero opening.", what.ImagePromptIntent);
        Assert.Equal("Use a short title and one clear viewing cue.", what.OverlayIntent);
        Assert.Equal("Even without audio, the viewer should know the event is Venus and Jupiter tonight.", what.AccessibilityIntent);
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
        Assert.Equal(6, document.RootElement.GetProperty("scenes").GetArrayLength());
        Assert.Equal("Generate a clean sky-location infographic background with horizon space.", document.RootElement.GetProperty("scenes")[1].GetProperty("imagePromptIntent").GetString());
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
