using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.Graph;

public static class KnowledgeGraphValidationFixture
{
    public static readonly DateTimeOffset FixedUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static AstronomyKnowledgeGraphValidationContext Context(AstronomyKnowledgeGraphPolicy? policy = null, AstronomyKnowledgeValidationMode mode = AstronomyKnowledgeValidationMode.Standard, AstronomyKnowledgeValidationSeverity minimumSeverity = AstronomyKnowledgeValidationSeverity.Information, string[]? scopes = null, string? repoId = null, string? repoVersion = null) => new(new AstronomyKnowledgeValidationRunId("graph-test-run"), FixedUtc, mode, minimumSeverity, scopes, repoId, repoVersion, policy);
    public static AstronomyKnowledgeGraphPolicy Policy(AstronomyKnowledgeGraphConnectivityPolicy connectivity = AstronomyKnowledgeGraphConnectivityPolicy.IndependentComponentsAllowed, AstronomyKnowledgeGraphDuplicateNodePolicy duplicate = AstronomyKnowledgeGraphDuplicateNodePolicy.IgnoreCompatible, AstronomyKnowledgeGraphExternalReferencePolicy external = AstronomyKnowledgeGraphExternalReferencePolicy.AllowExplicitExternal, bool requireRoot = false, bool uniqueRoots = false, bool requireReachability = false, bool reportUnused = true) => new(connectivity, duplicate, external, requireRoot, uniqueRoots, requireReachability, reportUnused);
    public static AstronomyKnowledgeStatement<TestPayload> Statement(string id, string subject = "earth", int revision = 1, string value = "value", KnowledgeStatementKind kind = KnowledgeStatementKind.Scientific) => new(new KnowledgeId(id), new KnowledgeVersion(revision), kind, KnowledgeFoundationStatus.Reviewed, new AstronomyEntityReference(subject), new TestPayload(value), KnowledgeAuditMetadata.Create(FixedUtc));
    public static AstronomyKnowledgeStatement<TypedTestPayload> TypedStatement(string id, string subject = "earth", int revision = 1, string typeId = "catalog.mass", string value = "v") => new(new KnowledgeId(id), new KnowledgeVersion(revision), KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus.Reviewed, new AstronomyEntityReference(subject), new TypedTestPayload(new AstronomyKnowledgeTypeId(typeId), value), KnowledgeAuditMetadata.Create(FixedUtc));
    public static AstronomyKnowledgeGraphNode Entity(string id = "earth", string? kind = "Planet") => new(id, AstronomyKnowledgeGraphNodeKind.Entity, kind);
    public static AstronomyKnowledgeGraphRelationship Rel(string id, string source, string target, AstronomyKnowledgeGraphRelationshipKind kind = AstronomyKnowledgeGraphRelationshipKind.RelatedTo, AstronomyKnowledgeGraphReferenceTargetKind sourceKind = AstronomyKnowledgeGraphReferenceTargetKind.Entity, AstronomyKnowledgeGraphReferenceTargetKind targetKind = AstronomyKnowledgeGraphReferenceTargetKind.Entity) => new(id, kind, source, sourceKind, target, targetKind);
    public static AstronomyKnowledgeGraphReference Ref(string id, string target, AstronomyKnowledgeGraphReferenceTargetKind kind = AstronomyKnowledgeGraphReferenceTargetKind.Entity, bool external = false) => new(id, kind, target, external);
    public static AstronomyKnowledgeValidationIssue AssertIssue(IEnumerable<AstronomyKnowledgeValidationIssue> issues, string code, string path, string ruleId) { var issue = Assert.Single(issues.Where(i => i.Code == code && i.Path == path && i.RuleId == ruleId)); Assert.Equal(AstronomyKnowledgeValidationSeverity.Error, issue.Severity); return issue; }
    public sealed record TestPayload(string Value) : IAstronomyKnowledgePayload;
    public sealed record OtherPayload(string Value) : IAstronomyKnowledgePayload;
    public sealed record TypedTestPayload(AstronomyKnowledgeTypeId TypeId, string Value) : ITypedAstronomyKnowledgePayload { public AstronomyKnowledgeDomain Domain => AstronomyKnowledgeDomain.Catalog; public AstronomyKnowledgePayloadFamily Family => AstronomyKnowledgePayloadFamily.CatalogReference; }
}
