using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum EditorialProductType { Hero, Thumbnail, Gallery, LongStory, ShortStory }

public sealed record EditorialStrategy
{
    public required string StrategyId { get; init; }
    public required EditorialProductType ProductType { get; init; }
    public required string EditorialGoal { get; init; }
    public required string ViewerEmotion { get; init; }
    public required string ViewerAction { get; init; }
    public required string AttentionWindow { get; init; }
    public required string InformationDensity { get; init; }
    public required string RecommendedVisualPriority { get; init; }
    public required string RecommendedTypographyPriority { get; init; }
    public required string RecommendedStoryEmphasis { get; init; }
    public required string RecommendedObservationEmphasis { get; init; }
    public required string RecommendedScienceEmphasis { get; init; }
    public required string RecommendedBrandEmphasis { get; init; }
    public required double Confidence { get; init; }
    public string Version { get; init; } = ProductEditorialStrategyEngine.Version;
}

public sealed record HeroEditorialStrategy(EditorialStrategy Strategy);
public sealed record ThumbnailEditorialStrategy(EditorialStrategy Strategy);
public sealed record GalleryEditorialStrategy(EditorialStrategy Strategy);
public sealed record LongStoryEditorialStrategy(EditorialStrategy Strategy);
public sealed record ShortStoryEditorialStrategy(EditorialStrategy Strategy);

public sealed record ProductEditorialStrategyResult
{
    public required HeroEditorialStrategy HeroEditorialStrategy { get; init; }
    public required ThumbnailEditorialStrategy ThumbnailEditorialStrategy { get; init; }
    public required GalleryEditorialStrategy GalleryEditorialStrategy { get; init; }
    public required LongStoryEditorialStrategy LongStoryEditorialStrategy { get; init; }
    public required ShortStoryEditorialStrategy ShortStoryEditorialStrategy { get; init; }
    public IReadOnlyList<EditorialStrategy> ProductStrategies => [HeroEditorialStrategy.Strategy, ThumbnailEditorialStrategy.Strategy, GalleryEditorialStrategy.Strategy, LongStoryEditorialStrategy.Strategy, ShortStoryEditorialStrategy.Strategy];
}

public sealed record ProductEditorialStrategyReview
{
    public required IReadOnlyList<EditorialStrategy> ProductStrategies { get; init; }
    public required IReadOnlyDictionary<string, string> EditorialGoals { get; init; }
    public required IReadOnlyDictionary<string, string> ViewerEmotions { get; init; }
    public required IReadOnlyDictionary<string, string> StoryEmphasis { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
}

public interface IProductEditorialStrategyEngine
{
    ProductEditorialStrategyResult Create(VisualStory story, StoryCompositionResult composition);
    Task<ProductEditorialStrategyReview> WriteDiagnosticsAsync(VisualStory story, StoryCompositionResult composition, string outputFolder, CancellationToken cancellationToken = default);
}

public sealed class ProductEditorialStrategyEngine : IProductEditorialStrategyEngine
{
    public const string Version = "4.3C";

    public ProductEditorialStrategyResult Create(VisualStory story, StoryCompositionResult composition) => new()
    {
        HeroEditorialStrategy = new HeroEditorialStrategy(Build(story, composition.HeroComposition.Decision, EditorialProductType.Hero)),
        ThumbnailEditorialStrategy = new ThumbnailEditorialStrategy(Build(story, composition.ThumbnailComposition.Decision, EditorialProductType.Thumbnail)),
        GalleryEditorialStrategy = new GalleryEditorialStrategy(Build(story, composition.GalleryComposition.Decision, EditorialProductType.Gallery)),
        LongStoryEditorialStrategy = new LongStoryEditorialStrategy(Build(story, composition.LongStoryComposition.Decision, EditorialProductType.LongStory)),
        ShortStoryEditorialStrategy = new ShortStoryEditorialStrategy(Build(story, composition.ShortStoryComposition.Decision, EditorialProductType.ShortStory))
    };

    public async Task<ProductEditorialStrategyReview> WriteDiagnosticsAsync(VisualStory story, StoryCompositionResult composition, string outputFolder, CancellationToken cancellationToken = default)
    {
        var review = BuildReview(Create(story, composition));
        Directory.CreateDirectory(outputFolder);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "ProductEditorialStrategyReview.json"), JsonSerializer.Serialize(review, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken);
        return review;
    }

    private static EditorialStrategy Build(VisualStory story, CompositionDecision composition, EditorialProductType product)
    {
        var defaults = Defaults(product);
        var planetPairing = IsPlanetPairing(story);
        return new EditorialStrategy
        {
            StrategyId = $"editorial_{story.StoryId}_{product}".ToLowerInvariant(),
            ProductType = product,
            EditorialGoal = defaults.Goal,
            ViewerEmotion = defaults.Emotion,
            ViewerAction = defaults.Action,
            AttentionWindow = defaults.AttentionWindow,
            InformationDensity = composition.RecommendedInformationDensity,
            RecommendedVisualPriority = planetPairing ? defaults.PairingVisualPriority : composition.PrimaryVisualFocus,
            RecommendedTypographyPriority = defaults.TypographyPriority,
            RecommendedStoryEmphasis = planetPairing ? "Emphasize the apparent relationship while keeping the underlying story identical." : story.PrimaryStory,
            RecommendedObservationEmphasis = planetPairing ? "Make observability of both planets and their apparent closeness clear." : story.ViewerTakeaway,
            RecommendedScienceEmphasis = planetPairing ? "Clarify apparent conjunction: line-of-sight closeness, not physical proximity." : story.VisualRelationship,
            RecommendedBrandEmphasis = defaults.BrandEmphasis,
            Confidence = Math.Clamp((story.StoryConfidence + composition.Confidence) / 2, 0, 1)
        };
    }

    private static ProductEditorialStrategyReview BuildReview(ProductEditorialStrategyResult result) => new()
    {
        ProductStrategies = result.ProductStrategies,
        EditorialGoals = result.ProductStrategies.ToDictionary(s => s.ProductType.ToString(), s => s.EditorialGoal),
        ViewerEmotions = result.ProductStrategies.ToDictionary(s => s.ProductType.ToString(), s => s.ViewerEmotion),
        StoryEmphasis = result.ProductStrategies.ToDictionary(s => s.ProductType.ToString(), s => s.RecommendedStoryEmphasis),
        Recommendations = result.ProductStrategies.Select(s => $"{s.ProductType}: {s.RecommendedVisualPriority}; typography: {s.RecommendedTypographyPriority}; density: {s.InformationDensity}").ToArray()
    };

    private static (string Goal, string Emotion, string Action, string AttentionWindow, string TypographyPriority, string BrandEmphasis, string PairingVisualPriority) Defaults(EditorialProductType product) => product switch
    {
        EditorialProductType.Hero => ("Stop scrolling.", "Wonder.", "Pause and recognize the astronomy story.", "Immediate glance", "Atmospheric and restrained", "Premium astronomy documentary presence", "Balanced planet relationship before individual planet identity."),
        EditorialProductType.Thumbnail => ("Increase CTR.", "Curiosity.", "Click to learn why the objects appear close.", "Sub-second", "Minimal, high-readability copy priority", "Clear, trustworthy astronomy signal", "High-contrast readable pairing without making one planet the hero."),
        EditorialProductType.Gallery => ("Teach visually.", "Discovery.", "Swipe through the explanation.", "Multi-panel progression", "Educational labels and concise sequencing", "Helpful visual learning system", "Progress from sky context to apparent closeness to viewing guidance."),
        EditorialProductType.LongStory => ("Explain.", "Understanding.", "Follow the full narrative and observation context.", "Sustained", "Moderate explanatory structure", "Calm expert documentary voice", "Use the pairing as the through-line for science and observing."),
        EditorialProductType.ShortStory => ("Immediate engagement.", "Excitement.", "Watch the quick explanation now.", "First seconds", "Low density, fast hook support", "Energetic but factual astronomy identity", "Instantly readable vertical relationship between the two planets."),
        _ => throw new ArgumentOutOfRangeException(nameof(product))
    };

    private static bool IsPlanetPairing(VisualStory story) => story.StoryId.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase) || story.VisualRelationship.Contains("pair", StringComparison.OrdinalIgnoreCase) || story.VisualRelationship.Contains("conjunction", StringComparison.OrdinalIgnoreCase);
}
