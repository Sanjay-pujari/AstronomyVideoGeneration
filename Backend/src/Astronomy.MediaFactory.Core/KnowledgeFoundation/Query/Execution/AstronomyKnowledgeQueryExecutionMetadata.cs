using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

public sealed record AstronomyKnowledgeQueryExecutionMetadata
{
    public AstronomyKnowledgeQueryExecutionMetadata(AstronomyKnowledgeQueryTarget target, AstronomyKnowledgeQueryFingerprint fingerprint, int sourceCount, int matchedCount, int returnedCount, int offset, int limit, bool hasMore, int appliedOrderCount = 0)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (sourceCount < 0) throw new ArgumentOutOfRangeException(nameof(sourceCount));
        if (matchedCount < 0) throw new ArgumentOutOfRangeException(nameof(matchedCount));
        if (returnedCount < 0) throw new ArgumentOutOfRangeException(nameof(returnedCount));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0 || limit > AstronomyKnowledgeQueryPage.MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        if (appliedOrderCount < 0) throw new ArgumentOutOfRangeException(nameof(appliedOrderCount));
        if (matchedCount > sourceCount) throw new ArgumentException("Matched count cannot exceed source count.", nameof(matchedCount));
        if (returnedCount > matchedCount) throw new ArgumentException("Returned count cannot exceed matched count.", nameof(returnedCount));
        if (hasMore != offset + returnedCount < matchedCount) throw new ArgumentException("HasMore is inconsistent with paging metadata.", nameof(hasMore));
        Target = target; Fingerprint = fingerprint; SourceCount = sourceCount; MatchedCount = matchedCount; ReturnedCount = returnedCount; Offset = offset; Limit = limit; HasMore = hasMore; AppliedOrderCount = appliedOrderCount;
    }
    public AstronomyKnowledgeQueryTarget Target { get; }
    public AstronomyKnowledgeQueryFingerprint Fingerprint { get; }
    public int SourceCount { get; }
    public int MatchedCount { get; }
    public int ReturnedCount { get; }
    public int Offset { get; }
    public int Limit { get; }
    public bool HasMore { get; }
    public int AppliedOrderCount { get; }
}
