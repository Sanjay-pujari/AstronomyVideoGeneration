namespace Astronomy.MediaFactory.Contracts;

public sealed class StellariumOptions
{
    public const string SectionName = "Stellarium";
    public string ExecutablePath { get; set; } = "";
    public string ScriptsDirectory { get; set; } = "";
    public string CaptureDirectory { get; set; } = "";
    public string DefaultLandscape { get; set; } = "guereins";
    public string DefaultProjection { get; set; } = "ProjectionPerspective";
}
