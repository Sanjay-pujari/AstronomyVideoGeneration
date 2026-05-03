using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class NightSkyVisibilityPlannerRankingTests
{
    private static readonly ObservationOptions Options = CreateOptions();

    private static ObservationOptions CreateOptions(int minimumObjectAltitudeDegrees = 0) => new()
    {
        Timezone = "UTC",
        VisibilitySearchStepMinutes = 30,
        MinimumObjectAltitudeDegrees = minimumObjectAltitudeDegrees,
        LocationName = "Test"
    };

    [Fact]
    public void BuildPlan_PrefersVenusOverDimDeepSky()
    {
        var planner = new NightSkyVisibilityPlanner();
        var plan = planner.BuildPlan(Options, new DateOnly(2026, 3, 17),
        [
            new NightSkyCandidateObject("Venus", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Andromeda Galaxy", "Galaxy", 1, "Binoculars", false),
            new NightSkyCandidateObject("Moon", "Moon", 1, "Naked eye", true)
        ]);

        var names = plan.SelectedScenes.Where(s => s.SceneType == "Object").Select(s => s.ObjectName).ToList();
        Assert.Contains("Venus", names);
    }

    [Fact]
    public void BuildPlan_IncludesMoonOnlyWhenVisible()
    {
        var planner = new NightSkyVisibilityPlanner();
        var options = CreateOptions(minimumObjectAltitudeDegrees: 65);

        var plan = planner.BuildPlan(options, new DateOnly(2026, 3, 17),
        [
            new NightSkyCandidateObject("Moon", "Moon", 1, "Naked eye", true),
            new NightSkyCandidateObject("Venus", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Jupiter", "Planet", 1, "Naked eye", true)
        ]);

        var names = plan.SelectedScenes.Where(s => s.SceneType == "Object").Select(s => s.ObjectName).ToList();
        Assert.DoesNotContain("Moon", names);
    }

    [Fact]
    public void BuildPlan_ExcludesLowAltitudeObjects()
    {
        var planner = new NightSkyVisibilityPlanner();
        var plan = planner.BuildPlan(Options, new DateOnly(2026, 3, 17),
        [
            new NightSkyCandidateObject("LowTarget", "DeepSky", 1, "Telescope", false),
            new NightSkyCandidateObject("Venus", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Jupiter", "Planet", 1, "Naked eye", true)
        ]);

        var names = plan.SelectedScenes.Where(s => s.SceneType == "Object").Select(s => s.ObjectName).ToList();
        Assert.DoesNotContain("LowTarget", names);
    }

    [Fact]
    public void BuildPlan_SelectsDiverseFinalThreeObjects()
    {
        var planner = new NightSkyVisibilityPlanner();
        var plan = planner.BuildPlan(Options, new DateOnly(2026, 3, 17),
        [
            new NightSkyCandidateObject("Moon", "Moon", 1, "Naked eye", true),
            new NightSkyCandidateObject("Venus", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Jupiter", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Mars", "Planet", 1, "Naked eye", true),
            new NightSkyCandidateObject("Orion Nebula", "DeepSky", 1, "Binoculars", true)
        ]);

        var objects = plan.SelectedScenes.Where(s => s.SceneType == "Object").ToList();
        Assert.Equal(3, objects.Count);
        Assert.True(objects.Count(o => o.ObjectType.Contains("Moon", StringComparison.OrdinalIgnoreCase)) <= 1);
        Assert.True(objects.Count(o => o.ObjectType.Contains("Planet", StringComparison.OrdinalIgnoreCase)) <= 2);
        Assert.True(objects.Count(o => o.ObjectType.Contains("Deep", StringComparison.OrdinalIgnoreCase)) <= 1);
    }
}
