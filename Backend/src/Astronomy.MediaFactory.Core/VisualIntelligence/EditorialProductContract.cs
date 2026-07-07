using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public abstract record EditorialProductContract
{
    public required string ProductId { get; init; }
    public required string ProductType { get; init; }
    public required string StoryId { get; init; }
    public required string StoryVersion { get; init; }
    public required string EditorialDecisionId { get; init; }
    public required string VisualStoryId { get; init; }
    public required string StoryCompositionId { get; init; }
    public required string ProductEditorialStrategyId { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string PrimaryStory { get; init; }
    public required string ViewerTakeaway { get; init; }
    public required string EditorialGoal { get; init; }
    public required string ViewerEmotion { get; init; }
    public required string DocumentaryTone { get; init; }
    public required string RecommendedComposition { get; init; }
    public required string RecommendedTypography { get; init; }
    public required string RecommendedInformationDensity { get; init; }
    public required string RecommendedVisualBalance { get; init; }
    public required IReadOnlyDictionary<string, string> RecommendedPlatformRecommendations { get; init; }
    public required double CreativeConfidence { get; init; }
    public required IReadOnlyDictionary<string, string> CreativeVersions { get; init; }
    public IReadOnlyDictionary<string, object> ExtensionFields { get; init; } = new Dictionary<string, object>();

    [JsonIgnore]
    public string CompositionId => StoryCompositionId;
    [JsonIgnore]
    public string EditorialStrategyId => ProductEditorialStrategyId;
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> PlatformRecommendations => RecommendedPlatformRecommendations;
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Versions => CreativeVersions;
}

public sealed record EditorialProductReview
{
    public required IReadOnlyList<string> SharedFields { get; init; }
    public required IReadOnlyList<string> HeroSpecificFields { get; init; }
    public required IReadOnlyList<string> GallerySpecificFields { get; init; }
    public required EditorialProductInheritanceValidation InheritanceValidation { get; init; }
    public required IReadOnlyList<string> SharedCreativeSources { get; init; }
    public required IReadOnlyList<string> Recommendations { get; init; }
}

public sealed record EditorialProductInheritanceValidation
{
    public required bool HeroDerivesFromEditorialProductContract { get; init; }
    public required bool GalleryDerivesFromEditorialProductContract { get; init; }
    public required bool HeroContainsAllSharedFields { get; init; }
    public required bool GalleryContainsAllSharedFields { get; init; }
    public required bool ArchitectureOnly { get; init; }
    public required bool ChangesRendering { get; init; }
    public required bool ChangesPrompts { get; init; }
    public required bool ChangesAzure { get; init; }
    public required bool ChangesProductionRouting { get; init; }
}

public static class EditorialProductContractDiagnostics
{
    public static readonly string[] SharedFields =
    [
        nameof(EditorialProductContract.ProductId),
        nameof(EditorialProductContract.ProductType),
        nameof(EditorialProductContract.StoryId),
        nameof(EditorialProductContract.StoryVersion),
        nameof(EditorialProductContract.EditorialDecisionId),
        nameof(EditorialProductContract.VisualStoryId),
        nameof(EditorialProductContract.StoryCompositionId),
        nameof(EditorialProductContract.ProductEditorialStrategyId),
        nameof(EditorialProductContract.ViewerQuestion),
        nameof(EditorialProductContract.PrimaryStory),
        nameof(EditorialProductContract.ViewerTakeaway),
        nameof(EditorialProductContract.EditorialGoal),
        nameof(EditorialProductContract.ViewerEmotion),
        nameof(EditorialProductContract.DocumentaryTone),
        nameof(EditorialProductContract.RecommendedComposition),
        nameof(EditorialProductContract.RecommendedTypography),
        nameof(EditorialProductContract.RecommendedInformationDensity),
        nameof(EditorialProductContract.RecommendedVisualBalance),
        nameof(EditorialProductContract.RecommendedPlatformRecommendations),
        nameof(EditorialProductContract.CreativeConfidence),
        nameof(EditorialProductContract.CreativeVersions),
        nameof(EditorialProductContract.ExtensionFields)
    ];

    public static readonly string[] HeroSpecificFields =
    [
        nameof(HeroIntelligenceContract.HeroSpecificFields)
    ];

    public static readonly string[] GallerySpecificFields =
    [
        nameof(GalleryIntelligenceContract.EditorialSequence),
        nameof(GalleryIntelligenceContract.LearningObjectives),
        nameof(GalleryIntelligenceContract.PageDefinitions)
    ];

    public static EditorialProductReview CreateReview() => new()
    {
        SharedFields = SharedFields,
        HeroSpecificFields = HeroSpecificFields,
        GallerySpecificFields = GallerySpecificFields,
        InheritanceValidation = new EditorialProductInheritanceValidation
        {
            HeroDerivesFromEditorialProductContract = typeof(HeroIntelligenceContract).IsSubclassOf(typeof(EditorialProductContract)),
            GalleryDerivesFromEditorialProductContract = typeof(GalleryIntelligenceContract).IsSubclassOf(typeof(EditorialProductContract)),
            HeroContainsAllSharedFields = SharedFields.All(field => typeof(HeroIntelligenceContract).GetProperty(field) is not null),
            GalleryContainsAllSharedFields = SharedFields.All(field => typeof(GalleryIntelligenceContract).GetProperty(field) is not null),
            ArchitectureOnly = true,
            ChangesRendering = false,
            ChangesPrompts = false,
            ChangesAzure = false,
            ChangesProductionRouting = false
        },
        SharedCreativeSources = ["EditorialDecision", "VisualStory", "StoryComposition", "ProductEditorialStrategy"],
        Recommendations = ["Keep Hero and Gallery on the shared EditorialProductContract foundation.", "Limit future Hero and Gallery changes to creative-quality refinement unless the shared contract changes."]
    };
}
