using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyVisibilityServiceSkyfieldTests
{
    [Fact]
    public async Task Skyfield_Success_Populates_Solar_Lunar_And_Object_Data()
    {
        await using var db = CreateDb();
        db.CelestialObjects.Add(new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 8, PhotogenicScore = 9, EducationalScore = 7, ViralityScore = 6 });
        await db.SaveChangesAsync();
        var response = new SkyfieldVisibilityResponse(true, new DateTime(2026,5,21,13,0,0,DateTimeKind.Utc), new DateTime(2026,5,22,0,30,0,DateTimeKind.Utc), "Waxing Gibbous", 73.2,
            [new SkyfieldVisibilityObjectResult("Moon", true, new DateTime(2026,5,21,14,0,0,DateTimeKind.Utc), new DateTime(2026,5,22,1,0,0,DateTimeKind.Utc), new DateTime(2026,5,21,19,0,0,DateTimeKind.Utc), 62, new DateTime(2026,5,21,19,30,0,DateTimeKind.Utc), new DateTime(2026,5,21,22,0,0,DateTimeKind.Utc), 10, null)], [], null);
        var svc = new AstronomyVisibilityService(db, new StubClient(response), Options.Create(new SkyfieldSidecarOptions()));

        var result = await svc.CalculateVisibilityAsync(new AstronomyVisibilityRequest("R", "Loc", 10, 20, "UTC", new DateOnly(2026, 5, 21), "Moon"), CancellationToken.None);

        Assert.Equal(response.SunsetUtc, result.SunsetUtc);
        Assert.Equal(response.SunriseUtc, result.SunriseUtc);
        Assert.Equal("Waxing Gibbous", result.MoonPhase);
        Assert.Equal(73.2, result.MoonIlluminationPercent);
        Assert.Equal(response.Objects[0].RiseUtc, result.VisibleObjects[0].RiseUtc);
        Assert.Equal(response.Objects[0].SetUtc, result.VisibleObjects[0].SetUtc);
        Assert.Equal(response.Objects[0].TransitUtc, result.VisibleObjects[0].TransitUtc);
    }

    [Fact]
    public async Task Skyfield_Failure_Falls_Back_With_Warning()
    {
        await using var db = CreateDb();
        db.CelestialObjects.Add(new CelestialObject { Code = "Moon", Name = "Moon", ObjectType = "Moon", NakedEyeVisible = true, Enabled = true, VisibilityPriority = 8, PhotogenicScore = 9, EducationalScore = 7, ViralityScore = 6 });
        await db.SaveChangesAsync();
        var svc = new AstronomyVisibilityService(db, new StubClient(new SkyfieldVisibilityResponse(false, null, null, null, null, [], [], "timeout")), Options.Create(new SkyfieldSidecarOptions()));

        var result = await svc.CalculateVisibilityAsync(new AstronomyVisibilityRequest("R", "Loc", 10, 20, "UTC", new DateOnly(2026, 5, 21), "Moon"), CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("fallback visibility approximation used", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, w => w.Contains("Visibility source: Fallback", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.VisibleObjects);
    }

    private static MediaFactoryDbContext CreateDb() => new(new DbContextOptionsBuilder<MediaFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubClient(SkyfieldVisibilityResponse response) : ISkyfieldVisibilityClient
    {
        public Task<SkyfieldVisibilityResponse> CalculateAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
