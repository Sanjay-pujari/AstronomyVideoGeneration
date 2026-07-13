using System.Reflection;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;
using Microsoft.Extensions.Configuration;
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
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();

        var r = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput("NamedFullMoon", null, null, null));

        Assert.Equal("NamedFullMoon", r.Profile.FamilyId);
        var diagnostics = Assert.IsType<FamilyProfileCompatibilityDiagnostics>(r.Diagnostics);
        Assert.Equal("V1", diagnostics.ResolutionAuthority);
    }

    [Fact]
    public void DiCreatedCatalogContainsExactly10ActiveProfiles()
    {
        using var provider = BuildServiceProvider();

        var catalog = provider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();

        Assert.Equal(10, catalog.Profiles.Count(p => p.ActiveInV1));
    }

    [Theory]
    [MemberData(nameof(ActiveV1Families))]
    public void DiCreatedCatalogResolvesEveryActiveV1Family(string inputEventType, string expectedFamily)
    {
        using var provider = BuildServiceProvider();

        var result = provider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>().ResolveEventType(inputEventType);

        Assert.Equal(AstronomyFamilyResolutionStatusV1.Resolved, result.Status);
        Assert.Equal(expectedFamily, result.ProfileId);
    }

    [Fact]
    public void DirectAndDiDefaultCatalogsContainSameProfileIdsInOrder()
    {
        using var provider = BuildServiceProvider();
        var direct = new AstronomyFamilyProfileCatalogV1();
        var fromDi = provider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();

        Assert.Equal(direct.Profiles.Select(p => p.FamilyId), fromDi.Profiles.Select(p => p.FamilyId));
    }

    [Theory]
    [MemberData(nameof(V1Aliases))]
    public void DirectAndDiDefaultCatalogsHaveSameAliasBehavior(string alias, string expectedFamily)
    {
        using var provider = BuildServiceProvider();
        var direct = new AstronomyFamilyProfileCatalogV1();
        var fromDi = provider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();

        Assert.Equal(direct.ResolveEventType(alias), fromDi.ResolveEventType(alias));
        Assert.Equal(expectedFamily, fromDi.ResolveEventType(alias).ProfileId);
    }

    [Fact]
    public void DiCreatedCatalogValidationSucceeds()
    {
        using var provider = BuildServiceProvider();

        var validation = provider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>().Validate();

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void ScopedResolverResolvesAllActiveV1FamiliesUsingSingletonCatalog()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();
        var resolver = scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();

        foreach (var family in ActiveV1Families().Select(row => (string)row[0]))
        {
            var result = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(family, null, null, null));
            Assert.Equal(family, result.Profile.FamilyId);
            Assert.Same(catalog, scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>());
        }
    }

    [Fact]
    public void NoPublicConstructorCanBeSelectedByDiToCreateEmptyCatalog()
    {
        var publicConstructors = typeof(AstronomyFamilyProfileCatalogV1).GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        var constructor = Assert.Single(publicConstructors);
        Assert.Empty(constructor.GetParameters());
    }

    [Fact]
    public void ApplicationServiceCollectionHasExactlyOneEffectiveV1CatalogRegistration()
    {
        var services = BuildApplicationServices();
        var registrations = services.Where(d => d.ServiceType == typeof(IAstronomyFamilyProfileCatalogV1)).ToArray();

        var registration = Assert.Single(registrations);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory);
    }

    [Fact]
    public void ApplicationV1FamilyRegistrationsHaveExpectedEffectiveLifetimes()
    {
        var services = BuildApplicationServices();

        AssertEffectiveLifetime<ICanonicalAstronomyEventIdentityResolverV1>(services, ServiceLifetime.Singleton);
        AssertEffectiveLifetime<IAstronomyFamilyProfileCatalogV1>(services, ServiceLifetime.Singleton);
        AssertEffectiveLifetime<IAstronomyFamilyProfileV1CompatibilityAdapter>(services, ServiceLifetime.Singleton);
        AssertEffectiveLifetime<IAstronomyFamilyProfileResolver>(services, ServiceLifetime.Scoped);
    }

    [Fact]
    public void NoOldFamilyCatalogFallbackOccursInProductionDi()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();

        foreach (var family in ActiveV1Families().Select(row => (string)row[0]))
        {
            var result = resolver.ResolveFamilyProfile(new AstronomyFamilyProfileResolutionInput(family, null, null, null));
            var diagnostics = Assert.IsType<FamilyProfileCompatibilityDiagnostics>(result.Diagnostics);
            Assert.Equal("V1", diagnostics.ResolutionAuthority);
        }
    }

    [Fact]
    public void ScopedFamilyResolverResolvesInsideScope()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();

        Assert.NotNull(resolver);
    }

    [Fact]
    public void ScopedFamilyResolverThrowsWhenResolvedFromRootProvider()
    {
        using var provider = BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IAstronomyFamilyProfileResolver>());

        Assert.Contains("Cannot resolve scoped service", ex.Message);
        Assert.Contains(nameof(IAstronomyFamilyProfileResolver), ex.Message);
    }

    [Fact]
    public void ScopedFamilyResolverResolvesInSeparateScopes()
    {
        using var provider = BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstResolver = firstScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();
        var secondResolver = secondScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileResolver>();

        Assert.NotNull(firstResolver);
        Assert.NotNull(secondResolver);
        Assert.NotSame(firstResolver, secondResolver);
    }

    [Fact]
    public void SingletonV1DependenciesAreSharedAcrossScopes()
    {
        using var provider = BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstIdentity = firstScope.ServiceProvider.GetRequiredService<ICanonicalAstronomyEventIdentityResolverV1>();
        var secondIdentity = secondScope.ServiceProvider.GetRequiredService<ICanonicalAstronomyEventIdentityResolverV1>();
        var firstCatalog = firstScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();
        var secondCatalog = secondScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileCatalogV1>();
        var firstAdapter = firstScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileV1CompatibilityAdapter>();
        var secondAdapter = secondScope.ServiceProvider.GetRequiredService<IAstronomyFamilyProfileV1CompatibilityAdapter>();

        Assert.Same(firstIdentity, secondIdentity);
        Assert.Same(firstCatalog, secondCatalog);
        Assert.Same(firstAdapter, secondAdapter);
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

    private static ServiceProvider BuildServiceProvider() => BuildApplicationServices().BuildServiceProvider(validateScopes: true);

    private static ServiceCollection BuildApplicationServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddMediaFactory(configuration);
        return services;
    }

    private static void AssertEffectiveLifetime<TService>(IServiceCollection services, ServiceLifetime expectedLifetime)
    {
        var registration = Assert.Single(services.Where(d => d.ServiceType == typeof(TService)));
        Assert.Equal(expectedLifetime, registration.Lifetime);
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
