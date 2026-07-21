using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Query;

public static class KnowledgeQueryFixture
{
    public static ServiceProvider Provider() => new ServiceCollection().AddAstronomyKnowledgeQueryModel().BuildServiceProvider();
    public static IAstronomyKnowledgeCatalog Catalog() => Provider().GetRequiredService<IAstronomyKnowledgeCatalog>();
    public static IAstronomyKnowledgeQueryValidator Validator() => Provider().GetRequiredService<IAstronomyKnowledgeQueryValidator>();
    public static AstronomyKnowledgeTypeId RealTypeId() => Catalog().Snapshot.KnowledgeTypes[0].KnowledgeTypeId!.Value;
    public static AstronomyKnowledgeDomain RealDomain() => Catalog().Snapshot.KnowledgeTypes[0].Domain!.Value;
    public static AstronomyKnowledgePayloadFamily RealFamily() => Catalog().Snapshot.KnowledgeTypes[0].Family!.Value;
    public static AstronomyEntityReference Subject(string id = "mars") => new(id);
    public static KnowledgeId StatementId(string id = "statement-1") => KnowledgeId.Create(id);
}
