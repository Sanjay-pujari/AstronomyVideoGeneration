using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductEditorialStrategyEngineTests
{
    private readonly StoryCompositionEngine compositionEngine = new();
    private readonly ProductEditorialStrategyEngine engine = new();

    [Fact]
    public void Create_Generates_All_Product_Strategies_With_Defaults()
    {
        var story = PlanetPairingStory();
        var result = engine.Create(story, compositionEngine.Compose(story));

        Assert.Equal(5, result.ProductStrategies.Count);
        Assert.Equal("Stop scrolling.", result.HeroEditorialStrategy.Strategy.EditorialGoal);
        Assert.Equal("Wonder.", result.HeroEditorialStrategy.Strategy.ViewerEmotion);
        Assert.Equal("Increase CTR.", result.ThumbnailEditorialStrategy.Strategy.EditorialGoal);
        Assert.Equal("Curiosity.", result.ThumbnailEditorialStrategy.Strategy.ViewerEmotion);
        Assert.Equal("Teach visually.", result.GalleryEditorialStrategy.Strategy.EditorialGoal);
        Assert.Equal("Discovery.", result.GalleryEditorialStrategy.Strategy.ViewerEmotion);
        Assert.Equal("Explain.", result.LongStoryEditorialStrategy.Strategy.EditorialGoal);
        Assert.Equal("Understanding.", result.LongStoryEditorialStrategy.Strategy.ViewerEmotion);
        Assert.Equal("Immediate engagement.", result.ShortStoryEditorialStrategy.Strategy.EditorialGoal);
        Assert.Equal("Excitement.", result.ShortStoryEditorialStrategy.Strategy.ViewerEmotion);
    }

    [Fact]
    public void Create_Applies_PlanetPairing_Editorial_Defaults()
    {
        var story = PlanetPairingStory();
        var result = engine.Create(story, compositionEngine.Compose(story));

        Assert.Contains("relationship", result.HeroEditorialStrategy.Strategy.RecommendedVisualPriority, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line-of-sight", result.ThumbnailEditorialStrategy.Strategy.RecommendedScienceEmphasis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("both planets", result.GalleryEditorialStrategy.Strategy.RecommendedObservationEmphasis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditorialStrategy_Serializes()
    {
        var story = PlanetPairingStory();
        var strategy = engine.Create(story, compositionEngine.Compose(story)).LongStoryEditorialStrategy.Strategy;

        var json = JsonSerializer.Serialize(strategy, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<EditorialStrategy>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal(EditorialProductType.LongStory, reparsed!.ProductType);
        Assert.Equal(ProductEditorialStrategyEngine.Version, reparsed.Version);
        Assert.Equal("Explain.", reparsed.EditorialGoal);
    }

    [Fact]
    public async Task Diagnostics_Writes_ProductEditorialStrategyReview()
    {
        var story = PlanetPairingStory();
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        var review = await engine.WriteDiagnosticsAsync(story, compositionEngine.Compose(story), folder);
        var json = await File.ReadAllTextAsync(Path.Combine(folder, "ProductEditorialStrategyReview.json"));

        Assert.Equal(5, review.ProductStrategies.Count);
        Assert.True(review.EditorialGoals.ContainsKey("Hero"));
        Assert.True(review.ViewerEmotions.ContainsKey("Thumbnail"));
        Assert.True(review.StoryEmphasis.ContainsKey("Gallery"));
        Assert.Contains("recommendations", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_Does_Not_Change_Composition_Output()
    {
        var story = PlanetPairingStory();
        var before = JsonSerializer.Serialize(compositionEngine.Compose(story), VisualIntelligenceJson.CreateSerializerOptions());

        _ = engine.Create(story, compositionEngine.Compose(story));

        var after = JsonSerializer.Serialize(compositionEngine.Compose(story), VisualIntelligenceJson.CreateSerializerOptions());
        Assert.Equal(before, after);
    }

    private static VisualStory PlanetPairingStory() => new()
    {
        StoryId = "PlanetPairing-editorial-strategy-test",
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
