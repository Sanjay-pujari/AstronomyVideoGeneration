using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyResolutionV1IntegrationTests
{
    public static IEnumerable<object[]> ActiveV1Families()
    {
        yield return ["PlanetPairing", "PlanetPairing"];
        yield return ["PlanetGrouping", "PlanetGrouping"];
        yield return ["MeteorShower", "MeteorShower"];
        yield return ["FullMoon", "FullMoon"];
        yield return ["NamedFullMoon", "NamedFullMoon"];
        yield return ["SolarEclipse", "SolarEclipse"];
        yield return ["LunarEclipse", "LunarEclipse"];
        yield return ["Occultation", "Occultation"];
        yield return ["Constellation", "Constellation"];
        yield return ["DeepSkyObject", "DeepSkyObject"];
    }

    public static IEnumerable<object[]> V1Aliases()
    {
        yield return ["PlanetaryConjunction", "PlanetPairing"];
        yield return ["PLANET_GROUPING", "PlanetGrouping"];
        yield return ["Meteor Shower", "MeteorShower"];
        yield return ["Named Full Moon", "NamedFullMoon"];
        yield return ["Solar Eclipse", "SolarEclipse"];
        yield return ["Lunar Eclipse", "LunarEclipse"];
        yield return ["Deep Sky Object", "DeepSkyObject"];
    }

    [Fact]
    public void ProductionFamilyResolverUsesV1DependenciesFromDi()
    {
        using var sp = BuildServiceProvider();
        var resolver = sp.GetRequiredService<IAstronomyFamilyProfileResolver>();

        var r = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("NamedFullMoon", null, null, null));

        Assert.Equal("NamedFullMoon", r.Profile.FamilyId);
        var diagnostics = Assert.IsType<FamilyProfileCompatibilityDiagnostics>(r.Diagnostics);
        Assert.Equal("V1", diagnostics.ResolutionAuthority);
    }

    [Theory]
    [MemberData(nameof(ActiveV1Families))]
    public void ProductionInputOverloadResolvesEveryActiveV1FamilyThroughSingleV1Authority(string inputEventType, string expectedFamily)
    {
        var identity = new CountingIdentityResolver();
        var catalog = new CountingFamilyCatalog();
        var adapter = new CountingCompatibilityAdapter();
        var resolver = new AstronomyFamilyProfileResolver(identity, catalog, adapter);

        var result = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(inputEventType, null, null, null));

        Assert.Equal(inputEventType, identity.LastEventType);
        Assert.True(identity.LastIdentity!.Supported);
        Assert.Equal(expectedFamily, identity.LastIdentity.CanonicalProfile);
        Assert.Equal(expectedFamily, catalog.LastResolvedEventType);
        Assert.Equal(expectedFamily, adapter.LastProfileId);
        Assert.Equal(expectedFamily, result.Profile.FamilyId);
        var diagnostics = Assert.IsType<FamilyProfileCompatibilityDiagnostics>(result.Diagnostics);
        Assert.Equal("V1", diagnostics.ResolutionAuthority);
        Assert.Equal(1, identity.ResolveCount);
        Assert.Equal(1, catalog.ResolveEventTypeCount);
        Assert.Equal(1, adapter.ConvertCount);
        Assert.Equal(0, catalog.OldCatalogFallbackCount);
    }

    [Theory]
    [MemberData(nameof(V1Aliases))]
    public void ProductionInputOverloadResolvesV1Aliases(string alias, string expectedFamily)
    {
        var identity = new CountingIdentityResolver();
        var catalog = new CountingFamilyCatalog();
        var adapter = new CountingCompatibilityAdapter();
        var resolver = new AstronomyFamilyProfileResolver(identity, catalog, adapter);

        var result = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(alias, null, null, null));

        Assert.Equal(alias, identity.LastEventType);
        Assert.True(identity.LastIdentity!.Supported);
        Assert.Equal(expectedFamily, identity.LastIdentity.CanonicalProfile);
        Assert.Equal(expectedFamily, result.Profile.FamilyId);
        Assert.Equal(1, identity.ResolveCount);
        Assert.Equal(1, catalog.ResolveEventTypeCount);
        Assert.Equal(1, adapter.ConvertCount);
        Assert.Equal(0, catalog.OldCatalogFallbackCount);
    }

    [Fact]
    public void ExplicitEventTypeIsNotErasedByNullOptionalFields()
    {
        var identity = new CountingIdentityResolver();
        var resolver = new AstronomyFamilyProfileResolver(identity, new CountingFamilyCatalog(), new CountingCompatibilityAdapter());

        var result = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("NamedFullMoon", null, null, null));

        Assert.Equal("NamedFullMoon", identity.LastEventType);
        Assert.Equal("NamedFullMoon", result.Profile.FamilyId);
    }

    [Fact]
    public void MissingEventTypeReportsInspectedInputFields()
    {
        var resolver = new AstronomyFamilyProfileResolver(new CountingIdentityResolver(), new CountingFamilyCatalog(), new CountingCompatibilityAdapter());

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(null, null, null, null)));

        Assert.Contains("Canonical astronomy event identity input is missing.", ex.Message);
        Assert.Contains("EventType=<missing>", ex.Message);
    }

    [Fact]
    public void UnknownEventTypeReportsUnsupportedEventTypeOnly()
    {
        var resolver = new AstronomyFamilyProfileResolver(new CountingIdentityResolver(), new CountingFamilyCatalog(), new CountingCompatibilityAdapter());

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("UnknownEvent", null, null, null)));

        Assert.Equal("Unsupported astronomy event type: UnknownEvent", ex.Message);
        Assert.DoesNotContain("Unsupported astronomy family", ex.Message);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalAstronomyEventIdentityResolverV1, CanonicalAstronomyEventIdentityResolverV1>();
        services.AddSingleton<IAstronomyFamilyProfileCatalogV1, AstronomyFamilyProfileCatalogV1>();
        services.AddSingleton<IAstronomyFamilyProfileV1CompatibilityAdapter, AstronomyFamilyProfileV1CompatibilityAdapter>();
        services.AddScoped<IAstronomyFamilyProfileResolver, AstronomyFamilyProfileResolver>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class CountingIdentityResolver : ICanonicalAstronomyEventIdentityResolverV1
    {
        private readonly CanonicalAstronomyEventIdentityResolverV1 _inner = new();
        public int ResolveCount { get; private set; }
        public string? LastEventType { get; private set; }
        public CanonicalAstronomyEventIdentity? LastIdentity { get; private set; }
        public CanonicalAstronomyEventIdentity Resolve(string? eventType, string resolutionSource = "ExplicitEventType")
        {
            ResolveCount++;
            LastEventType = eventType;
            LastIdentity = _inner.Resolve(eventType, resolutionSource);
            return LastIdentity;
        }
    }

    private sealed class CountingFamilyCatalog : IAstronomyFamilyProfileCatalogV1
    {
        private readonly AstronomyFamilyProfileCatalogV1 _inner = new();
        public int ResolveEventTypeCount { get; private set; }
        public int OldCatalogFallbackCount => 0;
        public string? LastResolvedEventType { get; private set; }
        public IReadOnlyCollection<AstronomyFamilyProfileV1> Profiles => _inner.Profiles;
        public bool TryGet(string familyId, out AstronomyFamilyProfileV1 profile) => _inner.TryGet(familyId, out profile);
        public AstronomyFamilyProfileV1 GetRequired(string familyId) => _inner.GetRequired(familyId);
        public AstronomyFamilyResolutionV1 ResolveEventType(string eventType) { ResolveEventTypeCount++; LastResolvedEventType = eventType; return _inner.ResolveEventType(eventType); }
        public FamilyProfileValidationResult Validate() => _inner.Validate();
        public bool IsActiveV1Family(string familyId) => _inner.IsActiveV1Family(familyId);
        public bool IsFutureFamily(string familyId) => _inner.IsFutureFamily(familyId);
    }

    private sealed class CountingCompatibilityAdapter : IAstronomyFamilyProfileV1CompatibilityAdapter
    {
        private readonly AstronomyFamilyProfileV1CompatibilityAdapter _inner = new();
        public int ConvertCount { get; private set; }
        public string? LastProfileId { get; private set; }
        public string AdapterId => _inner.AdapterId;
        public FamilyProfileCompatibilityResult Convert(AstronomyFamilyProfileV1 profile, FamilyProfileCompatibilityContext context) { ConvertCount++; LastProfileId = profile.FamilyId; return _inner.Convert(profile, context); }
    }
}
