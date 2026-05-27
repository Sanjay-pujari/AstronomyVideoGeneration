using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests;

public class WeeklyAstronomyEventExtractorAzimuthTests
{
    [Fact]
    public void Extract_Populates_Azimuth_For_Key_Objects()
    {
        var t = new DateTime(2026, 5, 20, 3, 0, 0, DateTimeKind.Utc);
        var day = new DailySkyForecastContextItem(
            DateOnly.FromDateTime(t), t, t.AddHours(8), "Waxing", 50, null, null,
            [
                new WeeklySkyForecastVisibleObjectItem("MOON", "Moon", "Moon", true, null, null, null, 62.94, 142.1, t, 90, 80, "SE", "Good"),
                new WeeklySkyForecastVisibleObjectItem("JUPITER", "Jupiter", "Planet", true, null, null, null, 58.21, 188.5, t, 88, 81, "S", "Good"),
                new WeeklySkyForecastVisibleObjectItem("VENUS", "Venus", "Planet", true, null, null, null, 34.10, 256.3, t, 85, 79, "SW", "Good")
            ],
            [], t, t.AddHours(2), 91, "Strong");

        var context = new WeeklySkyForecastContext("r", "loc", 0, 0, "UTC", DateOnly.FromDateTime(t), DateOnly.FromDateTime(t), "en", [day], [], [new RecommendedObservationNight(DateOnly.FromDateTime(t), 95, "Best", ["MOON"], t, t.AddHours(2))], "JUPITER", null, null, []);

        var result = new WeeklyAstronomyEventExtractor().Extract(context, "r", DateOnly.FromDateTime(t), DateOnly.FromDateTime(t), "en", null);

        result.ExtractedEvents.SelectMany(e => e.Objects).Should().Contain(x => x.ObjectCode == "MOON" && x.AzimuthDegrees.HasValue);
        result.ExtractedEvents.SelectMany(e => e.Objects).Should().Contain(x => x.ObjectCode == "JUPITER" && x.AzimuthDegrees.HasValue);
        result.ExtractedEvents.SelectMany(e => e.Objects).Should().Contain(x => x.ObjectCode == "VENUS" && x.AzimuthDegrees.HasValue);
    }

    [Fact]
    public void WeeklyAstronomyEventObject_Serializes_AzimuthDegrees()
    {
        var obj = new WeeklyAstronomyEventObject("VENUS", "Venus", 40, 123.4, -4, 90);
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json.Should().Contain("\"azimuthDegrees\":123.4");
    }
}
