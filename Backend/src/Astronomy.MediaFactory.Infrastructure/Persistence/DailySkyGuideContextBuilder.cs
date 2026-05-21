using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideContextBuilder(MediaFactoryDbContext db, IOptions<SchedulerOptions> schedulerOptions) : IDailySkyGuideContextBuilder
{
    public async Task<DailySkyGuideContext> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var warnings = new List<string>();
        var region = ResolveRegion(plan.RegionId, warnings);

        var scheduledUtc = plan.ScheduledUtc ?? DateTimeOffset.UtcNow;
        if (plan.ScheduledUtc is null)
        {
            warnings.Add("scheduled_utc missing on plan; used current UTC date as fallback target date.");
        }

        var targetDate = DateOnly.FromDateTime(scheduledUtc.UtcDateTime);
        var timezone = TryResolveTimeZone(region.Timezone, warnings);

        var viewingStartLocal = CreateLocalDateTime(targetDate, new TimeOnly(19, 30), timezone, warnings);
        var viewingEndLocal = CreateLocalDateTime(targetDate, new TimeOnly(22, 30), timezone, warnings);
        var viewingMiddleLocal = viewingStartLocal.Add((viewingEndLocal - viewingStartLocal) / 2);

        string? primaryName = null;
        if (!string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode))
        {
            var celestialObject = await db.CelestialObjects.AsNoTracking().FirstOrDefaultAsync(x => x.Code == plan.PrimaryCelestialObjectCode, cancellationToken);
            primaryName = celestialObject?.Name;
            if (primaryName is null)
            {
                warnings.Add($"primary celestial object '{plan.PrimaryCelestialObjectCode}' was not found in master data.");
            }
        }

        var visibleCodes = string.IsNullOrWhiteSpace(plan.PrimaryCelestialObjectCode)
            ? Array.Empty<string>()
            : new[] { plan.PrimaryCelestialObjectCode! };
        if (visibleCodes.Length == 0)
        {
            warnings.Add("visibleObjectCodes fallback applied: no primary celestial object code on plan.");
        }

        var thumbnailStrategy = ResolveThumbnailStrategy(plan, warnings);

        return new DailySkyGuideContext(
            plan.Id,
            plan.RegionId,
            region.LocationName,
            region.Latitude,
            region.Longitude,
            region.Timezone,
            targetDate,
            viewingStartLocal,
            viewingEndLocal,
            plan.PrimaryCelestialObjectCode,
            primaryName,
            visibleCodes,
            [viewingStartLocal.ToUniversalTime(), viewingMiddleLocal.ToUniversalTime(), viewingEndLocal.ToUniversalTime()],
            "Stellarium",
            "AzureSpeech",
            thumbnailStrategy,
            warnings);
    }

    private static DateTimeOffset CreateLocalDateTime(DateOnly date, TimeOnly time, TimeZoneInfo zone, List<string> warnings)
    {
        var localUnspecified = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(localUnspecified);
        if (zone.IsInvalidTime(localUnspecified))
        {
            warnings.Add($"local time {localUnspecified:yyyy-MM-dd HH:mm} is invalid in timezone '{zone.Id}'; used zone offset fallback.");
        }

        return new DateTimeOffset(localUnspecified, offset);
    }

    private static TimeZoneInfo TryResolveTimeZone(string timezone, List<string> warnings)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch
        {
            warnings.Add($"timezone '{timezone}' not found; defaulted to Asia/Kolkata.");
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }

    private static string ResolveThumbnailStrategy(ContentGenerationPlan plan, List<string> warnings)
    {
        if (string.Equals(plan.PrimaryCelestialObjectCode, "Moon", StringComparison.OrdinalIgnoreCase)) return "MoonDominant";
        if (!string.IsNullOrWhiteSpace(plan.ThumbnailStyleCode)) return plan.ThumbnailStyleCode;
        warnings.Add("thumbnail strategy fallback applied: using MultiObjectCollage.");
        return "MultiObjectCollage";
    }

    private RegionResolution ResolveRegion(string regionId, List<string> warnings)
    {
        var region = schedulerOptions.Value.Regions.Items.FirstOrDefault(x => x.RegionId.Equals(regionId, StringComparison.OrdinalIgnoreCase));
        if (region is not null)
        {
            return new RegionResolution(region.DisplayName, region.Latitude, region.Longitude, region.Timezone);
        }

        if (regionId.Equals("IN-RJ-UDAIPUR", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("region fallback applied: hardcoded Udaipur mapping used.");
            return new RegionResolution("Udaipur", 24.5854, 73.7125, "Asia/Kolkata");
        }

        warnings.Add($"region '{regionId}' not found; defaulted to hardcoded Udaipur mapping.");
        return new RegionResolution("Udaipur", 24.5854, 73.7125, "Asia/Kolkata");
    }

    private sealed record RegionResolution(string LocationName, double Latitude, double Longitude, string Timezone);
}
