namespace Astronomy.MediaFactory.Contracts;

public sealed class PublishingTargetsOptions
{
    public const string SectionName = "PublishingTargets";

    public bool YouTubeLong { get; set; }
    public bool YouTubeShort { get; set; }
    public bool FacebookLong { get; set; }
    public bool FacebookReel { get; set; }
    public bool InstagramReel { get; set; }
    public bool InstagramPost { get; set; }
    public bool InstagramCarousel { get; set; }
    public bool FacebookPost { get; set; }
    public bool FacebookCarousel { get; set; }
}
