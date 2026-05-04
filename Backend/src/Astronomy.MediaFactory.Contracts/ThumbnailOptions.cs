namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailOptions
{
    public const string SectionName = "Thumbnail";

    public bool Enabled { get; init; } = true;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
}
