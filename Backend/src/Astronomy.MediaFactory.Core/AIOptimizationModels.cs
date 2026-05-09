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
public sealed class AIOptimizationApplyRequest
{
    public AIOptimizationRecommendations? Recommendations { get; init; }
    public string ApprovedBy { get; init; } = "";
    public string ApprovalNotes { get; init; } = "";
    public IReadOnlyCollection<string>? AllowedApplyFields { get; init; }
}

public sealed class AIOptimizationApplyResult
{
    public bool Applied { get; init; }
    public string Status { get; init; } = "";
    public string? Reason { get; init; }
    public AIOptimizationAppliedProfile? Profile { get; init; }
    public IReadOnlyCollection<string> AppliedFields { get; init; } = [];
    public IReadOnlyCollection<string> IgnoredFields { get; init; } = [];
}

public sealed class AIOptimizationAppliedProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset ApprovedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AppliedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ApprovedBy { get; init; } = "";
    public string ApprovalNotes { get; init; } = "";
    public double ConfidenceScore { get; init; }
    public string ReasoningSummary { get; init; } = "";
    public AIOptimizationSafeValues AppliedValues { get; init; } = new();
    public AIOptimizationSafeValues PreviousValues { get; init; } = new();
    public IReadOnlyCollection<string> AppliedFields { get; init; } = [];
    public IReadOnlyCollection<string> IgnoredFields { get; init; } = [];
    public AIOptimizationRecommendations SourceRecommendations { get; init; } = new();
}

public sealed class AIOptimizationSafeValues
{
    public IReadOnlyCollection<string> RecommendedHooks { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedThumbnailText { get; init; } = [];
    public IReadOnlyCollection<IReadOnlyCollection<string>> RecommendedHashtagSets { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedPublishTimes { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedObjectsToBoost { get; init; } = [];
}
