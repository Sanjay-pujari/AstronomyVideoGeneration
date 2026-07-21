using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationDeterminismCertificationTests
{
    [Fact]
    public void Repeated_runtime_snapshots_queries_and_verification_are_identical()
    {
        using var a = KnowledgeFoundationCertificationFixture.Provider();
        using var b = KnowledgeFoundationCertificationFixture.Provider();
        Assert.Equal(a.GetRequiredService<IAstronomyTypedPayloadRegistry>().Descriptors.Select(d => d.Discriminator), b.GetRequiredService<IAstronomyTypedPayloadRegistry>().Descriptors.Select(d => d.Discriminator));
        Assert.Equal(a.GetRequiredService<IAstronomyKnowledgeCatalog>().Snapshot.Entries.Select(e => e.Id.ToString()), b.GetRequiredService<IAstronomyKnowledgeCatalog>().Snapshot.Entries.Select(e => e.Id.ToString()));
        Assert.Equal(a.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>().Descriptors.Select(d => d.RuleId), b.GetRequiredService<IAstronomyCrossDomainValidationRuleRegistry>().Descriptors.Select(d => d.RuleId));
        Assert.Equal(a.GetRequiredService<IAstronomyKnowledgeGraphValidationRuleRegistry>().Descriptors.Select(d => d.RuleId), b.GetRequiredService<IAstronomyKnowledgeGraphValidationRuleRegistry>().Descriptors.Select(d => d.RuleId));
        var q1 = new AstronomyKnowledgeCatalogQuery(codes: ["typed.physical.properties.v1"]);
        var q2 = new AstronomyKnowledgeCatalogQuery(codes: ["typed.physical.properties.v1"]);
        Assert.Equal(q1.Fingerprint, q2.Fingerprint);
        Assert.Equal(a.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>().Execute(q1).Items.Select(i => i.Id.ToString()), b.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>().Execute(q2).Items.Select(i => i.Id.ToString()));
        Assert.Equal(a.GetRequiredService<IAstronomyKnowledgeFoundationCapabilities>().Snapshot.Capabilities.Select(c => c.Id.Value), b.GetRequiredService<IAstronomyKnowledgeFoundationCapabilities>().Snapshot.Capabilities.Select(c => c.Id.Value));
        Assert.Equal(a.GetRequiredService<IAstronomyKnowledgeFoundationCompatibilityVerifier>().Verify().Issues.Select(i => i.Code), b.GetRequiredService<IAstronomyKnowledgeFoundationCompatibilityVerifier>().Verify().Issues.Select(i => i.Code));
    }
}
