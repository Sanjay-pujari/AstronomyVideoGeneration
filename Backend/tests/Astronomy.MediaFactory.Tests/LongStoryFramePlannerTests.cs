using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;

namespace Astronomy.MediaFactory.Tests;

public sealed class LongStoryFramePlannerTests
{
    private readonly NarrativeCompositionEngine narrativeEngine = new();
    private readonly LongStoryFramePlanner planner = new();

    [Fact]
    public void Plan_Builds_NineNativeLandscapeFrames_FromLongNarrativeTimeline()
    {
        var timeline = narrativeEngine.ComposeLong(PlanetPairingStory(), targetDuration: 300);

        var plan = planner.Plan(timeline, LongStoryFramePlatform.FacebookLong);

        Assert.Equal("16:9", plan.AspectRatio);
        Assert.Equal(LongStoryFramePlatform.FacebookLong, plan.Platform);
        Assert.Equal(9, plan.FrameCount);
        Assert.Equal(9, plan.FrameDefinitions.Count);
        Assert.Equal(timeline.TimelineId, plan.TimelineId);
        Assert.Equal(300, plan.TargetDurationSeconds);
        Assert.Equal(300, plan.FrameDefinitions.Sum(frame => frame.TargetDuration));
        Assert.Equal([
            NarrativeBeatRole.Hook,
            NarrativeBeatRole.Recognition,
            NarrativeBeatRole.ExplanationA,
            NarrativeBeatRole.ExplanationB,
            NarrativeBeatRole.ObservationA,
            NarrativeBeatRole.ObservationB,
            NarrativeBeatRole.InterestingFact,
            NarrativeBeatRole.Memory,
            NarrativeBeatRole.CallToAction
        ], plan.FrameDefinitions.Select(frame => frame.BeatRole).ToArray());
        Assert.All(plan.FrameDefinitions, frame =>
        {
            Assert.Contains("Native 16:9", frame.RecommendedComposition);
            Assert.Contains("Do not crop portrait or square assets", frame.RecommendedComposition);
            Assert.Contains("do not reuse short-frame composition", frame.RecommendedComposition);
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedSafeAreas));
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedTextDensity));
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedVisualTreatment));
        });
    }

    [Fact]
    public async Task WriteArtifacts_WritesDiagnosticsAndManifest_WithoutImages()
    {
        var timeline = narrativeEngine.ComposeLong(PlanetPairingStory());
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var (plan, review, manifest) = await planner.WriteArtifactsAsync(timeline, folder);

        Assert.Equal(9, review.FrameCount);
        Assert.Equal("16:9", review.AspectRatio);
        Assert.False(manifest.ImagesGenerated);
        Assert.Contains("production rendering replacement", manifest.RenderingStatus);
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "LongStoryFramePlan.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "LongStoryFrameReview.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "FrameGenerationDiagnostics.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "diagnostics", "VisualPromptDiagnostics.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "LongStoryFrameArtifactManifest.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "story-frame-plan.json")));
        Assert.True(File.Exists(Path.Combine(folder, "long-story-frames", "composition-model.json")));
        Assert.Equal("story-frame-plan.json", manifest.Artifacts["StoryFramePlan"]);
        Assert.Equal("composition-model.json", manifest.Artifacts["CompositionModel"]);
        Assert.Equal("diagnostics/LongStoryFrameReview.json", manifest.Artifacts["FrameReview"]);
        Assert.Equal("diagnostics/FrameGenerationDiagnostics.json", manifest.Artifacts["FrameGenerationDiagnostics"]);
        Assert.Equal("diagnostics/VisualPromptDiagnostics.json", manifest.Artifacts["VisualPromptDiagnostics"]);
        Assert.Equal("comparison/", manifest.Artifacts["ComparisonArtifacts"]);

        var json = await File.ReadAllTextAsync(Path.Combine(folder, "long-story-frames", "diagnostics", "LongStoryFramePlan.json"));
        var reparsed = JsonSerializer.Deserialize<LongStoryFramePlan>(json, VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(plan.PlanId, reparsed!.PlanId);
        Assert.Equal("4.7D", reparsed.Versions["longStoryFrames"]);
    }

    [Fact]
    public void Plan_DoesNotChangeExistingProductionScenePipelineContracts()
    {
        var timeline = narrativeEngine.ComposeLong(PlanetPairingStory());
        var storyEngine = new StoryCompositionEngine();
        var before = JsonSerializer.Serialize(storyEngine.Compose(PlanetPairingStory()), VisualIntelligenceJson.CreateSerializerOptions());

        _ = planner.Plan(timeline);

        var after = JsonSerializer.Serialize(storyEngine.Compose(PlanetPairingStory()), VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(before, after);
    }

    [Fact]
    public void Plan_RejectsShortTimeline_ToAvoidShortStoryChanges()
    {
        var shortTimeline = narrativeEngine.ComposeShort(PlanetPairingStory());

        Assert.Throws<ArgumentException>(() => planner.Plan(shortTimeline));
    }

    private static VisualStory PlanetPairingStory() => new()
    {
        StoryId = "PlanetPairing-long-frame-test",
        StoryTitle = "Venus and Jupiter close approach",
        ViewerQuestion = "Why do these planets look close?",
        PrimaryStory = "Two bright planets appear unusually close together.",
        ViewerTakeaway = "This is an apparent conjunction.",
        EmotionalHook = "Wonder.",
        PrimaryVisualSubject = "Relationship",
        SecondaryVisualSubjects = ["Venus", "Jupiter"],
        VisualRelationship = "The apparent conjunction relationship is the subject; do not prioritize the largest planet.",
        RecommendedComposition = "Balanced pairing",
        RecommendedViewerFocus = "Relationship first",
        DocumentaryTone = "Documentary",
        EnvironmentRecommendation = "Observed sky realism.",
        LightingRecommendation = "Natural twilight documentary lighting.",
        RecommendedNegativeSpace = "Shared negative space around both planets.",
        RecommendedOverlayZones = ["lower third"],
        RecommendedPlatformVariations = new Dictionary<string, VisualStoryPlatformVariation>
        {
            ["landscape"] = new() { Name = "Landscape Story", Recommendation = "wide documentary composition" }
        },
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
