using Astronomy.MediaFactory.Core;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventFamilyResolverTests
{
    [Theory]
    [InlineData("MeteorShower", EventFamily.Meteor)]
    [InlineData("PLANET_CONJUNCTION", EventFamily.PlanetGrouping)]
    [InlineData("PLANET_GROUPING", EventFamily.PlanetGrouping)]
    [InlineData("BLUE_MOON", EventFamily.Moon)]
    [InlineData("FullMoon", EventFamily.Moon)]
    [InlineData("NewMoon", EventFamily.Moon)]
    [InlineData("BlueMoon", EventFamily.Moon)]
    [InlineData("Supermoon", EventFamily.Moon)]
    [InlineData("Micromoon", EventFamily.Moon)]
    [InlineData("MoonPhase", EventFamily.Moon)]
    [InlineData("LUNAR_ECLIPSE", EventFamily.Eclipse)]
    [InlineData("COMET", EventFamily.SpecialEvent)]
    [InlineData("unknown", EventFamily.Unknown)]
    public void Resolve_MapsKnownEventTypesToExpectedFamily(string eventType, EventFamily expected)
    {
        var family = EventFamilyResolver.Resolve(eventType, contentCategoryCode: null, primaryObjects: [], secondaryObjects: []);

        Assert.Equal(expected, family);
    }

    [Fact]
    public void Resolve_MoonProfileUsesMoonPhaseGuideThumbnailContract()
    {
        var profile = EventFamilyProfiles.Resolve(EventFamily.Moon, "BLUE_MOON");

        Assert.Equal(EventFamily.Moon, profile.Family);
        Assert.Equal("Moon", profile.ValidatorProfile);
        Assert.Equal("MoonPhaseGuideThumbnail", profile.ThumbnailCompositionType);
        Assert.Contains("meteor", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("planet pairing", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("moonGuideCardAdded", profile.RequiredDiagnosticFields);
    }
}
