using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class AstronomyVisibilityService(MediaFactoryDbContext db) : IAstronomyVisibilityService
{
    public async Task<AstronomyVisibilityResult> CalculateVisibilityAsync(AstronomyVisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(request.Latitude));
        if (request.Longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(request.Longitude));
        var warnings = new List<string>();
        var zone = ResolveZone(request.Timezone, warnings);

        var sunsetLocal = request.TargetDate.ToDateTime(new TimeOnly(18,45), DateTimeKind.Unspecified);
        var sunriseLocal = request.TargetDate.AddDays(1).ToDateTime(new TimeOnly(6,0), DateTimeKind.Unspecified);
        warnings.Add("Using fallback sunset/sunrise times.");

        var sunsetUtc = TimeZoneInfo.ConvertTimeToUtc(sunsetLocal, zone);
        var sunriseUtc = TimeZoneInfo.ConvertTimeToUtc(sunriseLocal, zone);
        var bestStartLocal = sunsetLocal.AddMinutes(45);
        var latestEndLocal = request.TargetDate.ToDateTime(new TimeOnly(23,30), DateTimeKind.Unspecified);
        var preSunriseLocal = sunriseLocal.AddMinutes(-90);
        var bestEndLocal = latestEndLocal < preSunriseLocal ? latestEndLocal : preSunriseLocal;

        var (moonPhase, moonIllum) = ApproxMoonPhase(request.TargetDate);
        warnings.Add("Using approximate moon phase calculation.");

        var enabled = await db.CelestialObjects.AsNoTracking().Where(x=>x.Enabled).ToListAsync(cancellationToken);
        var preferred = string.IsNullOrWhiteSpace(request.PreferredObjectCode) ? null : enabled.FirstOrDefault(x=>x.Code==request.PreferredObjectCode);
        var selected = enabled.Where(x=>x.NakedEyeVisible).ToList();
        if (preferred is not null && selected.All(x=>x.Code!=preferred.Code))
        {
            selected.Add(preferred);
            warnings.Add($"Preferred object '{preferred.Code}' included though not naked-eye visible.");
        }

        var visible = selected.Select(x => new VisibleCelestialObjectResult(
            x.Code, x.Name, x.ObjectType, true,
            null, null, null,
            TimeZoneInfo.ConvertTimeToUtc(bestStartLocal, zone),
            TimeZoneInfo.ConvertTimeToUtc(bestEndLocal, zone),
            Convert.ToDouble(x.VisibilityPriority), Convert.ToDouble(x.VisibilityPriority), Convert.ToDouble(x.PhotogenicScore), Convert.ToDouble(x.EducationalScore), Convert.ToDouble(x.ViralityScore), null))
            .OrderByDescending(x => string.Equals(x.ObjectCode, request.PreferredObjectCode, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.VisibilityScore)
            .ThenByDescending(x => x.PhotographyScore)
            .ToList();

        return new AstronomyVisibilityResult(request.RegionId, request.LocationName, request.Latitude, request.Longitude, request.Timezone,
            request.TargetDate, sunsetUtc, sunriseUtc, TimeZoneInfo.ConvertTimeToUtc(bestStartLocal, zone), TimeZoneInfo.ConvertTimeToUtc(bestEndLocal, zone),
            moonPhase, moonIllum, visible, warnings);
    }

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
