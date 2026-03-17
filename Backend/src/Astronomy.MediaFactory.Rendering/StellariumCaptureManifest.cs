using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumCaptureManifest
{
    public DateOnly Date { get; init; }
    public string LocationName { get; init; } = "";
    public string ScriptsDirectory { get; init; } = "";
    public string CaptureDirectory { get; init; } = "";
    public IReadOnlyCollection<StellariumScene> Scenes { get; init; } = Array.Empty<StellariumScene>();
}
