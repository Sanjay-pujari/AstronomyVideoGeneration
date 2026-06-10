using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class PythonSkyfieldAccuracyProvider(ILogger<PythonSkyfieldAccuracyProvider> logger) : ISkyfieldAccuracyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SkyfieldAccuracyResult> VerifyMoonPhasesAsync(int year, RegionScheduleOptions region, CancellationToken cancellationToken) =>
        RunSkyfieldAsync("moon", year, region, cancellationToken);

    public Task<SkyfieldAccuracyResult> ComputePlanetPairingsAsync(int year, RegionScheduleOptions region, CancellationToken cancellationToken) =>
        RunSkyfieldAsync("planets", year, region, cancellationToken);

    public Task<SkyfieldAccuracyResult> AdjustMeteorMoonlightAsync(IReadOnlyList<AstronomyEventPreviewItem> events, RegionScheduleOptions region, CancellationToken cancellationToken)
    {
        var meteorYears = events.Where(e => e.EventType.Contains("Meteor", StringComparison.OrdinalIgnoreCase)).Select(e => e.PeakUtc.Year).Distinct().ToArray();
        return meteorYears.Length == 0
            ? Task.FromResult(new SkyfieldAccuracyResult())
            : RunSkyfieldAsync("meteors", meteorYears.Min(), region, cancellationToken);
    }

    private async Task<SkyfieldAccuracyResult> RunSkyfieldAsync(string mode, int year, RegionScheduleOptions region, CancellationToken cancellationToken)
    {
        var result = new SkyfieldAccuracyResult();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"astronomy-event-skyfield-{mode}-{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(scriptPath, SkyfieldScript, cancellationToken);
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add(mode);
            process.StartInfo.ArgumentList.Add(year.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Latitude.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Longitude.ToString(CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add(region.Timezone);
            process.StartInfo.ArgumentList.Add(FindEphemerisPath() ?? string.Empty);
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                result.Warnings.Add($"Skyfield {mode} computation failed; keeping approximate/manual statuses where applicable. {TrimWarning(stderr)}");
                return result;
            }

            var computed = JsonSerializer.Deserialize<SkyfieldAccuracyResult>(stdout, JsonOptions);
            if (computed is null)
            {
                result.Warnings.Add($"Skyfield {mode} computation returned no usable JSON; keeping approximate/manual statuses where applicable.");
                return result;
            }

            return computed;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Skyfield {Mode} computation unavailable.", mode);
            result.Warnings.Add($"Skyfield {mode} computation unavailable; keeping approximate/manual statuses where applicable. {ex.Message}");
            return result;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static string? FindEphemerisPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Backend", "python", "skyfield_sidecar", "de421.bsp"),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "python", "skyfield_sidecar", "de421.bsp"),
            Path.Combine(Directory.GetCurrentDirectory(), "python", "skyfield_sidecar", "de421.bsp")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string TrimWarning(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "No stderr details were provided.";
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 240 ? text : text[..240] + "…";
    }

    private const string SkyfieldScript = """
import json, sys, math
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
from skyfield import almanac
from skyfield.api import load, wgs84
mode=sys.argv[1]; year=int(sys.argv[2]); lat=float(sys.argv[3]); lon=float(sys.argv[4]); tz=ZoneInfo(sys.argv[5]); eph_path=sys.argv[6]
ts=load.timescale(); eph=load(eph_path or 'de421.bsp')
earth=eph['earth']; sun=eph['sun']; observer=earth+wgs84.latlon(lat, lon)
planets={'Mercury':'mercury','Venus':'venus','Mars':'mars','Jupiter':'jupiter barycenter','Saturn':'saturn barycenter'}
out={'planetPairings':[], 'moonPhases':[], 'meteorMoonlight':[], 'warnings':[]}
def iso(dt): return dt.astimezone(timezone.utc).isoformat().replace('+00:00','Z')
def local(dt): return dt.astimezone(tz).strftime('%Y-%m-%d %H:%M %z')
def quality(sep): return 'Excellent pairing' if sep <= 1.5 else ('Close pairing' if sep <= 3 else 'Broad grouping candidate')
def direction(az):
    dirs=['North','Northeast','East','Southeast','South','Southwest','West','Northwest']
    return dirs[int(((az+22.5)%360)//45)]
def moon_illum(dt):
    try: return float(almanac.fraction_illuminated(eph, 'moon', ts.from_datetime(dt))) * 100.0
    except Exception:
        phase=float(almanac.moon_phase(eph, ts.from_datetime(dt)).degrees)
        return (1-math.cos(math.radians(phase)))/2*100
if mode == 'moon':
    try:
        t0=ts.utc(year,1,1); t1=ts.utc(year+1,1,1)
        times, phases = almanac.find_discrete(t0, t1, almanac.moon_phases(eph))
        names=['NewMoon','FirstQuarter','FullMoon','LastQuarter']
        for t,p in zip(times,phases):
            if names[int(p)] in ('NewMoon','FullMoon'):
                dt=t.utc_datetime().replace(tzinfo=timezone.utc)
                out['moonPhases'].append({'phase':names[int(p)], 'peakUtc':iso(dt), 'localPeakTime':local(dt)})
    except Exception as e:
        out['warnings'].append('Skyfield moon phase computation failed; approximate moon events were not promoted. %s' % e)
elif mode == 'planets':
    try:
        samples=[]; start=datetime(year,1,1,tzinfo=timezone.utc); end=datetime(year+1,1,1,tzinfo=timezone.utc); dt=start
        while dt < end:
            t=ts.from_datetime(dt)
            sun_alt=(observer.at(t).observe(sun).apparent()).altaz()[0].degrees
            if sun_alt <= -6:
                apparent={}
                for name,key in planets.items():
                    app=observer.at(t).observe(eph[key]).apparent(); alt,az,d=app.altaz()
                    if alt.degrees >= 8: apparent[name]=(app, alt.degrees, az.degrees)
                names=list(apparent.keys())
                for i in range(len(names)):
                    for j in range(i+1,len(names)):
                        a,b=names[i],names[j]; sep=apparent[a][0].separation_from(apparent[b][0]).degrees
                        if sep <= 6:
                            samples.append((a,b,dt,sep,apparent[a][1],apparent[b][1],sun_alt,(apparent[a][2]+apparent[b][2])/2))
            dt += timedelta(hours=2)
        best={}
        for a,b,dt,sep,aa,bb,sa,az in samples:
            key=(a,b,dt.astimezone(tz).strftime('%Y-%m-%d'))
            if key not in best or sep < best[key][3]: best[key]=(a,b,dt,sep,aa,bb,sa,az)
        chosen=[]
        for pair in sorted(set((k[0],k[1]) for k in best)):
            vals=sorted([v for k,v in best.items() if k[:2]==pair], key=lambda x:x[2])
            cluster=[]
            for v in vals:
                if not cluster or (v[2]-cluster[-1][2]).total_seconds() <= 36*3600: cluster.append(v)
                else:
                    chosen.append(min(cluster, key=lambda x:x[3])); cluster=[v]
            if cluster: chosen.append(min(cluster, key=lambda x:x[3]))
        bright={'Venus','Jupiter'}
        for a,b,dt,sep,aa,bb,sa,az in sorted(chosen, key=lambda x:(x[2],x[3])):
            out['planetPairings'].append({'primaryObject':a,'secondaryObject':b,'peakUtc':iso(dt),'angularSeparationDegrees':sep,'objectAltitudesDegrees':{a:aa,b:bb},'sunAltitudeDegrees':sa,'bestViewingLocalTime':local(dt),'skyDirectionHint':direction(az),'quality':quality(sep),'involvesBrightPlanet':a in bright or b in bright})
    except Exception as e:
        out['warnings'].append('Skyfield planet-pairing computation failed; ManualSeed planet events were not replaced. %s' % e)
elif mode == 'meteors':
    for month,day in [(1,4),(4,22),(5,6),(7,30),(8,12),(10,21),(11,17),(12,14),(12,22)]:
        dt=datetime(year,month,day,6,0,0,tzinfo=timezone.utc)
        illum=moon_illum(dt)
        interference='Low' if illum <= 30 else ('Medium' if illum <= 70 else 'High')
        adj=5 if interference=='Low' else (-7 if interference=='Medium' else -15)
        out['meteorMoonlight'].append({'peakUtc':iso(dt),'moonIlluminationPercent':illum,'moonInterference':interference,'visibilityScoreAdjustment':adj,'bestViewingWindowLocal':'Post-midnight to pre-dawn local time when radiant is highest and twilight is absent.','radiantVisibilityNote':'Moonlight estimate computed for the rule-based peak; exact radiant altitude model is not asserted.'})
else:
    out['warnings'].append('Unknown Skyfield accuracy mode: %s' % mode)
print(json.dumps(out))
""";
}
