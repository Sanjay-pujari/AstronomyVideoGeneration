using Astronomy.SscIntelligence.Contracts;

namespace Astronomy.SscIntelligence.NightWindow;

public sealed class NightWindowResolver : INightWindowResolver
{
    public NightWindowResult Resolve(DateTime date, string timezone, double latitude, double longitude, VisibilityRules rules, DateTime? astronomicalNightStartUtc = null, DateTime? astronomicalNightEndUtc = null, double? sunAltitudeDeg = null)
    {
        if (date == DateTime.MinValue || date == default)
        {
            date = BuildFallbackUtc(DateOnly.FromDateTime(DateTime.UtcNow), timezone);
            return BuildResult(date, timezone, true, sunAltitudeDeg, "fallback-invalid-date");
        }

        var candidateUtc = date.Kind == DateTimeKind.Utc ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc);
        if (candidateUtc.TimeOfDay == TimeSpan.Zero || candidateUtc == DateTime.MinValue)
        {
            candidateUtc = BuildFallbackUtc(DateOnly.FromDateTime(candidateUtc), timezone);
            return BuildResult(candidateUtc, timezone, true, sunAltitudeDeg, "fallback-midnight-utc");
        }

        if (astronomicalNightStartUtc.HasValue && astronomicalNightEndUtc.HasValue)
        {
            var best = astronomicalNightStartUtc.Value + TimeSpan.FromTicks((astronomicalNightEndUtc.Value - astronomicalNightStartUtc.Value).Ticks / 2);
            return BuildResult(best, timezone, !sunAltitudeDeg.HasValue || sunAltitudeDeg <= rules.TwilightSunAltitudeThresholdDeg, sunAltitudeDeg, "astronomical-night-midpoint");
        }

        var isNight = !sunAltitudeDeg.HasValue || sunAltitudeDeg <= rules.TwilightSunAltitudeThresholdDeg;
        if (!isNight)
        {
            candidateUtc = BuildFallbackUtc(DateOnly.FromDateTime(candidateUtc), timezone);
            return BuildResult(candidateUtc, timezone, true, sunAltitudeDeg, "fallback-daylight-detected");
        }

        return BuildResult(candidateUtc, timezone, isNight, sunAltitudeDeg, "input-observation-time");
    }

    private static NightWindowResult BuildResult(DateTime utc, string timezone, bool isNight, double? sunAltitudeDeg, string reason)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        return new NightWindowResult(DateTime.SpecifyKind(utc, DateTimeKind.Utc), local, isNight, sunAltitudeDeg, reason);
    }

    private static DateTime BuildFallbackUtc(DateOnly date, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var local = date.ToDateTime(new TimeOnly(20,45), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }
}
