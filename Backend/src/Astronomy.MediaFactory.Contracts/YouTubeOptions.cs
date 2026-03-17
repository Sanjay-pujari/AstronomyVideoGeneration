namespace Astronomy.MediaFactory.Contracts;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ApplicationName { get; set; } = "AstronomyVideoGenerator";
    public string PrivacyStatus { get; set; } = "private";
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenFilePath { get; set; }
}
