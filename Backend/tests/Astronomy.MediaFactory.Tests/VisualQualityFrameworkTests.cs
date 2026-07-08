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

        Assert.Contains("VisualPromptPolicyComposer.Compose(VisualPromptProduct.Hero).PositiveGuidance", source);
        Assert.Contains("VisualQualityFramework.Astronomy().CreateReview(\"Hero\")", source);
        Assert.Contains("VisualPromptPolicyComposer.CreateReview(VisualPromptProduct.Hero)", source);
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
        Assert.All(topics, topic => Assert.Contains("page-specific editorial composition", topic.AzureImage2Prompt, StringComparison.OrdinalIgnoreCase));
        Assert.All(topics, topic => Assert.Contains("no fake labels or embedded text", topic.AzureImage2Prompt, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StoryFramePromptBuilders_ConsumeVisualQualityFrameworkWithoutRenderingChanges()
    {
        var shortTimeline = CreateTimeline(NarrativeTimelineType.ShortDocumentary, [NarrativeBeatRole.Hook, NarrativeBeatRole.Recognition, NarrativeBeatRole.Explanation, NarrativeBeatRole.Observation, NarrativeBeatRole.CallToAction]);
        var longTimeline = CreateTimeline(NarrativeTimelineType.LongDocumentary, [NarrativeBeatRole.Hook, NarrativeBeatRole.Recognition, NarrativeBeatRole.ExplanationA, NarrativeBeatRole.ExplanationB, NarrativeBeatRole.ObservationA, NarrativeBeatRole.ObservationB, NarrativeBeatRole.InterestingFact, NarrativeBeatRole.Memory, NarrativeBeatRole.CallToAction]);

        var output = Path.Combine(Path.GetTempPath(), "visual-quality-framework-tests", Guid.NewGuid().ToString("N"));
        await new ShortStoryFramePlanner().WriteArtifactsAsync(shortTimeline, output);
        await new LongStoryFramePlanner().WriteArtifactsAsync(longTimeline, output);
        var options = VisualIntelligenceJson.CreateSerializerOptions();
        var shortPackage = System.Text.Json.JsonSerializer.Deserialize<StoryFramePromptPackage>(await File.ReadAllTextAsync(Path.Combine(output, "short-story-frames", "diagnostics", "frame-prompts", "frame01-hook-prompt.json")), options)!;
        var longPackage = System.Text.Json.JsonSerializer.Deserialize<StoryFramePromptPackage>(await File.ReadAllTextAsync(Path.Combine(output, "long-story-frames", "diagnostics", "frame-prompts", "frame01-hook-prompt.json")), options)!;
        Assert.True(File.Exists(Path.Combine(output, "short-story-frames", "diagnostics", "VisualQualityFrameworkReview.json")));
        Assert.True(File.Exists(Path.Combine(output, "long-story-frames", "diagnostics", "VisualQualityFrameworkReview.json")));
        Assert.True(File.Exists(Path.Combine(output, "short-story-frames", "diagnostics", "VisualPromptPolicyReview.json")));
        Assert.True(File.Exists(Path.Combine(output, "long-story-frames", "diagnostics", "VisualPromptPolicyReview.json")));

        Assert.All(new[] { shortPackage, longPackage }, package =>
        {
            Assert.Contains(VisualQualityFramework.Version, package.PositivePrompt);
            Assert.Contains(DocumentaryVisualLanguage.Version, package.PositivePrompt);
            Assert.Contains("No generated embedded text", package.PositivePrompt);
            Assert.Contains("no fake labels or embedded text", package.NegativePrompt, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(false, package.Diagnostics["azureCallsMade"]);
            Assert.Equal(false, package.Diagnostics["imageGenerationRequested"]);
            Assert.Equal(false, package.Diagnostics["scenePromptReplacementApplied"]);
            Assert.Equal(VisualQualityFramework.Version, package.Versions["visualQualityFramework"]);
        });
        Assert.Contains("native 9:16 portrait documentary composition", shortPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strong vertical hierarchy", shortPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native 16:9 landscape documentary composition", longPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clear cinematic focal point", longPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crop", shortPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crop", longPackage.PositivePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedVisualPromptPolicyComposer_AppliesProductGuidanceAndNegativePolicy()
    {
        var hero = VisualPromptPolicyComposer.Compose(VisualPromptProduct.Hero);
        var gallery = VisualPromptPolicyComposer.Compose(VisualPromptProduct.Gallery);
        var longFrame = VisualPromptPolicyComposer.Compose(VisualPromptProduct.LongStoryFrame);
        var shortFrame = VisualPromptPolicyComposer.Compose(VisualPromptProduct.ShortStoryFrame);

        Assert.Contains("premium science documentary style", hero.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one hero per frame", hero.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page-specific editorial composition", gallery.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native 16:9 landscape documentary composition", longFrame.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native 9:16 portrait documentary composition", shortFrame.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.All(new[] { hero, gallery, longFrame, shortFrame }, policy =>
        {
            Assert.Contains("no fantasy art", policy.NegativeGuidance, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no fake labels or embedded text", policy.NegativeGuidance, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Azure", policy.PositiveGuidance, StringComparison.OrdinalIgnoreCase);
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

    private static NarrativeTimeline CreateTimeline(NarrativeTimelineType timelineType, IReadOnlyList<NarrativeBeatRole> roles)
    {
        var beats = roles.Select((role, index) => new NarrativeBeat
        {
            BeatId = $"beat-{index + 1:00}",
            BeatRole = role,
            BeatGoal = role.ToString(),
            ViewerQuestion = "What is happening?",
            ViewerEmotion = "wonder",
            TargetDuration = 5,
            VisualPriority = "Show Jupiter and Venus",
            NarrationPriority = "Explain conjunction",
            EducationalPriority = "Recognition",
            RecommendedComposition = "One hero per frame",
            Confidence = .9
        }).ToArray();

        return new NarrativeTimeline
        {
            TimelineId = "timeline-test",
            TimelineType = timelineType,
            StoryId = "story-test",
            StoryTitle = "Jupiter Venus conjunction",
            TargetDuration = beats.Sum(beat => beat.TargetDuration),
            Beats = beats,
            BeatTimingAllocation = beats.ToDictionary(beat => beat.BeatRole.ToString(), beat => beat.TargetDuration),
            Confidence = .9
        };
    }
}
