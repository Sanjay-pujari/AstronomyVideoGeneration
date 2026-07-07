using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ShortStoryFramePlannerTests
{
    private readonly NarrativeCompositionEngine narrativeEngine = new();
    private readonly ShortStoryFramePlanner planner = new();

    [Fact]
    public void Plan_Builds_FiveNativePortraitFrames_FromShortNarrativeTimeline()
    {
        var timeline = narrativeEngine.ComposeShort(PlanetPairingStory(), targetDuration: 50);

        var plan = planner.Plan(timeline, ShortStoryFramePlatform.InstagramReels);

        Assert.Equal("9:16", plan.AspectRatio);
        Assert.Equal(ShortStoryFramePlatform.InstagramReels, plan.Platform);
        Assert.Equal(5, plan.FrameCount);
        Assert.Equal(5, plan.FrameDefinitions.Count);
        Assert.Equal(timeline.TimelineId, plan.TimelineId);
        Assert.Equal(50, plan.TargetDurationSeconds);
        Assert.Equal(50, plan.FrameDefinitions.Sum(frame => frame.TargetDuration));
        Assert.Equal([
            NarrativeBeatRole.Hook,
            NarrativeBeatRole.Recognition,
            NarrativeBeatRole.Explanation,
            NarrativeBeatRole.Observation,
            NarrativeBeatRole.CallToAction
        ], plan.FrameDefinitions.Select(frame => frame.BeatRole).ToArray());
        Assert.All(plan.FrameDefinitions, frame =>
        {
            Assert.Contains("Native 9:16", frame.RecommendedComposition);
            Assert.Contains("Do not crop landscape assets", frame.RecommendedComposition);
            Assert.Contains("do not reuse long-frame composition", frame.RecommendedComposition);
            Assert.Contains("fast comprehension", frame.RecommendedComposition);
            Assert.Contains("vertical visual hierarchy", frame.RecommendedComposition);
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedSafeAreas));
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedTextDensity));
            Assert.False(string.IsNullOrWhiteSpace(frame.RecommendedVisualTreatment));
        });
    }

    [Fact]
    public async Task WriteArtifacts_WritesDiagnosticsAndManifest_WithoutImages()
    {
        var timeline = narrativeEngine.ComposeShort(PlanetPairingStory());
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var (plan, review, manifest) = await planner.WriteArtifactsAsync(timeline, folder, ShortStoryFramePlatform.FacebookShorts);

        Assert.Equal(5, review.FrameCount);
        Assert.Equal("9:16", review.AspectRatio);
        Assert.False(manifest.ImagesGenerated);
        Assert.Contains("no production rendering replacement", manifest.RenderingStatus);
        Assert.Contains("no Azure routing changes", manifest.RenderingStatus);
        Assert.True(Directory.Exists(Path.Combine(folder, "short-story-frames", "comparison")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "diagnostics", "ShortStoryFramePlan.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "diagnostics", "ShortStoryFrameReview.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "diagnostics", "FrameGenerationDiagnostics.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "diagnostics", "VisualPromptDiagnostics.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "ShortStoryFrameArtifactManifest.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "story-frame-plan.json")));
        Assert.True(File.Exists(Path.Combine(folder, "short-story-frames", "composition-model.json")));
        Assert.Equal("story-frame-plan.json", manifest.Artifacts["StoryFramePlan"]);
        Assert.Equal("composition-model.json", manifest.Artifacts["CompositionModel"]);
        Assert.Equal("diagnostics/ShortStoryFrameReview.json", manifest.Artifacts["FrameReview"]);
        Assert.Equal("diagnostics/FrameGenerationDiagnostics.json", manifest.Artifacts["FrameGenerationDiagnostics"]);
        Assert.Equal("diagnostics/VisualPromptDiagnostics.json", manifest.Artifacts["VisualPromptDiagnostics"]);
        Assert.Equal("comparison/", manifest.Artifacts["ComparisonArtifacts"]);

        var json = await File.ReadAllTextAsync(Path.Combine(folder, "short-story-frames", "diagnostics", "ShortStoryFramePlan.json"));
        var reparsed = JsonSerializer.Deserialize<ShortStoryFramePlan>(json, VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(plan.PlanId, reparsed!.PlanId);
        Assert.Equal("4.7D", reparsed.Versions["shortStoryFrames"]);
    }

    [Fact]
    public void Plan_DoesNotChangeExistingProductionScenePipelineContracts()
    {
        var timeline = narrativeEngine.ComposeShort(PlanetPairingStory());
        var storyEngine = new StoryCompositionEngine();
        var before = JsonSerializer.Serialize(storyEngine.Compose(PlanetPairingStory()), VisualIntelligenceJson.CreateSerializerOptions());

        _ = planner.Plan(timeline);

        var after = JsonSerializer.Serialize(storyEngine.Compose(PlanetPairingStory()), VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(before, after);
    }

    [Fact]
    public void Plan_RejectsLongTimeline_ToAvoidLongStoryChanges()
    {
        var longTimeline = narrativeEngine.ComposeLong(PlanetPairingStory());

        Assert.Throws<ArgumentException>(() => planner.Plan(longTimeline));
    }

    private static VisualStory PlanetPairingStory() => new()
    {
        StoryId = "PlanetPairing-short-frame-test",
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
            ["portrait"] = new() { Name = "Portrait Story", Recommendation = "native vertical composition" }
        },
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
