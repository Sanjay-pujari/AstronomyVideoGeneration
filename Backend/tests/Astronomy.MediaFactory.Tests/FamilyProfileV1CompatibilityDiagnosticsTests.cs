using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
namespace Astronomy.MediaFactory.Tests;
public sealed class FamilyProfileV1CompatibilityDiagnosticsTests
{
    [Fact] public void DiagnosticsCarryV1AuthorityAndGeneratedRequirements()
    {
        var d = new FamilyProfileCompatibilityDiagnostics("PlanetaryConjunction", "PlanetPairing", "PlanetPairing", "PlanetPairing", true, "adapter", "PlanetPairing", ["PrimaryObjects"], ["EditorialContext"], [], [], "V1", [], null);
        Assert.Equal("V1", d.ResolutionAuthority);
        Assert.Contains("PrimaryObjects", d.GeneratedLegacyRequirements);
    }
}
