using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionEventVocabularyInventoryTests
{
    private static readonly CanonicalAstronomyEventIdentityResolverV1 IdentityResolver = new();
    private static readonly AstronomyFamilyProfileResolver FamilyResolver = new(IdentityResolver, new AstronomyFamilyProfileCatalogV1(), new AstronomyFamilyProfileV1CompatibilityAdapter());

    [Theory]
    [InlineData("PLANET_CONJUNCTION", "PlanetPairing")]
    public void ConfirmedProductionAliasesResolve(string rawCode, string expectedFamily)
    {
        var identity = IdentityResolver.Resolve(rawCode, "ConfirmedProductionAliasInventory");
        var family = FamilyResolver.ResolveFamilyProfile(new CanonicalEventIdentity(identity.CanonicalEventType!, identity.CanonicalFamily, identity.CanonicalProfile, identity.InputEventType, identity.CanonicalEventType!, identity.ResolutionSource, identity.AppliedAliases.Count > 0, new Dictionary<string,string?>(), [], [], []));
        Assert.True(identity.Supported);
        Assert.Equal(expectedFamily, identity.CanonicalEventType);
        Assert.Equal(expectedFamily, family.Profile.FamilyId);
    }

    [Fact]
    public void ProductionEventVocabularyInventoryReportsEvidenceDrivenStatus()
    {
        var rows = ProductionEventVocabularyInventory.Rows;
        Assert.Contains(rows, r => r.RawEventCode == "PLANET_CONJUNCTION" && r.Classification == "active" && r.AliasCatalogStatus == "alias-catalog-hit");
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.RawEventCode));
            Assert.False(string.IsNullOrWhiteSpace(row.CanonicalIdentity));
            Assert.False(string.IsNullOrWhiteSpace(row.CanonicalFamily));
            Assert.Contains(row.Classification, new[] { "active", "future", "unsupported" });
        });
    }
}

public sealed record ProductionEventVocabularyInventoryRow(string RawEventCode, string CanonicalIdentity, string CanonicalFamily, string AliasCatalogStatus, string Classification, string EvidenceSource);

public static class ProductionEventVocabularyInventory
{
    public static IReadOnlyList<ProductionEventVocabularyInventoryRow> Rows { get; } =
    [
        new("PLANET_CONJUNCTION", "PlanetPairing", "PlanetPairing", "alias-catalog-hit", "active", "AstronomyEventDetectionService emits EventType=PLANET_CONJUNCTION"),
        new("Solar Eclipse", "SolarEclipse", "SolarEclipse", "alias-catalog-hit", "active", "ManualContentPlanCreationTests production planning request"),
        new("Meteor Shower", "MeteorShower", "MeteorShower", "alias-catalog-hit", "active", "AstronomyEventIntelligenceService meteor shower catalog"),
        new("Named Full Moon", "NamedFullMoon", "NamedFullMoon", "alias-catalog-hit", "active", "family profile named full moon production taxonomy")
    ];
}
