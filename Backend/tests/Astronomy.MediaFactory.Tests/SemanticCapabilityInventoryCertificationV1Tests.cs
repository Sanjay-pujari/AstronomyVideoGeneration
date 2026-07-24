using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticCapabilityInventoryCertificationV1Tests
{
    [Fact]
    public void Canonical_Inventory_Has_Vocabulary_Catalog_Policy_Adapter_And_Family_Validation_Coverage()
    {
        var vocabulary = SemanticCapabilityVocabularyV1.CanonicalIds;
        var catalog = new SemanticCapabilityCatalogV1();
        var policies = new SemanticSourcePolicyCatalogV1();
        var adapters = new SemanticSourceAdapterRegistryV1();
        var families = new AstronomyFamilyProfileCatalogV1();

        Assert.Equal(19, vocabulary.Count);
        Assert.Empty(vocabulary.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1));
        Assert.True(catalog.Validate().IsValid, string.Join("; ", catalog.Validate().Errors));
        Assert.True(policies.Validate().IsValid, string.Join("; ", policies.Validate().Errors));
        Assert.True(families.Validate().IsValid, string.Join("; ", families.Validate().Errors));

        foreach (var id in vocabulary)
        {
            var capabilityId = new SemanticCapabilityId(id);
            Assert.True(catalog.TryGet(capabilityId, out _), $"Missing catalog definition for {id}.");
            Assert.True(policies.TryGet(capabilityId, out _), $"Missing source policy for {id}.");
            Assert.NotEmpty(adapters.GetAdapters(capabilityId));
        }
    }

    [Fact]
    public void Legacy_Migration_Does_Not_Own_Canonical_CulturalNameContext_Identifier()
    {
        var catalog = new SemanticCapabilityCatalogV1();
        var result = catalog.ResolveLegacyTerm(SemanticCapabilityVocabularyV1.CulturalNameContext);

        Assert.Equal(LegacySemanticCapabilityResolutionStatus.CanonicalMatch, result.Status);
        Assert.Equal(SemanticCapabilityVocabularyV1.CulturalNameContext, result.CanonicalCapabilityId!.Value.Value);
        Assert.DoesNotContain(LegacySemanticCapabilityMapV1.Entries, e => e.LegacyTerm.Equals(SemanticCapabilityVocabularyV1.CulturalNameContext, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compatibility_Adapter_Projects_Only_Canonical_V1_Profile_Requirements()
    {
        var catalog = new AstronomyFamilyProfileCatalogV1();
        var adapter = new AstronomyFamilyProfileV1CompatibilityAdapter();
        var canonicalIds = SemanticCapabilityVocabularyV1.CanonicalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in catalog.Profiles)
        {
            Assert.All(profile.LongFormStructure.Beats.Concat(profile.ShortFormStructure.Beats).SelectMany(b => b.Requirements), r => Assert.Contains(r.SemanticCapabilityId.Value, canonicalIds));
            var converted = adapter.Convert(profile, new FamilyProfileCompatibilityContext(profile.FamilyId, profile.FamilyId, profile.FamilyId, false));
            Assert.True(converted.Succeeded, string.Join("; ", converted.BlockingErrors));
        }
    }
}
