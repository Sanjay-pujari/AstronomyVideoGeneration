using System.ComponentModel.DataAnnotations;
namespace Astronomy.MediaFactory.Contracts;

public enum SceneGenerationMode
{
    Hybrid = 0,
    ObjectFocused = 1,
    CompositionFocused = 2
}

public sealed class StellariumOptions
{
    public const string SectionName = "Stellarium";

    public bool Enabled { get; set; } = false;

    [MaxLength(1024)]
    public string OutputRoot { get; set; } = "outputs/content-plans";

    [Range(1, 600)]
    public int CaptureTimeoutSeconds { get; set; } = 60;

    [Range(0, 120)]
    public double StartupWaitSeconds { get; set; } = 5;

    [Range(0, 120)]
    public double ScriptExecutionWaitSeconds { get; set; } = 5;

    [Range(0, 120)]
    public double PreCaptureWaitSeconds { get; set; } = 3;

    [Range(0, 120)]
    public double PostCaptureWaitSeconds { get; set; } = 2;

    public bool UseExistingCaptureUtility { get; set; } = true;

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

    [Range(0, 120)]
    public double WeeklyApiLaunchWarmupSeconds { get; set; } = 8;

    [Range(0, 120)]
    public double WeeklyCameraSettleSeconds { get; set; } = 3;

    [Range(0, 120)]
    public double WeeklyPreScreenshotWaitSeconds { get; set; } = 2;

    public SceneGenerationMode DailySkyGuideSceneGenerationMode { get; set; } = SceneGenerationMode.Hybrid;

    [Range(1, 12)]
    public int MaxCompositionScenes { get; set; } = 5;

    [Range(1, 12)]
    public int MaxFocusedScenes { get; set; } = 3;
}
