using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record HeroIntelligenceContract : EditorialProductContract
{
    public required string PlanId { get; init; }
    public required string EventType { get; init; }
    public required string EventFamily { get; init; }
    [JsonIgnore]
    public string HeroCompositionId => CompositionId;

    [JsonIgnore]
    public string HeroEditorialStrategyId => EditorialStrategyId;
    public required string EmotionalHook { get; init; }
    public required string CompositionGoal { get; init; }
    public required string VisualRelationship { get; init; }
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> PlatformVariantRecommendations => PlatformRecommendations;
    public required HeroIntelligenceConfidenceSummary ConfidenceSummary { get; init; }
    public CreativeKnowledgeReviewSummary? CreativeKnowledgeReview { get; init; }
    public bool FallbackApplied { get; init; }
    public IReadOnlyList<string> MissingInputs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record HeroIntelligenceConfidenceSummary(double EditorialDecisionConfidence, double VisualStoryConfidence, double HeroCompositionConfidence, double HeroEditorialStrategyConfidence, double? QualityScore);

public sealed record CreativeKnowledgeReviewSummary(string KnowledgeUsed, string StoryGoal, string ViewerQuestion, string CompositionStrategy, IReadOnlyList<string> EditorialNotes);
