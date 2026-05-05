using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record NightSkyCandidateObject(string ObjectName, string ObjectType, int Priority, string RecommendedTool, bool BeginnerFriendly);
public sealed class NightSkyObjectVisibility
{
    public required NightSkyCandidateObject Candidate { get; init; }
    public required IReadOnlyList<VisibilitySample> Samples { get; init; }
    public VisibilitySample? BestSample { get; init; }
    public bool IsVisible => BestSample is not null;
    public string VisibilityReason { get; init; } = "";
}

public sealed class SceneObservationContext
{
    public string SceneId { get; init; } = "";
    public string SceneTitle { get; init; } = "";
    public string SceneType { get; init; } = "";
    public int SceneIndex { get; init; }
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string? PrimaryObject { get; init; }
    public bool IncludePolarisOrientation { get; init; }
    public DateTime LocalObservationTime { get; init; }
    public DateTimeOffset UtcObservationTime { get; init; }
    public string Timezone { get; init; } = "";
    public double? AltitudeDegrees { get; init; }
    public double? AzimuthDegrees { get; init; }
    public string? DirectionLabel { get; init; }
    public bool IsVisible { get; init; }
    public string VisibilityReason { get; init; } = "";
    public string RecommendedTool { get; init; } = "Naked eye";
    public string NarrationFocus { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string LocationName { get; init; } = "";
}

public sealed class NightSkyPlan
{
    public string LocationName { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Timezone { get; init; } = "";
    public DateOnly TargetDate { get; init; }
    public DateTime SunsetLocal { get; init; }
    public DateTime SunriseLocal { get; init; }
    public DateTime NightWindowStartLocal { get; init; }
    public DateTime NightWindowEndLocal { get; init; }
    public IReadOnlyList<NightSkyObjectVisibility> VisibleObjects { get; init; } = [];
    public IReadOnlyList<NightSkyObjectVisibility> NotVisibleObjects { get; init; } = [];
    public IReadOnlyList<SceneObservationContext> SelectedScenes { get; init; } = [];
}

public interface INightSkyVisibilityPlanner
{
    NightSkyPlan BuildPlan(ObservationOptions options, DateOnly targetDate, IReadOnlyList<NightSkyCandidateObject>? candidates = null);
}

public sealed class NightSkyVisibilityPlanner : INightSkyVisibilityPlanner
{
    public static readonly IReadOnlyList<NightSkyCandidateObject> DefaultCandidates = [
        new("Moon", "Moon", 100, "Naked eye", true),new("Mercury", "Planet", 70, "Binoculars", false),new("Venus", "Planet", 90, "Naked eye", true),
        new("Mars", "Planet", 85, "Naked eye / binoculars", true),new("Jupiter", "Planet", 95, "Naked eye / binoculars", true),new("Saturn", "Planet", 88, "Binoculars / telescope", true),
        new("Uranus", "Planet", 50, "Telescope", false),new("Neptune", "Planet", 45, "Telescope", false),new("Polaris", "Star", 92, "Naked eye", true),new("Sirius", "Star", 89, "Naked eye", true),
        new("Betelgeuse", "Star", 75, "Naked eye", true),new("Rigel", "Star", 74, "Naked eye", true),new("Orion Nebula", "DeepSky", 80, "Binoculars / telescope", true),
        new("Pleiades", "Cluster", 83, "Naked eye / binoculars", true),new("Andromeda Galaxy", "Galaxy", 78, "Binoculars / telescope", false)
    ];

    public NightSkyPlan BuildPlan(ObservationOptions options, DateOnly targetDate, IReadOnlyList<NightSkyCandidateObject>? candidates = null)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(options.Timezone);
        var sunset = targetDate.ToDateTime(new TimeOnly(18, 30));
        var sunrise = targetDate.AddDays(1).ToDateTime(new TimeOnly(6, 0));
        var step = TimeSpan.FromMinutes(options.VisibilitySearchStepMinutes);
        var evaluated = (candidates ?? DefaultCandidates).Select(c => Evaluate(c, sunset, sunrise, step, tz, options)).ToList();

        var scored = ScoreAndRank(evaluated, step);
        var selectedVisible = SelectDiverseTopObjects(scored, 3).ToList();
        var visible = selectedVisible.Select(x => x.Visibility).ToList();
        var notVisible = evaluated.Where(x => !x.IsVisible || selectedVisible.All(s => !ReferenceEquals(s.Visibility, x))).ToList();
        var scenes = BuildScenes(visible, sunset, sunrise, tz, options);

        return new NightSkyPlan { LocationName = options.LocationName, Latitude = options.Latitude, Longitude = options.Longitude, Timezone = options.Timezone, TargetDate = targetDate, SunsetLocal = sunset, SunriseLocal = sunrise, NightWindowStartLocal = sunset, NightWindowEndLocal = sunrise, VisibleObjects = visible, NotVisibleObjects = notVisible, SelectedScenes = scenes };
    }

    private static NightSkyObjectVisibility Evaluate(NightSkyCandidateObject c, DateTime sunset, DateTime sunrise, TimeSpan step, TimeZoneInfo tz, ObservationOptions options)
    {
        var samples = ObservationTimeService.BuildSamples(c.ObjectName, sunset, sunrise, step, tz, options.MinimumObjectAltitudeDegrees);
        var visible = samples.Where(x => x.IsVisibleCandidate).ToList();
        var best = visible.Count == 0 ? null : (options.PreferHighestAltitude ? visible.MaxBy(x => x.AltitudeDegrees) : visible.First());
        return new NightSkyObjectVisibility { Candidate = c, Samples = samples, BestSample = best, VisibilityReason = best is null ? $"Below {options.MinimumObjectAltitudeDegrees:F1}° all night." : $"Visible near {best.LocalObservationTime:t} at {best.AltitudeDegrees:F1}°." };
    }

    private static IEnumerable<(NightSkyObjectVisibility Visibility, double Score)> ScoreAndRank(IReadOnlyList<NightSkyObjectVisibility> evaluated, TimeSpan step)
    {
        var durations = evaluated.ToDictionary(x => x, x => x.Samples.Count(s => s.IsVisibleCandidate) * step.TotalMinutes);
        var maxDuration = Math.Max(1, durations.Values.DefaultIfEmpty(0).Max());

        foreach (var item in evaluated)
        {
            var altitude = item.BestSample?.AltitudeDegrees ?? 0;
            var duration = durations[item];
            if (altitude < 10 || duration < 20)
            {
                Console.WriteLine($"[DailySkyGuideRanking] {item.Candidate.ObjectName} | Score=0.000 | Altitude={altitude:F1} | Duration={duration:F0}m | Rejected");
                continue;
            }

            var altitudeScore = altitude / 90d;
            var brightnessScore = ResolveBrightnessScore(item.Candidate.ObjectName, item.Candidate.ObjectType);
            var visibilityDurationScore = duration / maxDuration;
            var typePriorityScore = ResolveTypePriorityScore(item.Candidate.ObjectType);
            var score = (altitudeScore * 0.4) + (brightnessScore * 0.3) + (visibilityDurationScore * 0.2) + (typePriorityScore * 0.1);
            Console.WriteLine($"[DailySkyGuideRanking] {item.Candidate.ObjectName} | Score={score:F3} | Altitude={altitude:F1} | Duration={duration:F0}m | Eligible");
            yield return (item, score);
        }
    }

    private static IEnumerable<(NightSkyObjectVisibility Visibility, double Score)> SelectDiverseTopObjects(IEnumerable<(NightSkyObjectVisibility Visibility, double Score)> ranked, int take)
    {
        var selected = new List<(NightSkyObjectVisibility Visibility, double Score)>();
        var moonCount = 0;
        var planetCount = 0;
        var deepSkyCount = 0;

        foreach (var item in ranked.OrderByDescending(x => x.Score))
        {
            var type = item.Visibility.Candidate.ObjectType.ToLowerInvariant();
            var isMoon = type.Contains("moon");
            var isPlanet = type.Contains("planet");
            var isDeepSky = type.Contains("deep") || type.Contains("cluster") || type.Contains("galaxy");
            if ((isMoon && moonCount >= 1) || (isPlanet && planetCount >= 2) || (isDeepSky && deepSkyCount >= 1))
            {
                Console.WriteLine($"[DailySkyGuideRanking] {item.Visibility.Candidate.ObjectName} | Score={item.Score:F3} | Altitude={item.Visibility.BestSample?.AltitudeDegrees ?? 0:F1} | Duration=n/a | Rejected");
                continue;
            }

            selected.Add(item);
            moonCount += isMoon ? 1 : 0;
            planetCount += isPlanet ? 1 : 0;
            deepSkyCount += isDeepSky ? 1 : 0;
            Console.WriteLine($"[DailySkyGuideRanking] {item.Visibility.Candidate.ObjectName} | Score={item.Score:F3} | Altitude={item.Visibility.BestSample?.AltitudeDegrees ?? 0:F1} | Duration=n/a | Selected");
            if (selected.Count == take)
                break;
        }

        return selected;
    }

    private static double ResolveBrightnessScore(string objectName, string objectType)
    {
        var n = objectName.ToLowerInvariant();
        if (n.Contains("moon")) return 1.0;
        if (n.Contains("venus")) return 0.95;
        if (n.Contains("jupiter")) return 0.9;
        if (n.Contains("saturn")) return 0.8;
        var t = objectType.ToLowerInvariant();
        if (t.Contains("star")) return 0.7;
        if (t.Contains("deep") || t.Contains("cluster") || t.Contains("galaxy")) return 0.5;
        if (t.Contains("planet")) return 0.8;
        return 0.6;
    }

    private static double ResolveTypePriorityScore(string objectType)
    {
        var t = objectType.ToLowerInvariant();
        if (t.Contains("moon")) return 1.0;
        if (t.Contains("planet")) return 0.9;
        if (t.Contains("star")) return 0.7;
        if (t.Contains("deep") || t.Contains("cluster") || t.Contains("galaxy")) return 0.6;
        return 0.6;
    }

    private static IReadOnlyList<SceneObservationContext> BuildScenes(IReadOnlyList<NightSkyObjectVisibility> visible, DateTime sunset, DateTime sunrise, TimeZoneInfo tz, ObservationOptions options)
    {
        var overview = sunset.AddMinutes(options.SkyOverviewMinutesAfterSunset);
        var closingLocal = Clamp(sunset.Date.AddHours(options.DefaultObservationHour), sunset, sunrise);
        var selectedObjects = visible.Take(3).ToList();
        var scenes = new List<SceneObservationContext> { new() { SceneId="scene-1", SceneTitle="Sky overview", SceneType="Overview", ObjectName="Sky", ObjectType="Overview", LocalObservationTime=overview, UtcObservationTime=ToUtc(overview,tz), Timezone=options.Timezone, IsVisible=true, VisibilityReason="Night overview", RecommendedTool="Naked eye", NarrationFocus="Constellation lines and orientation." } };
        for (var i=0;i<selectedObjects.Count;i++)
        {
            var b = selectedObjects[i].BestSample!; var c = selectedObjects[i].Candidate;
            scenes.Add(new SceneObservationContext { SceneId=$"scene-{i+2}", SceneTitle=$"{c.ObjectName} focus", SceneType="Object", ObjectName=c.ObjectName, ObjectType=c.ObjectType, LocalObservationTime=b.LocalObservationTime, UtcObservationTime=b.UtcObservationTime, Timezone=options.Timezone, AltitudeDegrees=b.AltitudeDegrees, AzimuthDegrees=b.AzimuthDegrees, DirectionLabel=b.DirectionLabel, IsVisible=true, VisibilityReason=selectedObjects[i].VisibilityReason, RecommendedTool=c.RecommendedTool, NarrationFocus="Object visibility and how to observe it." });
        }
        while (scenes.Count < 5) scenes.Add(new SceneObservationContext { SceneId=$"scene-{scenes.Count+1}", SceneTitle="Sky tips", SceneType="Tips", ObjectName="Sky", ObjectType="Overview", LocalObservationTime=closingLocal, UtcObservationTime=ToUtc(closingLocal,tz), Timezone=options.Timezone, IsVisible=true, VisibilityReason="Fill scene without inventing objects.", RecommendedTool="Naked eye", NarrationFocus="General viewing tips." });
        return scenes;
    }
    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) => value < min ? min : value > max ? max : value;
    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz) => new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
}
