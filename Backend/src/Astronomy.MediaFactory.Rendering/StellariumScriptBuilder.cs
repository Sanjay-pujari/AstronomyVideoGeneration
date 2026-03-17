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
        var screenshotName = Path.GetFileName(scene.OutputImagePath);

        return $$"""
core.clear("natural");
LandscapeMgr.setCurrentLandscapeName("{{_options.DefaultLandscape}}");
StelMovementMgr.setCurrentProjectionTypeKey("{{_options.DefaultProjection}}");
core.setDate("{{utcDate}}", "utc");
core.selectObjectByName("{{scene.TargetObject}}", false);
StelMovementMgr.zoomTo(35, 0.0);
core.wait(0.5);
core.screenshot("{{screenshotName}}", false, "png");
""";
    }
}
