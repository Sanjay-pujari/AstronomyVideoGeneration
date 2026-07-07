using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core.VisualIntelligence;

public sealed record HeroIntelligenceContract : EditorialProductContract
{
    public required HeroSpecificFields HeroSpecificFields { get; init; }

    [JsonIgnore]
    public string PlanId => HeroSpecificFields.PlanId;
    [JsonIgnore]
    public string EventType => HeroSpecificFields.EventType;
    [JsonIgnore]
    public string EventFamily => HeroSpecificFields.EventFamily;
    [JsonIgnore]
    public string HeroCompositionId => CompositionId;

    [JsonIgnore]
    public string HeroEditorialStrategyId => EditorialStrategyId;
    [JsonIgnore]
    public string EmotionalHook => HeroSpecificFields.EmotionalHook;
    [JsonIgnore]
    public string CompositionGoal => HeroSpecificFields.CompositionGoal;
    [JsonIgnore]
    public string VisualRelationship => HeroSpecificFields.VisualRelationship;
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> PlatformVariantRecommendations => PlatformRecommendations;
    [JsonIgnore]
    public HeroIntelligenceConfidenceSummary ConfidenceSummary => HeroSpecificFields.ConfidenceSummary;
    [JsonIgnore]
    public CreativeKnowledgeReviewSummary? CreativeKnowledgeReview => HeroSpecificFields.CreativeKnowledgeReview;
    [JsonIgnore]
    public bool FallbackApplied => HeroSpecificFields.FallbackApplied;
    [JsonIgnore]
    public IReadOnlyList<string> MissingInputs => HeroSpecificFields.MissingInputs;
    [JsonIgnore]
    public IReadOnlyList<string> Warnings => HeroSpecificFields.Warnings;
}

public sealed record HeroSpecificFields
{
    public required string PlanId { get; init; }
    public required string EventType { get; init; }
    public required string EventFamily { get; init; }
    public required string EmotionalHook { get; init; }
    public required string CompositionGoal { get; init; }
    public required string VisualRelationship { get; init; }
    public required HeroIntelligenceConfidenceSummary ConfidenceSummary { get; init; }
    public CreativeKnowledgeReviewSummary? CreativeKnowledgeReview { get; init; }
    public bool FallbackApplied { get; init; }
    public IReadOnlyList<string> MissingInputs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record HeroIntelligenceConfidenceSummary(double EditorialDecisionConfidence, double VisualStoryConfidence, double HeroCompositionConfidence, double HeroEditorialStrategyConfidence, double? QualityScore);

public sealed record CreativeKnowledgeReviewSummary(string KnowledgeUsed, string StoryGoal, string ViewerQuestion, string CompositionStrategy, IReadOnlyList<string> EditorialNotes);
