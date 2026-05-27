using Astronomy.MediaFactory.Api;
using Astronomy.MediaFactory.Core;
using Astronomy.SscIntelligence.Resolution;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astronomy.MediaFactory.Tests;

public class WeeklySkyfieldObjectHydrationTests
{
    [Fact]
    public void BuildTemporalCandidates_HydratesMoonJupiterVenus()
    {
        var eventUtc = new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc);
        var ev = new WeeklyAstronomyEvent(
            "e1",
            WeeklyAstronomyEventType.Grouping,
            "Moon + Jupiter + Venus",
            "test",
            [
                new WeeklyAstronomyEventObject("MOON", "Moon", 41.2, 103.1, -12.4, 0.9),
                new WeeklyAstronomyEventObject("JUPITER", "Jupiter", 37.9, 121.4, -2.1, 0.8),
                new WeeklyAstronomyEventObject("VENUS", "Venus", 18.3, 138.0, -4.0, 0.7)
            ],
            "MOON",
            3,
            DateOnly.FromDateTime(eventUtc),
            TimeOnly.FromDateTime(eventUtc),
            "W",
            41.2,
            103.1,
            12,
            -12.4,
            0.8,
            70,
            60,
            "StellariumScene",
            "Grouping",
            "narration",
            []);

        var candidates = WeeklySkyfieldObjectHydration.BuildTemporalCandidates(
            [ev],
            new HashSet<string>(["moon", "jupiter", "venus"], StringComparer.OrdinalIgnoreCase),
            e => e.BestDateLocal.HasValue && e.BestTimeLocal.HasValue ? DateTime.SpecifyKind(e.BestDateLocal.Value.ToDateTime(e.BestTimeLocal.Value), DateTimeKind.Utc) : null,
            s => (s ?? string.Empty).Trim().ToLowerInvariant(),
            (code, name, aliases) => aliases.Contains((code ?? name ?? string.Empty).Trim().ToLowerInvariant()),
            NullLogger.Instance,
            "Scene-1",
            "Moon");

        candidates.Should().HaveCountGreaterThan(0);
        candidates.Select(c => c.Name).Should().BeEquivalentTo(["MOON", "JUPITER", "VENUS"]);
        candidates.Should().OnlyContain(c => c.SnapshotUtc == eventUtc && c.AltitudeDegrees != 0 && c.AzimuthDegrees != 0);
    }

    [Fact]
    public void TemporalResolver_ResolvesNearestTime_FromHydratedCandidates()
    {
        var resolver = new SkyfieldTemporalResolver();
        var requestedUtc = new DateTime(2026, 5, 27, 19, 37, 0, DateTimeKind.Utc);
        var candidates = new List<SkyfieldTemporalCandidate>
        {
            new("MOON", new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc), 40, 100, -12),
            new("MOON", new DateTime(2026, 5, 27, 19, 45, 0, DateTimeKind.Utc), 42, 106, -12.1)
        };

        var result = resolver.Resolve("Moon", requestedUtc, candidates, 20);

        result.MatchFound.Should().BeTrue();
        result.MatchedTimeUtc.Should().Be(new DateTime(2026, 5, 27, 19, 30, 0, DateTimeKind.Utc));
        result.AltitudeDegrees.Should().Be(40);
        result.AzimuthDegrees.Should().Be(100);
    }
}
