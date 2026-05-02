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
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
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
        var visible = evaluated.Where(x => x.IsVisible).OrderByDescending(x => x.Candidate.Priority).ThenByDescending(x => x.BestSample!.AltitudeDegrees).ThenByDescending(x => x.Candidate.BeginnerFriendly).ToList();
        var notVisible = evaluated.Where(x => !x.IsVisible).ToList();
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
