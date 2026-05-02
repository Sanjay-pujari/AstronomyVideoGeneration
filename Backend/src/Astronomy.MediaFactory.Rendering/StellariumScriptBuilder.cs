using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumScriptBuilder
{
    private readonly StellariumOptions _options;

    public StellariumScriptBuilder(StellariumOptions options) => _options = options;

    public string BuildSceneScript(StellariumScene scene)
    {
        var sceneObservationTimeUtc = scene.ObservationContext.UtcObservationTime.ToUniversalTime();
        var utcDate = sceneObservationTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
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
        var escapedLocationName = (scene.ObservationContext.LocationName ?? scene.LocationName ?? "").Replace("\"", "\\\"");
        var normalizedScreenshotDir = screenshotDir.Replace("\\", "/").Replace("\"", "\\\"");
        var sceneObjectName = string.IsNullOrWhiteSpace(scene.ObservationContext.ObjectName)
            ? scene.TargetObject
            : scene.ObservationContext.ObjectName;
        var labelText = ResolveLabelText(sceneObjectName);
        var profile = DetermineVisualProfile(scene);
        var observerLongitude = scene.ObservationContext.Longitude;
        var observerLatitude = scene.ObservationContext.Latitude;
        var shouldSelectObject = !string.Equals(sceneObjectName, "Sky", StringComparison.OrdinalIgnoreCase);

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
core.setObserverLocation({{observerLongitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{observerLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");
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

if ({{shouldSelectObject.ToString().ToLowerInvariant()}}) {
    core.selectObjectByName("{{sceneObjectName.Replace(""", "\\"")}}", true);
    if (typeof StelObjectMgr.setFlagSelectedObjectPointer === "function") {
        StelObjectMgr.setFlagSelectedObjectPointer(true);
    }
    if ("{{profile}}" !== "overview" && typeof LabelMgr !== "undefined" && typeof LabelMgr.labelObject === "function") {
        LabelMgr.labelObject("{{labelText.Replace("\"", "\\\"")}}", "{{sceneObjectName.Replace("\"", "\\\"")}}", true, 22, "#ffff66", "NE", 20, "TextOnly");
    }
    core.wait(1.0);
    core.wait(0.5);
    core.moveToSelectedObject(2.0);
    StelMovementMgr.setFlagTracking(true);
}
StelMovementMgr.zoomTo({{zoom}}, 2.0);
core.wait({{renderSettleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
if (typeof core.setGuiVisible === "function") {
    core.setGuiVisible(false);
}
if ("{{profile}}" === "overview" && typeof core.setSelectedObjectMarkerVisible === "function") {
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


    private static string ResolveLabelText(string? targetObject)
    {
        return string.IsNullOrWhiteSpace(targetObject) ? "Sky" : targetObject.Trim();
    }
}
