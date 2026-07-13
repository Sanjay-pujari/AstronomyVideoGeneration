using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyFamilyAliasCatalogV1Tests
{
    [Theory]
    [InlineData("PlanetaryConjunction", "PlanetPairing")]
    [InlineData("Planet Grouping", "PlanetGrouping")]
    [InlineData("MultiPlanetGrouping", "PlanetGrouping")]
    [InlineData("PLANET_GROUPING", "PlanetGrouping")]
    [InlineData("Meteor Shower", "MeteorShower")]
    [InlineData("Full Moon", "FullMoon")]
    [InlineData("Named Full Moon", "NamedFullMoon")]
    [InlineData("Solar Eclipse", "SolarEclipse")]
    [InlineData("Lunar Eclipse", "LunarEclipse")]
    [InlineData("Deep Sky Object", "DeepSkyObject")]
    [InlineData("DeepSky", "DeepSkyObject")]
    public void EveryApprovedAliasResolvesDeterministically(string alias, string expected)
    {
        var result = new AstronomyFamilyProfileCatalogV1().ResolveEventType(alias);
        Assert.Equal(AstronomyFamilyResolutionStatusV1.Resolved, result.Status);
        Assert.Equal(expected, result.CanonicalFamilyId);
        Assert.True(result.AliasApplied);
    }

    [Fact] public void PlanetaryConjunctionResolvesOnlyToPlanetPairing() => Assert.Equal("PlanetPairing", new AstronomyFamilyProfileCatalogV1().ResolveEventType("PlanetaryConjunction").CanonicalFamilyId);
    [Fact] public void DuplicateAliasesFailValidation() { var aliases = new AstronomyFamilyAliasCatalogV1([new("Dup","PlanetPairing"), new("Dup","PlanetPairing")]); Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate(new AstronomyFamilyProfileCatalogV1().Profiles, aliases).Errors, e => e.Contains("Duplicate alias")); }
    [Fact] public void AliasCyclesFailValidation() { var aliases = new AstronomyFamilyAliasCatalogV1([new("PlanetPairing","MeteorShower"), new("MeteorShower","PlanetPairing")]); Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate(new AstronomyFamilyProfileCatalogV1().Profiles, aliases).Errors, e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase)); }
    [Fact] public void MissingTargetProfileFailsValidation() { var aliases = new AstronomyFamilyAliasCatalogV1([new("Bad","Nope")]); Assert.Contains(AstronomyFamilyProfileCatalogV1.Validate(new AstronomyFamilyProfileCatalogV1().Profiles, aliases).Errors, e => e.Contains("missing profile")); }
}
