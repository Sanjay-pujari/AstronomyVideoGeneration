using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Core.AstronomyDomain.Families;
using Astronomy.MediaFactory.Core.Constellations;
using Astronomy.MediaFactory.Core.Certification;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ConstellationOrionSprint1Tests
{
    [Fact]
    public void Constellation_resolves_as_first_class_event_family_without_content_strategy()
    {
        var resolution = EventFamilyResolver.ResolveWithDiagnostics("CONSTELLATION", null, ["Orion"], [], "Orion: How to Find the Hunter Constellation");
        resolution.Family.Should().Be(Astronomy.MediaFactory.Core.EventFamily.Constellation);
        resolution.Reason.Should().Contain("eventType");
    }

    [Theory]
    [InlineData("CONSTELLATION")]
    [InlineData("Constellation")]
    public void Canonical_semantic_family_resolves_constellation_aliases(string eventType)
    {
        var catalog = new AstronomyFamilyProfileCatalogV1();
        var result = catalog.ResolveEventType(eventType);
        result.Status.Should().Be(AstronomyFamilyResolutionStatusV1.Resolved);
        result.CanonicalFamilyId.Should().Be("Constellation");
        result.ActiveInV1.Should().BeTrue();
    }

    [Fact]
    public void Orion_domain_entity_resolves_through_registered_constellation_family()
    {
        var provider = new OrionConstellationKnowledgeProvider();
        var registry = new AstronomyDomainFamilyRegistry([new ConstellationDomainFamily()]);
        var family = registry.ResolveByEventType("CONSTELLATION");
        var knowledge = provider.GetOrion();
        family.FamilyId.Should().Be(ConstellationFamilyIds.FamilyId);
        registry.ResolveForEntity(knowledge.Entity.Identity).FamilyId.Should().Be(ConstellationFamilyIds.FamilyId);
        family.ValidateEntity(knowledge.Entity).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Orion_knowledge_keeps_science_and_culture_separate_and_source_attributed()
    {
        var knowledge = new OrionConstellationKnowledgeProvider().GetOrion();
        knowledge.IauAbbreviation.Should().Be("Ori");
        knowledge.PrincipalStars.Should().Contain(s => s.Name == "Betelgeuse");
        knowledge.NotableDeepSkyObjects.Should().Contain(o => o.CatalogId == "M42");
        knowledge.CulturalTraditions.Should().OnlyContain(t => t.SourceIds.Count > 0);
        string.Join(" ", knowledge.ScientificSignificance).Should().NotContain("hunter", because: "classical hunter stories are cultural context, not scientific evidence");
    }

    [Fact]
    public void Orion_plan_fixture_uses_canonical_constellation_event_type_not_content_strategy()
    {
        var path = Path.Combine(FindRepoRoot(), "Backend/src/Astronomy.MediaFactory.Infrastructure/Production/Constellations/Seeds/orion-content-generation-plan.json");
        var json = File.ReadAllText(path);
        json.Should().Contain("\"primaryAstronomyEventTypeCode\": \"CONSTELLATION\"");
        json.Should().NotContain("contentStrategy", because: "family resolution must not depend on ContentStrategy");
    }

    [Fact]
    public void Certification_registry_resolves_constellation_and_preserves_unknown_behavior()
    {
        var services = new ServiceCollection().AddCgA1CertificationFoundation().BuildServiceProvider();
        var registry = services.GetRequiredService<IFamilyCertificationProfileRegistry>();
        registry.TryResolve("CONSTELLATION", out var profile).Should().BeTrue();
        profile!.FamilyId.Should().Be("CONSTELLATION");
        registry.TryResolve("NotARealFamily", out _).Should().BeFalse();
        registry.TryResolve("MeteorShower", out var meteor).Should().BeTrue();
        meteor!.FamilyId.Should().Be("MeteorShower");
    }


    [Fact]
    public void Certification_semantic_catalog_resolves_single_constellation_registration()
    {
        var catalog = new CertificationSemanticFactCatalog();

        catalog.ResolveFamily("CONSTELLATION").CanonicalSemanticValueId.Should().Be("Constellation");
        catalog.ResolveFamily("Constellation").FamilyId.Should().Be("CONSTELLATION");
        catalog.ResolveCanonicalValue(" Constellation ").Should().Be("Constellation");
    }

    [Fact]
    public void Certification_foundation_registration_is_idempotent_for_constellation_profile()
    {
        var services = new ServiceCollection().AddCgA1CertificationFoundation().AddCgA1CertificationFoundation();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetServices<IFamilyCertificationProfile>().OfType<ConstellationCertificationProfile>().Should().ContainSingle();
        provider.GetRequiredService<IFamilyCertificationProfileRegistry>().Resolve("Constellation").FamilyId.Should().Be("CONSTELLATION");
    }

    [Fact]
    public void Certification_semantic_catalog_allows_same_provider_canonical_and_alias_collision()
    {
        var profile = TestFamily("CONSTELLATION", ["Constellation"], "Constellation");
        var catalog = new CertificationSemanticFactCatalog([profile]);

        catalog.ResolveFamily("Constellation").Should().BeSameAs(profile);
    }

    [Fact]
    public void Certification_semantic_catalog_deduplicates_same_provider_alias_case_and_whitespace()
    {
        var profile = TestFamily("CONSTELLATION", [" Constellation ", "constellation"], "Constellation");
        var catalog = new CertificationSemanticFactCatalog([profile]);

        catalog.ResolveFamily("constellation").Should().BeSameAs(profile);
    }

    [Fact]
    public void Certification_semantic_catalog_rejects_different_providers_claiming_constellation()
    {
        var first = TestFamily("CONSTELLATION", [], "Constellation");
        var second = TestFamily("Constellation", [], "OtherConstellation");

        Action act = () => _ = new CertificationSemanticFactCatalog([first, second]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate certification semantic-fact key 'Constellation'*Constellation*");
    }

    [Fact]
    public void Certification_semantic_catalog_rejects_different_providers_claiming_same_constellation_alias()
    {
        var first = TestFamily("OrionConstellation", ["Constellation"], "OrionConstellation");
        var second = TestFamily("LegacyConstellation", [" constellation "], "LegacyConstellation");

        Action act = () => _ = new CertificationSemanticFactCatalog([first, second]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate certification semantic-fact key*Constellation*");
    }

    [Fact]
    public void Certification_semantic_catalog_resolves_all_existing_astronomy_families_without_conflict()
    {
        var catalog = new CertificationSemanticFactCatalog();

        catalog.ResolveFamily("MeteorShower").CanonicalSemanticValueId.Should().Be("MeteorActivity");
        catalog.ResolveFamily("Meteor Shower").CanonicalSemanticValueId.Should().Be("MeteorActivity");
        catalog.ResolveFamily("PlanetConjunction").CanonicalSemanticValueId.Should().Be("PlanetPairing");
        catalog.ResolveFamily("PLANET_CONJUNCTION").CanonicalSemanticValueId.Should().Be("PlanetPairing");
        catalog.ResolveFamily("CONSTELLATION").CanonicalSemanticValueId.Should().Be("Constellation");
    }

    [Fact]
    public void Certification_semantic_catalog_does_not_silently_choose_conflicting_constellation_provider()
    {
        var first = TestFamily("CONSTELLATION", ["Orion"], "Constellation");
        var second = TestFamily("Orion", [], "OrionLegacy");

        Action act = () => _ = new CertificationSemanticFactCatalog([first, second]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate certification semantic-fact key 'Orion'*");
    }

    private static CertificationFamilySemanticProfileMetadata TestFamily(string familyId, IEnumerable<string> aliases, string canonicalSemanticValueId) =>
        new(
            familyId,
            aliases.ToHashSet(StringComparer.OrdinalIgnoreCase),
            canonicalSemanticValueId,
            ["ObjectKnowledge"],
            [],
            [],
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            []);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Backend", "Astronomy.MediaFactory.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
