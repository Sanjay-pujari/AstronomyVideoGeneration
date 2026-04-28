using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumScriptBuilder
{
    private readonly StellariumOptions _options;

    public StellariumScriptBuilder(StellariumOptions options) => _options = options;

    public string BuildSceneScript(StellariumScene scene)
    {
        var utcDate = scene.SceneTimeUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        // Stellarium's `core.screenshot()` takes a file prefix and an output directory.
        // Use the scene's output directory so the renderer finds the expected file.
        var screenshotPrefix = Path.GetFileNameWithoutExtension(scene.OutputImagePath);
        var screenshotDir = Path.GetDirectoryName(scene.OutputImagePath) ?? ".";

        return $$"""
core.clear("natural");
LandscapeMgr.setCurrentLandscapeName("{{_options.DefaultLandscape}}");
StelMovementMgr.setCurrentProjectionTypeKey("{{_options.DefaultProjection}}");
core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "", "Earth");
core.wait(0.5);
core.selectObjectByName("{{scene.TargetObject}}", false);
StelMovementMgr.zoomTo(35, 0.0);
core.wait(3.0);
core.screenshot("{{screenshotPrefix}}", false, "{{screenshotDir.Replace("\\", "\\\\")}}", true, "png");
core.wait(1.0);
core.quitStellarium();
""";
    }
}
