namespace Astronomy.MediaFactory.Contracts;

public sealed class SkyfieldSidecarOptions
{
    public const string SectionName = "SkyfieldSidecar";
    public string BaseUrl { get; set; } = "http://localhost:8010";
}
