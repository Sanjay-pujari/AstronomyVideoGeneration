using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase2EventIntelligenceTests
{
    [Theory]
    [InlineData("CONSTELLATION", "CONSTELLATION")]
    [InlineData("MeteorShower", "METEOR_SHOWER")]
    [InlineData("PLANET_CONJUNCTION", "PLANET_CONJUNCTION")]
    [InlineData("PlanetPairing", "PLANET_PAIRING")]
    [InlineData("PlanetGrouping", "PLANET_GROUPING")]
    [InlineData("FullMoon", "NAMED_FULL_MOON")]
    [InlineData("NewMoon", "NEW_MOON")]
    [InlineData("LunarEclipse", "LUNAR_ECLIPSE")]
    [InlineData("SolarEclipse", "SOLAR_ECLIPSE")]
    [InlineData("DSO", "DEEP_SKY_OBJECT")]
    public void Family_aliases_resolve_deterministically(string alias, string expected)
    {
        var sut = new ProductionEventFamilyResolver();
        var first = sut.Resolve(new(alias));
        var second = sut.Resolve(new(alias));
        Assert.True(first.IsKnownFamily);
        Assert.Equal(expected, first.EventFamily);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Unknown_family_is_explicit_not_known()
    {
        var result = new ProductionEventFamilyResolver().Resolve(new("UNKNOWN_EVENT"));
        Assert.False(result.IsKnownFamily);
        Assert.Equal("UNKNOWN", result.EventFamily);
    }

    [Theory]
    [InlineData(70, 100, "70/100")]
    [InlineData(7, 10, "7/10")]
    public void Normalized_score_preserves_declared_scale(decimal value, decimal maximum, string display)
        => Assert.Equal(display, NormalizedScore.Create(value, maximum).DisplayText);

    [Fact]
    public void Normalized_score_rejects_out_of_range_values()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NormalizedScore.Create(70, 10));
}
