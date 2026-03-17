using System.ComponentModel.DataAnnotations;

namespace Astronomy.MediaFactory.Contracts;

public sealed class SkyfieldSidecarOptions
{
    public const string SectionName = "SkyfieldSidecar";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:8010";
}
