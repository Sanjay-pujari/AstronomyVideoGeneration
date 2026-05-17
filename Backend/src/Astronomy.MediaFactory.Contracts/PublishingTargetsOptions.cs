namespace Astronomy.MediaFactory.Contracts;

public sealed class PublishingTargetsOptions
{
    public const string SectionName = "PublishingTargets";

    public bool YouTubeLong { get; set; } = true;
    public bool YouTubeShort { get; set; } = true;
    public bool FacebookLong { get; set; } = true;
    public bool FacebookReel { get; set; } = true;
    public bool InstagramReel { get; set; } = true;
}
