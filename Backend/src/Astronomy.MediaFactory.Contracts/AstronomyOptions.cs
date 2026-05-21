namespace Astronomy.MediaFactory.Contracts;

public sealed class AstronomyOptions
{
    public const string SectionName = "Astronomy";
    public bool UseSkyfield { get; set; } = true;
    public string SkyfieldSidecarBaseUrl { get; set; } = "http://localhost:8000";
    public int SkyfieldTimeoutSeconds { get; set; } = 30;
    public bool FallbackOnSkyfieldFailure { get; set; } = true;
}
