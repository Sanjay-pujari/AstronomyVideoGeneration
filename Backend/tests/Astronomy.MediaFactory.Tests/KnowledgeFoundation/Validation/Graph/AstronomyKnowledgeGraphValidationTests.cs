using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.Graph;

public sealed class AstronomyKnowledgeGraphValidationTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static AstronomyKnowledgeGraphValidationContext Context(AstronomyKnowledgeGraphPolicy? policy = null, AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard) => new(new AstronomyKnowledgeValidationRunId("graph-test-run"), FixedUtc, mode, policy: policy);
    private static IAstronomyKnowledgeStatement Statement(string id, string subject, int revision = 1, IAstronomyKnowledgePayload? payload = null) => new AstronomyKnowledgeStatement<TestPayload>(new KnowledgeId(id), new KnowledgeVersion(revision), KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Reviewed, new AstronomyEntityReference(subject), (TestPayload)(payload ?? new TestPayload("value")), KnowledgeAuditMetadata.Create(FixedUtc));
    private sealed record TestPayload(string Value) : IAstronomyKnowledgePayload;

    [Fact]
    public void NodeIdentity_UniqueStrongIdsPassAndIncompatibleDuplicatesFail()
    {
        var valid = new AstronomyKnowledgeGraphValidationSet(nodes: [new("earth", AstronomyKnowledgeGraphNodeKind.Entity, "Planet")]);
        Assert.Empty(new AstronomyGraphNodeIdentityValidationRule().Validate(valid, Context()));
        var invalid = new AstronomyKnowledgeGraphValidationSet(nodes: [new("earth", AstronomyKnowledgeGraphNodeKind.Entity, "Planet"), new("earth", AstronomyKnowledgeGraphNodeKind.Statement)]);
        var issue = Assert.Single(new AstronomyGraphNodeIdentityValidationRule().Validate(invalid, Context()));
        Assert.Equal(AstronomyKnowledgeGraphValidationCodes.NodeIdentityConflict, issue.Code);
        Assert.Equal("graph.node.identity", issue.RuleId);
        Assert.Equal("$.nodes[1]", issue.Path);
    }

    [Fact]
    public void StatementIdentity_DuplicateStatementRevisionAndMissingSubjectFail()
    {
        var graph = new AstronomyKnowledgeGraphValidationSet(statements: [Statement("s1", "earth"), Statement("s1", "earth")]);
        var issues = new AstronomyGraphStatementIdentityValidationRule().Validate(graph, Context()).ToArray();
        Assert.Contains(issues, i => i.Code == AstronomyKnowledgeGraphValidationCodes.StatementIdentityDuplicate);
        Assert.Contains(issues, i => i.Code == AstronomyKnowledgeGraphValidationCodes.StatementSubjectMissing);
    }

    [Fact]
    public void ReferenceIntegrity_MissingAndAmbiguousTargetsAreReported()
    {
        var graph = new AstronomyKnowledgeGraphValidationSet(nodes: [new("earth", AstronomyKnowledgeGraphNodeKind.Entity), new("earth", AstronomyKnowledgeGraphNodeKind.Entity)], references: [new("r1", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "mars"), new("r2", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "earth")]);
        var issues = new AstronomyGraphReferenceIntegrityValidationRule().Validate(graph, Context()).ToArray();
        Assert.Contains(issues, i => i.Code == AstronomyKnowledgeGraphValidationCodes.ReferenceTargetMissing);
        Assert.Contains(issues, i => i.Code == AstronomyKnowledgeGraphValidationCodes.ReferenceTargetAmbiguous);
    }

    [Fact]
    public void CycleRule_AllowsRelatedToButRejectsForbiddenCycles()
    {
        var allowed = new AstronomyKnowledgeGraphValidationSet(relationships: [new("r1", AstronomyKnowledgeGraphRelationshipKind.RelatedTo, "a", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "b", AstronomyKnowledgeGraphReferenceTargetKind.Entity), new("r2", AstronomyKnowledgeGraphRelationshipKind.RelatedTo, "b", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "a", AstronomyKnowledgeGraphReferenceTargetKind.Entity)]);
        Assert.Empty(new AstronomyGraphCycleValidationRule().Validate(allowed, Context()));
        var invalid = new AstronomyKnowledgeGraphValidationSet(relationships: [new("r1", AstronomyKnowledgeGraphRelationshipKind.DerivedFrom, "a", AstronomyKnowledgeGraphReferenceTargetKind.Entity, "a", AstronomyKnowledgeGraphReferenceTargetKind.Entity)]);
        Assert.Contains(new AstronomyGraphCycleValidationRule().Validate(invalid, Context()), i => i.Code == AstronomyKnowledgeGraphValidationCodes.ForbiddenSelfReference);
    }

    [Fact]
    public void Validator_ExecutesRegisteredRulesInStableOrderAndFiltersSeverity()
    {
        var validator = new AstronomyKnowledgeGraphValidator([new AstronomyGraphNodeIdentityValidationRule(), new AstronomyGraphOrphanValidationRule()]);
        var graph = new AstronomyKnowledgeGraphValidationSet(nodes: [new("earth", AstronomyKnowledgeGraphNodeKind.Entity), new("earth", AstronomyKnowledgeGraphNodeKind.Statement)]);
        var result = validator.Validate(graph, new AstronomyKnowledgeGraphValidationContext(new AstronomyKnowledgeValidationRunId("run"), FixedUtc, minimumSeverity: AstronomyKnowledgeValidationSeverity.Error));
        Assert.Single(result.Issues);
        Assert.Equal(AstronomyKnowledgeGraphValidationCodes.NodeIdentityConflict, result.Issues[0].Code);
    }

    [Fact]
    public void Registration_IsIdempotentAndRegistersEachRuleOnce()
    {
        var services = new ServiceCollection().AddAstronomyKnowledgeGraphValidation().AddAstronomyKnowledgeGraphValidation();
        using var provider = services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IAstronomyKnowledgeGraphValidationRuleRegistry>().Descriptors;
        Assert.Equal(11, descriptors.Count);
        Assert.Equal(descriptors.Count, descriptors.Select(d => d.RuleId).Distinct(StringComparer.Ordinal).Count());
        Assert.NotNull(provider.GetRequiredService<IAstronomyKnowledgeGraphValidator>());
    }
}
