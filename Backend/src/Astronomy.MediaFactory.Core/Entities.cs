using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.Common;
namespace Astronomy.MediaFactory.Core;

public sealed class PipelineRun : EntityBase
{
    public DateOnly RunDate { get; set; }
    public ContentType ContentType { get; set; }
    public string LocationName { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Queued;
    public string? FailureReason { get; set; }
    public bool PublishToYouTube { get; set; }
    public string? YouTubeVideoId { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
}

public sealed class PipelineStageExecution : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string StageName { get; set; } = "";
    public string Status { get; set; } = "Started";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed class AstronomyEvent : EntityBase
{
    public DateOnly EventDate { get; set; }
    public string Category { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public double RankScore { get; set; }
    public string VisibilityWindow { get; set; } = "";
    public string Direction { get; set; } = "";
    public string ObservationTool { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class GeneratedScript : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public ContentType ContentType { get; set; }
    public DateOnly ScriptDate { get; set; }
    public string Prompt { get; set; } = "";
    public string ScriptBody { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string TagsCsv { get; set; } = "";
    public int EstimatedDurationSeconds { get; set; }
    public string? OptimizedTitle { get; set; }
    public string? AlternateTitlesCsv { get; set; }
    public string? OptimizedDescription { get; set; }
    public string? OptimizedTagsCsv { get; set; }
    public string? OptimizedHashtagsCsv { get; set; }
    public string? ThumbnailTextSuggestionsCsv { get; set; }
    public string? HookLine { get; set; }
    public string? PromptFeedbackContextJson { get; set; }
}

public sealed class MediaAsset : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string AssetType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string? BlobPath { get; set; }
    public string? PublicUrl { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class PublishedVideo : EntityBase
{
    public string Title { get; set; } = "";
    public string? YouTubeVideoId { get; set; }
    public string? BlobUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Published";
    public string? OptimizedTitle { get; set; }
    public string? OptimizedDescription { get; set; }
    public string? OptimizedTagsCsv { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool ThumbnailUploadedToYouTube { get; set; }
}


public sealed class ShortVideo : EntityBase
{
    public Guid ParentVideoId { get; set; }
    public string? YouTubeVideoId { get; set; }
    public int Duration { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PipelineJob : EntityBase
{
    public PipelineJobType JobType { get; set; }
    public Guid? ParentPipelineRunId { get; set; }
    public PipelineJobStatus Status { get; set; } = PipelineJobStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateOnly RunDate { get; set; }
    public ContentType ContentType { get; set; }
    public string LocationName { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public bool PublishToYouTube { get; set; }
    public bool UseTopicPlanner { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public sealed class VideoAnalytics : EntityBase
{
    public string VideoId { get; set; } = "";
    public long Views { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public int DurationSeconds { get; set; }
    public double? AverageViewDurationSeconds { get; set; }
    public double? CtrPercent { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    public ContentType ContentType { get; set; }
    public bool IsShort { get; set; }
    public string? ParentVideoId { get; set; }
    public string? Title { get; set; }
    public string? HookLine { get; set; }
}
