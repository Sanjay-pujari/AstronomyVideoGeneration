using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed class SceneObservationTime
{
    public string SceneId { get; init; } = "";
    public string SceneTitle { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public DateTime LocalObservationTime { get; init; }
    public DateTimeOffset UtcObservationTime { get; init; }
    public string Timezone { get; init; } = "";
    public string Reason { get; init; } = "";
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

        DateTime Clamp(DateTime t) => t < sunset ? sunset : t > sunrise ? sunrise : t;
        var overview = Clamp(sunset.AddMinutes(observationOptions.SkyOverviewMinutesAfterSunset));
        var defaultLocal = Clamp(targetDate.ToDateTime(new TimeOnly(observationOptions.DefaultObservationHour, 0)));
        var deepSky = Clamp(targetDate.ToDateTime(new TimeOnly(23, 30)));

        SceneObservationTime Build(string id,string title,string obj,DateTime local,string reason) => new()
        {
            SceneId=id, SceneTitle=title, ObjectName=obj, LocalObservationTime=local, Timezone=observationOptions.Timezone,
            UtcObservationTime=new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime(), Reason=reason
        };

        return [
            Build("sky-overview","Sky overview","Polaris",overview,"sunset + configured offset"),
            Build("moon","Moon focus",context.Events.FirstOrDefault(x=>x.Category.Contains("moon",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Moon",defaultLocal,"default nighttime hour fallback"),
            Build("planet","Bright planet",context.Events.FirstOrDefault(x=>x.Category.Contains("planet",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Jupiter",defaultLocal,"default nighttime hour fallback"),
            Build("deep-sky","Deep sky target",context.Events.FirstOrDefault(x=>x.Category.Contains("deep",StringComparison.OrdinalIgnoreCase))?.ObjectName??"Deep sky",deepSky,"late dark sky time"),
            Build("closing","Closing wide sky","Polaris",defaultLocal,"default observation hour")
        ];
    }
}
