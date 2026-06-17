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
    [InlineData("Comet", EventFamily.SpecialEvent)]
    [InlineData("DeepSkyObject", EventFamily.SpecialEvent)]
    [InlineData("Deep Sky Object", EventFamily.SpecialEvent)]
    [InlineData("Constellation", EventFamily.SpecialEvent)]
    [InlineData("Occultation", EventFamily.SpecialEvent)]
    [InlineData("unknown", EventFamily.Unknown)]
    public void Resolve_MapsKnownEventTypesToExpectedFamily(string eventType, EventFamily expected)
    {
        var family = EventFamilyResolver.Resolve(eventType, contentCategoryCode: null, primaryObjects: [], secondaryObjects: []);

        Assert.Equal(expected, family);
    }

    [Theory]
    [InlineData("Comet", "SpecialEvent:Comet", "comet tail", "dark-sky/binocular guidance")]
    [InlineData("DeepSkyObject", "SpecialEvent:DeepSkyObject", "nebula, cluster, or galaxy style target", "telescope/binocular guidance")]
    [InlineData("Constellation", "SpecialEvent:Constellation", "star pattern lines", "direction guide")]
    [InlineData("Occultation", "SpecialEvent:Occultation", "foreground object crossing or covering background object", "occultation timing")]
    public void Resolve_SpecialEventProfileUsesSubtypeGuidanceWithoutValidatedFamilyLeakage(
        string eventType,
        string selectedProfile,
        string requiredVisualElement,
        string requiredOverlayElement)
    {
        var family = EventFamilyResolver.Resolve(eventType, contentCategoryCode: null, primaryObjects: [], secondaryObjects: []);
        var profile = EventFamilyProfiles.Resolve(family, eventType);

        Assert.Equal(EventFamily.SpecialEvent, family);
        Assert.Equal(EventFamily.SpecialEvent, profile.Family);
        Assert.Equal(selectedProfile, profile.SelectedProfile);
        Assert.Contains(requiredVisualElement, profile.RequiredVisualElements, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(requiredOverlayElement, profile.RequiredOverlayElements, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("detectedFamily", profile.RequiredDiagnosticFields);
        Assert.Contains("primaryEventTypeCode", profile.RequiredDiagnosticFields);
        Assert.Contains("selectedProfile", profile.RequiredDiagnosticFields);
        Assert.Contains("forbiddenTerms", profile.RequiredDiagnosticFields);
        Assert.Contains("requiredVisualElements", profile.RequiredDiagnosticFields);
        Assert.Contains("requiredOverlayElements", profile.RequiredDiagnosticFields);
        Assert.Contains("meteor radiant", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("meteor streak", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("solar eclipse safety", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_SpecialEventNonOccultationForbidsPlanetGroupingSeparationLabels()
    {
        var comet = EventFamilyProfiles.Resolve(EventFamily.SpecialEvent, "Comet");
        var occultation = EventFamilyProfiles.Resolve(EventFamily.SpecialEvent, "Occultation");

        Assert.Contains("separation label", comet.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.True(occultation.AllowsSeparationCue);
        Assert.DoesNotContain("separation label", occultation.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
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
