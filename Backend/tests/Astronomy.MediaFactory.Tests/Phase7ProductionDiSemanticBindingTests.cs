using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Collection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Resolution.V1.Engine;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Contracts;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using System.Collections.Immutable;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionSourcePolicyCatalogNonEmptyTests
{
    [Fact]
    public void AddMediaFactory_RegistersPopulatedProductionSourcePolicyCatalog()
    {
        using var provider = BuildProvider();
        var catalog = provider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        Assert.NotEmpty(catalog.Policies);
        foreach (var capability in RequiredProductionCapabilities)
            Assert.True(catalog.TryGet(new SemanticCapabilityId(capability), out _), $"Missing policy for {capability}.");
    }

    internal static ServiceProvider BuildProvider() => new ServiceCollection().AddMediaFactory(new ConfigurationBuilder().Build()).BuildServiceProvider();

    internal static readonly string[] RequiredProductionCapabilities =
    [
        SemanticCapabilityVocabularyV1.AstronomicalObjects,
        SemanticCapabilityVocabularyV1.EventIdentity,
        SemanticCapabilityVocabularyV1.EventWindow,
        SemanticCapabilityVocabularyV1.ObservationLocation,
        SemanticCapabilityVocabularyV1.AngularSeparation,
        SemanticCapabilityVocabularyV1.ObservationEquipment,
        SemanticCapabilityVocabularyV1.ObservationConditions,
        SemanticCapabilityVocabularyV1.DomainScientificKnowledge
    ];

    internal static SemanticSourceAdapterContextV1 BuildJupiterVenusProductionShapeContext()
    {
        var provenance = ImmutableArray<SemanticSourceProvenanceV1>.Empty;
        var objects = ImmutableArray.Create(
            new AstronomicalObjectValue("Jupiter", "Planet", "Primary", "gas giant", provenance),
            new AstronomicalObjectValue("Venus", "Planet", "Primary", "terrestrial planet", provenance));
        var window = new EventWindowValue(
            DateTimeOffset.Parse("2026-08-12T02:00:00Z"),
            DateTimeOffset.Parse("2026-08-12T03:00:00Z"),
            DateTimeOffset.Parse("2026-08-12T04:00:00Z"),
            null,
            null,
            null,
            null,
            "UTC",
            "around dawn");
        return new SemanticSourceAdapterContextV1(
            EventIdentity: new CanonicalAstronomyEventIdentity("jupiter-venus-conjunction", "PlanetPairing", "PlanetPairing", "Jupiter-Venus conjunction", "ProductionFixture"),
            ProductionEventIntelligence: new ProductionEventIntelligenceSourceV1(
                EventType: "jupiter-venus-conjunction",
                FamilyId: "PlanetPairing",
                ProfileId: "PlanetPairing",
                PrimaryObjects: objects,
                EventWindow: window,
                AngularSeparation: new AngularSeparationValue(1.0m, 60, null, "close", "apparent conjunction", DateTimeOffset.Parse("2026-08-12T03:00:00Z")),
                Verified: true),
            ObservationMetadata: new ObservationMetadataSourceV1(
                EventWindow: window,
                AngularSeparation: new AngularSeparationValue(1.0m, 60, null, "close", "apparent conjunction", DateTimeOffset.Parse("2026-08-12T03:00:00Z")),
                ObservationDirection: new ObservationDirectionValue("east", 90, 15, "low before sunrise", "low in the eastern sky before sunrise"),
                ObservationLocation: new ObservationLocationValue("United States", null, null, null, "America/New_York", false),
                ObservationConditions: new ObservationConditionsValue(null, "clear eastern horizon", "before sunrise", "bright planets visible in twilight", "avoid obstructions"),
                ObservationEquipment: new ObservationEquipmentValue(true, true, false, null, null, false),
                Verified: true),
            AstronomyDomainKnowledge: new AstronomyDomainKnowledgeSourceV1(
                EquipmentGuidance: new ObservationEquipmentValue(true, true, false, null, null, false),
                DomainKnowledge: new DomainScientificKnowledgeValue("apparent line-of-sight alignment", "Jupiter and Venus appear close together from Earth's perspective while remaining far apart in space.", "demonstrates planetary motion along the ecliptic", "use a clear horizon"),
                Verified: true),
            Language: "en",
            TimeZone: "America/New_York");
    }
}

public sealed class ProductionSemanticRegistryNonEmptyTests
{
    [Fact]
    public void AddMediaFactory_RegistersPopulatedProductionSemanticAdapterRegistry()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        var registry = provider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        Assert.NotEmpty(registry.Adapters);
        Assert.Contains(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.AstronomicalObjects)), a => a.AdapterId == "v1.astronomical-objects.production-event-intelligence");
        Assert.Contains(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.EventIdentity)), a => a.AdapterId == "v1.event-identity.event-identity-context");
        Assert.Contains(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.EventWindow)), a => a.AdapterId == "v1.event-window.observation-metadata");
        Assert.Contains(registry.GetAdapters(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.DomainScientificKnowledge)), a => a.AdapterId == "v1.domain-scientific-knowledge.domain-provider");
    }
}

public sealed class Phase7ProductionDiSemanticBindingTests
{
    [Fact]
    public void ProductionScope_ResolvesPhase7ServicesThroughSharedPopulatedDiGraph()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();

        var catalog = scope.ServiceProvider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        var registry = scope.ServiceProvider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var collector = scope.ServiceProvider.GetRequiredService<ISemanticCandidateCollectorV1>();
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var generator = scope.ServiceProvider.GetRequiredService<NarrationGeneratorV5>();

        Assert.NotNull(collector);
        Assert.NotNull(engine);
        Assert.NotNull(resolver);
        Assert.NotNull(generator);
        Assert.NotEmpty(catalog.Policies);
        Assert.NotEmpty(registry.Adapters);

        var context = ProductionSourcePolicyCatalogNonEmptyTests.BuildJupiterVenusProductionShapeContext();
        foreach (var legacyOrCanonical in new[] { "PrimaryObjects", "EventIdentity", "ObservationTiming", "LocationContext", "AngularRelationship", "ObservationMode", "VisibilityConditions", "ApparentAlignmentExplanation" })
        {
            var request = new SemanticResolutionRequestV1(
                new SemanticCapabilityId(legacyOrCanonical),
                true,
                SemanticRequirementLevelV1.Required,
                SemanticMissingValueBehaviorV1.BlockRequired,
                SemanticEvidenceStrengthV1.Weak,
                Enum.GetValues<SemanticEvidenceCategoryV1>(),
                context,
                "PlanetPairing",
                "long",
                "production-shaped");

            var result = engine.Resolve(request);
            Assert.NotEqual(SemanticResolutionStatusV1.ArchitectureError, result.Fact.Status);
            Assert.DoesNotContain("No source policy", result.Fact.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(result.Diagnostics.InvokedAdapterIds);
        }
    }
}
