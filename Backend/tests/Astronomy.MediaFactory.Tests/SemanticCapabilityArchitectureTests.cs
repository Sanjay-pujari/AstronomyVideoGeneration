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


    [Fact]
    public void BinocularGuidanceIsClassifiedAsObservationModeAlias()
    {
        var catalog = new SemanticCapabilityCatalog();
        Assert.True(catalog.TryGet("BinocularGuidance", out var definition));
        Assert.Equal("ObservationMode", definition.CapabilityId);
        Assert.Contains("BinocularGuidance", definition.AcceptedAliases);
        Assert.DoesNotContain(catalog.Capabilities, c => c.CapabilityId == "BinocularGuidance");
    }

    [Fact]
    public void PlanetPairingCoverageEnumeratesFormatsRolesAndHasNoZeroPathCapabilities()
    {
        var catalog = new SemanticCapabilityCatalog();
        var registry = new SemanticCapabilitySourceRegistry(catalog);
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"PlanetPairing\"}"), null);

        var rows = registry.ValidateCoverageDetailed([profile]);

        Assert.Contains(rows, r => r.FamilyProfile == "PlanetPairing" && r.Format == "long" && r.BeatRole == "Observation" && r.Capability == "ObservationTiming" && r.Required);
        Assert.Contains(rows, r => r.Capability == "BinocularGuidance" && !r.Required && r.CatalogRegistrationFound && r.ResolutionPathValid);
        Assert.Empty(rows.Where(r => r.Required && !r.ResolutionPathValid));
        Assert.Empty(rows.Where(r => r.CatalogRegistrationFound && r.RegisteredAdapterIds.Count == 0 && r.ApprovedDerivationRuleIds.Count == 0 && r.ApprovedDomainProviderIds.Count == 0));
    }

    [Fact]
    public void RequiredCapabilityWithNoResolutionPathFailsCoverageAndAllInvalidsAreReportedTogether()
    {
        var catalog = new SemanticCapabilityCatalog();
        var registry = new SemanticCapabilitySourceRegistry(catalog);
        var profile = new AstronomyFamilyProfile("Synthetic", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["MissingRequiredA", "MissingRequiredB"], [], ["Hook"], ["Hook"], "", "", [], [], []);

        var rows = registry.ValidateCoverageDetailed([profile]).Where(r => !r.ResolutionPathValid).ToArray();

        Assert.Contains(rows, r => r.Capability == "MissingRequiredA" && r.FailureReason == "CatalogRegistrationMissing");
        Assert.Contains(rows, r => r.Capability == "MissingRequiredB" && r.FailureReason == "CatalogRegistrationMissing");
        Assert.True(rows.Length >= 2);
    }

    [Fact]
    public void SemanticSourceContainsNoMarsJupiterOrTitleSpecificResolverConditions()
    {
        var root = Path.Combine("..", "..", "..", "..", "src", "Astronomy.MediaFactory.Infrastructure");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains(Path.Combine("Narration")) || f.Contains(Path.Combine("Orchestration", "RC2")))
            .ToArray();
        var source = string.Join("\n", files.Select(File.ReadAllText));
        Assert.DoesNotContain("Mars and Jupiter", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close Pairing", source, StringComparison.OrdinalIgnoreCase);
    }

    private static ISemanticCapabilityResolver BuildResolver()
    {
        var catalog = new SemanticCapabilityCatalog();
        return new SemanticCapabilityResolver(catalog, new SemanticCapabilitySourceRegistry(catalog));
    }
}
