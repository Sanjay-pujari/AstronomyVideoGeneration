namespace Astronomy.MediaFactory.Core;

public sealed class HookOptimizationResultEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineRunId { get; set; }
    public string Hook { get; set; } = "";
    public string Language { get; set; } = "en";
    public string EventType { get; set; } = "";
    public double CuriosityScore { get; set; }
    public double EmotionalImpactScore { get; set; }
    public double ClarityScore { get; set; }
    public double ClickProbability { get; set; }
    public double FinalScore { get; set; }
    public string RecommendationReason { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class ThumbnailOptimizationResultEntity { public Guid Id { get; set; } = Guid.NewGuid(); public Guid PipelineRunId { get; set; } public int ObjectCount { get; set; } public double Brightness { get; set; } public int TextLength { get; set; } public string Language { get; set; } = "en"; public double HookIntensity { get; set; } public double CompositionScore { get; set; } public double FinalScore { get; set; } public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow; }
public sealed class TrendSignalEntity { public Guid Id { get; set; } = Guid.NewGuid(); public DateOnly SignalDate { get; set; } public string Topic { get; set; } = ""; public double Score { get; set; } public string Source { get; set; } = "static"; public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow; }
public sealed class PublishingOptimizationResultEntity { public Guid Id { get; set; } = Guid.NewGuid(); public Guid PipelineRunId { get; set; } public DateTimeOffset RecommendedPublishTimeUtc { get; set; } public string RecommendedHashtagsJson { get; set; } = "[]"; public string RecommendedTagsJson { get; set; } = "[]"; public string RecommendedAudienceType { get; set; } = "General"; public string PlatformPriorityJson { get; set; } = "[]"; public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow; }
