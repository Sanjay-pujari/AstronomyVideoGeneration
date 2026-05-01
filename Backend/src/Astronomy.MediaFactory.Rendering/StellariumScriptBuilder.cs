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
        var screenshotPrefix = Path.GetFileNameWithoutExtension(scene.OutputImagePath);
        var screenshotDir = Path.GetDirectoryName(scene.OutputImagePath) ?? ".";
        var zoom = scene.SceneId.Contains("sky-overview", StringComparison.OrdinalIgnoreCase)
            || scene.SceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase)
            ? 80
            : 35;
        var renderSettleSeconds = scene.SceneId.Contains("sky-overview", StringComparison.OrdinalIgnoreCase)
            || scene.SceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase)
            ? 10.0
            : 8.0;
        var escapedLocationName = (scene.LocationName ?? "").Replace("\"", "\\\"");
        var normalizedScreenshotDir = screenshotDir.Replace("\\", "/").Replace("\"", "\\\"");
        var genericTargetObject = ResolveGenericObjectName(scene.TargetObject);
        var profile = DetermineVisualProfile(scene);

        return $$"""
core.clear("natural");

function safeCall(target, methodName, args) {
    if (typeof target !== "undefined" && target && typeof target[methodName] === "function") {
        target[methodName].apply(target, args);
    }
}

LandscapeMgr.setCurrentLandscapeName("{{_options.DefaultLandscape}}");
LandscapeMgr.setFlagLandscape(true);
LandscapeMgr.setFlagAtmosphere(false);
if (typeof core.setProjectionMode === "function") {
    core.setProjectionMode("{{_options.DefaultProjection}}");
}
core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");
core.wait(1.0);

safeCall(StelSkyDrawer, "setFlagStarName", [false]);
safeCall(ConstellationMgr, "setFlagLines", [false]);
safeCall(ConstellationMgr, "setFlagLabels", [false]);
safeCall(StelObjectMgr, "setFlagSelectedObjectPointer", [false]);

if ("{{profile}}" === "overview") {
    safeCall(ConstellationMgr, "setFlagLines", [true]);
    safeCall(ConstellationMgr, "setFlagLabels", [true]);
}

if ("{{profile}}" === "planet-moon") {
    safeCall(ConstellationMgr, "setFlagLines", [false]);
    safeCall(StelObjectMgr, "setFlagSelectedObjectPointer", [true]);
}

if ("{{profile}}" === "deep-sky") {
    safeCall(ConstellationMgr, "setFlagLabels", [false]);
    safeCall(StelSkyDrawer, "setFlagStarName", [false]);
}

core.selectObjectByName("{{genericTargetObject}}", true);
core.wait(0.5);
core.moveToSelectedObject(2.0);
StelMovementMgr.setFlagTracking(true);
StelMovementMgr.zoomTo({{zoom}}, 2.0);
core.wait({{renderSettleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
if (typeof core.setGuiVisible === "function") {
    core.setGuiVisible(false);
}
if (typeof core.setSelectedObjectMarkerVisible === "function") {
    core.setSelectedObjectMarkerVisible(false);
}
if (typeof StelFileMgr !== "undefined" && StelFileMgr && typeof StelFileMgr.setScreenshotDir === "function") {
    StelFileMgr.setScreenshotDir("{{normalizedScreenshotDir}}");
}
core.wait(1.0);
core.screenshot("{{screenshotPrefix}}", false, "{{normalizedScreenshotDir}}", true, "png");
core.wait(2.0);
core.quitStellarium();
""";
    }

    private static string DetermineVisualProfile(StellariumScene scene)
    {
        var sceneId = scene.SceneId ?? string.Empty;
        var target = scene.TargetObject ?? string.Empty;

        if (sceneId.Contains("overview", StringComparison.OrdinalIgnoreCase)
            || sceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase))
        {
            return "overview";
        }

        if (sceneId.Contains("deep-sky", StringComparison.OrdinalIgnoreCase)
            || sceneId.Contains("nebula", StringComparison.OrdinalIgnoreCase)
            || target.Contains("nebula", StringComparison.OrdinalIgnoreCase)
            || target.Contains("galaxy", StringComparison.OrdinalIgnoreCase)
            || target.Contains("cluster", StringComparison.OrdinalIgnoreCase))
        {
            return "deep-sky";
        }

        return "planet-moon";
    }

    private static string ResolveGenericObjectName(string targetObject)
    {
        if (string.IsNullOrWhiteSpace(targetObject))
        {
            return "Moon";
        }

        var normalized = targetObject.Trim();
        var knownObjects = new[]
        {
            "Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto"
        };

        foreach (var objectName in knownObjects)
        {
            if (normalized.Contains(objectName, StringComparison.OrdinalIgnoreCase))
            {
                return objectName;
            }
        }

        return normalized;
    }
}
