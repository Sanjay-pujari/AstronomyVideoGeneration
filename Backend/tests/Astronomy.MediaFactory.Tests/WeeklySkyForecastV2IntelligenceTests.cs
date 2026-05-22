using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class WeeklySkyForecastV2IntelligenceTests
{
    [Fact]
    public async Task V2_Intelligence_Extracts_Best_Night_And_Planet_And_Story_Arc()
    {
        var service = new WeeklySkyForecastV2IntelligenceService(new StubContextBuilder(), new WeeklySkyForecastV2EventIntelligenceBuilder());
        var response = await service.PreviewAsync(new WeeklySkyForecastV2IntelligenceRequest("WeeklySkyForecast", "en", "IN-RJ-UDAIPUR", "Udaipur", DateTimeOffset.Parse("2026-05-22T18:00:00Z"), diagnostics: true), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(response.EventIntelligence, e => e.EventType == "best_overall_night");
        Assert.Contains(response.EventIntelligence, e => e.EventType == "best_planet");
        Assert.False(string.IsNullOrWhiteSpace(response.WeeklyStoryArc.Headline));
        Assert.NotEmpty(response.RecommendedVisualStrategies);
    }

    [Fact]
    public void V2_Intelligence_Grouping_Does_Not_Claim_Conjunction()
    {
        var events = new WeeklySkyForecastV2EventIntelligenceBuilder().Build(BuildContext());
        var grouping = Assert.Single(events.Where(x => x.EventType is "planetary_grouping" or "moon_planet_pairing"));
        Assert.Contains("same viewing window grouping", grouping.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conjunction", grouping.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static WeeklySkyForecastContext BuildContext()
    {
        var t = DateTime.Parse("2026-05-24T18:00:00Z");
        var day1 = new DailySkyForecastContextItem(new DateOnly(2026, 5, 24), t, t, "Waxing", 33, null, null,
            [new WeeklySkyForecastVisibleObjectItem("MOON", "Moon", "Moon", true, null, null, null, 55, t, 90, 80, "SE", "Good"), new WeeklySkyForecastVisibleObjectItem("JUPITER", "Jupiter", "Planet", true, null, null, null, 60, t.AddMinutes(20), 92, 88, "SE", "Great"), new WeeklySkyForecastVisibleObjectItem("VENUS", "Venus", "Planet", true, null, null, null, 40, t.AddMinutes(30), 85, 82, "SE", "Great")],
            [], t, t.AddHours(2), 95, "Excellent");
        return new WeeklySkyForecastContext("IN-RJ-UDAIPUR", "Udaipur", 24, 73, "Asia/Kolkata", new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 28), "en", [day1, day1, day1, day1, day1, day1, day1], [], [new RecommendedObservationNight(new DateOnly(2026, 5, 24), 95, "Best", ["MOON", "JUPITER", "VENUS"], t, t.AddHours(2))], "JUPITER", new DateOnly(2026, 5, 24), new DateOnly(2026, 5, 25), []);
    }

    private sealed class StubContextBuilder : IWeeklySkyForecastContextBuilder
    {
        public Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken) => Task.FromResult(BuildContext());
    }
}
