using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class VisibilitySample
{
    public DateTime LocalObservationTime { get; init; }
    public DateTimeOffset UtcObservationTime { get; init; }
    public double AltitudeDegrees { get; init; }
    public double AzimuthDegrees { get; init; }
    public string DirectionLabel { get; init; } = "N";
    public bool IsAboveHorizon { get; init; }
    public bool IsVisibleCandidate { get; init; }
}

public sealed class SceneObservationTime
{
    public string SceneId { get; init; } = "";
    public string SceneTitle { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public DateTime LocalObservationTime { get; init; }
    public DateTimeOffset UtcObservationTime { get; init; }
    public string Timezone { get; init; } = "";
    public string Reason { get; init; } = "";
    public double AltitudeDegrees { get; init; }
    public double AzimuthDegrees { get; init; }
    public string DirectionLabel { get; init; } = "N";
    public bool IsVisible { get; init; }
    public string VisibilityReason { get; init; } = "";
    public IReadOnlyList<VisibilitySample> VisibilitySearchSamples { get; init; } = [];
}

public interface IObservationTimeService
{
    IReadOnlyList<SceneObservationTime> SelectSceneTimes(AstronomyContext context, DateOnly targetDate, ObservationOptions observationOptions);
}

public sealed class ObservationTimeService : IObservationTimeService
{
    public IReadOnlyList<SceneObservationTime> SelectSceneTimes(AstronomyContext context, DateOnly targetDate, ObservationOptions observationOptions)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(observationOptions.Timezone);
        var sunset = targetDate.ToDateTime(new TimeOnly(18, 30));
        var sunrise = targetDate.AddDays(1).ToDateTime(new TimeOnly(6, 0));
        var step = TimeSpan.FromMinutes(Math.Max(10, observationOptions.VisibilitySearchStepMinutes));

        SceneObservationTime BuildObjectScene(string sceneId, string title, string objectName)
        {
            var samples = BuildSamples(objectName, sunset, sunrise, step, tz, observationOptions.MinimumObjectAltitudeDegrees);
            var visible = samples.Where(s => s.IsVisibleCandidate).ToList();
            var best = visible.Count == 0
                ? samples.OrderByDescending(x => x.AltitudeDegrees).First()
                : (observationOptions.PreferHighestAltitude ? visible.OrderByDescending(x => x.AltitudeDegrees).First() : visible.First());

            return new SceneObservationTime
            {
                SceneId = sceneId,
                SceneTitle = title,
                ObjectName = objectName,
                LocalObservationTime = best.LocalObservationTime,
                UtcObservationTime = best.UtcObservationTime,
                Timezone = observationOptions.Timezone,
                Reason = visible.Count == 0 ? "not visible in configured night window" : "selected from visibility search in night window",
                AltitudeDegrees = best.AltitudeDegrees,
                AzimuthDegrees = best.AzimuthDegrees,
                DirectionLabel = best.DirectionLabel,
                IsVisible = visible.Count > 0,
                VisibilityReason = visible.Count == 0 ? $"Altitude stayed below {observationOptions.MinimumObjectAltitudeDegrees:F1}°." : $"Altitude reached {best.AltitudeDegrees:F1}°.",
                VisibilitySearchSamples = samples
            };
        }

        var overview = sunset.AddMinutes(observationOptions.SkyOverviewMinutesAfterSunset);
        return
        [
            new SceneObservationTime { SceneId = "sky-overview", SceneTitle = "Sky overview", ObjectName = "Polaris", LocalObservationTime = overview, UtcObservationTime = new DateTimeOffset(overview, tz.GetUtcOffset(overview)).ToUniversalTime(), Timezone = observationOptions.Timezone, Reason = "sunset + configured offset", IsVisible = true, VisibilityReason = "overview scene" },
            BuildObjectScene("moon", "Moon focus", context.Events.FirstOrDefault(x=>x.Category.Contains("moon",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Moon"),
            BuildObjectScene("planet", "Bright planet", context.Events.FirstOrDefault(x=>x.Category.Contains("planet",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Jupiter"),
            BuildObjectScene("deep-sky", "Deep sky target", context.Events.FirstOrDefault(x=>x.Category.Contains("deep",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Orion"),
            new SceneObservationTime { SceneId = "closing", SceneTitle = "Closing wide sky", ObjectName = "Polaris", LocalObservationTime = sunset.AddHours(3), UtcObservationTime = new DateTimeOffset(sunset.AddHours(3), tz.GetUtcOffset(sunset.AddHours(3))).ToUniversalTime(), Timezone = observationOptions.Timezone, Reason = "late night closing", IsVisible = true, VisibilityReason = "closing scene" }
        ];
    }

    private static List<VisibilitySample> BuildSamples(string objectName, DateTime sunset, DateTime sunrise, TimeSpan step, TimeZoneInfo tz, double minimumAltitude)
    {
        var list = new List<VisibilitySample>();
        var seed = Math.Abs(objectName.GetHashCode());
        for (var time = sunset; time <= sunrise; time = time.Add(step))
        {
            var hours = (time - sunset).TotalHours;
            var altitude = -5 + 45 * Math.Sin((hours / 12d) * Math.PI + (seed % 90) * Math.PI / 180d);
            var az = (seed % 360 + hours * 22) % 360;
            list.Add(new VisibilitySample
            {
                LocalObservationTime = time,
                UtcObservationTime = new DateTimeOffset(time, tz.GetUtcOffset(time)).ToUniversalTime(),
                AltitudeDegrees = Math.Round(altitude, 2),
                AzimuthDegrees = Math.Round(az, 2),
                DirectionLabel = Cardinal(az),
                IsAboveHorizon = altitude >= 0,
                IsVisibleCandidate = altitude >= minimumAltitude
            });
        }
        return list;
    }

    private static string Cardinal(double az) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(az / 45d) % 8];
}
