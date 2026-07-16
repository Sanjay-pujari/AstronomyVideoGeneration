using Astronomy.MediaFactory.Infrastructure.Production.Narration.Semantics.Sources.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class SemanticSourcePolicyRegistryConsistencyValidatorV1Tests
{
    [Fact]
    public void AddMediaFactory_ProductionPolicies_HaveMatchingRegisteredAdapters()
    {
        using var provider = ProductionSourcePolicyCatalogNonEmptyTests.BuildProvider();
        var validator = provider.GetRequiredService<SemanticSourcePolicyRegistryConsistencyValidatorV1>();
        var report = validator.Validate();
        Assert.True(report.Succeeded, string.Join(" | ", report.Issues.Select(i => $"{i.CapabilityId}:{i.SourceId}:{i.Code}:{i.Message}")));
    }
}
