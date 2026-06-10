using System.ComponentModel.DataAnnotations;

namespace Astronomy.MediaFactory.Contracts;

public sealed class SkyfieldSidecarOptions
{
    public const string SectionName = "SkyfieldSidecar";

    public bool Enabled { get; set; } = true;

    [Url]
    public string BaseUrl { get; set; } = "http://localhost:8010";

    public int TimeoutSeconds { get; set; } = 30;

    public int YearlyAccuracyTimeoutSeconds { get; set; } = 180;

    public bool FallbackOnFailure { get; set; } = true;
}
