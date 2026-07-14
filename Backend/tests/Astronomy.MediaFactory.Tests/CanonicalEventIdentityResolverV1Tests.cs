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
    public void RuntimeUsesV1EventIdentityOnlyThroughApprovedServices()
    {
        var root = Path.Combine(RepositoryTestPaths.Root(), "Backend", "src", "Astronomy.MediaFactory.Infrastructure");

        Assert.True(Directory.Exists(root), $"Expected infrastructure source root to exist at '{root}'.");

        var serviceCollection = File.ReadAllText(Path.Combine(root, "Extensions", "ServiceCollectionExtensions.cs"));
        Assert.Contains("AddSingleton<ICanonicalAstronomyEventIdentityResolverV1, CanonicalAstronomyEventIdentityResolverV1>", serviceCollection);

        var familyResolver = File.ReadAllText(Path.Combine(root, "Production", "Narration", "Semantics", "AstronomyFamilyProfileResolver.cs"));
        Assert.Contains("ICanonicalAstronomyEventIdentityResolverV1", familyResolver);

        var approvedInstantiationFiles = new[]
        {
            Path.Combine(root, "Extensions", "ServiceCollectionExtensions.cs"),
            Path.Combine(root, "Production", "Narration", "Semantics", "AstronomyFamilyProfileResolver.cs")
        }.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtimeFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).ToArray();

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            var relativePath = Path.GetRelativePath(root, runtimeFile);

            Assert.False(
                source.Contains("new CanonicalAstronomyEventIdentityResolverV1", StringComparison.Ordinal) && !approvedInstantiationFiles.Contains(Path.GetFullPath(runtimeFile)),
                $"Production source file '{relativePath}' must not directly instantiate CanonicalAstronomyEventIdentityResolverV1 outside approved semantic bootstrap/resolver code.");
            Assert.False(
                source.Contains("AstronomyEventAliasCatalogV1", StringComparison.Ordinal) && !relativePath.StartsWith(Path.Combine("Production", "Narration", "Semantics", "Identity"), StringComparison.Ordinal),
                $"Production source file '{relativePath}' must not reference AstronomyEventAliasCatalogV1.");
        }

        var narrationGenerator = File.ReadAllText(Path.Combine(root, "Orchestration", "RC2", "NarrationGeneratorV5.cs"));
        Assert.DoesNotContain("AstronomyEventAliasCatalogV1", narrationGenerator);
        Assert.DoesNotContain("CanonicalAstronomyEventIdentityResolverV1", narrationGenerator);

        var pipeline = File.ReadAllText(Path.Combine(root, "Persistence", "ProductionPipelineExecutionService.cs"));
        Assert.DoesNotContain("AstronomyEventAliasCatalogV1", pipeline);
    }

    private static bool IsInDirectory(string directory, string file)
    {
        var relativePath = Path.GetRelativePath(directory, file);

        return relativePath == "."
            || (!relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                && relativePath != "..");
    }

}
