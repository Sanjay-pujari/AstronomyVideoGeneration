using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationEndToEndCertificationTests
{
    static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Complete_deterministic_foundation_scenario_succeeds()
    {
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        var catalog = provider.GetRequiredService<IAstronomyKnowledgeCatalog>();
        var catalogResult = provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>().Execute(new AstronomyKnowledgeCatalogQuery(codes: ["typed.physical.properties.v1"]));
        Assert.Equal(AstronomyKnowledgeQueryExecutionStatus.Succeeded, catalogResult.Status);
        Assert.Equal(["typed.physical.properties.v1"], catalogResult.Items.Select(i => i.Code));
        var statement = new AstronomyKnowledgeStatement<CertificationPayload>(new("cert.statement"), KnowledgeVersion.Initial, KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Reviewed, new("cert.entity"), new CertificationPayload(), new KnowledgeAuditMetadata(T, "certifier"));
        var statementResult = provider.GetRequiredService<IAstronomyKnowledgeStatementQueryEngine>().Execute(new AstronomyKnowledgeStatementQuery(statementIds: [new KnowledgeId("cert.statement")]), [statement]);
        Assert.Equal(AstronomyKnowledgeQueryExecutionStatus.Succeeded, statementResult.Status);
        Assert.Equal(["cert.statement"], statementResult.Items.Select(i => i.Id.Value));
        Assert.True(provider.GetRequiredService<IAstronomyCrossDomainValidator>().Validate(new AstronomyCrossDomainValidationSet([new CertificationPayload()]), new(new("cert.run"), T)).IsValid);
        Assert.Contains(catalog.Snapshot.KnowledgeTypes, e => e.Code == "typed.physical.properties.v1");
        Assert.Equal(12, provider.GetRequiredService<IAstronomyKnowledgeFoundationCapabilities>().Snapshot.Capabilities.Count);
        Assert.True(provider.GetRequiredService<IAstronomyKnowledgeFoundationCompatibilityVerifier>().Verify().IsCompatible);
    }

    [Fact]
    public void Invalid_ownership_scenario_routes_defects_to_single_layers()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeId("bad id"));
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        var queryIssues = provider.GetRequiredService<IAstronomyKnowledgeQueryValidator>().Validate(new AstronomyKnowledgeStatementQuery(statementKinds: new([KnowledgeStatementKind.Scientific, KnowledgeStatementKind.Visual], AstronomyKnowledgeQueryMatchMode.All))).Issues;
        Assert.Equal([AstronomyKnowledgeQueryValidationCode.UnsupportedCombination], queryIssues.Select(i => i.Code));
        var graphIssues = provider.GetRequiredService<IAstronomyKnowledgeGraphValidator>().Validate(new AstronomyKnowledgeGraphValidationSet(references: [new("ref.missing", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "missing")]), new(new("graph.run"), T)).Issues;
        Assert.Contains(graphIssues, i => i.Code == AstronomyKnowledgeGraphValidationCodes.ReferenceTargetMissing && i.RuleId == "graph.reference.integrity");
    }
    public sealed record CertificationPayload : ITypedAstronomyKnowledgePayload { public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Physical; public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.PhysicalProperty; }
}
