namespace Astronomy.MediaFactory.Core;

public sealed class PublishRequest
{
    public Guid PipelineRunId { get; init; }
    public string Platform { get; init; } = "YouTube";
    public string VideoPath { get; init; } = string.Empty;
    public string ThumbnailPath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string PrivacyStatus { get; init; } = "private";
    public bool UploadThumbnail { get; init; }
}

public sealed class PublishResult
{
    public bool Success { get; init; }
    public string Platform { get; init; } = "YouTube";
    public string? VideoId { get; init; }
    public string? VideoUrl { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelTitle { get; init; }
    public string? Error { get; init; }
    public string Mode { get; init; } = "DryRun";
    public DateTime PublishedUtc { get; init; } = DateTime.UtcNow;
}

public sealed class YouTubeChannelInfo
{
    public string ChannelId { get; init; } = string.Empty;
    public string ChannelTitle { get; init; } = string.Empty;
}
