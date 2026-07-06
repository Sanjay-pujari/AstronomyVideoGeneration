using System.Text.Json;
using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public enum CompositionProductType { Hero, Thumbnail, Gallery, LongStory, ShortStory }
public enum CompositionPlatform { Landscape, Portrait, Square }

public sealed record CompositionDecision
{
    public required string CompositionId { get; init; }
    public string CompositionVersion { get; init; } = StoryCompositionEngine.Version;
    public required CompositionProductType ProductType { get; init; }
    public required CompositionPlatform Platform { get; init; }
    public required string StoryId { get; init; }
    public required string CompositionGoal { get; init; }
    public required string ViewerAttentionStrategy { get; init; }
    public required string PrimaryVisualFocus { get; init; }
    public IReadOnlyList<string> SecondaryVisualFocus { get; init; } = [];
    public required string RecommendedHierarchy { get; init; }
    public required string RecommendedNegativeSpace { get; init; }
    public IReadOnlyList<string> RecommendedOverlayZones { get; init; } = [];
    public required string RecommendedCameraStyle { get; init; }
    public required string RecommendedLensStyle { get; init; }
    public required string RecommendedScaleRelationship { get; init; }
    public required string RecommendedEnvironment { get; init; }
    public required string RecommendedLighting { get; init; }
    public required string RecommendedTypographyDensity { get; init; }
    public required string RecommendedInformationDensity { get; init; }
    public required string RecommendedVisualBalance { get; init; }
    public required string RecommendedMotionFeel { get; init; }
    public IReadOnlyList<string> RecommendedCompositionNotes { get; init; } = [];
    public required double Confidence { get; init; }
    public IReadOnlyDictionary<string, object?> ExtensionFields { get; init; } = new Dictionary<string, object?>();
}

public sealed record HeroComposition(CompositionDecision Decision);
public sealed record ThumbnailComposition(CompositionDecision Decision);
public sealed record GalleryComposition(CompositionDecision Decision);
public sealed record LongStoryComposition(CompositionDecision Decision);
public sealed record ShortStoryComposition(CompositionDecision Decision);

public sealed record StoryCompositionResult
{
    public required HeroComposition HeroComposition { get; init; }
    public required ThumbnailComposition ThumbnailComposition { get; init; }
    public required GalleryComposition GalleryComposition { get; init; }
    public required LongStoryComposition LongStoryComposition { get; init; }
    public required ShortStoryComposition ShortStoryComposition { get; init; }
    public IReadOnlyList<CompositionDecision> Decisions => [HeroComposition.Decision, ThumbnailComposition.Decision, GalleryComposition.Decision, LongStoryComposition.Decision, ShortStoryComposition.Decision];
}

public sealed record StoryCompositionReview
{
    public required string CompositionStrategy { get; init; }
    public required string ViewerAttentionStrategy { get; init; }
    public required IReadOnlyDictionary<string, string> PlatformRecommendations { get; init; }
    public required string VisualBalance { get; init; }
    public required string InformationDensity { get; init; }
    public required string RecommendedAspectRatio { get; init; }
    public IReadOnlyList<string> EditorialNotes { get; init; } = [];
    public required double Confidence { get; init; }
}

public interface IStoryCompositionEngine
{
    StoryCompositionResult Compose(VisualStory story, CompositionPlatform platform = CompositionPlatform.Landscape);
    Task<StoryCompositionReview> WriteDiagnosticsAsync(VisualStory story, string outputFolder, CompositionPlatform platform = CompositionPlatform.Landscape, CancellationToken cancellationToken = default);
}

public sealed class StoryCompositionEngine : IStoryCompositionEngine
{
    public const string Version = "4.3B";

    public StoryCompositionResult Compose(VisualStory story, CompositionPlatform platform = CompositionPlatform.Landscape) => new()
    {
        HeroComposition = new HeroComposition(Build(story, CompositionProductType.Hero, platform)),
        ThumbnailComposition = new ThumbnailComposition(Build(story, CompositionProductType.Thumbnail, platform)),
        GalleryComposition = new GalleryComposition(Build(story, CompositionProductType.Gallery, platform)),
        LongStoryComposition = new LongStoryComposition(Build(story, CompositionProductType.LongStory, platform)),
        ShortStoryComposition = new ShortStoryComposition(Build(story, CompositionProductType.ShortStory, CompositionPlatform.Portrait))
    };

    public async Task<StoryCompositionReview> WriteDiagnosticsAsync(VisualStory story, string outputFolder, CompositionPlatform platform = CompositionPlatform.Landscape, CancellationToken cancellationToken = default)
    {
        var result = Compose(story, platform);
        var review = BuildReview(result, platform);
        Directory.CreateDirectory(outputFolder);
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "StoryCompositionReview.json"), JsonSerializer.Serialize(review, VisualIntelligenceJson.CreateSerializerOptions(writeIndented: true)), cancellationToken);
        return review;
    }

    private static CompositionDecision Build(VisualStory story, CompositionProductType product, CompositionPlatform platform)
    {
        var planetPairing = IsPlanetPairing(story);
        var strategy = ProductStrategy(product, planetPairing);
        var platformSpec = PlatformSpec(platform);
        return new CompositionDecision
        {
            CompositionId = $"composition_{story.StoryId}_{product}_{platform}".ToLowerInvariant(),
            ProductType = product,
            Platform = platform,
            StoryId = story.StoryId,
            CompositionGoal = strategy.Goal,
            ViewerAttentionStrategy = strategy.Attention,
            PrimaryVisualFocus = planetPairing ? strategy.PlanetPairingFocus : story.PrimaryVisualSubject,
            SecondaryVisualFocus = story.SecondaryVisualSubjects,
            RecommendedHierarchy = strategy.Hierarchy,
            RecommendedNegativeSpace = platformSpec.NegativeSpace,
            RecommendedOverlayZones = platformSpec.OverlayZones,
            RecommendedCameraStyle = platformSpec.Camera,
            RecommendedLensStyle = platformSpec.Lens,
            RecommendedScaleRelationship = planetPairing ? strategy.PlanetPairingScale : "Story-led scale relationship; avoid largest-object-wins unless scientifically relevant.",
            RecommendedEnvironment = story.EnvironmentRecommendation,
            RecommendedLighting = product == CompositionProductType.Thumbnail ? "Higher contrast documentary lighting; still no artificial prompt/image instructions." : story.LightingRecommendation,
            RecommendedTypographyDensity = strategy.TypographyDensity,
            RecommendedInformationDensity = strategy.InformationDensity,
            RecommendedVisualBalance = planetPairing ? strategy.PlanetPairingBalance : platformSpec.Balance,
            RecommendedMotionFeel = strategy.MotionFeel,
            RecommendedCompositionNotes = strategy.Notes.Concat(platformSpec.Notes).ToArray(),
            Confidence = Math.Clamp(story.StoryConfidence, 0, 1),
            ExtensionFields = new Dictionary<string, object?> { ["sourceStoryVersion"] = story.StoryVersion, ["engineDoesNotGeneratePrompts"] = true, ["nativeComposition"] = true, ["neverDerivedByCropping"] = product == CompositionProductType.ShortStory }
        };
    }

    private static StoryCompositionReview BuildReview(StoryCompositionResult result, CompositionPlatform platform) => new()
    {
        CompositionStrategy = "Product-specific story composition decisions from one VisualStoryModel; no prompts or images generated.",
        ViewerAttentionStrategy = string.Join(" | ", result.Decisions.Select(d => $"{d.ProductType}: {d.ViewerAttentionStrategy}")),
        PlatformRecommendations = Enum.GetValues<CompositionPlatform>().ToDictionary(p => p.ToString().ToLowerInvariant(), p => PlatformSpec(p).Recommendation),
        VisualBalance = result.HeroComposition.Decision.RecommendedVisualBalance,
        InformationDensity = result.ThumbnailComposition.Decision.RecommendedInformationDensity,
        RecommendedAspectRatio = platform switch { CompositionPlatform.Portrait => "9:16", CompositionPlatform.Square => "1:1", _ => "16:9" },
        EditorialNotes = result.Decisions.SelectMany(d => d.RecommendedCompositionNotes).Distinct().ToArray(),
        Confidence = result.Decisions.Average(d => d.Confidence)
    };

    private static bool IsPlanetPairing(VisualStory story) => story.StoryId.Contains("PlanetPairing", StringComparison.OrdinalIgnoreCase) || story.VisualRelationship.Contains("pair", StringComparison.OrdinalIgnoreCase) || story.VisualRelationship.Contains("conjunction", StringComparison.OrdinalIgnoreCase);

    private static (string Goal, string Attention, string Hierarchy, string TypographyDensity, string InformationDensity, string MotionFeel, string PlanetPairingFocus, string PlanetPairingScale, string PlanetPairingBalance, string[] Notes) ProductStrategy(CompositionProductType product, bool planetPairing) => product switch
    {
        CompositionProductType.Hero => ("Stop scrolling.", "Immediate recognition through one iconic image.", "One iconic image; story relationship first.", "Minimal", "Low", "Documentary stillness with premium presence.", "Balanced planets communicating their relationship.", "Balanced apparent pairing; neither planet dominates by size.", "Balanced planets, relationship, documentary.", ["Hero composition is one iconic image."]),
        CompositionProductType.Thumbnail => ("Maximize CTR.", "Instant high-impact contrast with minimal information.", "Large readable subjects; simple focal path.", "Very low", "Minimal", "Punchy, high-impact visual snap.", "Larger planets with higher contrast for CTR emphasis.", "Planets may be visually larger for readability while preserving relationship.", "Higher contrast, larger planets, CTR emphasis.", ["Thumbnail composition is high impact with minimal information."]),
        CompositionProductType.Gallery => ("Teach visually.", "Progressive comprehension across an educational sequence.", "Sequence from context to relationship to observation.", "Moderate", "Educational", "Guided progression.", "Educational sequence showing how the planet relationship is understood.", "Scale can vary by panel to teach context, pairing, and observation.", "Progressive educational balance.", ["Gallery composition is progressive and educational."]),
        CompositionProductType.LongStory => ("Explain.", "Sustained attention through landscape documentary storytelling.", "Wide establishing context followed by explanatory focus.", "Moderate", "Medium", "Measured documentary flow.", "Landscape storytelling of the planet relationship.", "Landscape scale uses sky context around both planets.", "Documentary landscape balance.", ["Long Story composition is landscape documentary."]),
        CompositionProductType.ShortStory => ("Hook immediately.", "Very fast viewer comprehension in native vertical composition.", "Portrait-first vertical focal stack; no crop-derived framing.", "Low", "Low", "Fast vertical hook.", "Portrait storytelling with very fast comprehension of the pairing.", "Native vertical relationship, never derived by cropping.", "Portrait-first fast comprehension balance.", ["Short Story composition is portrait-first, native vertical, never derived by cropping."]),
        _ => throw new ArgumentOutOfRangeException(nameof(product))
    };

    private static (string Recommendation, string NegativeSpace, IReadOnlyList<string> OverlayZones, string Camera, string Lens, string Balance, string[] Notes) PlatformSpec(CompositionPlatform platform) => platform switch
    {
        CompositionPlatform.Portrait => ("Native portrait 9:16 composition with vertical subject relationship and stacked safe zones.", "Vertical breathing room above and below the story relationship.", ["top safe band", "lower safe band", "side edge micro-labels"], "Portrait-first vertical documentary framing.", "Moderate telephoto or vertical sky compression.", "Vertical balance with fast centerline readability.", ["Portrait recommendation is native vertical, not a crop."]),
        CompositionPlatform.Square => ("Native square 1:1 composition with centered balance and compact documentary clarity.", "Even perimeter negative space for compact overlays.", ["top edge", "bottom edge", "corner badges"], "Centered square documentary framing.", "Natural perspective with compact focal containment.", "Symmetric compact balance.", ["Square recommendation uses compact centered balance."]),
        _ => ("Native landscape 16:9 composition with wide documentary context and lower-third safety.", "Wide shared negative space across the story relationship.", ["lower third", "outer edges", "top corners"], "Wide landscape documentary framing.", "Natural wide-to-normal documentary lens.", "Horizontal documentary balance.", ["Landscape recommendation uses wide documentary context."])
    };
}
