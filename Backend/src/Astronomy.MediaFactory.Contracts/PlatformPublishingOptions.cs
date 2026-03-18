namespace Astronomy.MediaFactory.Contracts;

public sealed class PlatformPublishingOptions
{
    public const string SectionName = "PlatformPublishing";
    public bool YouTubeShortsEnabled { get; set; } = true;
    public bool InstagramReelsEnabled { get; set; }
    public bool FacebookEnabled { get; set; }
}

public sealed class InstagramPublishingOptions
{
    public const string SectionName = "InstagramPublishing";
    public bool PublishingEnabled { get; set; }
    public string? AccessToken { get; set; }
    public string? BusinessAccountId { get; set; }
}

public sealed class FacebookPublishingOptions
{
    public const string SectionName = "FacebookPublishing";
    public bool PublishingEnabled { get; set; }
    public string? AccessToken { get; set; }
    public string? PageId { get; set; }
}
