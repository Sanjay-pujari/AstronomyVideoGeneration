using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ObservationTimeServiceSelectionTests
{
    private static readonly ObservationOptions Options = new() { Timezone = "UTC", MinimumObjectAltitudeDegrees = 10 };

    [Fact]
    public void SelectSceneTimes_DeduplicatesPolarisToSingleObjectScene()
    {
        var context = BuildContext(("Star", "Polaris"), ("Star", "Polaris"), ("Star", "Polaris"));
        var result = new ObservationTimeService().SelectSceneTimes(context, new DateOnly(2026, 5, 2), Options);

        var objectScenes = result.Where(x => x.SceneId.StartsWith("object-", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(objectScenes);
        Assert.Equal("Polaris", objectScenes[0].ObjectName);
    }

    [Fact]
    public void SelectSceneTimes_UsesDistinctObjectNames_WhenEnoughVisibleObjectsExist()
    {
        var context = BuildContext(("Moon", "Moon"), ("Planet", "Jupiter"), ("Deep Sky", "Orion Nebula"), ("Star", "Polaris"));
        var result = new ObservationTimeService().SelectSceneTimes(context, new DateOnly(2026, 5, 2), Options);

        var scenes2To4 = result.Skip(1).Take(3).ToList();
        Assert.Equal(3, scenes2To4.Select(x => x.ObjectName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SelectSceneTimes_UsesFillerScenes_WhenOnlyOneVisibleObjectExists()
    {
        var options = new ObservationOptions { Timezone = "UTC", MinimumObjectAltitudeDegrees = 50 };
        var context = BuildContext(("Star", "Polaris"));
        var result = new ObservationTimeService().SelectSceneTimes(context, new DateOnly(2026, 5, 2), options);

        var scenes2To4 = result.Skip(1).Take(3).ToList();
        Assert.Equal("Polaris", scenes2To4[0].ObjectName);
        Assert.Equal("Sky", scenes2To4[1].ObjectName);
        Assert.Equal("Sky", scenes2To4[2].ObjectName);
        Assert.All(scenes2To4.Skip(1), x => Assert.Contains("Filler scene because fewer distinct visible targets were available", x.VisibilityReason, StringComparison.Ordinal));
    }

    [Fact]
    public void SelectSceneTimes_NeverProducesDuplicateObjectScenes()
    {
        var context = BuildContext(("Planet", "Jupiter"), ("Planet", "Jupiter"), ("Moon", "Moon"), ("Star", "Polaris"));
        var result = new ObservationTimeService().SelectSceneTimes(context, new DateOnly(2026, 5, 2), Options);

        var objectNames = result.Where(x => x.SceneId.StartsWith("object-", StringComparison.OrdinalIgnoreCase)).Select(x => x.ObjectName).ToList();
        Assert.Equal(objectNames.Count, objectNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static AstronomyContext BuildContext(params (string Category, string ObjectName)[] events)
        => new()
        {
            Date = new DateOnly(2026, 5, 2),
            LocationName = "Test",
            TimeZone = "UTC",
            Events = events.Select(x => new AstronomyEventModel { Category = x.Category, ObjectName = x.ObjectName, Score = 0.9 }).ToList()
        };
}
