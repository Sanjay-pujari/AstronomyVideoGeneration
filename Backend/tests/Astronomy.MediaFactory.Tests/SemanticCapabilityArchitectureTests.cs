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

        Assert.Contains(rows, r => r.Capability == "MissingRequiredA" && r.FailureReason == "CapabilityNotRegistered");
        Assert.Contains(rows, r => r.Capability == "MissingRequiredB" && r.FailureReason == "CapabilityNotRegistered");
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


    [Fact]
    public void ZhrExistsInCatalogAndAliasesResolveCanonicalCapability()
    {
        var catalog = new SemanticCapabilityCatalog();

        Assert.True(catalog.TryGet("Zhr", out var zhr));
        Assert.Equal("Zhr", zhr.CapabilityId);
        Assert.True(zhr.Localizable);
        Assert.True(zhr.Narratable);
        Assert.True(zhr.EventSpecific);
        Assert.Equal("OptionalEventSpecific", zhr.Strictness);
        Assert.Contains("ZHR", zhr.AcceptedAliases);
        Assert.True(catalog.TryGet("ZHR", out var alias));
        Assert.Equal("Zhr", alias.CapabilityId);
        Assert.True(catalog.TryGet("ZenithalHourlyRate", out alias));
        Assert.Equal("Zhr", alias.CapabilityId);
        Assert.True(catalog.TryGet("Zenithal Hourly Rate", out alias));
        Assert.Equal("Zhr", alias.CapabilityId);
    }

    [Fact]
    public void MeteorShowerProfileReferencesCanonicalOptionalZhr()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"MeteorShower\"}"), null);

        Assert.Contains("Zhr", profile.OptionalFactTypes);
        Assert.DoesNotContain(profile.RequiredFactTypes, f => f.Equals("Zhr", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(profile.OptionalFactTypes, f => f == "ZHR" || f == "ZenithalHourlyRate" || f == "Zenithal Hourly Rate");
    }

    [Fact]
    public void MeteorShowerCoverageTreatsOptionalZhrWithoutCurrentCandidateAsValid()
    {
        var catalog = new SemanticCapabilityCatalog();
        var registry = new SemanticCapabilitySourceRegistry(catalog);
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"MeteorShower\"}"), null);

        var rows = registry.ValidateCoverageDetailed([profile]).Where(r => r.Capability == "Zhr").ToArray();

        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.False(r.Required);
            Assert.True(r.CatalogRegistrationFound);
            Assert.True(r.ResolutionPathValid);
            Assert.Null(r.FailureReason);
        });
        Assert.Empty(registry.ValidateCoverage([profile]));
    }

    [Fact]
    public void RequiredZhrWithNoResolutionPathBlocksCoverage()
    {
        var registry = new SemanticCapabilitySourceRegistry(new CatalogWithZhrWithoutSources());
        var profile = new AstronomyFamilyProfile("Synthetic", "TimedObservationEvent", "ObservationExplainer", "SkyWatchShort", ["Zhr"], [], ["Hook"], ["Hook"], "", "", [], [], []);

        var row = Assert.Single(registry.ValidateCoverageDetailed([profile]));

        Assert.True(row.Required);
        Assert.False(row.ResolutionPathValid);
        Assert.Equal("NoApprovedSourceAvailable", row.FailureReason);
    }

    [Fact]
    public void RegistryReportAndRuntimeAgreeForRegisteredOptionalZhr()
    {
        var catalog = new SemanticCapabilityCatalog();
        var registry = new SemanticCapabilitySourceRegistry(catalog);
        var profile = AstronomyFamilyProfileCatalog.Resolve(Json("{\"family\":\"MeteorShower\"}"), null);
        var resolver = new SemanticCapabilityResolver(catalog, registry);

        var reportValid = registry.ValidateCoverageDetailed([profile]).Where(r => r.Capability == "Zhr").All(r => r.ResolutionPathValid);
        var runtime = resolver.Resolve("Zhr", new SemanticCapabilitySourceContext("MeteorShower", "long", null, null, null, null, null, Json("{\"eventTitle\":\"Geminids Meteor Shower Peak\"}"), null, null), English);

        Assert.True(reportValid);
        Assert.Equal("Unresolved", runtime.Status);
        Assert.Null(runtime.SelectedSource);
        Assert.Contains(runtime.Warnings, w => w.Contains("SourceValueMissing"));
    }

    private sealed class CatalogWithZhrWithoutSources : ISemanticCapabilityCatalog
    {
        private readonly SemanticCapabilityDefinition _zhr = new("Zhr", ["Zhr", "ZHR", "ZenithalHourlyRate", "Zenithal Hourly Rate"], 75, "OptionalEventSpecific", true, true, [], [], [], true);
        public IReadOnlyList<SemanticCapabilityDefinition> Capabilities => [_zhr];
        public SemanticCapabilityDefinition GetRequired(string capabilityId) => TryGet(capabilityId, out var definition) ? definition : throw new InvalidOperationException($"Capability registration invalid: Capability = {capabilityId}");
        public bool TryGet(string capabilityId, out SemanticCapabilityDefinition definition) { definition = _zhr; return capabilityId.Equals("Zhr", StringComparison.OrdinalIgnoreCase) || _zhr.AcceptedAliases.Contains(capabilityId, StringComparer.OrdinalIgnoreCase); }
        public void Validate() { }
    }

    private static ISemanticCapabilityResolver BuildResolver()
    {
        var catalog = new SemanticCapabilityCatalog();
        return new SemanticCapabilityResolver(catalog, new SemanticCapabilitySourceRegistry(catalog));
    }
}
