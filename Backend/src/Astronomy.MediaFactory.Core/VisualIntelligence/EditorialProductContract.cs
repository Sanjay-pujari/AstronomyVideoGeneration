using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public abstract record EditorialProductContract
{
    public required string ProductId { get; init; }
    public required string StoryId { get; init; }
    public required string EditorialDecisionId { get; init; }
    public required string VisualStoryId { get; init; }
    public required string CompositionId { get; init; }
    public required string EditorialStrategyId { get; init; }
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
    public required IReadOnlyDictionary<string, string> PlatformRecommendations { get; init; }
    public required double CreativeConfidence { get; init; }
    public required IReadOnlyDictionary<string, string> Versions { get; init; }
}

public sealed record EditorialProductReview
{
    public required IReadOnlyList<string> SharedFields { get; init; }
    public required IReadOnlyList<string> HeroSpecificFields { get; init; }
    public required IReadOnlyList<string> GallerySpecificFields { get; init; }
    public required EditorialProductInheritanceValidation InheritanceValidation { get; init; }
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
        nameof(EditorialProductContract.StoryId),
        nameof(EditorialProductContract.EditorialDecisionId),
        nameof(EditorialProductContract.VisualStoryId),
        nameof(EditorialProductContract.CompositionId),
        nameof(EditorialProductContract.EditorialStrategyId),
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
        nameof(EditorialProductContract.PlatformRecommendations),
        nameof(EditorialProductContract.CreativeConfidence),
        nameof(EditorialProductContract.Versions)
    ];

    public static readonly string[] HeroSpecificFields =
    [
        nameof(HeroIntelligenceContract.PlanId),
        nameof(HeroIntelligenceContract.EventType),
        nameof(HeroIntelligenceContract.EventFamily),
        nameof(HeroIntelligenceContract.EmotionalHook),
        nameof(HeroIntelligenceContract.CompositionGoal),
        nameof(HeroIntelligenceContract.VisualRelationship),
        nameof(HeroIntelligenceContract.ConfidenceSummary),
        nameof(HeroIntelligenceContract.CreativeKnowledgeReview),
        nameof(HeroIntelligenceContract.FallbackApplied),
        nameof(HeroIntelligenceContract.MissingInputs),
        nameof(HeroIntelligenceContract.Warnings)
    ];

    public static readonly string[] GallerySpecificFields =
    [
        nameof(GalleryIntelligenceContract.StoryProgression),
        nameof(GalleryIntelligenceContract.EditorialSequence),
        nameof(GalleryIntelligenceContract.NarrativeFlow),
        nameof(GalleryIntelligenceContract.LearningObjectives)
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
        }
    };
}
