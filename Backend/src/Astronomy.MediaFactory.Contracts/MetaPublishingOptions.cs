namespace Astronomy.MediaFactory.Contracts;

public sealed class MetaPublishingOptions
{
    public const string SectionName = "MetaPublishing";

    public bool Enabled { get; set; }
    public bool PublishFacebookReel { get; set; } = true;
    public bool PublishInstagramReel { get; set; }
    public bool PublishFacebookLong { get; set; }
    public bool PublishFacebookFullVideo { get; set; }
    public bool PublishInstagramFullVideo { get; set; }
    public string Mode { get; set; } = "DryRun";
    public string FacebookPageId { get; set; } = "";
    public string CaptionHashtagSuffix { get; set; } = "#Astronomy #NightSky #Stargazing";
    public int FacebookReelProcessingPollSeconds { get; set; } = 10;
    public int FacebookReelProcessingMaxAttempts { get; set; } = 12;
    public int FacebookVerificationPollAttempts { get; set; } = 12;
    public int FacebookVerificationPollDelaySeconds { get; set; } = 15;
    public bool TreatProcessingTimeoutAsSuccess { get; set; } = true;
    public int InstagramContainerPollSeconds { get; set; } = 10;
    public int InstagramContainerMaxAttempts { get; set; } = 18;
    public string PublicMediaBaseUrl { get; set; } = "";
    public bool PublicMediaUploadEnabled { get; set; }
    public bool RequirePublishedState { get; set; } = true;
    public bool UsePosterFrameFallbackForFacebookReels { get; set; } = true;
    public bool UsePosterFrameFallbackForReels { get; set; } = true;
    public double FacebookReelPosterFrameDurationSeconds { get; set; } = 0.75d;
    public double PosterFrameDurationSeconds { get; set; } = 0.75d;
}
