using Microsoft.Extensions.DependencyInjection;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Families.Compatibility;
using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Identity;

namespace Astronomy.MediaFactory.Tests;

public sealed class FamilyResolutionV1IntegrationTests
{
    [Fact] public void ProductionFamilyResolverUsesV1DependenciesFromDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICanonicalAstronomyEventIdentityResolverV1, CanonicalAstronomyEventIdentityResolverV1>();
        services.AddSingleton<IAstronomyFamilyProfileCatalogV1, AstronomyFamilyProfileCatalogV1>();
        services.AddSingleton<IAstronomyFamilyProfileV1CompatibilityAdapter, AstronomyFamilyProfileV1CompatibilityAdapter>();
        services.AddScoped<IAstronomyFamilyProfileResolver, AstronomyFamilyProfileResolver>();
        using var sp = services.BuildServiceProvider();
        var r = sp.GetRequiredService<IAstronomyFamilyProfileResolver>().ResolveFamilyProfile(new Astronomy.MediaFactory.Infrastructure.Orchestration.RC2.AstronomyFamilyProfileResolutionInput("NamedFullMoon", null, null, null));
        Assert.Equal("NamedFullMoon", r.Profile.FamilyId);
        Assert.IsType<FamilyProfileCompatibilityDiagnostics>(r.Diagnostics);
    }
}
