using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyEventDetectionServiceTests
{
    [Fact]
    public async Task DetectEvents_SkyfieldSuccess_AddsReasonableVisibilityCandidates()
    {
        await using var db = CreateDb();
        var visibility = new AstronomyVisibilityResult(
            "udaipur",
            "Udaipur",
            24.5854,
            73.7125,
            "Asia/Kolkata",
            new DateOnly(2026, 6, 5),
            new DateTime(2026, 6, 5, 13, 47, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 6, 0, 12, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 5, 18, 0, 0, DateTimeKind.Utc),
            "Waning Gibbous",
            74,
            [
                new VisibleCelestialObjectResult("MOON", "Moon", "Moon", true, null, null, null, new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc), new DateTime(2026, 6, 5, 15, 15, 0, DateTimeKind.Utc), 5, 7, 7, 7, 7, "Visible", 18, 115, null, null),
                new VisibleCelestialObjectResult("JUPITER", "Jupiter", "Planet", true, null, null, null, new DateTime(2026, 6, 5, 15, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 5, 15, 45, 0, DateTimeKind.Utc), 8, 9, 8, 8, 8, "Visible", 42, 110, -2.2m, null),
                new VisibleCelestialObjectResult("VENUS", "Venus", "Planet", true, null, null, null, new DateTime(2026, 6, 5, 15, 5, 0, DateTimeKind.Utc), new DateTime(2026, 6, 5, 15, 50, 0, DateTimeKind.Utc), 7, 8.5, 8, 8, 9, "Visible", 40, 112, -4.0m, null)
            ],
            ["Visibility source: Skyfield."]);
        var service = new AstronomyEventDetectionService(db, new StubVisibilityService(visibility), NullLogger<AstronomyEventDetectionService>.Instance);

        var result = await service.DetectEventsAsync(new AstronomyEventDetectionRequest(
            "udaipur",
            "Udaipur",
            24.5854,
            73.7125,
            "Asia/Kolkata",
            new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 5, 23, 59, 0, TimeSpan.Zero),
            ["MoonSpecial", "BrightPlanetVisibility", "PlanetGrouping", "PlanetConjunction"],
            DryRun: true), CancellationToken.None);

        Assert.Contains(result.Events, e => e.EventType == "MOON_SPECIAL");
        Assert.Contains(result.Events, e => e.EventType == "BRIGHT_PLANET_VISIBILITY");
        Assert.Contains(result.Events, e => e.EventType == "PLANET_GROUPING");
        Assert.Contains(result.Events, e => e.EventType == "PLANET_CONJUNCTION");
        Assert.Equal(1, result.Diagnostics?.DaysScanned);
        Assert.Equal(1, result.Diagnostics?.SkyfieldDaysSuccessful);
        Assert.Equal(3, result.Diagnostics?.VisibleObjectCount);
        Assert.Contains(result.Diagnostics!.CandidateReasons, r => r.EventType == "BRIGHT_PLANET_VISIBILITY" && r.CandidateReason.Contains("10", StringComparison.OrdinalIgnoreCase));
    }

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubVisibilityService(AstronomyVisibilityResult visibility) : IAstronomyVisibilityService
    {
        public Task<AstronomyVisibilityResult> CalculateVisibilityAsync(AstronomyVisibilityRequest request, CancellationToken cancellationToken) => Task.FromResult(visibility);
    }
}
