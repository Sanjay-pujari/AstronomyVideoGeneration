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
    /// <summary>Enables construction of the internal governed package independently of uploads.</summary>
    public bool PackageEnabled { get; set; } = true;
    public bool ExternalPublishingEnabled { get; set; }
    public string PublishingPolicyVersion { get; set; } = "phase20-publishing-policy/1.0";
    public bool ManualReviewRequired { get; set; } = true;
    public bool PortableExportEnabled { get; set; }
    /// <summary>Maximum age of a Publishing claim before another worker may recover it.</summary>
    public int InProgressLeaseMinutes { get; set; } = 15;
}
