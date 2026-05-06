namespace Astronomy.MediaFactory.Contracts;

public sealed class PublishingOptions
{
    public const string SectionName = "Publishing";
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "DryRun";
    public string DefaultPrivacyStatus { get; set; } = "private";
    public bool UploadThumbnail { get; set; } = true;
    public bool PublishLongVideo { get; set; } = true;
    public bool PublishShortVideo { get; set; } = true;
    public bool RequirePrePublishValidation { get; set; } = true;
}
