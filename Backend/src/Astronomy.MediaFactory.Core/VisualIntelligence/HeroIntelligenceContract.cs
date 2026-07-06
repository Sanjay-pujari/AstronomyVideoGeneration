namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record HeroIntelligenceContract
{
    public required string PlanId { get; init; }
    public required string EventType { get; init; }
    public required string EventFamily { get; init; }
    public required string EditorialDecisionId { get; init; }
    public required string VisualStoryId { get; init; }
    public required string HeroCompositionId { get; init; }
    public required string HeroEditorialStrategyId { get; init; }
    public required string ViewerQuestion { get; init; }
    public required string PrimaryStory { get; init; }
    public required string ViewerTakeaway { get; init; }
    public required string EmotionalHook { get; init; }
    public required string CompositionGoal { get; init; }
    public required string EditorialGoal { get; init; }
    public required string ViewerEmotion { get; init; }
    public required string VisualRelationship { get; init; }
    public required IReadOnlyDictionary<string, string> PlatformVariantRecommendations { get; init; }
    public required HeroIntelligenceConfidenceSummary ConfidenceSummary { get; init; }
    public CreativeKnowledgeReviewSummary? CreativeKnowledgeReview { get; init; }
    public bool FallbackApplied { get; init; }
    public IReadOnlyList<string> MissingInputs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record HeroIntelligenceConfidenceSummary(double EditorialDecisionConfidence, double VisualStoryConfidence, double HeroCompositionConfidence, double HeroEditorialStrategyConfidence, double? QualityScore);

public sealed record CreativeKnowledgeReviewSummary(string KnowledgeUsed, string StoryGoal, string ViewerQuestion, string CompositionStrategy, IReadOnlyList<string> EditorialNotes);
