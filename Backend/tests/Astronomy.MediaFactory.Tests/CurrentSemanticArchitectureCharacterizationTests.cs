using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Tests;

public sealed class CurrentNarrationV5DependencyCharacterizationTests
{
    [Fact]
    public void CurrentBehavior_NarrationGeneratorV5IsActivePhase7GeneratorAndKeepsStaticFallbacks()
    {
        var production = TestPaths.Source("Persistence", "ProductionPipelineExecutionService.cs");
        var generator = TestPaths.Source("Orchestration", "RC2", "NarrationGeneratorV5.cs");
        var productionSource = File.ReadAllText(production);
        var generatorSource = File.ReadAllText(generator);

        Assert.Contains("PhaseGenerateNarrationPlanAsync", productionSource);
        Assert.Contains("NarrationGeneratorV5", productionSource);
        Assert.Contains("BuildAndWriteDiagnosticsAsync", productionSource);
        Assert.Contains("new NarrationGeneratorV5", productionSource); // direct-construction fallback
        Assert.Contains("IRequiredSemanticFactResolver requiredSemanticFactResolver", generatorSource);
        Assert.Contains("INarrationRealizer narrationRealizer", generatorSource);
        Assert.Contains("IAstronomyFamilyProfileResolver familyProfileResolver", generatorSource);
        Assert.Contains("SemanticDefaults.RequiredSemanticFactResolver", generatorSource); // compatibility constructor
        Assert.Contains("SemanticDefaults.SemanticCapabilitySourceRegistry.ValidateCoverageDetailed", generatorSource);
    }

    [Fact]
    public void ExistingImplementation_PrimaryConstructorAcceptsInjectedSemanticServicesAndCompatibilityConstructorExists()
    {
        var constructors = typeof(NarrationGeneratorV5).GetConstructors();

        Assert.Contains(constructors, c => c.GetParameters().Any(p => p.ParameterType == typeof(IRequiredSemanticFactResolver))
            && c.GetParameters().Any(p => p.ParameterType == typeof(INarrationRealizer))
            && c.GetParameters().Any(p => p.ParameterType == typeof(IAstronomyFamilyProfileResolver)));
        Assert.Contains(constructors, c => c.GetParameters().Length >= 1
            && c.GetParameters()[0].ParameterType == typeof(ILogger<NarrationGeneratorV5>)
            && c.GetParameters().All(p => p.ParameterType != typeof(IRequiredSemanticFactResolver)));
    }
}

public sealed class CurrentAstronomyFamilyProfileCharacterizationTests
{
    [Theory]
    [InlineData("PlanetPairing", "PlanetPairing")]
    [InlineData("PlanetaryConjunction", "PlanetaryConjunction")]
    [InlineData("Occultation", "Occultation")]
    [InlineData("Eclipse", "Eclipse")]
    [InlineData("MeteorShower", "MeteorShower")]
    [InlineData("NamedFullMoon", "NamedFullMoon")]
    [InlineData("FullMoon", "FullMoon")]
    [InlineData("Constellation", "Constellation")]
    [InlineData("PlanetProfile", "PlanetProfile")]
    [InlineData("Comet", "Comet")]
    [InlineData("DeepSkyObject", "DeepSkyObject")]
    [InlineData("BlackHoleOrScientificExplainer", "BlackHoleOrScientificExplainer")]
    [InlineData("SolarEclipse", "Eclipse")]
    [InlineData("LunarEclipse", "Eclipse")]
    public void Characterizes_CurrentFamilyProfileMappings(string eventType, string expectedProfile)
        => Assert.Equal(expectedProfile, ResolveProfile(eventType).FamilyId);

    [Theory]
    [InlineData("PlanetGrouping", "Unsupported astronomy event type: PlanetGrouping")]
    [InlineData("Opposition", "Unsupported astronomy event type: Opposition")]
    [InlineData("Elongation", "Unsupported astronomy event type: Elongation")]
    [InlineData("Transit", "Unsupported astronomy event type: Transit")]
    [InlineData("LunarPhase", "Unsupported astronomy event type: LunarPhase")]
    public void CurrentBehavior_UnsupportedOrAbsentFamilyProfilesThrow(string eventType, string message)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ResolveProfile(eventType));
        Assert.Contains(message, ex.Message);
    }

    private static AstronomyFamilyProfile ResolveProfile(string eventType)
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(eventType, null, null, [], null));
        return AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity).Profile;
    }
}

public sealed class CurrentSemanticCapabilityCatalogCharacterizationTests
{
    private readonly SemanticCapabilityCatalog _catalog = new();

    [Theory]
    [InlineData("EventIdentity")]
    [InlineData("PrimaryObjects")]
    [InlineData("ObservationTiming")]
    [InlineData("AngularRelationship")]
    [InlineData("AngularSeparation")]
    [InlineData("ObservationMode")]
    [InlineData("VisibilityMethod")]
    [InlineData("Zhr")]
    public void Characterizes_DirectCanonicalRegistrations(string id)
    {
        Assert.True(_catalog.TryGet(id, out var definition));
        Assert.Equal(id, definition.CapabilityId);
    }

    [Theory]
    [InlineData("EventDateOrWindow", "EventDate")]
    [InlineData("AngularRelationship", "AngularRelationship")]
    [InlineData("AngularSeparation", "AngularSeparation")]
    [InlineData("ObservationMode", "ObservationMode")]
    [InlineData("VisibilityMethod", "VisibilityMethod")]
    [InlineData("LocationContext", "ObservationLocation")]
    [InlineData("ApparentPairingScience", "ApparentAlignmentExplanation")]
    [InlineData("ZHR", "Zhr")]
    [InlineData("ZenithalHourlyRate", "Zhr")]
    public void CurrentBehavior_AliasesResolveToCurrentCanonicalCapability(string alias, string expected)
    {
        Assert.True(_catalog.TryGet(alias, out var definition));
        Assert.Equal(expected, definition.CapabilityId);
    }

    [Fact]
    public void Characterizes_ReciprocalAliasesAndOverlap()
    {
        Assert.Contains(_catalog.GetRequired("AngularRelationship").AcceptedAliases, a => a == "AngularSeparation");
        Assert.Contains(_catalog.GetRequired("AngularSeparation").AcceptedAliases, a => a == "AngularRelationship");
        Assert.Contains(_catalog.GetRequired("ObservationMode").AcceptedAliases, a => a == "VisibilityMethod");
        Assert.Contains(_catalog.GetRequired("VisibilityMethod").AcceptedAliases, a => a == "ObservationMode");
        Assert.Contains(_catalog.Capabilities, c => c.CapabilityId == "Mechanism"); // generic-loop registration
    }

    [Theory]
    [InlineData("Duration")]
    [InlineData("ReappearanceTime")]
    [InlineData("Magnitude")]
    [InlineData("MoonriseTime")]
    [InlineData("CulturalNameContext")]
    [InlineData("Mythology")]
    [InlineData("BestSeason")]
    [InlineData("DeepSkyObjects")]
    [InlineData("Distance")]
    [InlineData("Moons")]
    [InlineData("Atmosphere")]
    [InlineData("Perihelion")]
    [InlineData("DiscoveryHistory")]
    [InlineData("ImagingNotes")]
    [InlineData("ObservationMethod")]
    public void CurrentBehavior_ProfileReferencedCapabilitiesAbsentFromCatalog(string id)
        => Assert.False(_catalog.TryGet(id, out _));
}

public sealed class CurrentSemanticSourceRegistryCharacterizationTests
{
    private readonly SemanticCapabilitySourceRegistry _registry = new(new SemanticCapabilityCatalog());

    [Theory]
    [InlineData("EventIdentity")]
    [InlineData("PrimaryObjects")]
    [InlineData("ObservationTiming")]
    [InlineData("ObservationDirection")]
    [InlineData("ObservationLocation")]
    [InlineData("Zhr")]
    public void Characterizes_ReachableAdapters(string capability)
        => Assert.NotEmpty(_registry.GetAdapters(capability));

    [Fact]
    public void CurrentBehavior_AdapterSupportedCapabilityMismatchesArePreserved()
    {
        Assert.Equal("AngularSeparation", Adapter("AngularSeparationAdapter").SupportedCapabilityId);
        Assert.Equal("VisibilityMethod", Adapter("VisibilityMethodAdapter").SupportedCapabilityId);
        Assert.Equal("ObservationTiming", Adapter("LocalPeakTimeObservationTimingAdapter").SupportedCapabilityId);
        Assert.Equal("ApparentAlignmentExplanation", Adapter("DomainKnowledgeApparentAlignmentAdapter").SupportedCapabilityId);
        Assert.NotEmpty(_registry.GetAdapters("AngularRelationship"));
        Assert.NotEmpty(_registry.GetAdapters("VisibilityConditions"));
        Assert.NotEmpty(_registry.GetAdapters("PhysicalProximityClarification"));
        Assert.Empty(_registry.GetAdapters("Mechanism"));
    }

    private ISemanticCapabilitySourceAdapter Adapter(string id) => Assert.Single(_registry.Adapters.Where(a => a.AdapterId == id));
}

public sealed class CurrentSemanticPolicyCharacterizationTests
{
    [Fact]
    public void CurrentBehavior_OptionalRegisteredNoValueIsCoverageValidButRuntimeOmitted()
    {
        var catalog = new SemanticCapabilityCatalog();
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"MeteorShower\"}"), null);
        var rows = new SemanticCapabilitySourceRegistry(catalog).ValidateCoverageDetailed([profile]).Where(r => r.Capability == "Zhr").ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.ResolutionPathValid));

        var result = new SemanticCapabilityResolver(catalog, new SemanticCapabilitySourceRegistry(catalog))
            .Resolve("Zhr", new SemanticCapabilitySourceContext("MeteorShower", "long", null, null, null, null, null, TestJson.Json("{\"eventType\":\"MeteorShower\"}"), null, null), LanguageProfileResolver.Resolve("en"));
        Assert.Equal("Unresolved", result.Status);
    }

    [Fact]
    public void CurrentBehavior_OptionalMissingFromCatalogReportsCapabilityNotRegistered()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"NamedFullMoon\"}"), null);
        var rows = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]);
        Assert.Contains(rows, r => r.Capability == "MoonriseTime" && !r.Required && r.FailureReason == "CapabilityNotRegistered");
        Assert.Throws<InvalidOperationException>(() => new SemanticCapabilityCatalog().GetRequired("MoonriseTime"));
    }

    [Fact]
    public void CurrentBehavior_RequiredNoSourcePathInvalidCoverageButRawScannerCanResolve()
    {
        var registry = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog());
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"Eclipse\"}"), null);
        Assert.Contains(registry.ValidateCoverageDetailed([profile]), r => r.Required && r.Capability == "EclipseType" && r.FailureReason == "NoApprovedSourceAvailable");

        var resolution = TestResolver.Resolve(profile, eventIntel: "{\"eventType\":\"Eclipse\",\"eclipseType\":\"partial solar eclipse\",\"visibilityRegion\":\"Americas\",\"safetyGuidance\":\"Use certified filters\",\"mechanism\":\"Moon shadow crosses Earth\",\"eventDateOrWindow\":\"2026-08-12\"}");
        Assert.Contains(resolution.Beats, b => b.RequiredFacts.Any(f => f.FactType == "EclipseType"));
    }

    [Fact]
    public void CurrentBehavior_MissingRequiredSemanticFactMakesPhase7ValidatorBlockingIssue()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"MeteorShower\"}"), null);
        var resolution = TestResolver.Resolve(profile, eventIntel: "{\"eventType\":\"MeteorShower\"}");
        var issues = RequiredSemanticFactPhase7Validator.Validate(resolution);
        Assert.Contains(issues, i => i.Field == "Radiant" || i.Field == "PeakWindow");
    }
}

public sealed class CurrentSemanticFallbackCharacterizationTests
{
    [Fact]
    public void Characterizes_RawJsonGenericAdapterZhrAndDomainFallbacks()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"PlanetPairing\"}"), null);
        var resolution = TestResolver.Resolve(profile,
            eventIntel: "{\"eventType\":\"PlanetPairing\",\"details\":{\"objectPair\":[\"Venus\",\"Jupiter\"],\"nested\":{\"expectedZhr\":42}}}",
            observation: "{\"outer\":{\"direction\":\"western sky\",\"localPeakTime\":\"after sunset\",\"location\":\"global\"}}");

        Assert.Contains(resolution.Beats.SelectMany(b => b.RequiredFacts), f => f.FactType == "PhysicalProximityClarification" && f.FactOrigin == "Source" || f.FactType == "PhysicalProximityClarification");
        var zhr = new ZhrAdapter();
        Assert.True(zhr.TryExtract(new SemanticCapabilitySourceContext("MeteorShower", "long", null, null, null, null, null, TestJson.Json("{\"deep\":{\"expectedZhr\":120}}"), null, null), out var candidate, out _));
        Assert.Equal("deep.expectedZhr", candidate.SourceField);

        var source = File.ReadAllText(TestPaths.Source("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        Assert.Contains("AddJsonFacts", source);
        Assert.Contains("private static bool TryDerive", source);
        Assert.Contains("return false;", source.Substring(source.IndexOf("private static bool TryDerive", StringComparison.Ordinal)));
    }
}

public sealed class CurrentKnownSemanticFailureCharacterizationTests
{
    [Fact]
    public void CurrentBehavior_NamedFullMoonCoverageFailsForExactMissingCatalogCapabilities()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"NamedFullMoon\"}"), null);
        var invalid = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]).Where(r => !r.ResolutionPathValid).ToArray();
        Assert.Contains(invalid, r => r.Capability == "MoonriseTime" && r.FailureReason == "CapabilityNotRegistered");
        Assert.Contains(invalid, r => r.Capability == "CulturalNameContext" && r.FailureReason == "CapabilityNotRegistered");
    }

    [Fact]
    public void CurrentBehavior_PlanetGroupingAliasMapsButProfileIsAbsent()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput("PLANET_GROUPING", null, null, [], null));
        Assert.Equal("PlanetGrouping", identity.EventType);
        var ex = Assert.Throws<InvalidOperationException>(() => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity));
        Assert.Contains("Unsupported astronomy event type: PlanetGrouping", ex.Message);
    }

    [Fact]
    public void CurrentBehavior_SolarEclipseMapsToGenericEclipseAndGenericEclipseHasSourceGaps()
    {
        Assert.Equal("Eclipse", AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"SolarEclipse\"}"), null).FamilyId);
        var rows = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"Eclipse\"}"), null)]);
        Assert.Contains(rows, r => r.Capability == "EclipseType" && r.FailureReason == "NoApprovedSourceAvailable");
        Assert.Contains(rows, r => r.Capability == "SafetyGuidance" && r.FailureReason == "NoApprovedSourceAvailable");
        Assert.Contains(rows, r => r.Capability == "Mechanism" && r.FailureReason == "NoApprovedSourceAvailable");
    }
}

public sealed class CurrentCrossFamilyPhase7CharacterizationTests
{
    [Theory]
    [InlineData("PlanetPairing", "{\"eventType\":\"PlanetPairing\",\"objectPair\":[\"Mars\",\"Jupiter\"],\"apparentPairingScience\":\"line of sight\"}", "{\"direction\":\"east\",\"localPeakTime\":\"dawn\",\"location\":\"global\"}")]
    [InlineData("MeteorShower", "{\"eventType\":\"MeteorShower\",\"name\":\"Geminids\",\"eventDateOrWindow\":\"mid December\",\"radiant\":\"Gemini\",\"peakWindow\":\"pre-dawn\"}", "{\"direction\":\"northeast\"}")]
    public void Characterizes_StructurallySuccessfulFamilies(string eventType, string intel, string observation)
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new CanonicalEventIdentityResolutionInput(eventType, null, null, [], null));
        var profile = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity).Profile;
        var coverage = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]);
        var resolution = TestResolver.Resolve(profile, intel, observation);
        var issues = RequiredSemanticFactPhase7Validator.Validate(resolution);

        Assert.Equal(eventType, identity.EventType);
        Assert.Equal(eventType, profile.FamilyId);
        Assert.NotEmpty(coverage);
        Assert.NotEmpty(resolution.Beats);
        Assert.Empty(issues);
        Assert.Equal(resolution.Beats.Count, resolution.Beats.Select(b => (b.Format, b.DocumentaryBeatId)).Distinct().Count());
    }
}

public sealed class CurrentSemanticLanguageParityCharacterizationTests
{
    [Theory]
    [InlineData("PlanetPairing")]
    [InlineData("MeteorShower")]
    public void Characterizes_EnglishHindiResolveSameProfileAndRequirementStructure(string eventType)
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json($"{{\"eventType\":\"{eventType}\"}}"), null);
        var en = TestResolver.Resolve(profile, language: "en");
        var hi = TestResolver.Resolve(profile, language: "hi");
        Assert.Equal(en.Beats.Select(b => (b.Format, b.NarrativeRole, Required: string.Join(",", b.MissingRequiredFacts.Order()))), hi.Beats.Select(b => (b.Format, b.NarrativeRole, Required: string.Join(",", b.MissingRequiredFacts.Order()))));
    }

    [Fact]
    public void CurrentBehavior_NamedFullMoonEnglishHindiFailForSameCatalogReasons()
    {
        var profile = AstronomyFamilyProfileCatalog.Resolve(TestJson.Json("{\"eventType\":\"NamedFullMoon\"}"), null);
        var rows = new SemanticCapabilitySourceRegistry(new SemanticCapabilityCatalog()).ValidateCoverageDetailed([profile]).Where(r => !r.ResolutionPathValid).Select(r => r.Capability).Order().ToArray();
        Assert.Contains("MoonriseTime", rows);
        Assert.Contains("CulturalNameContext", rows);
    }
}

public sealed class CurrentSemanticArchitectureStaticCharacterizationTests
{
    [Fact]
    public void ExistingImplementation_StaticMigrationBaselineIsColocatedAndUsesDefaults()
    {
        var source = File.ReadAllText(TestPaths.Source("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        foreach (var token in new[] { "CanonicalEventIdentityResolver", "AstronomyFamilyProfileCatalog", "RequiredSemanticFactResolver", "AstronomyDomainKnowledgeProvider", "NarrationRealizer", "RequiredSemanticFactPhase7Validator" })
            Assert.Contains(token, source);
        Assert.True(File.Exists(TestPaths.Source("Production", "Narration", "Semantics", "SemanticDefaults.cs")));
        Assert.Contains("AddJsonFacts", source);
        Assert.Contains("TryDerive", source);
        Assert.Contains("SemanticDefaults.SemanticCapabilitySourceRegistry", source);
        Assert.Contains("new NarrationGeneratorV5", File.ReadAllText(TestPaths.Source("Persistence", "ProductionPipelineExecutionService.cs")));
    }
}

internal static class TestResolver
{
    public static RequiredSemanticFactResolutionResult Resolve(AstronomyFamilyProfile profile, string? eventIntel = null, string? observation = null, string language = "en")
        => new RequiredSemanticFactResolver().Resolve(new RequiredSemanticFactResolutionInput(
            profile,
            TestJson.Json("{\"beats\":[{\"beatOrder\":1,\"narrativeRole\":\"Hook\",\"documentaryBeatId\":\"long-hook\"},{\"beatOrder\":2,\"narrativeRole\":\"Orientation\",\"documentaryBeatId\":\"long-orientation\"},{\"beatOrder\":3,\"narrativeRole\":\"Timing\",\"documentaryBeatId\":\"long-timing\"},{\"beatOrder\":4,\"narrativeRole\":\"Science\",\"documentaryBeatId\":\"long-science\"},{\"beatOrder\":5,\"narrativeRole\":\"Observation\",\"documentaryBeatId\":\"long-observation\"}]}"),
            TestJson.Json("{\"beats\":[{\"beatOrder\":1,\"narrativeRole\":\"Hook\",\"documentaryBeatId\":\"short-hook\"},{\"beatOrder\":2,\"narrativeRole\":\"Timing\",\"documentaryBeatId\":\"short-timing\"},{\"beatOrder\":3,\"narrativeRole\":\"Science\",\"documentaryBeatId\":\"short-science\"}]}"),
            null,
            null,
            TestJson.Json(eventIntel ?? "{\"eventType\":\"PlanetPairing\",\"objectPair\":[\"Mars\",\"Jupiter\"],\"eventDateOrWindow\":\"tonight\",\"radiant\":\"Gemini\",\"peakWindow\":\"pre-dawn\",\"name\":\"Geminids\",\"apparentPairingScience\":\"line of sight\",\"physicalProximityClarification\":\"not physically close\"}"),
            TestJson.Json(observation ?? "{\"direction\":\"east\",\"localPeakTime\":\"dawn\",\"location\":\"global\"}"),
            null,
            LanguageProfileResolver.Resolve(language)));
}

internal static class TestJson
{
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

internal static class TestPaths
{
    public static string Source(params string[] parts)
        => Path.Combine(new[] { "..", "..", "..", "..", "src", "Astronomy.MediaFactory.Infrastructure" }.Concat(parts).ToArray());
}
