namespace Astronomy.MediaFactory.Contracts;

public sealed class MonetizationOptions
{
    public const string SectionName = "Monetization";
    public string AffiliateBaseUrl { get; set; } = "";
    public string DefaultAffiliateTag { get; set; } = "";
    public bool EnableAffiliateLinks { get; set; } = true;
    public bool EnablePinnedCommentText { get; set; } = true;
    public bool EnableSponsorSlots { get; set; } = false;
    public string? SponsorText { get; set; }
}
