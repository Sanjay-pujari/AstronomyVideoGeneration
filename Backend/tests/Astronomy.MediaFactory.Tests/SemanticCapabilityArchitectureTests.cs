using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticCapabilityArchitectureTests
{
    private static readonly LanguageProfile English = LanguageProfileResolver.Resolve("en");
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void CatalogRejectsDuplicateCapabilityIdsByConstructionAndRegistersRequiredMinimumSet()
    {
        var catalog = new SemanticCapabilityCatalog();
        catalog.Validate();
        Assert.Equal(catalog.Capabilities.Count, catalog.Capabilities.Select(c => c.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(catalog.Capabilities, c => c.CapabilityId == "EventIdentity");
        Assert.Contains(catalog.Capabilities, c => c.CapabilityId == "ObservationTiming");
        Assert.Contains(catalog.Capabilities, c => c.CapabilityId == "ScientificIdentity");
    }

    [Fact]
    public void RegistryRejectsDuplicateAdapterIdsAndValidatesPlanetPairingCoverage()
    {
        var catalog = new SemanticCapabilityCatalog();
        var registry = new SemanticCapabilitySourceRegistry(catalog);
        registry.Validate();
        Assert.Equal(registry.Adapters.Count, registry.Adapters.Select(a => a.AdapterId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"PlanetPairing\"}"), null);
        Assert.Empty(registry.ValidateCoverage([profile]));
    }

    [Fact]
    public void EventIdentityResolvesFromTitleAndFamily()
    {
        var resolver = BuildResolver();
        var context = new SemanticCapabilitySourceContext("PlanetPairing", "long", Json("{\"title\":\"Mars meets Jupiter\",\"eventType\":\"PlanetPairing\"}"), null, null, null, null, null, null, null);
        var result = resolver.Resolve("EventIdentity", context, English);
        Assert.Equal("Resolved", result.Status);
        Assert.Equal("Mars meets Jupiter", result.CanonicalValue);
    }

    [Fact]
    public void ExplicitLocalTimeOutranksRawUtcAndUtcRequiresVerifiedTimezone()
    {
        var resolver = BuildResolver();
        var withLocal = new SemanticCapabilitySourceContext("PlanetPairing", "long", null, null, null, null, null, null, Json("{\"localPeakTime\":\"before dawn\",\"peakUtc\":\"2026-07-11T01:00:00Z\",\"timezone\":\"Asia/Kolkata\"}"), null);
        var local = resolver.Resolve("ObservationTiming", withLocal, English);
        Assert.Equal("before dawn", local.CanonicalValue);

        var utcWithoutZone = new SemanticCapabilitySourceContext("PlanetPairing", "long", null, null, null, null, null, null, Json("{\"peakUtc\":\"2026-07-11T01:00:00Z\"}"), null);
        var unresolved = resolver.Resolve("ObservationTiming", utcWithoutZone, English);
        Assert.Contains(unresolved.RejectedSources, r => r.Reason == "VerificationFailed");
    }

    [Fact]
    public void NarrationGeneratorDoesNotContainPrivateNestedSemanticCapabilityResolver()
    {
        var source = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "Astronomy.MediaFactory.Infrastructure", "Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        Assert.DoesNotContain("private sealed class SemanticCapabilityResolver", source);
        Assert.DoesNotContain("new RequiredSemanticFactResolver", source);
        Assert.DoesNotContain("new NarrationRealizer", source);
    }

    private static ISemanticCapabilityResolver BuildResolver()
    {
        var catalog = new SemanticCapabilityCatalog();
        return new SemanticCapabilityResolver(catalog, new SemanticCapabilitySourceRegistry(catalog));
    }
}
