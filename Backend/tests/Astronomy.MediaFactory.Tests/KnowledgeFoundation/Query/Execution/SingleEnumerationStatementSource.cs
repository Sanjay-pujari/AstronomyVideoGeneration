using System.Collections;
using Astronomy.MediaFactory.Core.KnowledgeFoundation;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation.Query.Execution;

public sealed class SingleEnumerationStatementSource(IEnumerable<IAstronomyKnowledgeStatement> statements) : IEnumerable<IAstronomyKnowledgeStatement>
{
    public int EnumerationCount { get; private set; }
    public IEnumerator<IAstronomyKnowledgeStatement> GetEnumerator() { EnumerationCount++; if (EnumerationCount > 1) throw new InvalidOperationException("Source was enumerated more than once."); return statements.GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
