namespace Astronomy.MediaFactory.AIOptimization;

public sealed record HookOptimizationRequest(IReadOnlyCollection<string> GeneratedHooks, string Language, IReadOnlyCollection<string> ObjectList, string EventMetadata, string TargetAudience, string EventType, Guid? PipelineRunId = null);
public sealed record HookScoreResult(string Hook,double CuriosityScore,double EmotionalImpactScore,double ClarityScore,double ClickProbability,double FinalScore,string RecommendationReason);
public sealed record HookOptimizationReport(Guid? PipelineRunId,IReadOnlyCollection<string> GeneratedHooks,IReadOnlyCollection<HookScoreResult> Scores,HookScoreResult? SelectedRecommendedHook,string Language,string TargetAudience,string EventType);
public sealed record ThumbnailMetadataScoreRequest(Guid? PipelineRunId,int ObjectCount,double Brightness,int TextLength,string Language,double HookIntensity,double CompositionScore);
public sealed record ThumbnailOptimizationResult(Guid Id,Guid? PipelineRunId,int ObjectCount,double Brightness,int TextLength,string Language,double HookIntensity,double CompositionScore,double FinalScore,DateTimeOffset CreatedUtc);
public sealed record TrendSignalResult(Guid Id,DateOnly SignalDate,string Topic,double Score,string Source,DateTimeOffset CreatedUtc);
public sealed record PublishingOptimizationResult(Guid Id,Guid? PipelineRunId,DateTimeOffset RecommendedPublishTimeUtc,IReadOnlyCollection<string> RecommendedHashtags,IReadOnlyCollection<string> RecommendedTags,string RecommendedAudienceType,IReadOnlyCollection<string> PlatformPriority,DateTimeOffset CreatedUtc);
