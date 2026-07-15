using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyProfileV1CompatibilityParityTests
{
    private static readonly string[] RequiredActiveFamilies =
    [
        "PlanetPairing",
        "MeteorShower",
        "NamedFullMoon",
        "SolarEclipse",
        "LunarEclipse",
        "PlanetGrouping",
        "Constellation",
        "DeepSkyObject",
        "FullMoon"
    ];

    [Theory]
    [MemberData(nameof(ActiveFamilies))]
    public void CompatibilityAdapterPreservesV1ProfileRequirementParity(string familyId)
    {
        var catalog = new AstronomyFamilyProfileCatalogV1();
        var v1 = catalog.GetRequired(familyId);
        var result = new AstronomyFamilyProfileV1CompatibilityAdapter().Convert(v1, new FamilyProfileCompatibilityContext(familyId, familyId, familyId, false));

        Assert.True(result.Succeeded, string.Join("; ", result.BlockingErrors));
        Assert.NotNull(result.LegacyProfile);
        Assert.Equal(v1.SupportedFormats, result.Diagnostics.SupportedFormats);
        Assert.Equal(v1.LongFormStructure.Beats.Select(b => b.BeatRole), result.Diagnostics.LongBeatRoles);
        Assert.Equal(v1.ShortFormStructure.Beats.Select(b => b.BeatRole), result.Diagnostics.ShortBeatRoles);

        var v1Requirements = v1.LongFormStructure.Beats.Concat(v1.ShortFormStructure.Beats)
            .SelectMany(b => b.Requirements)
            .GroupBy(r => r.SemanticCapabilityId.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(v1Requirements.Keys.Order(StringComparer.OrdinalIgnoreCase), result.Diagnostics.Mappings.Select(m => m.V1CapabilityId).Order(StringComparer.OrdinalIgnoreCase));
        foreach (var mapping in result.Diagnostics.Mappings)
        {
            var requirement = v1Requirements[mapping.V1CapabilityId];
            Assert.Equal(requirement.RequirementLevel.ToString(), mapping.RequirementLevel);
            Assert.Equal(requirement.MissingValueBehavior.ToString(), mapping.MissingValueBehavior);
            Assert.Equal(requirement.MayOmit, mapping.MayOmit);
            Assert.Equal(requirement.BlocksPhase7, mapping.BlocksPhase7);
            Assert.Equal(requirement.MinimumEvidenceStrength, mapping.MinimumConfidence);
            Assert.Equal(requirement.AllowedEvidenceCategories, mapping.AllowedSources);
            Assert.NotEmpty(mapping.LongBeatRoles);
            Assert.NotEmpty(mapping.ShortBeatRoles);
        }
    }

    [Fact]
    public void PlanetPairingObservationDirectionRemainsOptionalOmittableCompatibilityFact()
    {
        var catalog = new AstronomyFamilyProfileCatalogV1();
        var v1 = catalog.GetRequired("PlanetPairing");
        var result = new AstronomyFamilyProfileV1CompatibilityAdapter().Convert(v1, new FamilyProfileCompatibilityContext("PlanetPairing", "PlanetPairing", "PlanetPairing", false));

        var mapping = Assert.Single(result.Diagnostics.Mappings, m => m.V1CapabilityId == "ObservationDirection");
        Assert.Equal("Optional", mapping.RequirementLevel);
        Assert.Equal("OmitCapability", mapping.MissingValueBehavior);
        Assert.True(mapping.MayOmit);
        Assert.False(mapping.BlocksPhase7);
        Assert.DoesNotContain("ObservationDirection", result.LegacyProfile!.RequiredFactTypes);
        Assert.Contains("ObservationDirection", result.LegacyProfile.OptionalFactTypes);
    }

    public static IEnumerable<object[]> ActiveFamilies()
        => RequiredActiveFamilies.Select(f => new object[] { f });
}
