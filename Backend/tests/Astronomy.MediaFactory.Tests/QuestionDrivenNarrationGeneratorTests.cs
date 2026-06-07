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

        Assert.Contains("golden pilot event", ex.Message);
    }

    private static QuestionDrivenNarrationGenerator CreateGenerator(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<QuestionDrivenNarrationGenerator>.Instance);

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan() => new(
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
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static EnrichedQuestionSceneDto BuildScene(int sceneNumber, string questionType, string scenePurpose, string viewerQuestion, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            scenePurpose,
            viewerQuestion,
            sourceAnswer,
            $"Viewer takeaway for {questionType}.",
            $"Narration intent for {questionType}.",
            $"Visual intent for {questionType}.",
            $"Image prompt intent for {questionType}.",
            $"Overlay intent for {questionType}.",
            $"Accessibility intent for {questionType}.",
            true);

    private static async Task WriteEnrichedQuestionDrivenScenePlanAsync(string workingDirectory, EnrichedQuestionScenePlanDto plan)
    {
        var path = BuildPlanPath(workingDirectory, "question-driven-scene-plan.enriched.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, JsonOptions));
    }

    private static string BuildPlanPath(string workingDirectory, string fileName)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine", fileName);

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "question-driven-narration-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Normalize(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
