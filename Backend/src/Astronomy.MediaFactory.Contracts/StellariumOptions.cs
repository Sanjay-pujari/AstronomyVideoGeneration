using System.ComponentModel.DataAnnotations;

namespace Astronomy.MediaFactory.Contracts;

public sealed class StellariumOptions
{
    public const string SectionName = "Stellarium";

    [MaxLength(1024)]
    public string ExecutablePath { get; set; } = "";

    [MaxLength(1024)]
    public string ScriptsDirectory { get; set; } = "";

    [MaxLength(1024)]
    public string CaptureDirectory { get; set; } = "";

    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string DefaultLandscape { get; set; } = "guereins";

    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string DefaultProjection { get; set; } = "ProjectionPerspective";

    public bool DisableLandscapeForLowAltitudeObjects { get; set; } = true;

    [Range(0, 90)]
    public double LowAltitudeLandscapeCutoffDegrees { get; set; } = 25;

    public bool EnableCinematicMotion { get; set; } = false;

    [Range(1, 360)]
    public double CinematicZoomStart { get; set; } = 60;

    [Range(1, 360)]
    public double CinematicZoomEnd { get; set; } = 35;

    [Range(0, 120)]
    public double CinematicWaitBeforeScreenshotSeconds { get; set; } = 8;
}
