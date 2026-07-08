using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Rendering;

namespace Astronomy.MediaFactory.Tests;

public class VisualQualityFrameworkTests
{
    [Fact]
    public void Framework_LoadsAstronomyRules()
    {
        var framework = VisualQualityFramework.Astronomy();

        Assert.Equal("RC1-A.1", framework.FrameworkVersion);
        Assert.Contains("Premium Science Documentary", framework.EditorialStyle);
        Assert.Contains("Astronomy", framework.DomainOverrides.Keys);
        Assert.Contains(framework.DomainOverrides["Astronomy"], rule => rule.Contains("Planets must be physically recognizable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeroPromptBuilder_ConsumesVisualQualityFramework()
    {
        var source = File.ReadAllText(Path.Combine("Backend", "src", "Astronomy.MediaFactory.Infrastructure", "Persistence", "HeroAssetIntelligenceEngine.cs"));

        Assert.Contains("VisualQualityFramework.Astronomy().BuildPromptPolicyText()", source);
        Assert.Contains("VisualQualityFramework.Astronomy().CreateReview(\"Hero\")", source);
    }

    [Fact]
    public void GalleryPromptBuilder_ConsumesVisualQualityFramework()
    {
        var context = new AstroPulseGalleryService.GalleryContext("Planetary", "Jupiter Venus conjunction", "Jupiter and Venus", "premium astronomy", "Jun 7, 2026", "19:00", "west", "en", "en", "UTC", new EventObjectContext(["Jupiter", "Venus"], 2, "Jupiter and Venus", "Jupiter and Venus", "Jupiter and Venus", "Jupiter", "Venus", false, true, "Planetary", "test", [], [], true, false), []);
        var contract = AstroPulseGalleryService.ResolveGalleryContentContractForTesting(context);
        var topics = AstroPulseGalleryService.BuildTopics(contract);

        Assert.NotEmpty(topics);
        Assert.All(topics, topic => Assert.Contains(VisualQualityFramework.Version, topic.AzureImage2Prompt));
        Assert.All(topics, topic => Assert.Contains("One Hero Per Frame", topic.AzureImage2Prompt));
    }

    [Fact]
    public async Task StoryFramePromptBuilders_ConsumeVisualQualityFrameworkWithoutRenderingChanges()
    {
        var timeline = CreateTimeline();

        var output = Path.Combine(Path.GetTempPath(), "visual-quality-framework-tests", Guid.NewGuid().ToString("N"));
        await new ShortStoryFramePlanner().WriteArtifactsAsync(timeline, output);
        await new LongStoryFramePlanner().WriteArtifactsAsync(timeline, output);
        var options = VisualIntelligenceJson.CreateSerializerOptions();
        var shortPackage = System.Text.Json.JsonSerializer.Deserialize<StoryFramePromptPackage>(await File.ReadAllTextAsync(Path.Combine(output, "short-story-frames", "diagnostics", "frame-prompts", "frame01-hook-prompt.json")), options)!;
        var longPackage = System.Text.Json.JsonSerializer.Deserialize<StoryFramePromptPackage>(await File.ReadAllTextAsync(Path.Combine(output, "long-story-frames", "diagnostics", "frame-prompts", "frame01-hook-prompt.json")), options)!;
        Assert.True(File.Exists(Path.Combine(output, "short-story-frames", "diagnostics", "VisualQualityFrameworkReview.json")));
        Assert.True(File.Exists(Path.Combine(output, "long-story-frames", "diagnostics", "VisualQualityFrameworkReview.json")));

        Assert.All(new[] { shortPackage, longPackage }, package =>
        {
            Assert.Contains(VisualQualityFramework.Version, package.PositivePrompt);
            Assert.Equal(false, package.Diagnostics["azureCallsMade"]);
            Assert.Equal(false, package.Diagnostics["imageGenerationRequested"]);
            Assert.Equal(false, package.Diagnostics["scenePromptReplacementApplied"]);
            Assert.Equal(VisualQualityFramework.Version, package.Versions["visualQualityFramework"]);
        });
    }

    [Fact]
    public void ThumbnailPromptBuilder_ConsumesVisualQualityFramework()
    {
        var prompt = new ThumbnailV7BackgroundPromptBuilder().Build(
            new EventVisualIntelligence("Conjunction", "Jupiter Venus conjunction", "Jupiter Venus", "west", ["Jupiter", "Venus"], "premium astronomy"),
            new HeroCompositionModel("hero prompt", "west", "premium astronomy"),
            new GalleryCompositionModel("west", "premium astronomy", []));

        Assert.Contains(VisualQualityFramework.Version, prompt);
        Assert.Contains("Planets must be physically recognizable", prompt);
    }

    private static NarrativeTimeline CreateTimeline()
    {
        var beat = new NarrativeBeat
        {
            BeatId = "beat-01",
            BeatRole = NarrativeBeatRole.Hook,
            BeatGoal = "Open",
            ViewerQuestion = "What is happening?",
            ViewerEmotion = "wonder",
            TargetDuration = 5,
            VisualPriority = "Show Jupiter and Venus",
            NarrationPriority = "Explain conjunction",
            EducationalPriority = "Recognition",
            RecommendedComposition = "One hero per frame",
            Confidence = .9
        };

        return new NarrativeTimeline
        {
            TimelineId = "timeline-test",
            TimelineType = NarrativeTimelineType.ShortDocumentary,
            StoryId = "story-test",
            StoryTitle = "Jupiter Venus conjunction",
            TargetDuration = 5,
            Beats = [beat],
            BeatTimingAllocation = new Dictionary<string, double> { [NarrativeBeatRole.Hook.ToString()] = 5 },
            Confidence = .9
        };
    }
}
