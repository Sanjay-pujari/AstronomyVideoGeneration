using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationTask2CertificationTests
{
    [Fact]
    public void Task2_inventory_counts_and_contract_constants_match_production()
    {
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        Assert.Equal(9, provider.GetRequiredService<IAstronomyTypedPayloadRegistry>().Descriptors.Count);
        var catalog = provider.GetRequiredService<IAstronomyKnowledgeCatalog>().Snapshot;
        Assert.Equal(9, catalog.KnowledgeTypes.Count);
        Assert.Equal(9, catalog.CrossDomainValidationRules.Count);
        Assert.Equal(11, catalog.GraphValidationRules.Count);
        Assert.Equal(100, AstronomyKnowledgeQueryPage.DefaultLimit);
        Assert.Equal(1000, AstronomyKnowledgeQueryPage.MaximumLimit);
        Assert.Equal([AstronomyKnowledgeCatalogSortField.Kind, AstronomyKnowledgeCatalogSortField.Order, AstronomyKnowledgeCatalogSortField.Code, AstronomyKnowledgeCatalogSortField.Id], AstronomyKnowledgeCatalogOrder.Default.Select(o => o.Field));
        Assert.Equal([AstronomyKnowledgeStatementSortField.Id, AstronomyKnowledgeStatementSortField.Revision], AstronomyKnowledgeStatementOrder.Default.Select(o => o.Field));
    }
}
