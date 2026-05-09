namespace Astronomy.MediaFactory.Core;

public sealed class AIOptimizationRecommendations
{
    public IReadOnlyCollection<string> RecommendedHooks { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedVideoIdeas { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedThumbnailText { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedPublishTimes { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedObjectsToBoost { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedObjectsToAvoid { get; init; } = [];
    public IReadOnlyCollection<IReadOnlyCollection<string>> RecommendedHashtagSets { get; init; } = [];
    public IReadOnlyCollection<string> RiskWarnings { get; init; } = [];
    public double ConfidenceScore { get; init; }
    public string ReasoningSummary { get; init; } = "";
}
