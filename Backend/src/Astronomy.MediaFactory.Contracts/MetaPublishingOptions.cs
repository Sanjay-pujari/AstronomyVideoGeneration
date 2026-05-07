namespace Astronomy.MediaFactory.Contracts;

public sealed class MetaPublishingOptions
{
    public const string SectionName = "MetaPublishing";

    public bool Enabled { get; set; }
    public bool PublishFacebookReel { get; set; } = true;
    public bool PublishInstagramReel { get; set; }
    public bool PublishFacebookFullVideo { get; set; }
    public bool PublishInstagramFullVideo { get; set; }
    public string Mode { get; set; } = "DryRun";
    public string FacebookPageId { get; set; } = "";
    public string CaptionHashtagSuffix { get; set; } = "#Astronomy #NightSky #Stargazing";
    public int FacebookReelProcessingPollSeconds { get; set; } = 10;
    public int FacebookReelProcessingMaxAttempts { get; set; } = 12;
    public int InstagramContainerPollSeconds { get; set; } = 10;
    public int InstagramContainerMaxAttempts { get; set; } = 18;
    public string PublicMediaBaseUrl { get; set; } = "";
    public bool PublicMediaUploadEnabled { get; set; }
    public bool RequirePublishedState { get; set; } = true;
}
