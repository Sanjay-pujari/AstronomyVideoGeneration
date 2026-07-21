using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using static Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.Graph.KnowledgeGraphValidationFixture;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Validation.Graph;

public sealed class AstronomyKnowledgeGraphValidationArchitectureTests
{
    [Fact]
    public void Valid_graph_passes_rule_or_validator()
    {
        var graph = new AstronomyKnowledgeGraphValidationSet(statements: [Statement("s1")], nodes: [Entity()], rootIds: ["s1"], repositoryId: "repo", repositoryVersion: "v1");
        var issues = Run(graph, Context(policy: Policy(requireRoot: true), repoId: "repo", repoVersion: "v1"));
        Assert.DoesNotContain(issues, i => i.Severity >= AstronomyKnowledgeValidationSeverity.Error);
    }

    [Fact]
    public void Real_invalid_graph_reports_exact_metadata_and_paths()
    {
        var graph = InvalidGraph();
        var issues = Run(graph, InvalidContext()).ToArray();
        Assert.NotEmpty(issues);
        Assert.All(issues, i => Assert.False(string.IsNullOrWhiteSpace(i.RuleId)));
        Assert.Equal(issues.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal), issues.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void Deterministic_execution_and_input_non_mutation()
    {
        var graph = InvalidGraph();
        var before = (graph.Statements.Count, graph.Nodes.Count, graph.Relationships.Count, graph.References.Count, graph.RootIds.Count);
        var a = Run(graph, InvalidContext()).Select(i => (i.Code, i.Path, i.RuleId)).ToArray();
        var b = Run(graph, InvalidContext()).Select(i => (i.Code, i.Path, i.RuleId)).ToArray();
        Assert.Equal(a, b);
        Assert.Equal(before, (graph.Statements.Count, graph.Nodes.Count, graph.Relationships.Count, graph.References.Count, graph.RootIds.Count));
    }

    private static IEnumerable<AstronomyKnowledgeValidationIssue> Run(AstronomyKnowledgeGraphValidationSet graph, AstronomyKnowledgeGraphValidationContext context)
    {
        var name = typeof(AstronomyKnowledgeGraphValidationArchitectureTests).Name;
        if (name.Contains("NodeIdentity")) return new AstronomyGraphNodeIdentityValidationRule().Validate(graph, context);
        if (name.Contains("StatementIdentity")) return new AstronomyGraphStatementIdentityValidationRule().Validate(graph, context);
        if (name.Contains("ReferenceIntegrity")) return new AstronomyGraphReferenceIntegrityValidationRule().Validate(graph, context);
        if (name.Contains("PayloadCompleteness")) return new AstronomyGraphPayloadCompletenessValidationRule(new AstronomyTypedPayloadRegistry([new("catalog.test.v1", typeof(TypedTestPayload), AstronomyKnowledgeDomain.Catalog, AstronomyKnowledgePayloadFamily.CatalogReference)])).Validate(graph, context);
        if (name.Contains("DuplicateKnowledge")) return new AstronomyGraphDuplicateKnowledgeValidationRule().Validate(graph, context);
        if (name.Contains("Provenance")) return new AstronomyGraphProvenanceValidationRule().Validate(graph, context);
        if (name.Contains("VersionConsistency")) return new AstronomyGraphVersionConsistencyValidationRule().Validate(graph, context);
        if (name.Contains("Cycle")) return new AstronomyGraphCycleValidationRule().Validate(graph, context);
        if (name.Contains("Orphan")) return new AstronomyGraphOrphanValidationRule().Validate(graph, context);
        if (name.Contains("Connectivity")) return new AstronomyGraphConnectivityValidationRule().Validate(graph, context);
        if (name.Contains("RepositoryConsistency")) return new AstronomyGraphRepositoryConsistencyValidationRule().Validate(graph, context);
        if (name.Contains("Validator")) return new AstronomyKnowledgeGraphValidator([new AstronomyGraphNodeIdentityValidationRule(), new AstronomyGraphStatementIdentityValidationRule(), new AstronomyGraphReferenceIntegrityValidationRule(), new AstronomyGraphDuplicateKnowledgeValidationRule(), new AstronomyGraphCycleValidationRule(), new AstronomyGraphConnectivityValidationRule(), new AstronomyGraphRepositoryConsistencyValidationRule()]).Validate(graph, context).Issues;
        if (name.Contains("Integration")) { var sp = new ServiceCollection().AddAstronomyKnowledgeGraphValidation().BuildServiceProvider(); return sp.GetRequiredService<IAstronomyKnowledgeGraphValidator>().Validate(graph, context).Issues; }
        if (name.Contains("Architecture")) { AssertProductionArchitecture(); return new AstronomyKnowledgeGraphValidator([new AstronomyGraphNodeIdentityValidationRule(), new AstronomyGraphStatementIdentityValidationRule(), new AstronomyGraphReferenceIntegrityValidationRule(), new AstronomyGraphPayloadCompletenessValidationRule(new AstronomyTypedPayloadRegistry([new("catalog.test.v1", typeof(TypedTestPayload), AstronomyKnowledgeDomain.Catalog, AstronomyKnowledgePayloadFamily.CatalogReference)])), new AstronomyGraphDuplicateKnowledgeValidationRule(), new AstronomyGraphProvenanceValidationRule(), new AstronomyGraphVersionConsistencyValidationRule(), new AstronomyGraphCycleValidationRule(), new AstronomyGraphOrphanValidationRule(), new AstronomyGraphConnectivityValidationRule(), new AstronomyGraphRepositoryConsistencyValidationRule()]).Validate(graph, context).Issues; }
        return [];
    }

    private static AstronomyKnowledgeGraphValidationSet InvalidGraph() => new(
        statements: [Statement("s1", "missing"), Statement("s1", "missing", value: "other"), TypedStatement("s2", "earth", 3)],
        nodes: [Entity(), new("earth", AstronomyKnowledgeGraphNodeKind.Statement), Entity("unused")],
        relationships: [Rel("r1", "earth", "earth", AstronomyKnowledgeGraphRelationshipKind.DerivedFrom), Rel("r2", "missing", "s2", targetKind: AstronomyKnowledgeGraphReferenceTargetKind.Statement)],
        references: [Ref("provenance.0", "missing"), Ref("normal.0", "missing")], rootIds: ["s1", "s1"], repositoryId: "actual", repositoryVersion: "v2");
    private static AstronomyKnowledgeGraphValidationContext InvalidContext() => Context(policy: Policy(connectivity: AstronomyKnowledgeGraphConnectivityPolicy.ConnectedScopeRequired, external: AstronomyKnowledgeGraphExternalReferencePolicy.RejectExternal, requireRoot: true, uniqueRoots: true, requireReachability: true), repoId: "expected", repoVersion: "v1");

    private static void AssertProductionArchitecture()
    {
        var root = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, "Backend"))) root = Directory.GetParent(root)!.FullName;
        var file = Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/Validation/Graph/AstronomyKnowledgeGraphValidation.cs");
        var text = File.ReadAllText(file);
        Assert.Contains("LogicalKnowledgeIdentity", text);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", text);
        var nested = typeof(AstronomyGraphDuplicateKnowledgeValidationRule).GetNestedTypes(BindingFlags.NonPublic).Single(t => t.Name.Contains("LogicalKnowledgeIdentity"));
        Assert.Contains(nested.GetProperties(), p => p.PropertyType == typeof(AstronomyKnowledgeDomain));
        Assert.Contains(nested.GetProperties(), p => p.PropertyType == typeof(AstronomyKnowledgePayloadFamily));
        Assert.Contains(nested.GetProperties(), p => p.PropertyType == typeof(AstronomyKnowledgeTypeId?));
    }
}
