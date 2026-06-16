using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventFamilyResolverTests
{
    [Theory]
    [InlineData("MeteorShower", EventFamily.Meteor)]
    [InlineData("PLANET_CONJUNCTION", EventFamily.PlanetGrouping)]
    [InlineData("PLANET_GROUPING", EventFamily.PlanetGrouping)]
    [InlineData("BLUE_MOON", EventFamily.Moon)]
    [InlineData("LUNAR_ECLIPSE", EventFamily.Eclipse)]
    [InlineData("COMET", EventFamily.SpecialEvent)]
    [InlineData("unknown", EventFamily.Unknown)]
    public void Resolve_MapsKnownEventTypesToExpectedFamily(string eventType, EventFamily expected)
    {
        var family = EventFamilyResolver.Resolve(eventType, contentCategoryCode: null, primaryObjects: [], secondaryObjects: []);

        Assert.Equal(expected, family);
    }
}
