using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Certification;

public sealed class AstronomyKnowledgeFoundationMutationCertificationTests
{
    [Fact]
    public void Queries_and_snapshots_copy_inputs_and_remain_immutable()
    {
        var codes = new List<string> { "typed.physical.properties.v1" };
        var query = new AstronomyKnowledgeCatalogQuery(codes: codes, page: new AstronomyKnowledgeQueryPage(0, 1));
        codes.Add("typed.temporal.pattern.v1");
        Assert.Equal(["typed.physical.properties.v1"], query.Codes);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)query.Codes!).Add("x"));
        using var provider = KnowledgeFoundationCertificationFixture.Provider();
        var before = provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>().Execute(query).Items.Select(i => i.Code).ToArray();
        var after = provider.GetRequiredService<IAstronomyKnowledgeCatalogQueryEngine>().Execute(query).Items.Select(i => i.Code).ToArray();
        Assert.Equal(before, after);
        Assert.Equal(["typed.physical.properties.v1"], after);
    }
}
