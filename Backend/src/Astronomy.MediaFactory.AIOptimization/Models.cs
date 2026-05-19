namespace Astronomy.MediaFactory.AIOptimization;

public sealed record HookOptimizationRequest(
    IReadOnlyCollection<string> GeneratedHooks,
    string Language,
    IReadOnlyCollection<string> Objects,
    string EventType,
    string? TargetAudience);

public sealed record HookScoreResult(
    string Hook,
    double CuriosityScore,
    double EmotionalImpactScore,
    double ClarityScore,
    double ClickProbability,
    double FinalScore,
    string RecommendationReason);

public sealed record ThumbnailOptimizationResult(
    Guid PipelineRunId,
    int ObjectCount,
    double Brightness,
    int TextLength,
    string Language,
    double HookIntensity,
    double CompositionScore,
    DateTimeOffset CreatedUtc);

public sealed record PublishingOptimizationResult(
    Guid PipelineRunId,
    DateTimeOffset RecommendedPublishTime,
    IReadOnlyCollection<string> RecommendedHashtags,
    IReadOnlyCollection<string> RecommendedTags,
    string RecommendedAudienceType,
    IReadOnlyCollection<string> PlatformPriority,
    DateTimeOffset CreatedUtc);

public sealed record TrendSignalResult(
    DateOnly SignalDate,
    string Topic,
    double Score,
    string Source,
    DateTimeOffset CreatedUtc);
