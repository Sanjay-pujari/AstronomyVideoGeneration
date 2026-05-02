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
    private static readonly string[] FillerTitles = ["Constellation overview", "Viewing tips", "Dark-sky tips", "What to look for tonight"];

    public IReadOnlyList<SceneObservationTime> SelectSceneTimes(AstronomyContext context, DateOnly targetDate, ObservationOptions observationOptions)
    {
        if (context.SceneObservationContexts.Count > 0)
        {
            return context.SceneObservationContexts.Select(s => new SceneObservationTime
            {
                SceneId = s.SceneId,
                SceneTitle = s.SceneTitle,
                ObjectName = s.ObjectName,
                LocalObservationTime = s.LocalObservationTime,
                UtcObservationTime = s.UtcObservationTime,
                Timezone = s.Timezone,
                Reason = s.NarrationFocus,
                AltitudeDegrees = s.AltitudeDegrees ?? 0,
                AzimuthDegrees = s.AzimuthDegrees ?? 0,
                DirectionLabel = s.DirectionLabel,
                IsVisible = s.IsVisible,
                VisibilityReason = s.VisibilityReason,
                VisibilitySearchSamples = []
            }).ToList();
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(observationOptions.Timezone);
        var sunset = targetDate.ToDateTime(new TimeOnly(18, 30));
        var sunrise = targetDate.AddDays(1).ToDateTime(new TimeOnly(6, 0));
        var step = TimeSpan.FromMinutes(Math.Max(10, observationOptions.VisibilitySearchStepMinutes));

        SceneObservationTime BuildObjectScene(string sceneId, string title, string objectName)
        {
            var samples = BuildSamples(objectName, sunset, sunrise, step, tz, observationOptions.MinimumObjectAltitudeDegrees);
            if (samples.Count == 0)
            {
                var fallbackTime = sunset.AddHours(1);
                return new SceneObservationTime
                {
                    SceneId = sceneId,
                    SceneTitle = title,
                    ObjectName = objectName,
                    LocalObservationTime = fallbackTime,
                    UtcObservationTime = new DateTimeOffset(fallbackTime, tz.GetUtcOffset(fallbackTime)).ToUniversalTime(),
                    Timezone = observationOptions.Timezone,
                    Reason = "no ephemeris samples available",
                    IsVisible = false,
                    VisibilityReason = "Ephemeris unavailable.",
                    VisibilitySearchSamples = []
                };
            }
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
        var objectCandidates = context.Events
            .Select((astroEvent, index) => new
            {
                Event = astroEvent,
                Index = index,
                TypeRank = ResolveTypeRank(astroEvent.Category),
                Priority = ResolvePriority(astroEvent.Category, astroEvent.Score)
            })
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.TypeRank)
            .ThenBy(x => x.Index)
            .Select(x => BuildObjectScene($"candidate-{x.Index}", $"{x.Event.ObjectName} focus", x.Event.ObjectName))
            .OrderByDescending(x => ResolvePriority(GetCategoryForObject(context, x.ObjectName), 0))
            .ThenByDescending(x => x.AltitudeDegrees)
            .ThenByDescending(x => IsBeginnerFriendlyObjectName(x.ObjectName))
            .ToList();

        var selectedObjectScenes = new List<SceneObservationTime>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var polarisCount = 0;

        foreach (var candidate in objectCandidates)
        {
            if (!candidate.IsVisible)
                continue;
            if (!seenNames.Add(candidate.ObjectName))
                continue;
            if (candidate.ObjectName.Equals("Polaris", StringComparison.OrdinalIgnoreCase) && polarisCount >= 1)
                continue;
            if (candidate.ObjectName.Equals("Polaris", StringComparison.OrdinalIgnoreCase))
                polarisCount++;
            selectedObjectScenes.Add(new SceneObservationTime
            {
                SceneId = $"object-{selectedObjectScenes.Count + 1}",
                SceneTitle = $"{candidate.ObjectName} focus",
                ObjectName = candidate.ObjectName,
                LocalObservationTime = candidate.LocalObservationTime,
                UtcObservationTime = candidate.UtcObservationTime,
                Timezone = candidate.Timezone,
                Reason = candidate.Reason,
                AltitudeDegrees = candidate.AltitudeDegrees,
                AzimuthDegrees = candidate.AzimuthDegrees,
                DirectionLabel = candidate.DirectionLabel,
                IsVisible = candidate.IsVisible,
                VisibilityReason = candidate.VisibilityReason,
                VisibilitySearchSamples = candidate.VisibilitySearchSamples
            });
            if (selectedObjectScenes.Count == 3)
                break;
        }

        while (selectedObjectScenes.Count < 3)
        {
            var fillerIndex = selectedObjectScenes.Count;
            var fillerLocal = sunset.AddHours(1 + fillerIndex);
            selectedObjectScenes.Add(new SceneObservationTime
            {
                SceneId = $"filler-{fillerIndex + 1}",
                SceneTitle = FillerTitles[fillerIndex % FillerTitles.Length],
                ObjectName = "Sky",
                LocalObservationTime = fillerLocal,
                UtcObservationTime = new DateTimeOffset(fillerLocal, tz.GetUtcOffset(fillerLocal)).ToUniversalTime(),
                Timezone = observationOptions.Timezone,
                Reason = "filler scene",
                IsVisible = true,
                VisibilityReason = "Filler scene because fewer distinct visible targets were available"
            });
        }

        return
        [
            new SceneObservationTime { SceneId = "sky-overview", SceneTitle = "Sky overview", ObjectName = "Sky", LocalObservationTime = overview, UtcObservationTime = new DateTimeOffset(overview, tz.GetUtcOffset(overview)).ToUniversalTime(), Timezone = observationOptions.Timezone, Reason = "sunset + configured offset", IsVisible = true, VisibilityReason = "overview scene" },
            .. selectedObjectScenes,
            new SceneObservationTime { SceneId = "closing", SceneTitle = "Closing wide sky", ObjectName = "Sky", LocalObservationTime = sunset.AddHours(3), UtcObservationTime = new DateTimeOffset(sunset.AddHours(3), tz.GetUtcOffset(sunset.AddHours(3))).ToUniversalTime(), Timezone = observationOptions.Timezone, Reason = "late night closing", IsVisible = true, VisibilityReason = "closing scene" }
        ];
    }

    private static bool IsBeginnerFriendlyObjectName(string objectName)
        => NightSkyVisibilityPlanner.DefaultCandidates.FirstOrDefault(x => x.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase))?.BeginnerFriendly ?? false;

    private static string GetCategoryForObject(AstronomyContext context, string objectName)
        => context.Events.FirstOrDefault(x => x.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase))?.Category ?? "Other";

    private static int ResolveTypeRank(string category)
        => category.Trim().ToLowerInvariant() switch
        {
            var c when c.Contains("moon") => 4,
            var c when c.Contains("planet") => 3,
            var c when c.Contains("star") => 2,
            var c when c.Contains("deep") || c.Contains("cluster") || c.Contains("galaxy") => 1,
            _ => 0
        };

    private static int ResolvePriority(string category, double score)
        => Math.Max((int)Math.Round(score * 100), ResolveTypeRank(category) * 25);

    public static List<VisibilitySample> BuildSamples(string objectName, DateTime sunset, DateTime sunrise, TimeSpan step, TimeZoneInfo tz, double minimumAltitude)
    {
        var seedAltitude = objectName.Trim().ToLowerInvariant() switch
        {
            var n when n.Contains("moon") => 60,
            var n when n.Contains("jupiter") => 55,
            var n when n.Contains("venus") => 50,
            var n when n.Contains("saturn") => 48,
            var n when n.Contains("mars") => 46,
            var n when n.Contains("sirius") => 45,
            var n when n.Contains("polaris") => 43,
            var n when n.Contains("orion") => 40,
            var n when n.Contains("pleiades") => 38,
            var n when n.Contains("andromeda") => 36,
            _ => 30
        };
        var local = sunset.AddHours(1);
        var visible = seedAltitude >= minimumAltitude;
        return
        [
            new VisibilitySample
            {
                LocalObservationTime = local,
                UtcObservationTime = new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime(),
                AltitudeDegrees = seedAltitude,
                AzimuthDegrees = 180,
                DirectionLabel = Cardinal(180),
                IsAboveHorizon = seedAltitude > 0,
                IsVisibleCandidate = visible
            }
        ];
    }

    private static string Cardinal(double az) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(az / 45d) % 8];
}
