using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Rendering;

public sealed class StellariumScriptBuilder
{
    private readonly StellariumOptions _options;

    public StellariumScriptBuilder(StellariumOptions options) => _options = options;

    public string BuildSceneScript(StellariumScene scene)
    {
        var utcDate = scene.ObservationContext.UtcObservationTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var screenshotPrefix = Path.GetFileNameWithoutExtension(scene.OutputImagePath);
        var screenshotDir = (Path.GetDirectoryName(scene.OutputImagePath) ?? ".").Replace("\\", "/").Replace("\"", "\\\"");
        var escapedLocationName = (scene.ObservationContext.LocationName ?? scene.LocationName ?? "").Replace("\"", "\\\"");
        var objectName = string.IsNullOrWhiteSpace(scene.ObservationContext.ObjectName) ? scene.TargetObject : scene.ObservationContext.ObjectName;
        var escapedObjectName = (objectName ?? "Sky").Replace("\"", "\\\"");
        var isOverviewScene = IsOverviewScene(scene);
        var zoomLevel = isOverviewScene ? 80d : GetZoomLevel(scene.ObservationContext);
        var minimumObjectAltitudeDegrees = _options.LowAltitudeLandscapeCutoffDegrees;

        if (!isOverviewScene && (!scene.ObservationContext.IsVisible || scene.ObservationContext.AltitudeDegrees < minimumObjectAltitudeDegrees))
        {
            return BuildSafeSkyFallbackScript(scene, utcDate, screenshotPrefix, screenshotDir, escapedLocationName);
        }

        return isOverviewScene
            ? BuildOverviewScript(scene, utcDate, screenshotPrefix, screenshotDir, escapedLocationName, zoomLevel)
            : BuildObjectScript(scene, utcDate, screenshotPrefix, screenshotDir, escapedLocationName, escapedObjectName, zoomLevel);
    }

    private string BuildObjectScript(StellariumScene scene, string utcDate, string screenshotPrefix, string screenshotDir, string escapedLocationName, string escapedObjectName, double zoomLevel)
    {
        var cinematicZoomStart = GetCinematicZoomStart(scene.ObservationContext, _options.CinematicZoomStart);
        var cinematicZoomEnd = GetCinematicZoomEnd(scene.ObservationContext, _options.CinematicZoomEnd, zoomLevel);

        return $$"""
core.clear("natural");

// Disable landscape for object scenes
LandscapeMgr.setFlagLandscape(false);
LandscapeMgr.setFlagAtmosphere(false);

// Set date & location (MUST use SceneObservationContext)
core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.ObservationContext.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.ObservationContext.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");

// Wait for sky engine update
core.wait(2.0);

// Enable constellation context
ConstellationMgr.setFlagLines(true);
ConstellationMgr.setFlagLabels(true);
ConstellationMgr.setFlagBoundaries(false);

core.output("SceneId={{scene.SceneId}} | ObjectName={{scene.ObservationContext.ObjectName}} | UtcObservationTime={{scene.ObservationContext.UtcObservationTime:O}} | LocalObservationTime={{scene.ObservationContext.LocalObservationTime:O}} | AltitudeDegrees={{(scene.ObservationContext.AltitudeDegrees?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a")}} | ZoomLevel={{zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}} | LandscapeEnabled=false");
core.output("SceneId={{scene.SceneId}} | ObjectName={{scene.ObservationContext.ObjectName}} | EnableCinematicMotion={{_options.EnableCinematicMotion}} | ZoomStart={{cinematicZoomStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}} | ZoomEnd={{cinematicZoomEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)}} | WaitBeforeScreenshot={{_options.CinematicWaitBeforeScreenshotSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}}");

// Select object
core.selectObjectByName("{{escapedObjectName}}", true);
core.wait(1.0);

// Keep object label visible for screenshot.
try {
    if (typeof LabelMgr !== "undefined" &&
        typeof LabelMgr.labelObject === "function") {
        LabelMgr.labelObject("{{escapedObjectName}}", "{{escapedObjectName}}", true, 24);
    }
} catch (e) {
    core.output("Object label creation failed: " + e);
}

// Move camera to object
core.moveToSelectedObject(2.0);
core.wait(2.0);

// Enable tracking
StelMovementMgr.setFlagTracking(true);

{{BuildObjectZoomBlock(zoomLevel, cinematicZoomStart, cinematicZoomEnd)}}

// Screenshot
core.screenshot("{{screenshotPrefix}}", false, "{{screenshotDir}}", true, "png");

core.wait(2.0);
core.quitStellarium();
""";
    }

    private string BuildObjectZoomBlock(double zoomLevel, double cinematicZoomStart, double cinematicZoomEnd)
    {
        if (!_options.EnableCinematicMotion)
        {
            return $$"""
// Apply zoom (dynamic based on object type)
StelMovementMgr.zoomTo({{zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 2.0);

// Stabilize frame
core.wait(6.0);
""";
        }

        return $$"""
// Cinematic zoom-in enabled.
// Start wide, then smoothly zoom toward selected object.
StelMovementMgr.zoomTo({{cinematicZoomStart.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0.0);
core.wait(1.0);
StelMovementMgr.zoomTo({{cinematicZoomEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 6.0);
core.wait({{_options.CinematicWaitBeforeScreenshotSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
""";
    }

    private string BuildOverviewScript(StellariumScene scene, string utcDate, string screenshotPrefix, string screenshotDir, string escapedLocationName, double zoomLevel)
    {
        return $$"""
core.clear("natural");

LandscapeMgr.setCurrentLandscapeName("guereins");
LandscapeMgr.setFlagLandscape(true);
LandscapeMgr.setFlagAtmosphere(false);

core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.ObservationContext.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.ObservationContext.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");

core.wait(2.0);

ConstellationMgr.setFlagLines(true);
ConstellationMgr.setFlagLabels(true);

core.output("SceneId={{scene.SceneId}} | ObjectName={{scene.ObservationContext.ObjectName}} | UtcObservationTime={{scene.ObservationContext.UtcObservationTime:O}} | LocalObservationTime={{scene.ObservationContext.LocalObservationTime:O}} | AltitudeDegrees={{(scene.ObservationContext.AltitudeDegrees?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a")}} | ZoomLevel={{zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}} | LandscapeEnabled=true");

StelMovementMgr.zoomTo({{zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 2.0);

core.wait(6.0);

core.screenshot("{{screenshotPrefix}}", false, "{{screenshotDir}}", true, "png");

core.wait(2.0);
core.quitStellarium();
""";
    }

    private string BuildSafeSkyFallbackScript(StellariumScene scene, string utcDate, string screenshotPrefix, string screenshotDir, string escapedLocationName)
    {
        return $$"""
core.clear("natural");

LandscapeMgr.setCurrentLandscapeName("guereins");
LandscapeMgr.setFlagLandscape(true);
LandscapeMgr.setFlagAtmosphere(false);

core.setDate("{{utcDate}}", "utc");
core.setObserverLocation({{scene.ObservationContext.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, {{scene.ObservationContext.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0, 0, "{{escapedLocationName}}", "Earth");

core.wait(2.0);

ConstellationMgr.setFlagLines(true);
ConstellationMgr.setFlagLabels(true);

core.output("SceneId={{scene.SceneId}} fallback: object skipped due to visibility/altitude. IsVisible={{scene.ObservationContext.IsVisible}} AltitudeDegrees={{(scene.ObservationContext.AltitudeDegrees?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a")}} MinimumAltitudeDegrees={{_options.LowAltitudeLandscapeCutoffDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)}}");

StelMovementMgr.zoomTo(80, 2.0);

core.wait(6.0);

core.screenshot("{{screenshotPrefix}}", false, "{{screenshotDir}}", true, "png");

core.wait(2.0);
core.quitStellarium();
""";
    }

    private static bool IsOverviewScene(StellariumScene scene)
    {
        return scene.SceneId.Contains("sky-overview", StringComparison.OrdinalIgnoreCase)
            || scene.SceneId.Contains("wide-sky", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.ObservationContext.ObjectType, "Overview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene.ObservationContext.SceneType, "Overview", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetZoomLevel(SceneObservationContext scene)
    {
        if (scene.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase)) return 30;
        if (scene.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)) return 35;
        if (scene.ObjectType.Equals("Star", StringComparison.OrdinalIgnoreCase)) return 45;
        if (scene.ObjectType.Equals("DeepSky", StringComparison.OrdinalIgnoreCase)) return 55;
        if (scene.ObjectType.Equals("Cluster", StringComparison.OrdinalIgnoreCase) || scene.ObjectType.Equals("Galaxy", StringComparison.OrdinalIgnoreCase)) return 60;
        return 40;
    }

    private static double GetCinematicZoomStart(SceneObservationContext scene, double fallbackZoomStart)
    {
        if (scene.ObjectType.Equals("Moon", StringComparison.OrdinalIgnoreCase)) return 55;
        if (scene.ObjectType.Equals("Planet", StringComparison.OrdinalIgnoreCase)) return 60;
        if (scene.ObjectType.Equals("Star", StringComparison.OrdinalIgnoreCase)) return 70;
        if (scene.ObjectType.Equals("DeepSky", StringComparison.OrdinalIgnoreCase)
            || scene.ObjectType.Equals("Cluster", StringComparison.OrdinalIgnoreCase)
            || scene.ObjectType.Equals("Galaxy", StringComparison.OrdinalIgnoreCase)) return 75;
        return fallbackZoomStart;
    }

    private static double GetCinematicZoomEnd(SceneObservationContext scene, double fallbackZoomEnd, double objectTypeZoomLevel)
    {
        if (!string.IsNullOrWhiteSpace(scene.ObjectType)) return objectTypeZoomLevel;
        return fallbackZoomEnd;
    }
}
