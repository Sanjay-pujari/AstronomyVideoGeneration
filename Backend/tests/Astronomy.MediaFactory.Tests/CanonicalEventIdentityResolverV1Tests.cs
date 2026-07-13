using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Tests;

public sealed class CanonicalEventIdentityResolverV1Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> CanonicalIds => new()
    {
        "PlanetPairing", "PlanetGrouping", "MeteorShower", "FullMoon", "NamedFullMoon",
        "SolarEclipse", "LunarEclipse", "Occultation", "Constellation", "DeepSkyObject"
    };

    [Theory]
    [MemberData(nameof(CanonicalIds))]
    public void EveryCanonicalIdResolvesToItself(string canonicalId)
    {
        var identity = new CanonicalAstronomyEventIdentityResolverV1().Resolve(canonicalId);

        Assert.True(identity.Supported);
        Assert.Equal(canonicalId, identity.CanonicalEventType);
        Assert.Empty(identity.AppliedAliases);
    }

    [Theory]
    [InlineData("PlanetaryConjunction", "PlanetPairing")]
    [InlineData("Solar Eclipse", "SolarEclipse")]
    [InlineData("Lunar Eclipse", "LunarEclipse")]
    [InlineData("Meteor Shower", "MeteorShower")]
    [InlineData("Full Moon", "FullMoon")]
    [InlineData("Named Full Moon", "NamedFullMoon")]
    [InlineData("Deep Sky Object", "DeepSkyObject")]
    public void ApprovedAliasesResolveToCanonicalTypes(string alias, string expectedCanonical)
    {
        var identity = new CanonicalAstronomyEventIdentityResolverV1().Resolve(alias);

        Assert.True(identity.Supported);
        Assert.Equal(expectedCanonical, identity.CanonicalEventType);
        Assert.Equal([alias], identity.AppliedAliases);
    }

    [Fact]
    public void UnknownEventIsUnsupported()
    {
        var identity = new CanonicalAstronomyEventIdentityResolverV1().Resolve("Unknown event");

        Assert.False(identity.Supported);
        Assert.Null(identity.CanonicalEventType);
        Assert.Null(identity.CanonicalFamily);
        Assert.Contains("Unsupported astronomy event type 'Unknown event'.", identity.DiagnosticMessages);
    }

    [Fact]
    public void ResolverIsDeterministic()
    {
        var resolver = new CanonicalAstronomyEventIdentityResolverV1();

        var first = resolver.Resolve("Solar Eclipse", "TestSource");
        var second = resolver.Resolve("Solar Eclipse", "TestSource");

        Assert.Equal(JsonSerializer.Serialize(first, JsonOptions), JsonSerializer.Serialize(second, JsonOptions));
    }

    [Fact]
    public void DiagnosticsSerializeCorrectly()
    {
        var identity = new CanonicalAstronomyEventIdentityResolverV1().Resolve("Meteor Shower", "UnitTest");
        var diagnostics = CanonicalEventIdentityDiagnosticsV1.FromIdentity(identity);

        var json = JsonSerializer.Serialize(diagnostics, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<CanonicalEventIdentityDiagnosticsV1>(json, JsonOptions);

        Assert.Contains("\"canonicalEventType\":\"MeteorShower\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(diagnostics.CanonicalEventType, roundTrip!.CanonicalEventType);
        Assert.Equal(diagnostics.AppliedAliases, roundTrip.AppliedAliases);
        Assert.Equal(diagnostics.DiagnosticMessages, roundTrip.DiagnosticMessages);
    }

    [Fact]
    public void IdentityContractsRoundTrip()
    {
        var identity = new CanonicalAstronomyEventIdentityResolverV1().Resolve("Named Full Moon", "UnitTest");

        var json = JsonSerializer.Serialize(identity, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<CanonicalAstronomyEventIdentity>(json, JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal(identity.InputEventType, roundTrip!.InputEventType);
        Assert.Equal(identity.CanonicalEventType, roundTrip.CanonicalEventType);
        Assert.Equal(identity.CanonicalFamily, roundTrip.CanonicalFamily);
        Assert.Equal(identity.CanonicalProfile, roundTrip.CanonicalProfile);
        Assert.Equal(identity.AppliedAliases, roundTrip.AppliedAliases);
        Assert.Equal(identity.DiagnosticMessages, roundTrip.DiagnosticMessages);
    }

    [Fact]
    public void CurrentRuntimeDoesNotReferenceV1ResolverOrAliasCatalog()
    {
        var root = Path.Combine("..", "..", "..", "..", "src", "Astronomy.MediaFactory.Infrastructure");
        var runtimeFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.Combine("Production", "Narration", "Semantics", "Identity")))
            .ToArray();
        var source = string.Join('\n', runtimeFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("CanonicalAstronomyEventIdentityResolverV1", source);
        Assert.DoesNotContain("AstronomyEventAliasCatalogV1", source);
    }
}
