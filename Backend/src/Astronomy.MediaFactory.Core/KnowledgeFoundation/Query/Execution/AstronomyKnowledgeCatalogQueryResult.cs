using Astronomy.MediaFactory.Core.KnowledgeFoundation.Catalog;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.Query;

namespace Astronomy.MediaFactory.Core.KnowledgeFoundation.Query.Execution;

public sealed class AstronomyKnowledgeCatalogQueryResult
{
    public AstronomyKnowledgeCatalogQueryResult(AstronomyKnowledgeQueryExecutionStatus status, IEnumerable<AstronomyKnowledgeCatalogEntry> items, AstronomyKnowledgeQueryExecutionMetadata metadata, IEnumerable<AstronomyKnowledgeQueryValidationIssue>? validationIssues = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(items); Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        if (metadata.Target != AstronomyKnowledgeQueryTarget.CatalogEntry) throw new ArgumentException("Metadata target must be CatalogEntry.", nameof(metadata));
        Items = Array.AsReadOnly(items.Select(i => i ?? throw new ArgumentException("Items cannot contain null.", nameof(items))).ToArray());
        ValidationIssues = Array.AsReadOnly((validationIssues ?? []).Select(i => i ?? throw new ArgumentException("Validation issues cannot contain null.", nameof(validationIssues))).ToArray());
        if (status == AstronomyKnowledgeQueryExecutionStatus.Rejected && Items.Count != 0) throw new ArgumentException("Rejected result cannot contain items.", nameof(items));
        if (status == AstronomyKnowledgeQueryExecutionStatus.Succeeded && ValidationIssues.Count != 0) throw new ArgumentException("Successful result cannot contain validation issues.", nameof(validationIssues));
        Status = status;
    }
    public AstronomyKnowledgeQueryExecutionStatus Status { get; }
    public IReadOnlyList<AstronomyKnowledgeCatalogEntry> Items { get; }
    public AstronomyKnowledgeQueryExecutionMetadata Metadata { get; }
    public IReadOnlyList<AstronomyKnowledgeQueryValidationIssue> ValidationIssues { get; }
}
