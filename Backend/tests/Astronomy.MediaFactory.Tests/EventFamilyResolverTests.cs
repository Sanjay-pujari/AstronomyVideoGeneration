using Astronomy.MediaFactory.Core;
using CoreEventFamily = Astronomy.MediaFactory.Core.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class EventFamilyResolverTests
{
    [Theory]
    [InlineData("MeteorShower", CoreEventFamily.Meteor)]
    [InlineData("PLANET_CONJUNCTION", CoreEventFamily.PlanetGrouping)]
    [InlineData("PLANET_GROUPING", CoreEventFamily.PlanetGrouping)]
    [InlineData("BLUE_MOON", CoreEventFamily.Moon)]
    [InlineData("FullMoon", CoreEventFamily.Moon)]
    [InlineData("NewMoon", CoreEventFamily.Moon)]
    [InlineData("BlueMoon", CoreEventFamily.Moon)]
    [InlineData("Supermoon", CoreEventFamily.Moon)]
    [InlineData("Micromoon", CoreEventFamily.Moon)]
    [InlineData("MoonPhase", CoreEventFamily.Moon)]
    [InlineData("SolarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("LunarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("TotalSolarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("PartialSolarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("AnnularSolarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("TotalLunarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("PartialLunarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("PenumbralLunarEclipse", CoreEventFamily.Eclipse)]
    [InlineData("LUNAR_ECLIPSE", CoreEventFamily.Eclipse)]
    [InlineData("COMET", CoreEventFamily.SpecialEvent)]
    [InlineData("Comet", CoreEventFamily.SpecialEvent)]
    [InlineData("DeepSkyObject", CoreEventFamily.SpecialEvent)]
    [InlineData("Deep Sky Object", CoreEventFamily.SpecialEvent)]
    [InlineData("Constellation", CoreEventFamily.SpecialEvent)]
    [InlineData("Occultation", CoreEventFamily.SpecialEvent)]
    [InlineData("unknown", CoreEventFamily.Unknown)]
    public void Resolve_MapsKnownEventTypesToExpectedFamily(string eventType, CoreEventFamily expected)
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

        Assert.Equal(CoreEventFamily.SpecialEvent, family);
        Assert.Equal(CoreEventFamily.SpecialEvent, profile.Family);
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
        var comet = EventFamilyProfiles.Resolve(CoreEventFamily.SpecialEvent, "Comet");
        var occultation = EventFamilyProfiles.Resolve(CoreEventFamily.SpecialEvent, "Occultation");

        Assert.Contains("separation label", comet.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.True(occultation.AllowsSeparationCue);
        Assert.DoesNotContain("separation label", occultation.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_MoonProfileUsesMoonPhaseGuideThumbnailContract()
    {
        var profile = EventFamilyProfiles.Resolve(CoreEventFamily.Moon, "BLUE_MOON");

        Assert.Equal(CoreEventFamily.Moon, profile.Family);
        Assert.Equal("Moon", profile.ValidatorProfile);
        Assert.Equal("MoonPhaseGuideThumbnail", profile.ThumbnailCompositionType);
        Assert.Contains("meteor", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("planet pairing", profile.ForbiddenTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("moonGuideCardAdded", profile.RequiredDiagnosticFields);
    }

    [Fact]
    public void Resolve_EclipseProfileUsesEclipseGuideThumbnailContract()
    {
        var profile = EventFamilyProfiles.Resolve(CoreEventFamily.Eclipse, "SolarEclipse");

        Assert.Equal(CoreEventFamily.Eclipse, profile.Family);
        Assert.Equal("Eclipse", profile.ValidatorProfile);
        Assert.Equal("EclipseGuideThumbnail", profile.ThumbnailCompositionType);
        Assert.True(profile.AllowsGuideCard);
        Assert.True(profile.AllowsDirectionCue);
        Assert.Contains("eclipseType", profile.RequiredDiagnosticFields);
        Assert.Contains("observationWarning", profile.RequiredDiagnosticFields);
    }
}
