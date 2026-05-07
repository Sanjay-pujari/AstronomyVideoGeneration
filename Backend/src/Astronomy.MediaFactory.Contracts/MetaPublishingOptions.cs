namespace Astronomy.MediaFactory.Contracts;

public sealed class MetaPublishingOptions
{
    public const string SectionName = "MetaPublishing";

    public bool Enabled { get; set; }
    public bool PublishFacebookReel { get; set; } = true;
    public bool PublishInstagramReel { get; set; }
    public string Mode { get; set; } = "DryRun";
    public string FacebookPageId { get; set; } = "";
    public string CaptionHashtagSuffix { get; set; } = "#Astronomy #NightSky #Stargazing";
    public int FacebookReelProcessingPollSeconds { get; set; } = 10;
    public int FacebookReelProcessingMaxAttempts { get; set; } = 12;
    public bool RequirePublishedState { get; set; } = true;
}
