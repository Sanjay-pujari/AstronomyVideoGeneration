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
}
