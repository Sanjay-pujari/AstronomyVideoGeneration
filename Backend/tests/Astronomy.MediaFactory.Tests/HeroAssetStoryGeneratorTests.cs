using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Tests;

public sealed class HeroAssetStoryGeneratorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string EventId = "e7013ee4-55c6-4f01-b1d0-7c500f26f98b";
    private const string RegionId = "IN-RJ-UDAIPUR";

    [Fact]
    public async Task GenerateHeroAssetStoryAsync_DryRunReturnsPreviewOnlyAndUsesWhatWhereWhenWhySources()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: true,
            OverwriteExisting: false), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(EventId, result.EventId);
        Assert.Empty(result.GeneratedFiles);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(BuildOutputPath(workingDirectory)));
        Assert.Equal("TWO BRIGHT PLANETS TOGETHER", result.HeroStory.HeroHook);
        Assert.Equal("Venus and Jupiter will appear close together after sunset in Udaipur’s western sky.", result.HeroStory.HeroMessage);
        Assert.Equal("Look west shortly after sunset.", result.HeroStory.HeroAction);
        Assert.Equal("Venus and Jupiter above the western horizon.", result.HeroStory.HeroVisualFocus);
        Assert.Equal("Wonder", result.HeroStory.HeroEmotion);
        Assert.Equal("ScrollStoppingHeroAsset", result.HeroStory.HeroPlatformIntent);
        Assert.Equal(95, result.HeroStory.Scores.ScrollStoppingScore);
        Assert.Equal(95, result.HeroStory.Scores.ClickabilityScore);
        Assert.Equal(90, result.HeroStory.Scores.ShareabilityScore);
        Assert.Equal(95, result.HeroStory.Scores.UnderstandabilityScore);
        Assert.True(result.HeroStory.StoryScore >= 90);
        Assert.Equal("Venus and Jupiter will appear close together in Udaipur’s evening sky.", result.HeroStory.HeroStorySource.What);
        Assert.Equal("Look toward the western sky, about one-third above the horizon.", result.HeroStory.HeroStorySource.Where);
        Assert.Equal("Best viewing is around 7:23 PM IST, shortly after sunset.", result.HeroStory.HeroStorySource.When);
        Assert.Equal("Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing.", result.HeroStory.HeroStorySource.Why);
    }

    [Fact]
    public async Task GenerateHeroAssetStoryAsync_NonDryRunWritesHeroStoryJsonOnly()
    {
        var workingDirectory = CreateWorkingDirectory();
        await WriteInputFilesAsync(workingDirectory);
        var generator = CreateGenerator(workingDirectory);

        var result = await generator.GenerateHeroAssetStoryAsync(new HeroAssetStoryGenerationRequest(
            EventId,
            RegionId,
            "en",
            DryRun: false,
            OverwriteExisting: false), CancellationToken.None);

        var outputPath = BuildOutputPath(workingDirectory);
        Assert.True(result.IsValid);
        Assert.Single(result.GeneratedFiles);
        Assert.Equal(outputPath.Replace('\\', '/'), result.GeneratedFiles.Single());
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-landscape.png")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-square.png")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(outputPath)!, "hero-portrait.png")));

        var saved = JsonSerializer.Deserialize<HeroAssetStoryDto>(await File.ReadAllTextAsync(outputPath), JsonOptions);
        Assert.NotNull(saved);
        Assert.Equal(result.HeroStory.HeroHook, saved!.HeroHook);
    }

    private static HeroAssetStoryGenerator CreateGenerator(string workingDirectory)
        => new(Options.Create(new RenderingOptions { WorkingDirectory = workingDirectory }), NullLogger<HeroAssetStoryGenerator>.Instance);

    private static async Task WriteInputFilesAsync(string workingDirectory)
    {
        var questionEngineRoot = BuildQuestionEnginePath(workingDirectory);
        Directory.CreateDirectory(questionEngineRoot);
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-answer-set.json"), JsonSerializer.Serialize(BuildQuestionAnswerSet(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-scene-plan.enriched.json"), JsonSerializer.Serialize(BuildEnrichedPlan(), JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(questionEngineRoot, "question-driven-narration.json"), JsonSerializer.Serialize(BuildNarration(), JsonOptions));

        var sceneApprovalRoot = Path.Combine(questionEngineRoot, "scene-approval-v3");
        Directory.CreateDirectory(sceneApprovalRoot);
        for (var sceneNumber = 1; sceneNumber <= 6; sceneNumber++)
            await File.WriteAllBytesAsync(Path.Combine(sceneApprovalRoot, $"scene-{sceneNumber:000}-final.png"), [137, 80, 78, 71]);
    }

    private static QuestionAnswerSetDto BuildQuestionAnswerSet() => new(
        null,
        Guid.Parse(EventId),
        "VENUS_JUPITER_CLOSE_PAIRING",
        "Venus and Jupiter Close Pairing",
        "PlanetConjunction",
        RegionId,
        "en",
        "v1",
        "Approved",
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"),
        [
            new QuestionAnswerDto(null, AstronomyQuestionTypes.What, "What is happening?", "What", "Venus and Jupiter will appear close together in Udaipur’s evening sky.", 1),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Where, "Where should I look?", "Where", "Look toward the western sky, about one-third above the horizon.", 2),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.When, "When is best?", "When", "Best viewing is around 7:23 PM IST, shortly after sunset.", 3),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.How, "How can I find it?", "How", "Find bright Venus first, then look slightly nearby for Jupiter.", 4),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Why, "Why does it matter?", "Why", "Venus and Jupiter appear only 1.63° apart, creating a striking planetary pairing.", 5),
            new QuestionAnswerDto(null, AstronomyQuestionTypes.Action, "What should I do?", "Action", "If skies are clear, step outside after sunset and enjoy the view.", 6)
        ]);

    private static EnrichedQuestionScenePlanDto BuildEnrichedPlan() => new(
        EventId,
        RegionId,
        "en",
        "CasualSkyWatcher",
        "Beginner",
        [
            BuildScene(1, AstronomyQuestionTypes.What, "Venus and Jupiter appear close together tonight."),
            BuildScene(2, AstronomyQuestionTypes.Where, "Look west above the horizon for Venus and Jupiter."),
            BuildScene(3, AstronomyQuestionTypes.When, "The best time is around 7:23 PM IST after sunset."),
            BuildScene(4, AstronomyQuestionTypes.How, "Find Venus first, then look nearby for Jupiter while facing west."),
            BuildScene(5, AstronomyQuestionTypes.Why, "Venus and Jupiter form a close bright planetary pairing."),
            BuildScene(6, AstronomyQuestionTypes.Action, "Step outside tonight and look west for Venus and Jupiter.")
        ],
        true,
        DateTimeOffset.Parse("2026-06-07T14:00:00Z"));

    private static EnrichedQuestionSceneDto BuildScene(int sceneNumber, string questionType, string sourceAnswer)
        => new(
            sceneNumber,
            questionType,
            "Hero story input scene.",
            $"Viewer question for {questionType}.",
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
            BuildNarrationScene(1, AstronomyQuestionTypes.What, "Tonight, Venus and Jupiter appear close in Udaipur’s western sky."),
            BuildNarrationScene(2, AstronomyQuestionTypes.Where, "Face west and scan above the horizon to spot Venus and Jupiter."),
            BuildNarrationScene(3, AstronomyQuestionTypes.When, "The best viewing time is around 7:23 PM IST, shortly after sunset."),
            BuildNarrationScene(4, AstronomyQuestionTypes.How, "Find Venus first, then look nearby for Jupiter while you face west."),
            BuildNarrationScene(5, AstronomyQuestionTypes.Why, "It matters because two bright planets make a close, beautiful pairing."),
            BuildNarrationScene(6, AstronomyQuestionTypes.Action, "If skies are clear, step outside tonight and look west for Venus and Jupiter.")
        ],
        54,
        DateTimeOffset.Parse("2026-06-07T14:05:00Z"));

    private static QuestionDrivenNarrationSceneDto BuildNarrationScene(int sceneNumber, string questionType, string narrationText)
        => new(
            sceneNumber,
            questionType,
            "Hero story narration source.",
            $"Viewer question for {questionType}.",
            narrationText,
            narrationText,
            $"Narration intent for {questionType}.",
            narrationText,
            9,
            "Warm and clear.",
            narrationText);

    private static string BuildQuestionEnginePath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "question-engine");

    private static string BuildOutputPath(string workingDirectory)
        => Path.Combine(workingDirectory, "assets", RegionId, "events", EventId, "hero-assets", "hero-asset-story.json");

    private static string CreateWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hero-asset-story-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
