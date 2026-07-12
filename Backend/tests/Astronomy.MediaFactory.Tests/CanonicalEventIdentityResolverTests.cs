using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class CanonicalEventIdentityResolverTests
{
    [Fact]
    public void NamedFullMoonRequestPropagatesUnchangedToFamilyResolver()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new("NamedFullMoon", null, null, [], null));
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity);

        Assert.Equal("NamedFullMoon", identity.EventType);
        Assert.Equal("ProductionPipelineRequest.EventType", identity.ResolutionSource);
        Assert.Equal("NamedFullMoon", result.Resolved.ResolvedProfileId);
    }

    [Fact]
    public void WolfMoonResolvesFromEventTypeNotTitle()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new("NamedFullMoon", null, null, [], "Wolf Moon"));
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity);

        Assert.Equal("NamedFullMoon", identity.EventType);
        Assert.Equal("NamedFullMoon", result.Profile.FamilyId);
    }

    [Fact]
    public void DiagnosticsAndFamilyResolverUseSameCanonicalEventType()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new("NamedFullMoon", "MeteorShower", null, [], null));
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity);
        var diagnosticsJson = System.Text.Json.JsonSerializer.Serialize(CanonicalEventIdentityDiagnosticsBuilder.Build(identity, result));

        Assert.Equal("NamedFullMoon", identity.EventType);
        Assert.Equal("NamedFullMoon", result.Resolved.ResolvedProfileId);
        Assert.Contains("NamedFullMoon", diagnosticsJson);
        Assert.NotEmpty(identity.Conflicts);
    }

    [Fact]
    public void MissingNarrationDtoEventTypeDoesNotEraseRequestEventType()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new("NamedFullMoon", null, null, [], null));

        Assert.Equal("NamedFullMoon", identity.EventType);
        Assert.Empty(identity.BlockingErrors);
    }

    [Theory]
    [InlineData("PlanetPairing", "PlanetPairing")]
    [InlineData("MeteorShower", "MeteorShower")]
    [InlineData("SolarEclipse", "Eclipse")]
    public void SupportedTypesResolveFromCanonicalIdentity(string eventType, string expectedProfile)
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new(eventType, null, null, [], null));
        var result = AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity);

        Assert.Equal(expectedProfile, result.Resolved.ResolvedProfileId);
    }

    [Theory]
    [InlineData("PlanetGrouping")]
    [InlineData("UnsupportedCompletelyNewType")]
    public void UnsupportedTypesAreUnsupportedNotMissing(string eventType)
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new(eventType, null, null, [], null));
        var ex = Assert.Throws<InvalidOperationException>(() => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity));

        Assert.Contains("Unsupported astronomy event type: " + eventType, ex.Message);
        Assert.DoesNotContain("EventType = <missing>", ex.Message);
    }

    [Fact]
    public void TrulyAbsentTypeGivesSourceBySourceDiagnostics()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new(null, null, null, [], null));
        var ex = Assert.Throws<InvalidOperationException>(() => AstronomyFamilyProfileCatalog.ResolveFamilyProfile(identity));

        Assert.Contains("Canonical event identity missing", ex.Message);
        Assert.Contains("ProductionPipelineRequest.EventType=<missing>", ex.Message);
        Assert.Contains("ProductionEventIntelligence.EventType=<missing>", ex.Message);
    }

    [Fact]
    public void TitleBasedFamilyInferenceIsNotUsed()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new(null, null, null, [], "Wolf Moon"));

        Assert.Equal(string.Empty, identity.EventType);
        Assert.Contains("Canonical event identity missing", identity.BlockingErrors.Single());
    }

    [Fact]
    public void OneCanonicalResolverOwnsPrecedence()
    {
        var identity = CanonicalEventIdentityResolver.Resolve(new("PlanetPairing", "MeteorShower", "SolarEclipse", ["NamedFullMoon"], "FullMoon"));

        Assert.Equal("PlanetPairing", identity.EventType);
        Assert.Equal("ProductionPipelineRequest.EventType", identity.ResolutionSource);
        Assert.Equal(4, identity.Conflicts.Count);
    }
}
