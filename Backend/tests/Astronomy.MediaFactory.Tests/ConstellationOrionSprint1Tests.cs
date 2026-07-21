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
        resolution.Family.Should().Be(EventFamily.Constellation);
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Backend", "Astronomy.MediaFactory.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
