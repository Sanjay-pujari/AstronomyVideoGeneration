using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2IntelligenceTests
{
    [Fact]
    public async Task V2_Intelligence_Generates_Editorial_Package()
    {
        var service = new WeeklySkyForecastV2IntelligenceService(new StubContextBuilder(), new WeeklySkyForecastV2EventIntelligenceBuilder(), new WeeklySkyForecastV2EditorialIntelligenceBuilder());
        var response = await service.PreviewAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), diagnostics: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.EditorialStoryPackage);
        Assert.False(string.IsNullOrWhiteSpace(response.EditorialStoryPackage.Headline));
        Assert.False(string.IsNullOrWhiteSpace(response.EditorialStoryPackage.OpeningHook));
        Assert.True(response.EditorialStoryPackage.NarrativeArc.Count >= 5);
        Assert.NotNull(response.EditorialStoryPackage.ThumbnailDirection);
        Assert.True(response.EditorialStoryPackage.ShortsCandidates.Count >= 3);
        Assert.Equal(response.EditorialStoryPackage.ShortsCandidates.Count, response.EditorialStoryPackage.ShortsCandidates.Select(x => x.Title).Distinct().Count());
    }

    [Fact]
    public void V2_Intelligence_Grouping_Does_Not_Claim_Conjunction_And_Collapses()
    {
        var service = new WeeklySkyForecastV2EditorialIntelligenceBuilder();
        var events = new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext());
        var intelligence = BuildResponse(events);
        var pkg = service.BuildAsync(intelligence, CancellationToken.None).Result;

        Assert.Contains("same evening viewing window", pkg.HeroEvent.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conjunction", pkg.HeroEvent.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True((pkg.HeroEvent.SupportingDates?.Count ?? 0) > 1);
    }

    [Fact]
    public void V2_Intelligence_Headline_Mentions_Hero_Objects_And_Required_Beats_Present()
    {
        var pkg = new WeeklySkyForecastV2EditorialIntelligenceBuilder().BuildAsync(BuildResponse(new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext())), CancellationToken.None).Result;
        Assert.Contains("Moon", pkg.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "hook");
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "hero_sky_event");
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "best_observation_night");
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "moon_planet_highlight");
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "photography_tip");
        Assert.Contains(pkg.NarrativeArc, x => x.BeatType == "closing_recommendation");
    }

    private static WeeklySkyForecastV2IntelligenceResponse BuildResponse(IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> events)
    {
        return new WeeklySkyForecastV2IntelligenceResponse(null, "WeeklySkyForecast", true, new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "IN-RJ-UDAIPUR",
            new WeeklySkyForecastV2SkyfieldSummary(7, 21, 0, 1, "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25)),
            events,
            new WeeklyStoryArc("h", "s", "t", "o", ["a"], "c", ["MOON"], ["2026-05-24"], ["x"]),
            null!,
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
