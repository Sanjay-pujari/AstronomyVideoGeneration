using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Infrastructure;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Catalog;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Adapters.Registry;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class ApiHostRuntimeCompositionDiagnosticsTests
{
    [Fact]
    public void ApiHost_UsesExpectedPhase7ResolverAndAssembly()
    {
        var services = ProductionSourcePolicyCatalogNonEmptyTests.BuildServices();
        Assert.Single(services.Where(d => d.ServiceType == typeof(IRequiredSemanticFactResolver)));
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider(services);
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var registry = scope.ServiceProvider.GetRequiredService<ISemanticSourceAdapterRegistryV1>();
        var catalog = scope.ServiceProvider.GetRequiredService<ISemanticSourcePolicyCatalogV1>();

        Assert.IsType<RequiredSemanticFactResolver>(resolver);
        Assert.Equal("Sprint4G4-MeteorActivity-CanonicalProjection", MediaFactoryRuntimeIdentity.SemanticArchitectureMarker);
        Assert.Contains("Astronomy.MediaFactory.Infrastructure", MediaFactoryRuntimeIdentity.AssemblyLocation);
        Assert.Contains(registry.Adapters, a => a.AdapterId.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase) || a.SupportedCapabilityId.Value.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Policies, p => p.SemanticCapabilityId.Value.Contains("MeteorActivity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApiHost_GeminidsPhase7ResolverReturnsRadiantAndPeakWindow()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IRequiredSemanticFactResolver>();
        var result = resolver.Resolve(BuildGeminidsInput());
        var facts = result.Beats.SelectMany(b => b.RequiredFacts.Concat(b.OptionalFacts)).ToArray();
        var diagnostics = JsonSerializer.Serialize(result.Diagnostics);

        Assert.False(result.Blocking);
        Assert.Contains(facts, f => f.FactType == "Radiant");
        Assert.Contains(facts, f => f.FactType == "PeakWindow");
        Assert.Equal("Sprint4G4-MeteorActivity-CanonicalProjection", MediaFactoryRuntimeIdentity.SemanticArchitectureMarker);
        Assert.IsType<RequiredSemanticFactResolver>(resolver);
        Assert.Contains("MeteorActivity", diagnostics);
    }

    private static RequiredSemanticFactResolutionInput BuildGeminidsInput()
    {
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("MeteorShower", null, null, null, null, null)).Profile;
        var request = new ContentPlanProductionPipelineRequest(
            PlanId: Guid.Parse("d338923a-b49c-4111-872c-a46f2720ccb8"), Category: "Astronomy", Title: "Geminids Meteor Shower Peak", ShortTitle: "Geminids", EventType: "MeteorShower", RegionId: "US", Language: "en", PrimaryObjects: ["Geminids"], SecondaryObjects: ["Meteors"], StartUtc: DateTimeOffset.Parse("2026-12-13T00:00:00Z"), PeakUtc: DateTimeOffset.Parse("2026-12-14T07:00:00Z"), EndUtc: DateTimeOffset.Parse("2026-12-15T12:00:00Z"), ScheduledUtc: DateTimeOffset.Parse("2026-12-13T12:00:00Z"), SourceExternalEventId: "geminids-2026", PlannedFormat: "long", RequestedOutputs: ["long", "short"], VisibilityScore: 90, RarityScore: 70, AudienceInterestScore: 85, ContentOpportunityScore: 90, VerificationStatus: "Verified", VerificationSource: "ProductionParityTest", ContentStrategy: "MeteorShower", LocalPeakTime: "after midnight", SkyDirectionHint: "east to overhead", VisibilityRegion: "United States", MoonInterference: "low moon interference", BestViewingWindowLocal: "midnight to pre-dawn", RadiantVisibilityNote: null, MoonIlluminationPercent: 10m, RecommendedPublishWindow: null, RecommendedContentTypes: [], Warnings: [], SourceNotes: [], TimeZone: "America/New_York", AngularSeparationDegrees: null);
        return new RequiredSemanticFactResolutionInput(profile, Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), Json("{\"beats\":[{\"sceneId\":\"scene-1\",\"documentaryBeatId\":\"hook\",\"narrativeRole\":\"Hook\",\"allocatedFacts\":{}}]}"), null, null, Json("{\"eventType\":\"MeteorShower\"}"), null, null, LanguageProfileResolver.Resolve("en"), request, CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("MeteorShower", "MeteorShower", "MeteorShower", [], "MeteorShower")));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
