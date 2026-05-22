using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2IntelligenceTests
{
    [Fact]
    public async Task V2_Intelligence_Generates_Cinematic_Blueprint()
    {
        var service = new WeeklySkyForecastV2IntelligenceService(new StubContextBuilder(), new WeeklySkyForecastV2EventIntelligenceBuilder(), new WeeklySkyForecastV2EditorialIntelligenceBuilder(), new WeeklySkyForecastV2CinematicEditorialRefiner());
        var response = await service.PreviewAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), Diagnostics: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.CinematicStoryBlueprint);
        Assert.NotNull(response.EditorialStoryPackage);
        Assert.DoesNotContain("Same viewing window grouping", response.CinematicStoryBlueprint!.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Moon", response.CinematicStoryBlueprint.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.CinematicStoryBlueprint.NarrativeBeats.Count >= 6);
        Assert.Equal(3, response.CinematicStoryBlueprint.ShortsBlueprints.Count);
    }

    [Fact]
    public async Task V2_Cinematic_Refiner_Collapses_Repeated_Grouping_And_Keeps_Unique_Moments_And_Shorts()
    {
        var intelligence = BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext()));
        var editorial = await new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(intelligence, CancellationToken.None);
        var cinematic = await new WeeklySkyForecastV2CinematicEditorialRefiner().RefineAsync(editorial, intelligence with { EditorialStoryPackage = editorial }, CancellationToken.None);

        Assert.True(cinematic.HeroStory.SupportingDates.Count > 1);
        Assert.Equal(cinematic.CinematicMoments.Count, cinematic.CinematicMoments.Select(x => x.VisualUniquenessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, cinematic.ShortsBlueprints.Count);
        Assert.Equal(3, cinematic.ShortsBlueprints.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(cinematic.OpeningHook, new[] { "conjunction", "exact alignment", "rare alignment", "almost touching" }, StringComparer.OrdinalIgnoreCase);
    }

    private static WeeklySkyForecastV2IntelligenceResponse BuildResponse(IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> events)
    {
        return new WeeklySkyForecastV2IntelligenceResponse(null, "WeeklySkyForecast", true, new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "IN-RJ-UDAIPUR",
            new WeeklySkyForecastV2SkyfieldSummary(7, 21, 0, 1, "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25)),
            events,
            new WeeklyStoryArc("h", "s", "t", "o", ["a"], "c", ["MOON"], ["2026-05-24"], ["x"]),
            null!,
            null,
            ["Hybrid"],
            [],
            []);
    }

    private static WeeklySkyForecastContext BuildContext()
    {
        var start = new DateOnly(2026, 5, 22);
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var d = start.AddDays(i);
            var t = DateTime.Parse($"{d:yyyy-MM-dd}T18:00:00Z");
            return new DailySkyForecastContextItem(d, t, t, "Waxing", 33, null, null,
                [new WeeklySkyForecastVisibleObjectItem("MOON", "Moon", "Moon", true, null, null, null, 55, t, 90, 80, "W", "Good"), new WeeklySkyForecastVisibleObjectItem("JUPITER", "Jupiter", "Planet", true, null, null, null, 60, t.AddMinutes(20), 92, 88, "W", "Great"), new WeeklySkyForecastVisibleObjectItem("VENUS", "Venus", "Planet", true, null, null, null, 40, t.AddMinutes(30), 85, 82, "W", "Great")],
                [], t, t.AddHours(2), 95 - i, "Excellent");
        }).ToList();

        return new WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", start, start.AddDays(6), "en", days, [], [new RecommendedObservationNight(new DateOnly(2026, 5, 24), 95, "Best", ["MOON", "JUPITER", "VENUS"], DateTime.Parse("2026-05-24T18:00:00Z"), DateTime.Parse("2026-05-24T20:00:00Z"))], "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), []);
    }

    private sealed class StubContextBuilder : IWeeklySkyForecastContextBuilder
    {
        public Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken) => Task.FromResult(BuildContext());
    }
}
