namespace Astronomy.MediaFactory.Contracts;

public sealed class PlatformPublishingOptions
{
    public const string SectionName = "PlatformPublishing";
    public bool YouTubeShortsEnabled { get; set; } = true;
    public bool InstagramReelsEnabled { get; set; }
    public bool FacebookEnabled { get; set; }
    public string YouTubeShortsPreferredPublishLocalTime { get; set; } = "19:30";
    public string InstagramReelsPreferredPublishLocalTime { get; set; } = "21:00";
    public string FacebookPreferredPublishLocalTime { get; set; } = "20:30";
    public int PublishRetryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 20;
    public int PublishRetryCooldownSeconds { get; set; } = 30;
}
