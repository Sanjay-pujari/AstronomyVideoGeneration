using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CanonicalEventDispatchV1Tests
{
    private static readonly CanonicalAstronomyEventIdentityResolverV1 IdentityResolver = new();
    private static readonly AstronomyFamilyProfileResolver FamilyResolver = new(IdentityResolver, new AstronomyFamilyProfileCatalogV1(), new AstronomyFamilyProfileV1CompatibilityAdapter());

    [Fact]
    public void PlanetConjunctionAliasResolvesToActiveCanonicalPlanetPairing()
    {
        var identity = IdentityResolver.Resolve("PLANET_CONJUNCTION", "Phase7Benchmark");
        var family = FamilyResolver.ResolveFamilyProfile(ToLegacyIdentity(identity));

        Assert.True(identity.Supported);
        Assert.Equal("PlanetPairing", identity.CanonicalEventType);
        Assert.Equal("PlanetPairing", identity.CanonicalFamily);
        Assert.Equal("PlanetPairing", family.Profile.FamilyId);
    }

    [Fact]
    public void Phase7ResolverDoesNotThrowUnsupportedForPlanetConjunctionAlias()
    {
        var ex = Record.Exception(() => FamilyResolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("PLANET_CONJUNCTION", null, null, null)));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("PlanetConjunction")]
    [InlineData("PLANET_CONJUNCTION")]
    [InlineData("PlanetaryConjunction")]
    public void ApprovedPlanetPairingAliasesResolveIdentically(string eventType)
    {
        var identity = IdentityResolver.Resolve(eventType);
        var family = FamilyResolver.ResolveFamilyProfile(ToLegacyIdentity(identity));
        Assert.Equal("PlanetPairing", identity.CanonicalEventType);
        Assert.Equal("PlanetPairing", family.Profile.FamilyId);
    }

    [Theory]
    [InlineData("CONSTELLATION")]
    [InlineData("Constellation")]
    [InlineData("constellation")]
    public void Phase7AcceptsConstellationAliasesAndResolvesToConstellationProfile(string eventType)
    {
        var identity = IdentityResolver.Resolve(eventType, "Phase7ConstellationBoundary");
        var family = FamilyResolver.ResolveFamilyProfile(ToLegacyIdentity(identity));

        Assert.True(identity.Supported);
        Assert.Equal("Constellation", identity.CanonicalEventType);
        Assert.Equal("Constellation", identity.CanonicalFamily);
        Assert.Equal("Constellation", family.Profile.FamilyId);
        Assert.Equal("ConstellationShort", family.Profile.PreferredShortArchetype);
        Assert.Equal("No event date required.", family.Profile.TimingRequirements);
        Assert.Contains("Name", family.Profile.RequiredFactTypes);
        Assert.Contains("ScientificIdentity", family.Profile.RequiredFactTypes);
        Assert.DoesNotContain("EventDateOrWindow", family.Profile.RequiredFactTypes);
        Assert.DoesNotContain("LocalPeakTime", family.Profile.RequiredFactTypes);
    }

    [Theory]
    [InlineData("PLANET_CONJUNCTION", "PlanetPairing")]
    public void ConfirmedProductionAliasesResolve(string eventType, string expectedFamily)
    {
        var identity = IdentityResolver.Resolve(eventType);
        var family = FamilyResolver.ResolveFamilyProfile(ToLegacyIdentity(identity));
        Assert.True(identity.Supported);
        Assert.Equal(expectedFamily, identity.CanonicalEventType);
        Assert.Equal(expectedFamily, family.Profile.FamilyId);
    }

    [Fact]
    public void UnknownRawEventTypeReturnsPreciseTypedDiagnostic()
    {
        var identity = IdentityResolver.Resolve("UNKNOWN_SKY_EVENT", "Phase7UnitTest");
        Assert.False(identity.Supported);
        Assert.Null(identity.CanonicalEventType);
        Assert.Contains("Unsupported astronomy event type 'UNKNOWN_SKY_EVENT'.", identity.DiagnosticMessages);
    }

    [Fact]
    public void NarrationGeneratorV5ContainsNoRawAliasSwitch()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var dispatchStart = source.IndexOf("CanonicalEventIdentityResolver.Resolve", StringComparison.Ordinal);
        var dispatchEnd = source.IndexOf("familyProfileResolver.ResolveFamilyProfile", dispatchStart, StringComparison.Ordinal);
        var dispatchSource = source[dispatchStart..dispatchEnd];
        Assert.DoesNotContain("switch", dispatchSource);
        Assert.DoesNotContain("PLANET_CONJUNCTION", dispatchSource);
        Assert.DoesNotContain("METEOR_SHOWER", dispatchSource);
        Assert.DoesNotContain("SOLAR_ECLIPSE", dispatchSource);
        Assert.Contains("CanonicalEventIdentityResolver.Resolve", source);
        Assert.Contains("familyProfileResolver.ResolveFamilyProfile", source);
        var identityResolverSource = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Production", "Narration", "Semantics", "Identity", "CanonicalAstronomyEventIdentityResolverV1.cs"));
        Assert.Contains("AstronomyEventAliasCatalogV1", identityResolverSource);
        Assert.DoesNotContain("PLANET_CONJUNCTION", source);
    }

    [Fact]
    public void ProductionPipelineExecutionServiceDoesNotDuplicateProductionEventAliases()
    {
        var source = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Persistence", "ProductionPipelineExecutionService.cs"));
        var phase7Start = source.IndexOf("private async Task<IReadOnlyList<string>> PhaseGenerateNarrationPlanAsync", StringComparison.Ordinal);
        var phase7End = source.IndexOf("private static (BatchGenerateFromPlansRequest Request, BatchGenerateFromPlansResponse Response) BuildRc2OverlayRequestResponse", phase7Start, StringComparison.Ordinal);
        var phase7Source = source[phase7Start..phase7End];
        Assert.DoesNotContain("PLANET_CONJUNCTION", phase7Source);
        Assert.DoesNotContain("METEOR_SHOWER", phase7Source);
        Assert.DoesNotContain("SOLAR_ECLIPSE", phase7Source);
    }

    [Fact]
    public void Phase7ConsumesFamilyProfileResolverThroughDependencyInjection()
    {
        var generatorSource = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        var servicesSource = File.ReadAllText(RepositoryTestPaths.InfrastructureSource("Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Contains("IAstronomyFamilyProfileResolver familyProfileResolver", generatorSource);
        Assert.Contains("AddScoped<IAstronomyFamilyProfileResolver, AstronomyFamilyProfileResolver>", servicesSource);
    }

    [Fact]
    public void JupiterVenusBenchmarkClearsFamilyProfileResolution()
    {
        var identity = IdentityResolver.Resolve("PLANET_CONJUNCTION", "JupiterVenusBenchmarkFixture");
        var family = FamilyResolver.ResolveFamilyProfile(ToLegacyIdentity(identity));
        Assert.Equal("PlanetPairing", identity.CanonicalEventType);
        Assert.Equal("PlanetPairing", family.Resolved.CanonicalFamilyId);
    }

    [Fact]
    public void FamilyResolverUsesCanonicalEventTypeNotRawSourceType()
    {
        var identity = IdentityResolver.Resolve("PLANET_CONJUNCTION", "CanonicalResolverUnitTest");
        var legacy = ToLegacyIdentity(identity);

        var family = FamilyResolver.ResolveFamilyProfile(legacy);

        Assert.Equal("PlanetPairing", legacy.EventType);
        Assert.Equal("PLANET_CONJUNCTION", legacy.SourceEventType);
        Assert.Equal("PlanetPairing", family.Profile.FamilyId);
        Assert.Equal("PlanetPairing", family.Resolved.CanonicalFamilyId);
    }

    [Fact]
    public void CompatibilityAdapterPreservesRawSourceSeparatelyFromCanonicalIdentity()
    {
        var planetIdentity = IdentityResolver.Resolve("PLANET_CONJUNCTION", "AdapterUnitTest");
        var planetLegacy = ToLegacyIdentity(planetIdentity);
        Assert.Equal("PlanetPairing", planetLegacy.EventType);
        Assert.Equal("PLANET_CONJUNCTION", planetLegacy.SourceEventType);
        Assert.Equal("PlanetPairing", planetLegacy.EventFamily);
        Assert.Equal("PlanetPairing", planetLegacy.StrategyId);
        Assert.True(planetLegacy.AliasApplied);

        var constellationIdentity = IdentityResolver.Resolve("CONSTELLATION", "AdapterUnitTest");
        var constellationLegacy = ToLegacyIdentity(constellationIdentity);
        Assert.Equal("Constellation", constellationLegacy.EventType);
        Assert.Equal("CONSTELLATION", constellationLegacy.SourceEventType);
        Assert.Equal("Constellation", constellationLegacy.EventFamily);
        Assert.Equal("Constellation", constellationLegacy.StrategyId);
        Assert.False(constellationLegacy.AliasApplied);
    }

    private static CanonicalEventIdentity ToLegacyIdentity(CanonicalAstronomyEventIdentity identity) => new(
        identity.CanonicalEventType ?? identity.InputEventType ?? string.Empty,
        identity.CanonicalFamily,
        identity.CanonicalProfile,
        identity.InputEventType,
        identity.CanonicalEventType ?? identity.InputEventType ?? string.Empty,
        identity.ResolutionSource,
        identity.AppliedAliases.Count > 0,
        new Dictionary<string, string?> { ["UnitTest.EventType"] = identity.InputEventType },
        [],
        [],
        identity.Supported ? [] : identity.DiagnosticMessages);
}
