using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase2DependencyInjectionTests
{
    private static ServiceProvider CreatePhase2Provider()
    {
        var services = new ServiceCollection();
        services.AddPhase2ProductionEventIntelligence();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static ServiceProvider CreateProductionProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=phase2-di-test.invalid;Port=5432;Database=astronomy_mediafactory_test;Username=test_user;Password=test_password;Pooling=false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddMediaFactory(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void Phase2_module_resolves_its_complete_service_graph()
    {
        using var provider = CreatePhase2Provider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventIntelligencePhaseService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventFamilyResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceCapabilityResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceValidator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceCertifier>());
    }

    [Fact]
    public void Phase2_service_resolves_from_production_composition_root()
    {
        using var provider = CreateProductionProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionEventIntelligencePhaseService>());
    }

    [Fact]
    public void All_required_phase2_capabilities_are_registered_once()
    {
        using var provider = CreatePhase2Provider();
        using var scope = provider.CreateScope();
        var capabilities = scope.ServiceProvider.GetServices<IProductionEventIntelligenceCapability>().ToArray();

        Assert.Equal(
            ["Comet", "Constellation", "DeepSkyObject", "Eclipse", "GenericAstronomy", "LunarEvent", "MeteorShower", "PlanetGrouping", "PlanetaryAlignment"],
            capabilities.Select(capability => capability.CapabilityId).Order(StringComparer.Ordinal));
        Assert.Equal(
            capabilities.Length,
            capabilities.Select(capability => $"{capability.CapabilityId}:{capability.Version}")
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Production_pipeline_resolves_with_phase2_dependencies()
    {
        using var provider = CreateProductionProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionPipelineExecutionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductionPhaseRunner>());
    }

    [Fact]
    public void Production_pipeline_interfaces_resolve_to_same_scoped_instance()
    {
        using var provider = CreateProductionProvider();
        using var scope = provider.CreateScope();

        var pipeline = scope.ServiceProvider.GetRequiredService<IProductionPipelineExecutionService>();
        var runner = scope.ServiceProvider.GetRequiredService<IProductionPhaseRunner>();

        Assert.Same(pipeline, runner);
    }

    [Theory]
    [InlineData("CONSTELLATION", "Constellation")]
    [InlineData("METEOR_SHOWER", "MeteorShower")]
    [InlineData("PLANET_CONJUNCTION", "PlanetaryAlignment")]
    [InlineData("PLANET_GROUPING", "PlanetGrouping")]
    [InlineData("SOLAR_ECLIPSE", "Eclipse")]
    public void Capability_resolution_uses_registered_production_capabilities(string eventType, string expectedCapabilityId)
    {
        using var provider = CreatePhase2Provider();
        using var scope = provider.CreateScope();
        var familyResolver = scope.ServiceProvider.GetRequiredService<IProductionEventFamilyResolver>();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceCapabilityResolver>();

        var family = familyResolver.Resolve(new(eventType));
        var resolution = capabilityResolver.Resolve(family);

        Assert.Equal(expectedCapabilityId, resolution.CapabilityId);
        Assert.False(resolution.FallbackUsed);
    }

    [Theory]
    [InlineData("CONSTELLATION")]
    [InlineData("METEOR_SHOWER")]
    [InlineData("PLANET_CONJUNCTION")]
    [InlineData("PLANET_PAIRING")]
    [InlineData("PLANET_GROUPING")]
    [InlineData("NAMED_FULL_MOON")]
    [InlineData("NEW_MOON")]
    [InlineData("LUNAR_ECLIPSE")]
    [InlineData("SOLAR_ECLIPSE")]
    [InlineData("COMET")]
    [InlineData("DEEP_SKY_OBJECT")]
    public void Known_families_do_not_use_generic_fallback(string eventType)
    {
        using var provider = CreatePhase2Provider();
        using var scope = provider.CreateScope();
        var familyResolver = scope.ServiceProvider.GetRequiredService<IProductionEventFamilyResolver>();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceCapabilityResolver>();

        var family = familyResolver.Resolve(new(eventType));
        var resolution = capabilityResolver.Resolve(family);

        Assert.True(family.IsKnownFamily);
        Assert.False(resolution.FallbackUsed);
    }

    [Fact]
    public void Unknown_family_uses_explicit_generic_fallback()
    {
        using var provider = CreatePhase2Provider();
        using var scope = provider.CreateScope();
        var familyResolver = scope.ServiceProvider.GetRequiredService<IProductionEventFamilyResolver>();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<IProductionEventIntelligenceCapabilityResolver>();

        var family = familyResolver.Resolve(new("UNREGISTERED_ASTRONOMY_EVENT"));
        var resolution = capabilityResolver.Resolve(family);

        Assert.False(family.IsKnownFamily);
        Assert.True(resolution.FallbackUsed);
        Assert.Equal("GenericAstronomy", resolution.CapabilityId);
        Assert.False(string.IsNullOrWhiteSpace(resolution.FallbackReason));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(Phase2DependencyInjectionTests).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
