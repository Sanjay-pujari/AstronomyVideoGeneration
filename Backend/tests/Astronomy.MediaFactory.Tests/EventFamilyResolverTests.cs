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
    [InlineData("SolarEclipse", EventFamily.Eclipse)]
    [InlineData("LunarEclipse", EventFamily.Eclipse)]
    [InlineData("TotalSolarEclipse", EventFamily.Eclipse)]
    [InlineData("PartialSolarEclipse", EventFamily.Eclipse)]
    [InlineData("AnnularSolarEclipse", EventFamily.Eclipse)]
    [InlineData("TotalLunarEclipse", EventFamily.Eclipse)]
    [InlineData("PartialLunarEclipse", EventFamily.Eclipse)]
    [InlineData("PenumbralLunarEclipse", EventFamily.Eclipse)]
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

    [Fact]
    public void Resolve_EclipseProfileUsesEclipseGuideThumbnailContract()
    {
        var profile = EventFamilyProfiles.Resolve(EventFamily.Eclipse, "SolarEclipse");

        Assert.Equal(EventFamily.Eclipse, profile.Family);
        Assert.Equal("Eclipse", profile.ValidatorProfile);
        Assert.Equal("EclipseGuideThumbnail", profile.ThumbnailCompositionType);
        Assert.True(profile.AllowsGuideCard);
        Assert.True(profile.AllowsDirectionCue);
        Assert.Contains("eclipseType", profile.RequiredDiagnosticFields);
        Assert.Contains("observationWarning", profile.RequiredDiagnosticFields);
    }
}
