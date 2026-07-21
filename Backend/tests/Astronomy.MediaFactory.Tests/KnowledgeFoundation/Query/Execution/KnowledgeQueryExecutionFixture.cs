using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Query.Execution;

public sealed class KnowledgeQueryExecutionFixture
{
    public ServiceProvider Provider { get; } = new ServiceCollection().AddAstronomyKnowledgeQueryExecution().BuildServiceProvider();
    public IAstronomyKnowledgeCatalog Catalog => Provider.GetRequiredService<IAstronomyKnowledgeCatalog>();
    public IAstronomyKnowledgeQueryValidator Validator => Provider.GetRequiredService<IAstronomyKnowledgeQueryValidator>();
    public IAstronomyKnowledgeCatalogQueryEngine CatalogEngine => Provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>();
    public IAstronomyKnowledgeStatementQueryEngine StatementEngine => Provider.GetRequiredService<IAstronomyKnowledgeStatementQueryEngine>();
    public static readonly DateTimeOffset T1 = new(2026,1,1,0,0,0,TimeSpan.Zero);
    public static readonly DateTimeOffset T2 = new(2026,1,2,0,0,0,TimeSpan.Zero);
    public IAstronomyKnowledgeStatement Statement(string id="statement-a", int revision=1, string subject="mars", KnowledgeStatementKind kind=KnowledgeStatementKind.Scientific, KnowledgeFoundationStatus status=KnowledgeFoundationStatus.Reviewed, string? creator="creator-a") => new AstronomyKnowledgeStatement<TestPayload>(new KnowledgeId(id), new KnowledgeVersion(revision), kind, status, new AstronomyEntityReference(subject), new TestPayload(), creator is null ? new KnowledgeAuditMetadata(T1, null) : new KnowledgeAuditMetadata(T1, creator, T2, "updater"));
    public sealed record TestPayload : IAstronomyKnowledgePayload;
}
