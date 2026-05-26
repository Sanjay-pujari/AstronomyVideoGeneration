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
public interface IWeeklyConjunctionFramingEngine { (double CenterAz, double CenterAlt, double AzSpread, double AltSpread, List<string> Warnings) Compute(IReadOnlyList<WeeklyAstronomyEventObject> objects, string? fallbackDirection); }
public interface IWeeklyDynamicFovCalculator { double Compute(string renderMode, double? separation, double azSpread, double altSpread, IReadOnlyList<string> targets); }
public interface IWeeklySscSceneBuilder { IReadOnlyList<string> Build(WeeklyCinematicShot shot, WeeklySceneCompositionEntry composition); }
public interface IWeeklyScreenshotQualityValidator { WeeklySceneScreenshotQualityReport Validate(IReadOnlyList<WeeklyStellariumScreenshotScriptResult> scripts, IReadOnlyList<WeeklySceneCompositionEntry> composition); }

public sealed record WeeklySceneCompositionEntry(string ShotCode,string RenderMode,IReadOnlyList<string> TargetObjects,IReadOnlyList<string> IncludedObjects,IReadOnlyList<string> ExcludedObjects,double CenterAzimuth,double CenterAltitude,double AzimuthSpread,double AltitudeSpread,double ComputedFov,bool FallbackUsed,IReadOnlyList<string> Warnings);
public sealed record WeeklySceneCompositionPackage(IReadOnlyList<WeeklySceneCompositionEntry> Entries, IReadOnlyList<string> Errors, string DiagnosticsPath);
public sealed record WeeklySceneScreenshotQualityReport(bool IsValid, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

public sealed class WeeklyConjunctionFramingEngine : IWeeklyConjunctionFramingEngine
{
    public (double CenterAz, double CenterAlt, double AzSpread, double AltSpread, List<string> Warnings) Compute(IReadOnlyList<WeeklyAstronomyEventObject> objects, string? fallbackDirection)
    {
        var warnings = new List<string>();
        var az = objects.Select(o => o.AzimuthDegrees ?? DirectionToAz(fallbackDirection, warnings)).ToList();
        var alt = objects.Select(o => o.AltitudeDegrees ?? 20d).ToList();
        var (minAz,maxAz,spreadAz,centerAz)=ComputeAz(az);
        var minAlt = alt.Min(); var maxAlt = alt.Max(); var spreadAlt = maxAlt-minAlt;
        return (centerAz, (minAlt+maxAlt)/2d, spreadAz, spreadAlt, warnings);
    }
    static (double,double,double,double) ComputeAz(IReadOnlyList<double> values){ var sorted=values.OrderBy(x=>x).ToList(); var maxGap=-1d; var idx=0; for(int i=0;i<sorted.Count;i++){var a=sorted[i];var b=sorted[(i+1)%sorted.Count]+(i+1==sorted.Count?360:0); if(b-a>maxGap){maxGap=b-a;idx=i;}} var start=sorted[(idx+1)%sorted.Count]; var end=sorted[idx]+(idx<sorted.Count-1?0:360); var spread=end-start; var center=(start+end)/2d%360d; return (start%360,end%360,spread,center); }
    static double DirectionToAz(string? d,List<string> w){w.Add("MissingAzimuthForObject"); return (d??"W").StartsWith("E",StringComparison.OrdinalIgnoreCase)?90:(d??"W").StartsWith("S",StringComparison.OrdinalIgnoreCase)?180:(d??"W").StartsWith("N",StringComparison.OrdinalIgnoreCase)?0:270;}
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
    public IReadOnlyList<string> Build(WeeklyCinematicShot shot, WeeklySceneCompositionEntry composition)
    {
        var list=shot.PlannedSscCommands.Where(c=>!c.Contains("core.selectObjectByName(")&&!c.Contains("core.moveToSelectedObject(")).ToList();
        if (composition.RenderMode=="SingleFocus" && !string.IsNullOrWhiteSpace(shot.PrimaryObject))
        {
            list.Add($"core.selectObjectByName(\"{shot.PrimaryObject}\", true);"); list.Add("core.moveToSelectedObject(2.0);"); list.Add("StelMovementMgr.setFlagTracking(true);");
        }
        else
        {
            list.Add($"core.moveToAltAzi({composition.CenterAzimuth.ToString(CultureInfo.InvariantCulture)}, {composition.CenterAltitude.ToString(CultureInfo.InvariantCulture)}, 2.0);");
            list.Add("StelMovementMgr.setFlagTracking(false);");
        }
        return list;
    }
}

public sealed class WeeklySkySceneComposer(IWeeklyConjunctionFramingEngine framing, IWeeklyDynamicFovCalculator fovCalc, IWeeklySscSceneBuilder sscBuilder) : IWeeklySkySceneComposer
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
            var fr = framing.Compute(objs, eventExtractionResult.SelectedPrimaryEvent?.Direction);
            var fov = fovCalc.Compute(mode, eventExtractionResult.SelectedPrimaryEvent?.AngularSeparationDegrees, fr.AzSpread, fr.AltSpread, shot.TargetObjects);
            if ((mode is "Grouping" or "Conjunction") && objs.Count<2) errors.Add($"{shot.ShotCode} has fewer than 2 included objects");
            entries.Add(new WeeklySceneCompositionEntry(shot.ShotCode,mode,shot.TargetObjects,objs.Select(o=>o.ObjectCode).ToList(),excluded,fr.CenterAz,fr.CenterAlt,fr.AzSpread,fr.AltSpread,fov,fr.Warnings.Count>0,fr.Warnings));
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
