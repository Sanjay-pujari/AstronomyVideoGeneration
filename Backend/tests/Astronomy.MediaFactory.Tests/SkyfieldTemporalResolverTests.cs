using Astronomy.SscIntelligence.Resolution;

namespace Astronomy.MediaFactory.Tests;

public class SkyfieldTemporalResolverTests
{
    private readonly SkyfieldTemporalResolver _sut = new();

    [Fact]
    public void ExactMatch_Succeeds()
    {
        var t = new DateTime(2026, 5, 25, 21, 0, 0, DateTimeKind.Utc);
        var result = _sut.Resolve("Moon", t, [new("moon", t, 10, 100, -12)]);
        Assert.True(result.MatchFound);
        Assert.True(result.ExactMatch);
        Assert.Equal("skyfield.exact", result.Source);
    }

    [Fact]
    public void NearestSameDay_Succeeds()
    {
        var requested = new DateTime(2026, 5, 25, 21, 0, 0, DateTimeKind.Utc);
        var candidate = new DateTime(2026, 5, 25, 20, 21, 34, DateTimeKind.Utc);
        var result = _sut.Resolve("Moon", requested, [new("moon", candidate, 11, 101, -12)], maximumDeltaMinutes: 180);
        Assert.True(result.MatchFound);
        Assert.False(result.ExactMatch);
        Assert.Equal("skyfield.nearest-time", result.Source);
        Assert.Equal(candidate, result.MatchedTimeUtc);
    }

    [Fact]
    public void Requested2100_Candidate2021_Tolerance180_Resolves()
    {
        var requested = new DateTime(2026, 5, 25, 21, 0, 0, DateTimeKind.Utc);
        var candidate = new DateTime(2026, 5, 25, 20, 21, 0, DateTimeKind.Utc);
        var result = _sut.Resolve("Moon", requested, [new("moon", candidate, 11, 101, -12)], maximumDeltaMinutes: 180);
        Assert.True(result.MatchFound);
        Assert.Equal(candidate, result.MatchedTimeUtc);
        Assert.Equal("skyfield.nearest-time", result.Source);
    }

    [Fact]
    public void NearestAdjacentDayWithinTolerance_Succeeds()
    {
        var requested = new DateTime(2026, 5, 25, 23, 50, 0, DateTimeKind.Utc);
        var candidate = new DateTime(2026, 5, 26, 0, 10, 0, DateTimeKind.Utc);
        var result = _sut.Resolve("Venus", requested, [new("venus", candidate, 22, 202, -4)], maximumDeltaMinutes: 180);
        Assert.True(result.MatchFound);
        Assert.Equal("skyfield.nearest-time", result.Source);
    }

    [Fact]
    public void ExcessiveDelta_Rejected()
    {
        var requested = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc);
        var candidate = new DateTime(2026, 5, 26, 6, 10, 0, DateTimeKind.Utc);
        var result = _sut.Resolve("Jupiter", requested, [new("jupiter", candidate, 30, 150, -2)], maximumDeltaMinutes: 180);
        Assert.False(result.MatchFound);
        Assert.Equal("fallback", result.Source);
    }

    [Fact]
    public void Requested145134_Candidate202134_Tolerance180_Rejects()
    {
        var requested = new DateTime(2026, 5, 25, 14, 51, 34, DateTimeKind.Utc);
        var candidate = new DateTime(2026, 5, 25, 20, 21, 34, DateTimeKind.Utc);
        var result = _sut.Resolve("Jupiter", requested, [new("jupiter", candidate, 30, 150, -2)], maximumDeltaMinutes: 180);
        Assert.False(result.MatchFound);
        Assert.Equal("fallback", result.Source);
        Assert.True(result.DeltaMinutes > 300);
    }

    [Fact]
    public void DifferentObjects_HaveDifferentCoordinates()
    {
        var t = new DateTime(2026, 5, 25, 21, 0, 0, DateTimeKind.Utc);
        var candidates = new[]
        {
            new SkyfieldTemporalCandidate("moon", t, 11, 90, -12),
            new SkyfieldTemporalCandidate("venus", t, 33, 120, -4),
            new SkyfieldTemporalCandidate("jupiter", t, 44, 180, -2),
        };

        var moon = _sut.Resolve("moon", t, candidates);
        var venus = _sut.Resolve("venus", t, candidates);
        var jupiter = _sut.Resolve("jupiter", t, candidates);

        Assert.NotEqual((moon.AltitudeDegrees, moon.AzimuthDegrees), (venus.AltitudeDegrees, venus.AzimuthDegrees));
        Assert.NotEqual((venus.AltitudeDegrees, venus.AzimuthDegrees), (jupiter.AltitudeDegrees, jupiter.AzimuthDegrees));
    }
}
