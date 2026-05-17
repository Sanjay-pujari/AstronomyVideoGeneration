namespace Astronomy.MediaFactory.Contracts;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost:5005/api/youtubeoauth/callback";
    public string ApplicationName { get; set; } = "AstronomyVideoGeneration";
    public string ExpectedChannelTitle { get; set; } = "";
    public string ExpectedChannelId { get; set; } = "";
    public string PrivacyStatus { get; set; } = "private";
    public string CategoryId { get; set; } = "28";
    public string DefaultPrivacyStatus { get; set; } = "private";
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenFilePath { get; set; } = "youtube-oauth-token.json";
    public bool PublishingEnabled { get; set; }
    public bool UploadThumbnailForLongVideos { get; set; } = true;
    public bool UploadThumbnailForShorts { get; set; } = false;
    public bool UploadCustomThumbnailForShorts { get; set; } = true;
    public long MaxThumbnailSizeBytes { get; set; } = 2 * 1024 * 1024;
    public bool CompressThumbnailIfTooLarge { get; set; } = true;
    public int ThumbnailJpegQuality { get; set; } = 85;
    public int UploadRetryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 20;
    public int PublishRetryCooldownSeconds { get; set; } = 30;
}
