using Astronomy.MediaFactory.Core.KnowledgeFoundation;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Query.Execution;

public sealed class AstronomyKnowledgeCatalogQueryEngineFilteringTests
{
    [Fact]
    public void KnowledgeQueryExecution_basic_contract_holds()
    {
        var fixture = new KnowledgeQueryExecutionFixture();
        var catalogResult = fixture.CatalogEngine.Execute(new AstronomyKnowledgeCatalogQuery(page: new AstronomyKnowledgeQueryPage(0, 3)));
        Assert.Equal(AstronomyKnowledgeQueryExecutionStatus.Succeeded, catalogResult.Status);
        Assert.Equal(3, catalogResult.Items.Count);
        Assert.Equal(AstronomyKnowledgeQueryTarget.CatalogEntry, catalogResult.Metadata.Target);
        var source = new SingleEnumerationStatementSource(new[] { fixture.Statement("statement-b",2,"venus"), fixture.Statement("statement-a",1,"mars") });
        var statementResult = fixture.StatementEngine.Execute(new AstronomyKnowledgeStatementQuery(page: new AstronomyKnowledgeQueryPage(0, 1)), source);
        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(AstronomyKnowledgeQueryExecutionStatus.Succeeded, statementResult.Status);
        Assert.Equal("statement-a", statementResult.Items[0].Id.Value);
        Assert.Equal(2, statementResult.Metadata.SourceCount);
        Assert.Throws<ArgumentNullException>(() => fixture.CatalogEngine.Execute(null!));
    }
}
