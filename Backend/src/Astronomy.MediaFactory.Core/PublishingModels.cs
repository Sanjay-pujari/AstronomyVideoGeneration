namespace Astronomy.MediaFactory.Core;

public sealed class PublishAsset
{
    public string AssetType { get; init; } = "LongVideo";
    public string VideoPath { get; init; } = string.Empty;
    public string ThumbnailPath { get; init; } = string.Empty;
    public string LongThumbnailPath { get; init; } = string.Empty;
    public string ShortThumbnailPath { get; init; } = string.Empty;
    public string PlatformThumbnailPath { get; init; } = string.Empty;
    public string UploadedThumbnailUrl { get; init; } = string.Empty;
    public string ThumbnailSource { get; init; } = ThumbnailSources.GeneratedThumbnail;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string PrivacyStatus { get; init; } = "private";
    public bool UploadThumbnail { get; init; }
    public bool IsShort { get; init; }
    public bool? YouTubeShortEligible { get; init; }
}

public sealed class PublishRequest
{
    public Guid PipelineRunId { get; init; }
    public string Platform { get; init; } = "YouTube";
    public string AssetType { get; init; } = "LongVideo";
    public bool IsShort { get; init; }
    public string VideoPath { get; init; } = string.Empty;
    public string ThumbnailPath { get; init; } = string.Empty;
    public string LongThumbnailPath { get; init; } = string.Empty;
    public string ShortThumbnailPath { get; init; } = string.Empty;
    public string PlatformThumbnailPath { get; init; } = string.Empty;
    public string ThumbnailSource { get; init; } = ThumbnailSources.GeneratedThumbnail;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string PrivacyStatus { get; init; } = "private";
    public bool UploadThumbnail { get; init; }
    public bool? YouTubeShortEligible { get; init; }
}

public sealed class PublishResult
{
    public bool Success { get; init; }
    public string Platform { get; init; } = "YouTube";
    public string ContentType { get; init; } = "LongVideo";
    public string? UploadedThumbnailPath { get; init; }
    public string? UploadedThumbnailUrl { get; init; }
    public string? ThumbnailSource { get; init; }
    public bool ThumbnailUploadAttempted { get; init; }
    public bool ThumbnailUploadSuccess { get; init; }
    public string? ThumbnailWarning { get; init; }
    public string? VideoId { get; init; }
    public string? PostId { get; init; }
    public string? Url { get; init; }
    public string? VideoUrl { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelTitle { get; init; }
    public string? Error { get; init; }
    public List<string> Warnings { get; init; } = [];
    public string AssetType { get; init; } = "LongVideo";
    public bool IsShort { get; init; }
    public string Mode { get; init; } = "DryRun";
    public string? ContentKind { get; init; }
    public string? VideoPathUsed { get; init; }
    public string? ThumbnailPathUsed { get; init; }
    public string? ThumbnailStrategy { get; init; }
    public string? Warning { get; init; }
    public DateTime PublishedUtc { get; init; } = DateTime.UtcNow;
}

public sealed class YouTubeChannelInfo
{
    public string ChannelId { get; init; } = string.Empty;
    public string ChannelTitle { get; init; } = string.Empty;
}

public sealed class MetaPublishRequest
{
    public Guid PipelineRunId { get; init; }
    public string Platform { get; init; } = "Facebook";
    public string VideoPath { get; init; } = string.Empty;
    public string LongThumbnailPath { get; init; } = string.Empty;
    public string ShortThumbnailPath { get; init; } = string.Empty;
    public string PlatformThumbnailPath { get; init; } = string.Empty;
    public string UploadedThumbnailUrl { get; init; } = string.Empty;
    public string ThumbnailSource { get; init; } = ThumbnailSources.GeneratedThumbnail;
    public string Caption { get; init; } = string.Empty;
    public string ShortTitle { get; init; } = string.Empty;
    public bool IsReel { get; init; } = true;
    public bool PosterFrameApplied { get; init; }
    public string PosterFrameImagePath { get; init; } = string.Empty;
    public double PosterFrameDurationSeconds { get; init; }
}

public sealed class MetaPublishResult
{
    public bool Success { get; init; }
    public string Platform { get; init; } = "Facebook";
    public string ContentType { get; init; } = "Reel";
    public string? ContentKind { get; init; }
    public string? UploadedThumbnailPath { get; init; }
    public string? UploadedThumbnailUrl { get; init; }
    public string? ThumbnailSource { get; init; }
    public bool ThumbnailUploadAttempted { get; init; }
    public bool ThumbnailUploadSuccess { get; init; }
    public string? ThumbnailWarning { get; init; }
    public string Mode { get; init; } = "DryRun";
    public string? PostId { get; init; }
    public string? VideoId { get; init; }
    public string? Url { get; init; }
    public string? UploadMode { get; init; }
    public string? Error { get; init; }
    public bool PublishedVerified { get; init; }
    public string? VideoPathUsed { get; init; }
    public string? ThumbnailPathUsed { get; init; }
    public string? ThumbnailStrategy { get; init; }
    public string? Warning { get; init; }
    public List<string> Warnings { get; init; } = [];
    public bool PosterFrameApplied { get; init; }
    public string? PosterFrameVideoPath { get; init; }
    public DateTime PublishedUtc { get; init; } = DateTime.UtcNow;
}


public static class PlatformThumbnailContentTypes
{
    public const string LongVideo = "LongVideo";
    public const string ShortVideo = "ShortVideo";
    public const string Reel = "Reel";
}

public static class ThumbnailSources
{
    public const string GeneratedThumbnail = "GeneratedThumbnail";
    public const string FallbackThumbnail = "FallbackThumbnail";
    public const string PosterFrameFallback = "PosterFrameFallback";
    public const string None = "None";
}

public sealed class PlatformThumbnailResolution
{
    public string Platform { get; init; } = string.Empty;
    public string ContentType { get; init; } = PlatformThumbnailContentTypes.LongVideo;
    public string LongThumbnailPath { get; init; } = string.Empty;
    public string ShortThumbnailPath { get; init; } = string.Empty;
    public string PlatformThumbnailPath { get; init; } = string.Empty;
    public string ThumbnailSource { get; init; } = ThumbnailSources.GeneratedThumbnail;
    public bool IsValid { get; init; }
    public long? FileSizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Warning { get; init; }
}
