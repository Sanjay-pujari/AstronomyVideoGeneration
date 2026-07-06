using System.Text.Json;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Tests;

public sealed class StoryCompositionEngineTests
{
    private readonly StoryCompositionEngine engine = new();

    [Fact]
    public void Compose_Creates_All_Product_Strategies()
    {
        var result = engine.Compose(PlanetPairingStory());

        Assert.Equal("Stop scrolling.", result.HeroComposition.Decision.CompositionGoal);
        Assert.Contains("one iconic image", result.HeroComposition.Decision.RecommendedHierarchy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Maximize CTR.", result.ThumbnailComposition.Decision.CompositionGoal);
        Assert.Equal("Minimal", result.ThumbnailComposition.Decision.RecommendedInformationDensity);
        Assert.Equal("Teach visually.", result.GalleryComposition.Decision.CompositionGoal);
        Assert.Contains(result.GalleryComposition.Decision.RecommendedCompositionNotes, note => note.Contains("educational", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Explain.", result.LongStoryComposition.Decision.CompositionGoal);
        Assert.Contains(result.LongStoryComposition.Decision.RecommendedCompositionNotes, note => note.Contains("landscape documentary", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Hook immediately.", result.ShortStoryComposition.Decision.CompositionGoal);
        Assert.Equal(CompositionPlatform.Portrait, result.ShortStoryComposition.Decision.Platform);
        Assert.True((bool)result.ShortStoryComposition.Decision.ExtensionFields["neverDerivedByCropping"]!);
    }

    [Fact]
    public void Compose_Applies_PlanetPairing_Product_Composition()
    {
        var result = engine.Compose(PlanetPairingStory());

        Assert.Contains("Balanced planets", result.HeroComposition.Decision.RecommendedVisualBalance);
        Assert.Contains("higher contrast", result.ThumbnailComposition.Decision.RecommendedVisualBalance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Educational sequence", result.GalleryComposition.Decision.PrimaryVisualFocus);
        Assert.Contains("Landscape storytelling", result.LongStoryComposition.Decision.PrimaryVisualFocus);
        Assert.Contains("fast comprehension", result.ShortStoryComposition.Decision.PrimaryVisualFocus, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CompositionPlatform.Landscape, "16:9", "wide documentary")]
    [InlineData(CompositionPlatform.Portrait, "9:16", "native vertical")]
    [InlineData(CompositionPlatform.Square, "1:1", "centered balance")]
    public async Task Diagnostics_Includes_Platform_Recommendations(CompositionPlatform platform, string aspectRatio, string expectedRecommendation)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var review = await engine.WriteDiagnosticsAsync(PlanetPairingStory(), folder, platform);
        var json = await File.ReadAllTextAsync(Path.Combine(folder, "StoryCompositionReview.json"));

        Assert.Equal(aspectRatio, review.RecommendedAspectRatio);
        Assert.Contains(expectedRecommendation, json, StringComparison.OrdinalIgnoreCase);
        Assert.True(review.PlatformRecommendations.ContainsKey("landscape"));
        Assert.True(review.PlatformRecommendations.ContainsKey("portrait"));
        Assert.True(review.PlatformRecommendations.ContainsKey("square"));
    }

    [Fact]
    public void CompositionDecision_Serializes()
    {
        var decision = engine.Compose(PlanetPairingStory(), CompositionPlatform.Square).ThumbnailComposition.Decision;

        var json = JsonSerializer.Serialize(decision, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<CompositionDecision>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal(CompositionProductType.Thumbnail, reparsed!.ProductType);
        Assert.Equal(CompositionPlatform.Square, reparsed.Platform);
        Assert.Equal(StoryCompositionEngine.Version, reparsed.CompositionVersion);
        Assert.True(reparsed.ExtensionFields.ContainsKey("engineDoesNotGeneratePrompts"));
    }

    private static VisualStory PlanetPairingStory() => new()
    {
        StoryId = "PlanetPairing-director-test",
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
        StoryConfidence = .91,
        CreativeKnowledgeVersion = CreativeKnowledgeLibrary.Version,
        EditorialReasoningVersion = "4.3A"
    };
}
