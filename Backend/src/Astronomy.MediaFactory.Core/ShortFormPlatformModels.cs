using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public enum ShortFormPlatform
{
    YouTubeShorts = 1,
    InstagramReels = 2,
    Facebook = 3
}

public enum PlatformPublicationStatus
{
    Pending = 1,
    Skipped = 2,
    Published = 3,
    Failed = 4
}

public sealed class PlatformPublicationTarget
{
    public ShortFormPlatform Platform { get; set; }
    public bool Enabled { get; set; }
    public string Title { get; set; } = "";
    public string Caption { get; set; } = "";
    public IReadOnlyCollection<string> Hashtags { get; set; } = [];
    public string PreferredPublishLocalTime { get; set; } = "";
    public string VideoPath { get; set; } = "";
    public string? ThumbnailPath { get; set; }
    public PlatformPublicationStatus Status { get; set; } = PlatformPublicationStatus.Pending;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ExternalPostId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? YouTubeShortEligible { get; set; }
}

public sealed class ShortFormPublicationRequest
{
    public Guid ParentShortVideoId { get; init; }
    public ContentType ContentType { get; init; }
    public bool PublishToYouTube { get; init; }
    public string Title { get; init; } = "";
    public string Caption { get; init; } = "";
    public string? HookLine { get; init; }
    public IReadOnlyCollection<string> Tags { get; init; } = [];
    public IReadOnlyCollection<string> Hashtags { get; init; } = [];
    public string VideoPath { get; init; } = "";
    public string? ThumbnailPath { get; init; }
}
