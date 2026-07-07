using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrativeCompositionEngineTests
{
    private readonly NarrativeCompositionEngine engine = new();

    [Fact]
    public void ComposeLong_Builds_LongDocumentaryTimeline_WithCanonicalBeatOrder()
    {
        var timeline = engine.ComposeLong(PlanetPairingStory());

        Assert.Equal(NarrativeTimelineType.LongDocumentary, timeline.TimelineType);
        Assert.Equal(240, timeline.TargetDuration);
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
        ], timeline.Beats.Select(beat => beat.BeatRole).ToArray());
        Assert.All(timeline.Beats, beat => Assert.False(string.IsNullOrWhiteSpace(beat.ViewerEmotion)));
        Assert.Equal("Wonder.", timeline.Beats[0].ViewerEmotion);
    }

    [Fact]
    public void ComposeShort_Builds_ShortDocumentaryTimeline_WithCanonicalBeatOrder()
    {
        var timeline = engine.ComposeShort(PlanetPairingStory());

        Assert.Equal(NarrativeTimelineType.ShortDocumentary, timeline.TimelineType);
        Assert.Equal(45, timeline.TargetDuration);
        Assert.Equal([
            NarrativeBeatRole.Hook,
            NarrativeBeatRole.Recognition,
            NarrativeBeatRole.Explanation,
            NarrativeBeatRole.Observation,
            NarrativeBeatRole.CallToAction
        ], timeline.Beats.Select(beat => beat.BeatRole).ToArray());
    }

    [Fact]
    public void Compose_Allocates_Configurable_DurationBudget_ToBeats()
    {
        var longTimeline = engine.ComposeLong(PlanetPairingStory(), targetDuration: 300);
        var shortTimeline = engine.ComposeShort(PlanetPairingStory(), targetDuration: 60);

        Assert.Equal(300, longTimeline.Beats.Sum(beat => beat.TargetDuration));
        Assert.Equal(60, shortTimeline.Beats.Sum(beat => beat.TargetDuration));
        Assert.Equal(24, longTimeline.BeatTimingAllocation["Hook"]);
        Assert.Equal(12, shortTimeline.BeatTimingAllocation["Hook"]);
    }

    [Fact]
    public void NarrativeTimeline_Serializes_WithImmutableBeats()
    {
        var timeline = engine.ComposeLong(PlanetPairingStory());

        var json = JsonSerializer.Serialize(timeline, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<NarrativeTimeline>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal(NarrativeCompositionEngine.Version, reparsed!.Version);
        Assert.Equal(NarrativeBeatRole.Hook, reparsed.Beats[0].BeatRole);
        Assert.Equal("Balanced pairing", reparsed.Beats[0].RecommendedComposition);
        Assert.Contains("landscape", reparsed.Beats[0].RecommendedPlatformVariants);
    }

    [Fact]
    public async Task Diagnostics_Writes_NarrativeTimelineReview()
    {
        var timeline = engine.ComposeShort(PlanetPairingStory());
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var review = await engine.WriteDiagnosticsAsync(timeline, folder);
        var json = await File.ReadAllTextAsync(Path.Combine(folder, "NarrativeTimelineReview.json"));

        Assert.Equal(["Hook", "Recognition", "Explanation", "Observation", "CallToAction"], review.BeatOrder);
        Assert.True(review.BeatDurations.ContainsKey("Explanation"));
        Assert.Equal(timeline.Beats.Count, review.ViewerJourney.Count);
        Assert.Equal(timeline.Beats.Count, review.EmotionalCurve.Count);
        Assert.Equal(timeline.Beats.Count, review.EducationalProgression.Count);
        Assert.True(review.PlatformVariants.ContainsKey("Hook"));
        Assert.Contains("recommendations", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_Does_Not_Change_StoryComposition_Output()
    {
        var story = PlanetPairingStory();
        var compositionEngine = new StoryCompositionEngine();
        var before = JsonSerializer.Serialize(compositionEngine.Compose(story), VisualIntelligenceJson.CreateSerializerOptions());

        _ = engine.ComposeLong(story);
        _ = engine.ComposeShort(story);

        var after = JsonSerializer.Serialize(compositionEngine.Compose(story), VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(before, after);
    }

    private static VisualStory PlanetPairingStory() => new()
    {
        StoryId = "PlanetPairing-narrative-test",
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
            ["landscape"] = new() { Name = "Landscape Story", Recommendation = "wide documentary composition" },
            ["portrait"] = new() { Name = "Portrait Story", Recommendation = "native vertical composition" }
        },
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
