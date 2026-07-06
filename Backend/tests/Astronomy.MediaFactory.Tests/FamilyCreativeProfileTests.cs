using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.VisualIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using ContractEventFamily = Astronomy.MediaFactory.Contracts.EventFamily;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyCreativeProfileTests
{
    [Theory]
    [InlineData(ContractEventFamily.PlanetConjunction, "planet-pairing", typeof(PlanetPairingCreativeProfile))]
    [InlineData(ContractEventFamily.PlanetConjunction, "planet-grouping", typeof(PlanetGroupingCreativeProfile))]
    [InlineData(ContractEventFamily.MeteorShower, "meteor-shower", typeof(MeteorShowerCreativeProfile))]
    [InlineData(ContractEventFamily.LunarEvent, "named-full-moon", typeof(NamedFullMoonCreativeProfile))]
    [InlineData(ContractEventFamily.SolarEvent, "solar-eclipse", typeof(SolarEclipseCreativeProfile))]
    [InlineData(ContractEventFamily.LunarEvent, "lunar-eclipse", typeof(LunarEclipseCreativeProfile))]
    public void Resolver_selects_expected_family_profile(ContractEventFamily family, string eventType, Type expected)
    {
        var diagnostics = new List<DiagnosticMessage>();
        var profile = Resolver().Resolve(Context(family, eventType), diagnostics);

        Assert.IsType(expected, profile);
        Assert.Contains(diagnostics, d => d.Code == "visual_director.profile_selected");
    }

    [Fact]
    public void Resolver_unknown_family_returns_generic_fallback_with_warning()
    {
        var diagnostics = new List<DiagnosticMessage>();
        var profile = Resolver().Resolve(Context(ContractEventFamily.Unknown, "mystery"), diagnostics);

        Assert.IsType<GenericAstronomyCreativeProfile>(profile);
        Assert.Contains(diagnostics, d => d.Code == "visual_director.fallback_profile_used" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData(typeof(PlanetPairingCreativeProfile), "perfect circular planets")]
    [InlineData(typeof(PlanetGroupingCreativeProfile), "balanced hierarchy")]
    [InlineData(typeof(MeteorShowerCreativeProfile), "radiant-aware meteor streaks")]
    [InlineData(typeof(NamedFullMoonCreativeProfile), "realistic phase/maria/craters")]
    [InlineData(typeof(SolarEclipseCreativeProfile), "eclipse geometry-safe")]
    [InlineData(typeof(LunarEclipseCreativeProfile), "umbra/penumbra aware")]
    [InlineData(typeof(GenericAstronomyCreativeProfile), "minimal safe assumptions")]
    public void Profiles_generate_expected_family_specific_cdl_sections(Type profileType, string expected)
    {
        var profile = (IFamilyCreativeProfile)Activator.CreateInstance(profileType)!;
        var result = profile.Create(ContextFor(profileType));

        Assert.Contains(result.CdlDirectives, d => d.Value.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.NegativeConstraints.Scientific);
        Assert.NotEmpty(result.QualityTargets.Dimensions);
    }

    [Fact]
    public async Task VisualCreativeDirector_delegates_to_profile_resolver()
    {
        var resolver = new RecordingResolver(new MeteorShowerCreativeProfile());
        var director = new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance, resolver);

        var result = await director.CreateDirectionAsync(Context(ContractEventFamily.MeteorShower, "meteor-shower") with { FeatureFlags = Flags() });

        Assert.True(resolver.WasCalled);
        AssertDirectiveContains(result.Cdl!, "familyCreativeDirection", "radiant-aware meteor streaks");
    }

    [Fact]
    public async Task Json_serialization_remains_valid_with_family_profile_extensions()
    {
        var director = new VisualCreativeDirector(NullLogger<VisualCreativeDirector>.Instance);
        var result = await director.CreateDirectionAsync(Context(ContractEventFamily.SolarEvent, "solar-eclipse") with { FeatureFlags = Flags() });

        var json = JsonSerializer.Serialize(result.CreativeDirectionContract, VisualIntelligenceJson.CreateSerializerOptions());
        var reparsed = JsonSerializer.Deserialize<CreativeDirectionContract>(json, VisualIntelligenceJson.CreateSerializerOptions());

        Assert.NotNull(reparsed);
        Assert.Equal(ContractEventFamily.SolarEvent, reparsed!.EventFamily);
    }

    private static IFamilyCreativeProfileResolver Resolver() => new FamilyCreativeProfileResolver([new PlanetGroupingCreativeProfile(), new PlanetPairingCreativeProfile(), new MeteorShowerCreativeProfile(), new NamedFullMoonCreativeProfile(), new SolarEclipseCreativeProfile(), new LunarEclipseCreativeProfile(), new GenericAstronomyCreativeProfile()]);

    private static VisualIntelligenceOrchestrationContext ContextFor(Type profileType) => profileType.Name switch
    {
        nameof(PlanetPairingCreativeProfile) => Context(ContractEventFamily.PlanetConjunction, "planet-pairing") with { PrimaryObjects = ["Jupiter", "Venus"] },
        nameof(PlanetGroupingCreativeProfile) => Context(ContractEventFamily.PlanetConjunction, "planet-grouping") with { PrimaryObjects = ["Venus", "Mars", "Saturn"] },
        nameof(MeteorShowerCreativeProfile) => Context(ContractEventFamily.MeteorShower, "meteor-shower"),
        nameof(NamedFullMoonCreativeProfile) => Context(ContractEventFamily.LunarEvent, "named-full-moon"),
        nameof(SolarEclipseCreativeProfile) => Context(ContractEventFamily.SolarEvent, "solar-eclipse"),
        nameof(LunarEclipseCreativeProfile) => Context(ContractEventFamily.LunarEvent, "lunar-eclipse"),
        _ => Context(ContractEventFamily.Unknown, "mystery")
    };

    private static VisualIntelligenceOrchestrationContext Context(ContractEventFamily family, string eventType) => new()
    {
        CorrelationId = "profile-test",
        EventFamily = family,
        EventType = eventType,
        Language = "en",
        Platform = Platform.YouTubeThumbnail,
        AspectRatio = AspectRatio.Landscape16x9,
        RequestedAssetType = "thumbnail",
        FeatureFlags = Flags()
    };

    private static VisualIntelligenceFlagSnapshot Flags() => new() { UseVisualCreativeDirector = true, UseCDL = true, UseCreativeDirectionContract = true };
    private static void AssertDirectiveContains(CDL cdl, string name, string expected) => Assert.Contains(cdl.Directives, d => d.Name == name && d.Value.Contains(expected, StringComparison.OrdinalIgnoreCase));

    private sealed class RecordingResolver(IFamilyCreativeProfile profile) : IFamilyCreativeProfileResolver
    {
        public bool WasCalled { get; private set; }
        public IFamilyCreativeProfile Resolve(VisualIntelligenceOrchestrationContext context, IList<DiagnosticMessage> diagnostics)
        {
            WasCalled = true;
            diagnostics.Add(new DiagnosticMessage { Code = "test.resolver_called", Source = nameof(RecordingResolver) });
            return profile;
        }
    }
}
