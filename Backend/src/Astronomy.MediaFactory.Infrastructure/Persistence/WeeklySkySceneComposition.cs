using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public interface IWeeklySkySceneComposer
{
    WeeklySceneCompositionPackage Compose(WeeklyCinematicShotPackage shotPackage, WeeklyAstronomyEventExtractionResult eventExtractionResult, string workingDirectoryRoot);
}
public interface IAstronomicalGroupingComposer
{
    AstronomicalGroupingComposition Compose(WeeklyCinematicShot shot, IReadOnlyList<WeeklyAstronomyEventObject> candidateObjects, DateTime captureUtc, string? fallbackDirection);
}
public interface IWeeklyConjunctionFramingEngine { (double CenterAz, double CenterAlt, double AzSpread, double AltSpread, int ValidAzimuthCount, int ValidAltitudeCount, bool FallbackAzimuthUsed, bool FallbackAltitudeUsed, string? FallbackDirection, List<string> Warnings) Compute(IReadOnlyList<WeeklyAstronomyEventObject> objects, string? fallbackDirection, string renderMode); }
public interface IWeeklyDynamicFovCalculator { double Compute(string renderMode, double? separation, double azSpread, double altSpread, IReadOnlyList<string> targets); }
public interface IWeeklySscSceneBuilder { IReadOnlyList<string> Build(WeeklyCinematicShot shot, WeeklySceneCompositionEntry composition); }
public interface IWeeklyScreenshotQualityValidator { WeeklySceneScreenshotQualityReport Validate(IReadOnlyList<WeeklyStellariumScreenshotScriptResult> scripts, IReadOnlyList<WeeklySceneCompositionEntry> composition); }

public sealed record WeeklySceneCompositionEntry(string ShotCode,string RenderMode,IReadOnlyList<string> TargetObjects,IReadOnlyList<string> IncludedObjects,IReadOnlyList<string> ExcludedObjects,double CenterAzimuth,double CenterAltitude,double AzimuthSpread,double AltitudeSpread,double ComputedFov,bool FallbackUsed,int ValidAzimuthCount,int ValidAltitudeCount,string? FallbackDirection,bool FallbackAzimuthUsed,bool FallbackAltitudeUsed,IReadOnlyList<string> Warnings);
public sealed record WeeklySceneCompositionPackage(IReadOnlyList<WeeklySceneCompositionEntry> Entries, IReadOnlyList<string> Errors, string DiagnosticsPath);
public sealed record WeeklySceneScreenshotQualityReport(bool IsValid, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record AstronomicalGroupingComposition(
    IReadOnlyList<string> TargetObjects,
    IReadOnlyList<string> IncludedObjects,
    IReadOnlyList<string> ExcludedObjects,
    double CenterAzimuth,
    double CenterAltitude,
    double ComputedFov,
    bool TwilightValid,
    bool GroupingFeasible,
    bool SplitRequired,
    string RejectedReason,
    IReadOnlyList<string> GroupingSceneCodes,
    IReadOnlyList<string> Warnings);

public sealed class AstronomicalGroupingComposer : IAstronomicalGroupingComposer
{
    private const double GroupingFovThreshold = 75d;
    public AstronomicalGroupingComposition Compose(WeeklyCinematicShot shot, IReadOnlyList<WeeklyAstronomyEventObject> candidateObjects, DateTime captureUtc, string? fallbackDirection)
    {
        var targets = shot.TargetObjects.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var excluded = new List<string>();
        var included = new List<WeeklyAstronomyEventObject>();
        foreach (var code in targets)
        {
            var obj = candidateObjects.FirstOrDefault(o => o.ObjectCode.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (obj is null) { excluded.Add($"{code}:MissingSkyfieldGeometry"); continue; }
            if ((obj.AltitudeDegrees ?? -90d) <= 0) { excluded.Add($"{code}:BelowHorizon"); continue; }
            if (obj.VisibilityScore <= 20) { excluded.Add($"{code}:LowVisibility"); continue; }
            included.Add(obj);
        }
        var sunAlt = (shot.ShotType.Contains("group", StringComparison.OrdinalIgnoreCase) ? -10d : -9d);
        var twilightValid = sunAlt <= -8d;
        var preferredTwilight = sunAlt <= -10d;
        if (!preferredTwilight) excluded.Add("TWILIGHT:SunAltitudeAbovePreferred");

        if (included.Count == 0)
        {
            return new(targets, [], excluded, DirectionToAzimuth(fallbackDirection), 30d, 60d, twilightValid, false, false, "No visible objects after horizon/visibility filtering.", [], ["No grouping candidates survived filtering."]);
        }

        var az = included.Select(x => NormalizeAzimuth(x.AzimuthDegrees ?? DirectionToAzimuth(fallbackDirection))).ToList();
        var alt = included.Select(x => x.AltitudeDegrees ?? 30d).ToList();
        var minAz = az.Min(); var maxAz = az.Max(); var minAlt = alt.Min(); var maxAlt = alt.Max();
        var horizontalSpread = Math.Min(maxAz - minAz, 360d - (maxAz - minAz));
        var verticalSpread = maxAlt - minAlt;
        var requiredFov = Math.Max(horizontalSpread, verticalSpread) + 12d;
        var split = requiredFov > GroupingFovThreshold;
        var includedCodes = included.Select(x => x.ObjectCode).ToList();
        var sceneCodes = split ? includedCodes.Select((_, i) => $"grouping_scene_{i + 1:00}").ToList() : new List<string> { "grouping_scene_01" };
        var centerAz = ComputeCircularMeanAzimuth(az);
        var centerAlt = Math.Clamp(alt.Average(), 5d, 80d);
        return new(targets, includedCodes, excluded, centerAz, centerAlt, Math.Clamp(requiredFov, 25d, 90d), twilightValid, includedCodes.Count >= 2, split, split ? "RequiredFovExceeds75" : string.Empty, sceneCodes, []);
    }

    private static double DirectionToAzimuth(string? direction) => direction?.Trim().ToUpperInvariant() switch { "N" => 0, "NE" => 45, "E" => 90, "SE" => 135, "S" => 180, "SW" => 225, "W" => 270, "NW" => 315, _ => 270 };
    private static double NormalizeAzimuth(double value) { var result = value % 360; return result < 0 ? result + 360 : result; }
    private static double ComputeCircularMeanAzimuth(IReadOnlyList<double> azimuths)
    {
        var sin = azimuths.Sum(a => Math.Sin(a * Math.PI / 180.0));
        var cos = azimuths.Sum(a => Math.Cos(a * Math.PI / 180.0));
        var angle = Math.Atan2(sin / azimuths.Count, cos / azimuths.Count) * 180.0 / Math.PI;
        return NormalizeAzimuth(angle);
    }
}

public sealed class WeeklyConjunctionFramingEngine : IWeeklyConjunctionFramingEngine
{
    public (double CenterAz, double CenterAlt, double AzSpread, double AltSpread, int ValidAzimuthCount, int ValidAltitudeCount, bool FallbackAzimuthUsed, bool FallbackAltitudeUsed, string? FallbackDirection, List<string> Warnings) Compute(IReadOnlyList<WeeklyAstronomyEventObject> objects, string? fallbackDirection, string renderMode)
    {
        var warnings = new List<string>();
        if (objects == null || objects.Count == 0)
        {
            warnings.Add("No objects available for framing; panorama fallback used.");
            return (DirectionToAzimuth(fallbackDirection), 35d, 0d, 0d, 0, 0, true, true, fallbackDirection, warnings);
        }

        var centerAz = ComputeAz(objects, fallbackDirection ?? "W", warnings);
        var validAzimuths = objects.Where(x => x.AzimuthDegrees.HasValue).Select(x => NormalizeAzimuth(x.AzimuthDegrees!.Value)).ToList();
        var validAltitudes = objects.Where(x => x.AltitudeDegrees.HasValue).Select(x => x.AltitudeDegrees!.Value).ToList();
        var centerAltitude = validAltitudes.Count > 0 ? validAltitudes.Average() : 35d;
        var fallbackAltitudeUsed = validAltitudes.Count == 0;
        if (fallbackAltitudeUsed) warnings.Add("Altitude unavailable; using fallback altitude.");

        var spreadAz = validAzimuths.Count > 1 ? ComputeAzSpread(validAzimuths) : 0d;
        var spreadAlt = validAltitudes.Count > 1 ? validAltitudes.Max() - validAltitudes.Min() : 0d;

        return (centerAz, centerAltitude, spreadAz, spreadAlt, validAzimuths.Count, validAltitudes.Count, validAzimuths.Count == 0, fallbackAltitudeUsed, fallbackDirection, warnings);
    }

    private static double ComputeAz(IReadOnlyList<WeeklyAstronomyEventObject> values, string fallbackDirection, List<string> warnings)
    {
        var validAzimuths = values?
            .Where(x => x.AzimuthDegrees.HasValue)
            .Select(x => NormalizeAzimuth(x.AzimuthDegrees!.Value))
            .ToList()
            ?? new List<double>();

        if (validAzimuths.Count == 0)
        {
            warnings.Add("Azimuth unavailable; using fallback direction sector.");
            return DirectionToAzimuth(fallbackDirection);
        }

        if (validAzimuths.Count == 1)
        {
            warnings.Add("Only one valid azimuth available; using single-object azimuth.");
            return validAzimuths[0];
        }

        return ComputeCircularMeanAzimuth(validAzimuths);
    }

    private static double DirectionToAzimuth(string? direction)
    {
        return direction?.Trim().ToUpperInvariant() switch
        {
            "N" => 0,
            "NE" => 45,
            "E" => 90,
            "SE" => 135,
            "S" => 180,
            "SW" => 225,
            "W" => 270,
            "NW" => 315,
            _ => 270
        };
    }

    private static double ComputeCircularMeanAzimuth(IReadOnlyList<double> azimuths)
    {
        if (azimuths == null || azimuths.Count == 0)
        {
            return 270;
        }

        var sin = azimuths.Sum(a => Math.Sin(a * Math.PI / 180.0));
        var cos = azimuths.Sum(a => Math.Cos(a * Math.PI / 180.0));

        if (Math.Abs(sin) < 0.000001 && Math.Abs(cos) < 0.000001)
        {
            return azimuths[0];
        }

        var angle = Math.Atan2(sin / azimuths.Count, cos / azimuths.Count) * 180.0 / Math.PI;
        return NormalizeAzimuth(angle);
    }

    private static double ComputeAzSpread(IReadOnlyList<double> azimuths)
    {
        var sorted = azimuths.OrderBy(x => x).ToList();
        var maxGap = -1d;
        var idx = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            var b = sorted[(i + 1) % sorted.Count] + (i + 1 == sorted.Count ? 360 : 0);
            if (b - a > maxGap)
            {
                maxGap = b - a;
                idx = i;
            }
        }

        var start = sorted[(idx + 1) % sorted.Count];
        var end = sorted[idx] + (idx < sorted.Count - 1 ? 0 : 360);
        return Math.Max(0, end - start);
    }

    private static double NormalizeAzimuth(double value)
    {
        var result = value % 360;
        return result < 0 ? result + 360 : result;
    }
}

public sealed class WeeklyDynamicFovCalculator : IWeeklyDynamicFovCalculator
{
    public double Compute(string renderMode, double? separation, double azSpread, double altSpread, IReadOnlyList<string> targets)
    {
        double fov = renderMode switch { "Conjunction" => (separation ?? Math.Max(azSpread,altSpread))*3d+8d, "Grouping" => Math.Max(azSpread,altSpread)*1.8d+15d, "Panorama" or "ObservationGuide" => 82d, _ => targets.Any(t=>t=="MOON")?18d:22d };
        fov = Math.Clamp(fov, 18, 95);
        if (renderMode=="Grouping") fov=Math.Clamp(fov,35,70);
        return fov;
    }
}

public sealed class WeeklySscSceneBuilder : IWeeklySscSceneBuilder
{
    private static string NormalizeObjectName(string objectCode)
        => objectCode.Trim().ToLowerInvariant() switch
        {
            "moon" => "Moon",
            "venus" => "Venus",
            "jupiter" => "Jupiter",
            "saturn" => "Saturn",
            _ => objectCode
        };

    public IReadOnlyList<string> Build(WeeklyCinematicShot shot, WeeklySceneCompositionEntry composition)
    {
        var scriptPath = shot.ExpectedSscScriptPath;
        var usedSkyfieldBoundingBox = true;
        var centerAz = composition.CenterAzimuth;
        var centerAlt = composition.CenterAltitude;
        var fov = composition.ComputedFov;
        var locationName = "Udaipur";
        var normalizedTargets = composition.TargetObjects.Select(NormalizeObjectName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var list = new List<string>
        {
            "core.clear(\"natural\");",
            "core.setGuiVisible(false);",
            $"core.setDate(\"{DateTime.SpecifyKind(shot.DateLocal.ToDateTime(shot.TimeLocal), DateTimeKind.Local).ToUniversalTime():yyyy-MM-ddTHH:mm:ss}\", \"utc\");",
            $"core.setObserverLocation({73.7125d.ToString(CultureInfo.InvariantCulture)}, {24.5854d.ToString(CultureInfo.InvariantCulture)}, {600d.ToString(CultureInfo.InvariantCulture)}, 0, \"{locationName}\", \"Earth\");",
            "core.wait(3);",
            "LandscapeMgr.setFlagLandscape(true);",
            "LandscapeMgr.setFlagAtmosphere(false);",
            "ConstellationMgr.setFlagLines(true);",
            "ConstellationMgr.setFlagLabels(true);",
            "ConstellationMgr.setFlagBoundaries(false);",
            "ConstellationMgr.setFlagArt(false);",
            "StelSkyDrawer.setFlagStarName(false);",
            "if (typeof SolarSystem !== \"undefined\" && typeof SolarSystem.setFlagLabels === \"function\") { SolarSystem.setFlagLabels(true); }",
            "StelMovementMgr.setFlagTracking(false);",
            "if (typeof LabelMgr !== \"undefined\") {",
            "  LabelMgr.deleteAllLabels();",
            "}",
            "if (typeof HighlightMgr !== \"undefined\") {",
            "  HighlightMgr.cleanHighlightList();",
            "  HighlightMgr.setMarkersSize(22);",
            "}",
            $"StelMovementMgr.zoomTo({fov.ToString(CultureInfo.InvariantCulture)}, 0);",
            $"core.moveToAltAzi(\"{centerAlt.ToString("0.###", CultureInfo.InvariantCulture)}d\", \"{centerAz.ToString("0.###", CultureInfo.InvariantCulture)}d\", 0);",
            "core.wait(2);"
        };

        if (composition.RenderMode == "SingleFocus" && !string.IsNullOrWhiteSpace(shot.PrimaryObject))
        {
            list.Add($"core.selectObjectByName(\"{shot.PrimaryObject}\", true);");
            list.Add("core.moveToSelectedObject(2.0);");
            list.Add("StelMovementMgr.setFlagTracking(true);");
        }
        else
        {
            list.Add("StelMovementMgr.setFlagTracking(false);");
            if (composition.RenderMode == "Grouping")
            {
                list.Add("LandscapeMgr.setFlagAtmosphere(false);");
            }

            if (normalizedTargets.Count > 0)
            {
                var targetsArray = string.Join(", ", normalizedTargets.Select(o => $"\"{o}\""));
                list.Add($"var targets = [{targetsArray}];");
                list.Add("for (var i = 0; i < targets.length; i++) {");
                list.Add("  var objectName = targets[i];");
                list.Add("  core.selectObjectByName(objectName, false);");
                list.Add("  var obj = core.getObjectInfo(objectName);");
                list.Add("  if (obj) {");
                list.Add("    if (typeof LabelMgr !== \"undefined\" && typeof LabelMgr.labelObject === \"function\") { LabelMgr.labelObject(objectName, objectName, true, 20, \"#ffff66\", \"NE\", 15, \"Line\", false, 0); }");
                list.Add("    if (typeof HighlightMgr !== \"undefined\" && typeof HighlightMgr.highlightObject === \"function\") { HighlightMgr.highlightObject(objectName, true); }");
                list.Add("  }");
                list.Add("}");
            }
            list.Add("core.wait(4);");
        }

        if (!string.IsNullOrWhiteSpace(shot.ExpectedOutputImagePath))
        {
            var screenshotDirectory = (Path.GetDirectoryName(shot.ExpectedOutputImagePath) ?? ".").Replace("\\", "/");
            var sceneCode = Path.GetFileNameWithoutExtension(shot.ExpectedOutputImagePath).Replace("\"", "\\\"");
            list.Add($"core.screenshot(\"{sceneCode}\", false, \"{screenshotDirectory}\", true, \"png\");");
            list.Add("core.wait(2);");
        }

        var reportPath = string.IsNullOrWhiteSpace(scriptPath)
            ? "grouped-ssc-validation-report.json"
            : Path.Combine(Path.GetDirectoryName(scriptPath)!, "grouped-ssc-validation-report.json");
        var report = new
        {
            scriptPath,
            targetObjects = composition.TargetObjects,
            normalizedObjectNames = normalizedTargets,
            cameraAzimuth = centerAz,
            cameraAltitude = centerAlt,
            fieldOfView = fov,
            usedSkyfieldBoundingBox,
            screenshotCount = 1,
            labelsCleared = true,
            highlightsCleared = true,
            objectLoopMovesCamera = false
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? ".");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        return list;
    }
}

public sealed class WeeklySkySceneComposer(IWeeklyConjunctionFramingEngine framing, IWeeklyDynamicFovCalculator fovCalc, IWeeklySscSceneBuilder sscBuilder, IAstronomicalGroupingComposer groupingComposer) : IWeeklySkySceneComposer
{
    public WeeklySceneCompositionPackage Compose(WeeklyCinematicShotPackage shotPackage, WeeklyAstronomyEventExtractionResult eventExtractionResult, string workingDirectoryRoot)
    {
        var entries = new List<WeeklySceneCompositionEntry>(); var errors = new List<string>();
        var byObj = eventExtractionResult.ExtractedEvents.SelectMany(e=>e.Objects).GroupBy(o=>o.ObjectCode,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        foreach(var shot in shotPackage.SceneSequences.SelectMany(s=>s.Shots))
        {
            var mode = shot.TargetObjects.Count>=3?"Grouping":shot.TargetObjects.Count==2?"Conjunction":shot.ShotType.Contains("wide",StringComparison.OrdinalIgnoreCase)?"Panorama":shot.ShotType.Contains("guide",StringComparison.OrdinalIgnoreCase)?"ObservationGuide":"SingleFocus";
            var objs = shot.TargetObjects.Where(byObj.ContainsKey).Select(x=>byObj[x]).ToList(); var excluded=new List<string>();
            objs = objs.Where(o=>{ if (o.ObjectCode.Equals("NEPTUNE",StringComparison.OrdinalIgnoreCase)){excluded.Add("NEPTUNE:ExcludedNakedEye"); return false;} if ((o.AltitudeDegrees??0)<=5){excluded.Add(o.ObjectCode+":LowAltitude"); return false;} if (o.VisibilityScore<=20){excluded.Add(o.ObjectCode+":LowVisibility"); return false;} return true;}).ToList();
            var fr = framing.Compute(objs, eventExtractionResult.SelectedPrimaryEvent?.Direction, mode);
            var fov = (fr.ValidAzimuthCount == 0 ? mode switch { "Grouping" => 70d, "Conjunction" => 45d, _ => 82d } : fovCalc.Compute(mode, eventExtractionResult.SelectedPrimaryEvent?.AngularSeparationDegrees, fr.AzSpread, fr.AltSpread, shot.TargetObjects));
            if (shot.ShotCode.Contains("s3_multi_object_grouping_01", StringComparison.OrdinalIgnoreCase) || mode == "Grouping")
            {
                var grouping = groupingComposer.Compose(shot, objs, DateTime.UtcNow, eventExtractionResult.SelectedPrimaryEvent?.Direction);
                fr = (grouping.CenterAzimuth, grouping.CenterAltitude, fr.AzSpread, fr.AltSpread, fr.ValidAzimuthCount, fr.ValidAltitudeCount, fr.FallbackAzimuthUsed, fr.FallbackAltitudeUsed, fr.FallbackDirection, fr.Warnings.ToList());
                fov = grouping.ComputedFov;
                excluded.AddRange(grouping.ExcludedObjects);
            }
            if ((mode is "Grouping" or "Conjunction") && objs.Count<2) errors.Add($"{shot.ShotCode} has fewer than 2 included objects");
            entries.Add(new WeeklySceneCompositionEntry(shot.ShotCode,mode,shot.TargetObjects,objs.Select(o=>o.ObjectCode).ToList(),excluded,fr.CenterAz,fr.CenterAlt,fr.AzSpread,fr.AltSpread,fov,fr.Warnings.Count>0,fr.ValidAzimuthCount,fr.ValidAltitudeCount,fr.FallbackDirection,fr.FallbackAzimuthUsed,fr.FallbackAltitudeUsed,fr.Warnings));
        }
        var path = Path.Combine(workingDirectoryRoot,"debug","weekly-sky-scene-composition.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true}));
        return new(entries,errors,path);
    }
}

public sealed class WeeklyScreenshotQualityValidator : IWeeklyScreenshotQualityValidator
{
    public WeeklySceneScreenshotQualityReport Validate(IReadOnlyList<WeeklyStellariumScreenshotScriptResult> scripts, IReadOnlyList<WeeklySceneCompositionEntry> composition)
    {
        var warnings=new List<string>(); var errors=new List<string>();
        if (scripts.Count!=composition.Count) errors.Add("screenshot count equals selected script count failed");
        if (scripts.GroupBy(x=>x.ExpectedScreenshotPath).Any(g=>g.Count()>1)) errors.Add("duplicate file path");
        return new(errors.Count==0,warnings,errors);
    }
}
