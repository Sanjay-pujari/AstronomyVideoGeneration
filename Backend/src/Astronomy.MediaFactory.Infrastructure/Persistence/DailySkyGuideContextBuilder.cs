using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class DailySkyGuideContextBuilder(MediaFactoryDbContext db, IRegionResolutionService regionResolutionService, IAstronomyVisibilityService visibilityService, IStellariumScenePlannerResolver scenePlannerResolver) : IDailySkyGuideContextBuilder
{
    public async Task<DailySkyGuideContext> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var warnings = new List<string>();
        var region = await ResolveRegionAsync(plan.RegionId, plan.Title, warnings, cancellationToken);

        var scheduledUtc = plan.ScheduledUtc ?? DateTimeOffset.UtcNow;
        if (plan.ScheduledUtc is null) warnings.Add("scheduled_utc missing on plan; used current UTC date as fallback target date.");
        var targetDate = DateOnly.FromDateTime(scheduledUtc.UtcDateTime);
        var visibility = await visibilityService.CalculateVisibilityAsync(new AstronomyVisibilityRequest(
            plan.RegionId, region.LocationName, region.Latitude, region.Longitude, region.Timezone, targetDate, plan.PrimaryCelestialObjectCode, plan.Language), cancellationToken);
        warnings.AddRange(visibility.Warnings);
        var zone = TryResolveTimeZone(region.Timezone, warnings);
        var viewingStartLocal = TimeZoneInfo.ConvertTimeFromUtc(visibility.BestViewingStartUtc, zone);
        var viewingEndLocal = TimeZoneInfo.ConvertTimeFromUtc(visibility.BestViewingEndUtc, zone);
        var startOffset = new DateTimeOffset(viewingStartLocal, zone.GetUtcOffset(viewingStartLocal));
        var endOffset = new DateTimeOffset(viewingEndLocal, zone.GetUtcOffset(viewingEndLocal));
        var viewingMiddleLocal = startOffset.Add((endOffset - startOffset) / 2);

        string? primaryName = visibility.VisibleObjects.FirstOrDefault(x=>string.Equals(x.ObjectCode, plan.PrimaryCelestialObjectCode, StringComparison.OrdinalIgnoreCase))?.ObjectName;
        var visibleCodes = visibility.VisibleObjects.Select(x=>x.ObjectCode).ToArray();
        if (visibleCodes.Length == 0) warnings.Add("visibleObjectCodes fallback applied: no visible celestial objects found.");

        var thumbnailStrategy = ResolveThumbnailStrategy(plan, warnings);
        StellariumSceneCapturePlan? sceneCapturePlan = null;
        var planner = scenePlannerResolver.Resolve(plan.ContentCategoryCode);
        if (planner is not null)
        {
            sceneCapturePlan = await planner.BuildScenePlanAsync(plan, visibility, cancellationToken);
            warnings.AddRange(sceneCapturePlan.Warnings);
        }

        return new DailySkyGuideContext(
            plan.Id,
            plan.RegionId,
            region.LocationName,
            region.Latitude,
            region.Longitude,
            region.Timezone,
            targetDate,
            startOffset,
            endOffset,
            plan.PrimaryCelestialObjectCode,
            primaryName,
            visibleCodes,
            [viewingStartLocal.ToUniversalTime(), viewingMiddleLocal.ToUniversalTime(), viewingEndLocal.ToUniversalTime()],
            "Stellarium",
            "AzureSpeech",
            thumbnailStrategy,
            sceneCapturePlan,
            sceneCapturePlan?.Scenes.Count ?? 0,
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

    private async Task<RegionResolution> ResolveRegionAsync(string regionId, string? regionName, List<string> warnings, CancellationToken cancellationToken)
    {
        var region = await regionResolutionService.TryResolveAsync(regionId, regionName, cancellationToken);
        if (region is not null)
        {
            if (!string.Equals(region.CanonicalRegionId, region.RequestedRegionId, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"region alias resolved: requested '{region.RequestedRegionId}' mapped to canonical '{region.CanonicalRegionId}'.");
            }

            return new RegionResolution(region.LocationName, region.Latitude, region.Longitude, region.Timezone);
        }

        warnings.Add($"region '{regionId}' not found; defaulted to hardcoded Udaipur mapping.");
        return new RegionResolution("Udaipur", 24.5854, 73.7125, "Asia/Kolkata");
    }

    private sealed record RegionResolution(string LocationName, double Latitude, double Longitude, string Timezone);
}
