using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class QuestionDrivenVisualComposerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateQuestionDrivenVisualsAsync_DryRunReturnsCompletePreviewPlanWithoutWritingFiles()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var composer = CreateComposer(workingDirectory);

        var result = await composer.GenerateQuestionDrivenVisualsAsync(new QuestionDrivenVisualGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.Equal(EventId, result.EventId);
        Assert.Equal(6, result.SceneCount);
        Assert.Equal(0, result.FinalImageCount);
        Assert.Equal(0, result.SrtCount);
        Assert.Equal(6, result.PlannedImageCount);
        Assert.Equal(6, result.PlannedSrtCount);
        Assert.Equal(6, result.PlannedReviewCount);
        Assert.Equal(0, result.ApprovedSceneCount);
        Assert.Equal(0, result.FailedSceneCount);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.PlannedScenes);
        Assert.Equal(6, result.PlannedScenes!.Count);
        Assert.False(Directory.Exists(BuildSceneApprovalPath(workingDirectory)));

        Assert.All(result.PlannedScenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.NarrationText));
            Assert.False(string.IsNullOrWhiteSpace(scene.CaptionText));
            Assert.False(string.IsNullOrWhiteSpace(scene.AiBackgroundPrompt));
            Assert.False(string.IsNullOrWhiteSpace(scene.PlannedOutputs.FinalImagePath));
            Assert.False(string.IsNullOrWhiteSpace(scene.PlannedOutputs.SrtPath));
            Assert.True(scene.ValidationPreview.ImageSceneSpecific);
            Assert.True(scene.ValidationPreview.NarrationAligned);
            Assert.True(scene.ValidationPreview.SrtReady);
            Assert.True(scene.ValidationPreview.AccessibilityReady);
            Assert.Empty(scene.ValidationPreview.Issues);
        });

        var what = result.PlannedScenes[0];
        Assert.Equal(AstronomyQuestionTypes.What, what.QuestionType);
        Assert.Contains("professional astronomy magazine cover", what.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("golden-orange horizon", what.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Venus & Jupiter", what.ProgrammaticOverlayPlan.Title);
        Assert.Contains("Venus", what.ProgrammaticOverlayPlan.LocalAssetObjects);
        Assert.Contains("Jupiter", what.ProgrammaticOverlayPlan.LocalAssetObjects);
        Assert.Empty(what.ProgrammaticOverlayPlan.TimingMarkers);
        Assert.Empty(what.ProgrammaticOverlayPlan.Steps);

        var where = result.PlannedScenes[1];
        Assert.Equal(AstronomyQuestionTypes.Where, where.QuestionType);
        Assert.Contains("observation chart", where.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("West", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Venus", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Jupiter", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Horizon", where.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("West", where.ProgrammaticOverlayPlan.DirectionMarkers);

        var when = result.PlannedScenes[2];
        Assert.Equal(AstronomyQuestionTypes.When, when.QuestionType);
        Assert.Contains("real twilight transition", when.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7:23 PM IST", when.ProgrammaticOverlayPlan.TimingMarkers);
        Assert.Contains("Time", when.ProgrammaticOverlayPlan.Title);

        var how = result.PlannedScenes[3];
        Assert.Equal(AstronomyQuestionTypes.How, how.QuestionType);
        Assert.Equal(new[] { "Find Venus", "Look nearby for Jupiter", "Face west" }, how.ProgrammaticOverlayPlan.Steps);
        Assert.NotEmpty(how.ProgrammaticOverlayPlan.Arrows);

        var why = result.PlannedScenes[4];
        Assert.Equal(AstronomyQuestionTypes.Why, why.QuestionType);
        Assert.Contains("two of the brightest worlds sharing the evening sky", why.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Why It Matters", why.ProgrammaticOverlayPlan.Title);
        Assert.Contains("Venus", why.ProgrammaticOverlayPlan.Labels);
        Assert.Contains("Jupiter", why.ProgrammaticOverlayPlan.Labels);

        var action = result.PlannedScenes[5];
        Assert.Equal(AstronomyQuestionTypes.Action, action.QuestionType);
        Assert.Contains("poster-quality twilight", action.AiBackgroundPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Step Outside Tonight", action.ProgrammaticOverlayPlan.Title);
        Assert.Empty(action.ProgrammaticOverlayPlan.Steps);
    }

    private static QuestionDrivenVisualComposer CreateComposer(string workingDirectory)
        => new(
            Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }),
            new QuestionDrivenImagePromptGenerator(),
            new AstronomyInfographicRenderer(
                new AstronomyBackgroundLayerRenderer(),
                new CelestialObjectLayerRenderer(),
                new SkyGuidanceLayerRenderer(),
                new EducationalLayerRenderer(),
                new AnnotationLayerRenderer()),
            NullLogger<QuestionDrivenVisualComposer>.Instance);

    private static async Task WriteInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildNarration(), JsonOptions));
    }

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan() => new(
        EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter appear close together tonight."),
            BuildScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look west above the horizon for Venus and Jupiter."),
            BuildScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best time is around 7:23 PM IST after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find Venus first, then look nearby for Jupiter while facing west."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter form a close bright planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Step outside tonight and look west for Venus and Jupiter.")
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
            sourceAnswer,
            $"Narration intent for {questionType}.",
            $"Visual intent for {questionType}.",
            $"Image prompt intent for {questionType}.",
            $"Overlay intent for {questionType}.",
            $"Accessibility intent for {questionType}.",
            true);

    private static QuestionDrivenNarrationDto BuildNarration() => new(
        EventId,
        RegionId,
        "en",
        [
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "OpeningOverview", "What is happening?", "Venus and Jupiter appear close together tonight.", "Tonight, Venus and Jupiter appear close in Udaipur’s western sky.", "Venus and Jupiter shine close tonight."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "LocationGuide", "Where should I look?", "Look west above the horizon for Venus and Jupiter.", "Face west and scan above the horizon to spot Venus and Jupiter.", "Face west above the horizon."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "TimingGuide", "When is the best time?", "The best time is around 7:23 PM IST after sunset.", "The best viewing time is around 7:23 PM IST, shortly after sunset.", "Best around 7:23 PM IST."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "ObservationGuide", "How can I find it?", "Find Venus first, then look nearby for Jupiter while facing west.", "Find Venus first, then look nearby for Jupiter while you face west.", "Find Venus, then nearby Jupiter."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "Significance", "Why is it special?", "Venus and Jupiter form a close bright planetary pairing.", "It matters because two bright planets make a close, beautiful pairing.", "A close bright planetary pairing."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "ClosingAction", "What should I do now?", "Step outside tonight and look west for Venus and Jupiter.", "If skies are clear, step outside tonight and look west for Venus and Jupiter.", "Step outside and look west.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static QuestionDrivenNarrationSceneDto BuildNarrationScene(
        int sceneNumber,
        string questionType,
        string scenePurpose,
        string viewerQuestion,
        string viewerTakeaway,
        string narrationText,
        string captionText)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            viewerTakeaway,
            viewerTakeaway,
            $"Narration intent for {questionType}.",
            narrationText,
            9,
            "Warm and clear.",
            captionText);

    private static string BuildQuestionEnginePath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine");

    private static string BuildSceneApprovalPath(string workingDirectory)
        => Path.Combine(BuildQuestionEnginePath(workingDirectory), "scene-approval-v3");

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-driven-visual-composer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
