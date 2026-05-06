namespace Astronomy.MediaFactory.Contracts;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ApplicationName { get; set; } = "AstronomyVideoGeneration";
    public string PrivacyStatus { get; set; } = "private";
    public string CategoryId { get; set; } = "28";
    public string DefaultPrivacyStatus { get; set; } = "private";
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenFilePath { get; set; }
    public bool PublishingEnabled { get; set; }
    public int UploadRetryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int MaxRetryDelaySeconds { get; set; } = 20;
    public int PublishRetryCooldownSeconds { get; set; } = 30;
}
