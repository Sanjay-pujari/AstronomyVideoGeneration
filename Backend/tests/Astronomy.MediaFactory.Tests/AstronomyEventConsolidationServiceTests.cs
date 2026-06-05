using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyEventConsolidationServiceTests
{
    [Fact]
    public void Consolidate_PlanetConjunction_UsesMinimumSeparationAsPeakAndKeepsVisibilityWindow()
    {
        var service = new AstronomyEventConsolidationService();
        var events = new[]
        {
            Conjunction("evt-1", new DateTimeOffset(2026, 6, 5, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 5, 15, 0, 0, TimeSpan.Zero), 3.2),
            Conjunction("evt-2", new DateTimeOffset(2026, 6, 6, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 6, 15, 30, 0, TimeSpan.Zero), 1.4),
            Conjunction("evt-3", new DateTimeOffset(2026, 6, 7, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 7, 16, 0, 0, TimeSpan.Zero), 2.1)
        };

        var result = Assert.Single(service.Consolidate(events));

        Assert.Equal(new DateTimeOffset(2026, 6, 5, 13, 0, 0, TimeSpan.Zero), result.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 7, 16, 0, 0, TimeSpan.Zero), result.EndUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 6, 13, 30, 0, TimeSpan.Zero), result.PeakUtc);
        Assert.Contains("Jupiter and Venus", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-06-06", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minimum angular separation about 1.4°", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Visibility window runs from 2026-06-05 13:00 UTC to 2026-06-07 16:00 UTC", result.Summary, StringComparison.OrdinalIgnoreCase);

        using var raw = JsonDocument.Parse(result.RawDataJson!);
        var root = raw.RootElement;
        Assert.Equal("2026-06-06T13:30:00.0000000Z", root.GetProperty("peakDate").GetString());
        Assert.Equal(1.4, root.GetProperty("minimumAngularSeparationDegrees").GetDouble());
        Assert.Equal("2026-06-05T13:00:00.0000000Z", root.GetProperty("visibilityWindowStartUtc").GetString());
        Assert.Equal("2026-06-07T16:00:00.0000000Z", root.GetProperty("visibilityWindowEndUtc").GetString());
        Assert.Equal(3, root.GetProperty("sourceEventCount").GetInt32());
        Assert.Equal(new[] { "evt-1", "evt-2", "evt-3" }, root.GetProperty("sourceEventCodes").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Consolidate_PlanetConjunction_DoesNotMergeDifferentPairsInSameRegion()
    {
        var service = new AstronomyEventConsolidationService();
        var events = new[]
        {
            Conjunction("jv-1", new DateTimeOffset(2026, 6, 5, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 5, 15, 0, 0, TimeSpan.Zero), 2.8),
            Conjunction("ms-1", new DateTimeOffset(2026, 6, 5, 13, 10, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 5, 15, 10, 0, TimeSpan.Zero), 1.9, "Mars", "Saturn"),
            Conjunction("jv-2", new DateTimeOffset(2026, 6, 6, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 6, 15, 0, 0, TimeSpan.Zero), 1.2)
        };

        var result = service.Consolidate(events);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Objects.Select(o => o.ObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Jupiter", "Venus"]) && e.RawDataJson!.Contains("sourceEventCount\":2", StringComparison.Ordinal));
        Assert.Contains(result, e => e.Objects.Select(o => o.ObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Mars", "Saturn"]) && e.EventCode == "ms-1");
    }

    private static DetectedAstronomyEventDto Conjunction(string code, DateTimeOffset start, DateTimeOffset end, double separation, string first = "Jupiter", string second = "Venus")
    {
        var objects = new[]
        {
            new DetectedAstronomyEventObjectDto(null, first, "Planet", "Primary", first.ToUpperInvariant(), null, 8m, null),
            new DetectedAstronomyEventObjectDto(null, second, "Planet", "Companion", second.ToUpperInvariant(), null, 8m, null)
        };

        return new DetectedAstronomyEventDto(
            null,
            code,
            "PLANET_CONJUNCTION",
            $"Planet conjunction candidate: {first} and {second}",
            $"{first} and {second} are visible close together.",
            "Candidate conjunction based on test geometry.",
            start,
            start,
            end,
            "IN-RJ-UDAIPUR",
            "Udaipur",
            "Asia/Kolkata",
            "Planet",
            "Candidate",
            7m,
            7m,
            7m,
            7m,
            7m,
            objects,
            "{}",
            JsonSerializer.Serialize(new { angularSeparationDegrees = separation }),
            "{}");
    }
}
