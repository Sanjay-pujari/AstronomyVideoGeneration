using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyResolverV1RuntimeMigrationTests
{
    private readonly AstronomyFamilyProfileResolver _resolver = new(new CanonicalAstronomyEventIdentityResolverV1(), new AstronomyFamilyProfileCatalogV1(), new AstronomyFamilyProfileV1CompatibilityAdapter());
    private AstronomyFamilyProfileResolutionResult Resolve(string eventType) => _resolver.ResolveFamilyProfile(new CanonicalEventIdentity(eventType, null, null, eventType, eventType, "test", false, new Dictionary<string,string?>(), [], [], []));
    [Fact] public void PlanetaryConjunctionResolvesAsPlanetPairing() => Assert.Equal("PlanetPairing", Resolve("PlanetaryConjunction").Profile.FamilyId);
    [Fact] public void PlanetGroupingFamilyResolutionSucceeds() => Assert.Equal("PlanetGrouping", Resolve("PlanetGrouping").Profile.FamilyId);
    [Fact] public void SolarEclipseResolvesSeparatelyFromLunarEclipse() { Assert.Equal("SolarEclipse", Resolve("SolarEclipse").Profile.FamilyId); Assert.Equal("LunarEclipse", Resolve("LunarEclipse").Profile.FamilyId); }
    [Fact] public void NoActiveGenericEclipseResolutionRemains() { Assert.Throws<InvalidOperationException>(() => Resolve("Eclipse")); }
    [Fact] public void OccultationRemainsCompatible() => Assert.Equal("Occultation", Resolve("Occultation").Profile.FamilyId);
}
