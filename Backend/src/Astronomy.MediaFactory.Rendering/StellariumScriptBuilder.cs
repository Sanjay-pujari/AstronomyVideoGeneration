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
        var zoom = scene.SceneId.Contains("sky-overview", StringComparison.OrdinalIgnoreCase)
            || scene.SceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase)
            ? 80
            : 35;
        var renderSettleSeconds = scene.SceneId.Contains("sky-overview", StringComparison.OrdinalIgnoreCase)
            || scene.SceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase)
            ? 10.0
            : 7.0;
        var escapedLocationName = (scene.LocationName ?? "").Replace("\"", "\\\"");

        return $$"""
core.clear("natural");
LandscapeMgr.setCurrentLandscapeName("{{_options.DefaultLandscape}}");
LandscapeMgr.setFlagLandscape(true);
LandscapeMgr.setFlagAtmosphere(false);
StelMovementMgr.setCurrentProjectionTypeKey("{{_options.DefaultProjection}}");
core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");
// Give Stellarium time to apply location/time and render a few frames.
core.wait(1.0);
core.selectObjectByName("{{scene.TargetObject}}", true);
core.wait(0.5);
core.moveToSelectedObject(2.0);
StelMovementMgr.setFlagTracking(true);
StelMovementMgr.zoomTo({{zoom}}, 2.0);
core.wait({{renderSettleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
// Some Stellarium builds ignore the 'dir' argument of core.screenshot(); set it explicitly.
StelFileMgr.setScreenshotDir("{{screenshotDir.Replace("\\", "\\\\")}}");
core.wait(1.0);
core.screenshot("{{screenshotPrefix}}", false, "", true, "png");
// Ensure the file is flushed before quitting.
core.wait(5.0);
core.quitStellarium();
""";
    }
}
