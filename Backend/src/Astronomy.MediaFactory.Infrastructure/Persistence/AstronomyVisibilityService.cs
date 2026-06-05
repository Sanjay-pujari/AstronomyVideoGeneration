using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyVisibilityService(MediaFactoryDbContext db, ISkyfieldVisibilityClient skyfieldClient, IOptions<SkyfieldSidecarOptions> options) : IAstronomyVisibilityService
{
    public async Task<AstronomyVisibilityResult> CalculateVisibilityAsync(AstronomyVisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(request.Latitude));
        if (request.Longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(request.Longitude));
        var warnings = new List<string>();
        var zone = ResolveZone(request.Timezone, warnings);

        var enabled = await db.CelestialObjects.AsNoTracking().Where(x=>x.Enabled).ToListAsync(cancellationToken);
        var preferred = string.IsNullOrWhiteSpace(request.PreferredObjectCode) ? null : enabled.FirstOrDefault(x=>x.Code==request.PreferredObjectCode);
        var selected = enabled.Where(x=>x.NakedEyeVisible).ToList();
        if (preferred is not null && selected.All(x=>x.Code!=preferred.Code))
        {
            selected.Add(preferred);
            warnings.Add($"Preferred object '{preferred.Code}' included though not naked-eye visible.");
        }

        var cfg = options.Value;
        var skyReq = new SkyfieldVisibilityRequest(
            request.RegionId,
            request.LocationName,
            request.Latitude,
            request.Longitude,
            request.Timezone,
            request.TargetDate,
            selected.Select(x => new SkyfieldVisibilityCandidateRequest(x.Code, x.Name, x.ObjectType)).ToArray());
        var sky = await skyfieldClient.CalculateAsync(skyReq, cancellationToken);

        DateTime sunsetUtc;
        DateTime sunriseUtc;
        DateTime bestStartUtc;
        DateTime bestEndUtc;
        string moonPhase;
        double moonIllum;
        List<VisibleCelestialObjectResult> visible;

        if (cfg.Enabled && sky.Success && sky.SunsetUtc.HasValue && sky.SunriseUtc.HasValue)
        {
            warnings.Add("Visibility source: Skyfield.");
            warnings.AddRange(sky.Warnings);
            sunsetUtc = sky.SunsetUtc.Value;
            sunriseUtc = sky.SunriseUtc.Value;
            moonPhase = sky.MoonPhase ?? "Unknown";
            moonIllum = sky.MoonIlluminationPercent ?? 0;
            visible = BuildFromSkyfield(selected, sky, request.PreferredObjectCode, warnings);
            bestStartUtc = visible.Where(v => v.Visible).Select(v => v.BestViewingStartUtc).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(sunsetUtc.AddMinutes(45)).Min();
            bestEndUtc = visible.Where(v => v.Visible).Select(v => v.BestViewingEndUtc).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(sunriseUtc.AddMinutes(-90)).Max();
        }
        else
        {
            var sunsetLocal = request.TargetDate.ToDateTime(new TimeOnly(18,45), DateTimeKind.Unspecified);
            var sunriseLocal = request.TargetDate.AddDays(1).ToDateTime(new TimeOnly(6,0), DateTimeKind.Unspecified);
            warnings.Add("Using fallback sunset/sunrise times.");
            if (cfg.Enabled && cfg.FallbackOnFailure)
                warnings.Add("Skyfield calculation failed; fallback visibility approximation used.");
            warnings.Add("Visibility source: Fallback.");
            if (!string.IsNullOrWhiteSpace(sky.ErrorMessage)) warnings.Add($"Skyfield error: {sky.ErrorMessage}");
            sunsetUtc = TimeZoneInfo.ConvertTimeToUtc(sunsetLocal, zone);
            sunriseUtc = TimeZoneInfo.ConvertTimeToUtc(sunriseLocal, zone);
            var bestStartLocal = sunsetLocal.AddMinutes(45);
            var latestEndLocal = request.TargetDate.ToDateTime(new TimeOnly(23,30), DateTimeKind.Unspecified);
            var preSunriseLocal = sunriseLocal.AddMinutes(-90);
            var bestEndLocal = latestEndLocal < preSunriseLocal ? latestEndLocal : preSunriseLocal;
            bestStartUtc = TimeZoneInfo.ConvertTimeToUtc(bestStartLocal, zone);
            bestEndUtc = TimeZoneInfo.ConvertTimeToUtc(bestEndLocal, zone);
            (moonPhase, moonIllum) = ApproxMoonPhase(request.TargetDate);
            warnings.Add("Using approximate moon phase calculation.");
            visible = selected.Select(x => new VisibleCelestialObjectResult(
                x.Code, x.Name, x.ObjectType, true, null, null, null, bestStartUtc, bestEndUtc, Convert.ToDouble(x.VisibilityPriority), Convert.ToDouble(x.VisibilityPriority), Convert.ToDouble(x.PhotogenicScore), Convert.ToDouble(x.EducationalScore), Convert.ToDouble(x.ViralityScore), null)).ToList();
        }

        visible = visible.OrderByDescending(x => string.Equals(x.ObjectCode, request.PreferredObjectCode, StringComparison.OrdinalIgnoreCase) && x.Visible)
            .ThenByDescending(x => x.VisibilityScore).ThenByDescending(x => x.PhotographyScore).ToList();

        return new AstronomyVisibilityResult(request.RegionId, request.LocationName, request.Latitude, request.Longitude, request.Timezone,
            request.TargetDate, sunsetUtc, sunriseUtc, bestStartUtc, bestEndUtc,
            moonPhase, moonIllum, visible, warnings);
    }
    private static List<VisibleCelestialObjectResult> BuildFromSkyfield(List<CelestialObject> selected, SkyfieldVisibilityResponse sky, string? preferredObjectCode, List<string> warnings)
    {
        var map = sky.Objects.ToDictionary(x => x.ObjectCode, StringComparer.OrdinalIgnoreCase);
        var result = new List<VisibleCelestialObjectResult>();
        foreach (var x in selected)
        {
            if (!map.TryGetValue(x.Code, out var o))
            {
                result.Add(new VisibleCelestialObjectResult(x.Code, x.Name, x.ObjectType, false, null, null, null, null, null, 0, 0, 0, Convert.ToDouble(x.EducationalScore), Convert.ToDouble(x.ViralityScore), "Skyfield object result missing."));
                continue;
            }
            var altitudeScore = o.AltitudeScore > 0 ? o.AltitudeScore : AltitudeScoreFor(o.MaxAltitudeDegrees);
            var visibilityScore = (0.4 * altitudeScore) + (0.3 * Convert.ToDouble(x.VisibilityPriority)) + (0.15 * Convert.ToDouble(x.EducationalScore)) + (0.15 * Convert.ToDouble(x.ViralityScore));
            var photographyScore = (0.6 * Convert.ToDouble(x.PhotogenicScore)) + (0.4 * altitudeScore);
            result.Add(new VisibleCelestialObjectResult(x.Code, x.Name, x.ObjectType, o.Visible, o.RiseUtc, o.SetUtc, o.TransitUtc, o.BestViewingStartUtc, o.BestViewingEndUtc, altitudeScore, visibilityScore, photographyScore, Convert.ToDouble(x.EducationalScore), Convert.ToDouble(x.ViralityScore), o.Reason));
        }
        if (!string.IsNullOrWhiteSpace(preferredObjectCode) && result.All(r => !r.ObjectCode.Equals(preferredObjectCode, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Preferred object '{preferredObjectCode}' was not returned by Skyfield.");
            result.Add(new VisibleCelestialObjectResult(preferredObjectCode, preferredObjectCode, "Unknown", false, null, null, null, null, null, 0, 0, 0, 0, 0, "Preferred object not returned by Skyfield."));
        }
        return result;
    }
    private static double AltitudeScoreFor(double maxAltitude) => maxAltitude switch { >= 60 => 10, >= 45 => 8, >= 30 => 6, >= 15 => 4, _ => 2 };

    private static TimeZoneInfo ResolveZone(string timezone, List<string> warnings)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { warnings.Add($"timezone '{timezone}' not found; defaulted to Asia/Kolkata."); return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
    }

    private static (string,double) ApproxMoonPhase(DateOnly date)
    {
        var syn=29.53058867;
        var known=new DateTime(2000,1,6,18,14,0,DateTimeKind.Utc);
        var days=(date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc)-known).TotalDays;
        var age=((days%syn)+syn)%syn;
        var illum=(1-Math.Cos(2*Math.PI*age/syn))/2*100;
        string phase = age switch { <1.84566=>"New Moon", <5.53699=>"Waxing Crescent", <9.22831=>"First Quarter", <12.91963=>"Waxing Gibbous", <16.61096=>"Full Moon", <20.30228=>"Waning Gibbous", <23.99361=>"Last Quarter", <27.68493=>"Waning Crescent", _=>"New Moon"};
        return (phase, Math.Round(illum,2));
    }
}
