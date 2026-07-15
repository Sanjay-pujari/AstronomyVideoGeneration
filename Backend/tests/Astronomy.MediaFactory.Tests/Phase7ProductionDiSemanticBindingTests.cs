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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionSourcePolicyCatalogNonEmptyTests
{
    [Fact]
    public void AddMediaFactory_RegistersPopulatedProductionSourcePolicyCatalog()
    {
        var services = BuildServices();
        var sourcePolicyDescriptors = services
            .Where(d => d.ServiceType == typeof(ISemanticSourcePolicyCatalogV1))
            .Select((d, i) => $"#{i}: lifetime={d.Lifetime}, implementationType={d.ImplementationType?.FullName ?? "<factory/instance>"}, hasFactory={d.ImplementationFactory is not null}, hasInstance={d.ImplementationInstance is not null}")
            .ToArray();
        Assert.Single(sourcePolicyDescriptors);

        using var provider = BuildProvider(services);
        var catalog = provider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        var capabilityIds = catalog.Policies.Select(p => p.SemanticCapabilityId.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var diagnostic = $"CatalogType={catalog.GetType().FullName}; PolicyCount={catalog.Policies.Count}; CapabilityIds={string.Join(",", capabilityIds)}; Registrations={string.Join(" | ", sourcePolicyDescriptors)}";
        Assert.NotEmpty(catalog.Policies);
        Assert.True(catalog.Policies.Count > 0, diagnostic);
        foreach (var capability in RequiredProductionCapabilities)
            Assert.True(catalog.TryGet(new SemanticCapabilityId(capability), out _), $"Missing policy for {capability}. {diagnostic}");

        using var scope = provider.CreateScope();
        var scopedCatalog = scope.ServiceProvider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        Assert.Same(catalog, scopedCatalog);
        var concrete = provider.GetRequiredService<SemanticSourcePolicyCatalogV1>();
        Assert.Same(concrete, catalog);
        var engine = scope.ServiceProvider.GetRequiredService<ISemanticResolutionEngineV1>();
        Assert.IsType<SemanticResolutionEngineV1>(engine);
        var result = engine.Resolve(new SemanticResolutionRequestV1(new SemanticCapabilityId(SemanticCapabilityVocabularyV1.EventIdentity), true, SemanticRequirementLevelV1.Required, SemanticMissingValueBehaviorV1.BlockRequired, SemanticEvidenceStrengthV1.Weak, Enum.GetValues<SemanticEvidenceCategoryV1>(), BuildJupiterVenusProductionShapeContext(), "PlanetPairing", "long", "di-identity-proof"));
        Assert.NotEqual(SemanticResolutionStatusV1.ArchitectureError, result.Fact.Status);
        Assert.DoesNotContain("No source policy", result.Fact.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }

    internal static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=astronomy_tests;Username=test;Password=test",
                ["DatabaseSafety:AllowLocalhostPostgres"] = "true"
            })
            .Build();

        var services = BuildServices(configuration);

        return BuildProvider(services);
    }

    internal static ServiceCollection BuildServices(IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=astronomy_tests;Username=test;Password=test",
                ["DatabaseSafety:AllowLocalhostPostgres"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new Phase7ProductionDiTestHostEnvironment());
        services.AddMediaFactory(configuration);
        return services;
    }

    internal static ServiceProvider BuildProvider(IServiceCollection services)
    {
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

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

internal sealed class Phase7ProductionDiTestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Astronomy.MediaFactory.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
}

public sealed class ProductionSemanticRegistryNonEmptyTests
{
    [Fact]
    public void AddMediaFactory_RegistersPopulatedProductionSemanticAdapterRegistry()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        var registry = provider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var catalog = provider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();
        Assert.NotEmpty(registry.Adapters);
        AssertPolicyAndAdapter(catalog, registry, SemanticCapabilityVocabularyV1.AstronomicalObjects, "v1.astronomical-objects.production-event-intelligence");
        AssertPolicyAndAdapter(catalog, registry, SemanticCapabilityVocabularyV1.EventIdentity, "v1.event-identity.event-identity-context");
        AssertPolicyAndAdapter(catalog, registry, SemanticCapabilityVocabularyV1.EventWindow, "v1.event-window.observation-metadata");
        AssertPolicyAndAdapter(catalog, registry, SemanticCapabilityVocabularyV1.ObservationLocation, "v1.observation-location.observation-metadata");
        AssertPolicyAndAdapter(catalog, registry, SemanticCapabilityVocabularyV1.DomainScientificKnowledge, "v1.domain-scientific-knowledge.domain-provider");
    }

    private static void AssertPolicyAndAdapter(ISemanticSourcePolicyCatalogV1 catalog, ISemanticSourceAdapterRegistryV1 registry, string capabilityId, string adapterId)
    {
        Assert.True(catalog.TryGet(new SemanticCapabilityId(capabilityId), out var policy), $"Missing policy for {capabilityId}. PolicyCount={catalog.Policies.Count}.");
        var adapters = registry.GetAdapters(new SemanticCapabilityId(capabilityId)).ToArray();
        Assert.Contains(adapters, a => a.AdapterId == adapterId);
        Assert.Contains(policy.ApprovedSources, s => adapters.Any(a => a.SourceId == s.SourceId));
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
