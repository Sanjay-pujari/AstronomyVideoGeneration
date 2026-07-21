using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationRegistrationCertificationTests
{
    [Fact]
    public void Root_registration_is_idempotent_and_resolves_foundation_without_forbidden_services()
    {
        var services = new ServiceCollection();
        services.AddAstronomyKnowledgeFoundation();
        services.AddAstronomyKnowledgeFoundation();
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IAstronomyKnowledgeCatalog>(), provider.GetRequiredService<IAstronomyKnowledgeCatalog>());
        Assert.IsType<AstronomyKnowledgeCatalogQueryEngine>(provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>());
        Assert.IsType<AstronomyKnowledgeStatementQueryEngine>(provider.GetRequiredService<IAstronomyKnowledgeStatementQueryEngine>());
        Assert.True(provider.GetRequiredService<IAstronomyKnowledgeFoundationCompatibilityVerifier>().Verify().IsCompatible);
        Assert.DoesNotContain(services, d => d.ServiceType.Name.Contains("DbContext", StringComparison.Ordinal) || d.ServiceType.Name.Contains("Repository", StringComparison.Ordinal) || d.ServiceType.Name.Contains("IHostedService", StringComparison.Ordinal));
    }
}
