namespace Astronomy.MediaFactory.Core;

public sealed class StellariumScene
{
    public string SceneId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Caption { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string TargetObject { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public DateTimeOffset SceneTimeUtc { get; init; }
    public string OutputImagePath { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public string MetadataPath { get; init; } = "";
}
