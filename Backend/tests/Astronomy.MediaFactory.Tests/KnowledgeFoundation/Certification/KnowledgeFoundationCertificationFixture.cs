using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Integration;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.CrossDomain;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Validation.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class KnowledgeFoundationCertificationFixture
{
    public static readonly string[] RequiredDocs =
    [
        "README.md", "KnowledgeFoundationOverview.md", "KnowledgeFoundationLayers.md", "KnowledgeContracts.md", "TypedKnowledgeDomains.md",
        "ValidationArchitecture.md", "CrossDomainValidation.md", "KnowledgeGraphValidation.md", "KnowledgeCatalog.md", "KnowledgeQueryModel.md",
        "KnowledgeQueryExecution.md", "RegistrationAndCapabilities.md", "ExtensionGuide.md", "TestingAndCertification.md", "FrozenContracts.md",
        "KnownLimitations.md", "Task2CompletionReport.md"
    ];
    public static readonly string[] RequiredAdrs = Enumerable.Range(1, 13).Select(i => $"ADR-{i:000}").ToArray();
    public static readonly string[] PayloadIds =
    [
        "typed.classification.entity.v1", "typed.event.astronomical.v1", "typed.observational.conditions.v1", "typed.observational.visibility-windows.v1",
        "typed.orbital.keplerian-elements.v1", "typed.orbital.parameters.v1", "typed.physical.properties.v1", "typed.positional.spatial-position.v1", "typed.temporal.pattern.v1"
    ];
    public static readonly string[] CrossRuleIds =
    [
        "cross-domain.entity.consistency", "cross-domain.classification.consistency", "cross-domain.epoch.consistency", "cross-domain.reference-context.consistency",
        "cross-domain.measurement.consistency", "cross-domain.orbital-positional.consistency", "cross-domain.observation-visibility.consistency",
        "cross-domain.event-participant.consistency", "cross-domain.event-temporal.consistency"
    ];
    public static readonly string[] GraphRuleIds =
    [
        "graph.node.identity", "graph.statement.identity", "graph.reference.integrity", "graph.payload.completeness", "graph.duplicate-knowledge.integrity",
        "graph.provenance.integrity", "graph.version.consistency", "graph.cycle.integrity", "graph.orphan.integrity", "graph.connectivity.integrity", "graph.repository.consistency"
    ];
    public static readonly string[] CapabilityIds =
    [
        "typed-knowledge.registry", "validation.foundation", "validation.typed-validator", "validation.cross-domain", "validation.cross-domain.validator",
        "validation.graph", "validation.graph.validator", "catalog.metadata", "catalog.builder", "query.model", "query.execution.catalog", "query.execution.statement"
    ];
    public static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddAstronomyKnowledgeFoundation();
        return services.BuildServiceProvider();
    }
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Backend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
