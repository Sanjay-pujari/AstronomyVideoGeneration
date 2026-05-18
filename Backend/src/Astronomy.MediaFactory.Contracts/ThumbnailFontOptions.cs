namespace Astronomy.MediaFactory.Contracts;

public sealed class ThumbnailFontOptions
{
    public const string SectionName = "ThumbnailFonts";

    public string DefaultEnglishFont { get; init; } = "assets/fonts/Montserrat-ExtraBold.ttf";

    public string HindiFont { get; init; } = "assets/fonts/NotoSansDevanagari-Bold.ttf";
}
