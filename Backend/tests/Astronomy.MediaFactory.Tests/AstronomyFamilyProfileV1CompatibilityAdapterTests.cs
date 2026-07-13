using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;

namespace Astronomy.MediaFactory.Tests;

public sealed class AstronomyFamilyProfileV1CompatibilityAdapterTests
{
    private readonly AstronomyFamilyProfileCatalogV1 _catalog = new();
    private readonly AstronomyFamilyProfileV1CompatibilityAdapter _adapter = new();
    private FamilyProfileCompatibilityResult Convert(string id) => _adapter.Convert(_catalog.GetRequired(id), new(id, id, id, false));

    [Fact] public void PlanetPairingConvertsToValidLegacyProfile() { var r = Convert("PlanetPairing"); Assert.True(r.Succeeded); Assert.Equal("PlanetPairing", r.LegacyProfile!.FamilyId); Assert.Contains("PrimaryObjects", r.LegacyProfile.RequiredFactTypes); }
    [Fact] public void MeteorShowerPreservesCurrentRequirements() { var p = Convert("MeteorShower").LegacyProfile!; Assert.Contains("Name", p.RequiredFactTypes); Assert.Contains("EventDateOrWindow", p.RequiredFactTypes); Assert.Contains("Radiant", p.RequiredFactTypes); Assert.Contains("PeakWindow", p.RequiredFactTypes); Assert.Contains("ObservationDirection", p.RequiredFactTypes); Assert.Contains("ScientificImportance", p.RequiredFactTypes); Assert.Contains("Zhr", p.OptionalFactTypes); }
    [Fact] public void NamedFullMoonCulturalContextRemainsOptional() { var p = Convert("NamedFullMoon").LegacyProfile!; Assert.DoesNotContain("CulturalNameContext", p.RequiredFactTypes); Assert.Contains("CulturalNameContext", p.OptionalFactTypes); }
    [Fact] public void SolarAndLunarSafetyGuidanceRemainSeparated() { Assert.Contains("SafetyGuidance", Convert("SolarEclipse").LegacyProfile!.RequiredFactTypes); Assert.DoesNotContain("SafetyGuidance", Convert("LunarEclipse").LegacyProfile!.RequiredFactTypes); }
    [Fact] public void ReferenceFamiliesDoNotGainEventWindow() { Assert.DoesNotContain("EventDateOrWindow", Convert("Constellation").LegacyProfile!.RequiredFactTypes); Assert.DoesNotContain("EventDateOrWindow", Convert("DeepSkyObject").LegacyProfile!.RequiredFactTypes); }
    [Fact] public void PlanetGroupingPreservesMinimumObjectCountPolicyDiagnostically() => Assert.Equal(3, Convert("PlanetGrouping").Diagnostics.MinimumObjectCountPolicy);
    [Fact] public void EveryMappingIsExplicitlyClassified() { var r = Convert("SolarEclipse"); Assert.All(r.Diagnostics.Mappings, m => Assert.True(Enum.IsDefined(m.MappingKind))); }
    [Fact] public void OptionalOmissionIsNonBlockingAndRecorded() { var r = Convert("PlanetPairing"); Assert.True(r.Succeeded); Assert.Contains(SemanticCapabilityVocabularyV1.EditorialContext, r.Diagnostics.OmittedOptionalRequirements); }
    [Fact] public void DiagnosticsSerializeAndRoundTrip() { var d = Convert("PlanetPairing").Diagnostics; var copy = JsonSerializer.Deserialize<FamilyProfileCompatibilityDiagnostics>(JsonSerializer.Serialize(d)); Assert.Equal("V1", copy!.ResolutionAuthority); Assert.Equal(d.GeneratedLegacyFamilyId, copy.GeneratedLegacyFamilyId); }
}
