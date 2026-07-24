using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class CulturalNameContextSemanticRegistryV1Tests
{
    [Fact]
    public void Registry_Capability_Policy_And_Adapter_Exist()
    {
        var catalog = new SemanticCapabilityCatalogV1();
        Assert.True(catalog.TryGet(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext), out _));

        var policies = new SemanticSourcePolicyCatalogV1();
        Assert.True(policies.TryGet(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext), out var policy));
        Assert.Contains("StructuredKnowledgeOnly", policy!.ApprovedDerivationRuleIds);
        Assert.Contains(policy.ApprovedSources, s => s.SourceId == SemanticSourcePolicyVocabularyV1.CulturalAstronomyKnowledgeProvider);

        var registry = new SemanticSourceAdapterRegistryV1();
        Assert.Contains(registry.Adapters, a => a.SupportedCapabilityId.Value == SemanticCapabilityVocabularyV1.CulturalNameContext && a.AdapterId == "v1.cultural-name-context.structured-knowledge");
        Assert.NotEmpty(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext)));
    }

    [Fact]
    public void Resolution_KnowledgePresent_Resolved_KnowledgeAbsent_OptionalOmitted()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        var present = Context(new CulturalContextValue("Orion", "Greek mythology", "Hunter Orion and traditional sky culture", .9m, "Mediterranean", "Use reviewed cultural context only.", true));

        var resolved = engine.Resolve(Request(present));
        Assert.Equal(SemanticResolutionStatusV1.Resolved, resolved.Fact.Status);
        Assert.Equal("v1.cultural-name-context.structured-knowledge", resolved.Fact.WinningAdapterId);

        var omitted = engine.Resolve(Request(Context(null)));
        Assert.Equal(SemanticResolutionStatusV1.UnavailableOptional, omitted.Fact.Status);
        Assert.Empty(omitted.Diagnostics.BlockingIssueCodes);
    }


    [Fact]
    public void CulturalNameContext_Policy_Exists()
    {
        var policies = new SemanticSourcePolicyCatalogV1();
        Assert.True(policies.TryGet(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext), out var policy));
        Assert.Equal(SemanticCapabilityVocabularyV1.CulturalNameContext, policy!.CapabilityId.Value);
    }

    [Fact]
    public void CulturalNameContext_V1_Adapter_Exists()
    {
        var registry = new SemanticSourceAdapterRegistryV1();
        var adapter = Assert.Single(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext)).Where(a => a.AdapterId == "v1.cultural-name-context.structured-knowledge"));
        Assert.Equal(SemanticCapabilityVocabularyV1.CulturalNameContext, adapter.SupportedCapabilityId.Value);
    }

    [Fact]
    public void CulturalNameContext_Optional_Absence_Does_Not_Block()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>().Resolve(Request(Context(null)));
        Assert.Equal(SemanticResolutionStatusV1.UnavailableOptional, result.Fact.Status);
        Assert.Empty(result.Diagnostics.BlockingIssueCodes);
    }

    [Fact]
    public void CulturalNameContext_Verified_Knowledge_Resolves()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>().Resolve(Request(Context(new CulturalContextValue("Orion", "Greek mythology", "Reviewed sky-culture naming context", .9m, "Mediterranean", "Reviewed", true))));
        Assert.Equal(SemanticResolutionStatusV1.Resolved, result.Fact.Status);
        Assert.Equal("v1.cultural-name-context.structured-knowledge", result.Fact.WinningAdapterId);
    }

    [Fact]
    public void Legacy_Runtime_Cultural_Adapter_Can_Extract_Knowledge()
    {
        var registry = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog());
        var adapter = Assert.Single(registry.Adapters.Where(a => a.AdapterId == "CulturalNameContextStructuredKnowledgeAdapter"));
        var context = new SemanticCapabilitySourceContext("Constellation", "long", null, null, null, null, null, null, null, null, JsonDocument.Parse("{\"culturalNameContext\":\"Reviewed Orion naming context from structured astronomy knowledge.\"}").RootElement.Clone());

        Assert.True(adapter.TryExtract(context, out var candidate, out var rejection));
        Assert.Null(rejection);
        Assert.Equal("Astronomy Domain Knowledge Provider", candidate.Source);
        Assert.Equal("culturalNameContext", candidate.SourceField);
    }

    [Theory]
    [InlineData("CONSTELLATION")]
    [InlineData("Constellation")]
    [InlineData("constellation")]
    public void Constellation_Registry_Coverage_Passes(string eventType)
    {
        var profile = Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.AstronomyFamilyProfileCatalog.Resolve(TestJson.Json($"{{\"eventType\":\"{eventType}\"}}"), null);
        Assert.Equal("Constellation", profile.FamilyId);
        var rows = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]);
        Assert.DoesNotContain(rows, r => !r.ResolutionPathValid);
        Assert.Contains(rows, r => r.FamilyProfile == "Constellation" && r.Capability == "CulturalNameContext" && !r.Required);
    }

    [Theory]
    [InlineData("PlanetPairing")]
    [InlineData("MeteorShower")]
    [InlineData("SolarEclipse")]
    public void Existing_Family_Registry_Coverage_Remains_Passing(string eventType)
    {
        var profile = Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.AstronomyFamilyProfileCatalog.Resolve(TestJson.Json($"{{\"eventType\":\"{eventType}\"}}"), null);
        var rows = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]);
        Assert.DoesNotContain(rows, r => !r.ResolutionPathValid);
    }

    [Fact]
    public void Constellation_Legacy_Registry_Validation_Passes_Without_Changing_Other_Families()
    {
        var registry = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog());
        var profiles = new[] { "Constellation", "PlanetPairing", "MeteorShower", "SolarEclipse" }
            .Select(e => Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.AstronomyFamilyProfileCatalog.Resolve(TestJson.Json($"{{\"eventType\":\"{e}\"}}"), null))
            .ToArray();
        var rows = registry.ValidateCoverageDetailed(profiles);

        Assert.Contains(rows, r => r.FamilyProfile == "Constellation" && r.Capability == "CulturalNameContext" && !r.Required && r.CatalogRegistrationFound && r.RegisteredAdapterIds.Contains("CulturalNameContextStructuredKnowledgeAdapter") && r.ResolutionPathValid && r.FailureReason is null);
        Assert.DoesNotContain(rows, r => r.FamilyProfile == "Constellation" && r.Capability == "CulturalNameContext" && r.FailureReason == "CapabilityNotRegistered");
        Assert.DoesNotContain(rows, r => r.FamilyProfile is "PlanetPairing" or "MeteorShower" or "SolarEclipse" && r.Capability == "CulturalNameContext");
    }

    private static SemanticResolutionRequestV1 Request(SemanticSourceAdapterContextV1 context) => new(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.CulturalNameContext), false, SemanticRequirementLevelV1.Optional, SemanticMissingValueBehaviorV1.OmitOptional, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>(), context, "Constellation");

    private static SemanticSourceAdapterContextV1 Context(CulturalContextValue? value) => new(
        EventIdentity: new CanonicalAstronomyEventIdentity("Constellation", "Constellation", "Constellation", "Constellation", "test"),
        ProductionEventIntelligence: new ProductionEventIntelligenceSourceV1("Constellation", "Constellation", "Constellation"),
        CulturalAstronomyKnowledge: value is null ? new CulturalAstronomyKnowledgeSourceV1() : new CulturalAstronomyKnowledgeSourceV1(value));
}
