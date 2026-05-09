namespace Astronomy.MediaFactory.Contracts;

public sealed class GrowthOptions
{
    public const string SectionName = "Growth";
    public bool Enabled { get; set; } = true;
    public string WebsiteUrl { get; set; } = "";
    public string NewsletterUrl { get; set; } = "";
    public string AffiliateDisclosure { get; set; } = "Some links may be affiliate links.";
    public string DefaultCallToAction { get; set; } = "Follow AstroPulse for your daily sky guide.";
    public bool EnableAffiliateBlocks { get; set; } = false;
    public string AppDownloadUrl { get; set; } = "";
}
