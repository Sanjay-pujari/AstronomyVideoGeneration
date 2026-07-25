namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionCyclePlanner
{
    public DocumentaryNarrativeRevisionCyclePlan Plan(DocumentaryNarrativeDraft sourceDraft,
        DocumentaryNarrativeDraftValidationResult sourceValidationResult, string revisionRequestId,
        DocumentaryNarrativeRevisionRequestMetadata revisionRequestMetadata,
        DocumentaryNarrativeRevisionExecutionMetadata executionMetadata,
        DocumentaryNarrativeRevisionCycleMetadata cycleMetadata)
    {
        ArgumentNullException.ThrowIfNull(sourceDraft); ArgumentNullException.ThrowIfNull(sourceValidationResult);
        Guard.Required(revisionRequestId, nameof(revisionRequestId)); ArgumentNullException.ThrowIfNull(revisionRequestMetadata);
        ArgumentNullException.ThrowIfNull(executionMetadata); ArgumentNullException.ThrowIfNull(cycleMetadata);
        if (!string.Equals(sourceDraft.DraftId, sourceValidationResult.DraftId, StringComparison.Ordinal)) throw new ArgumentException("Validation result must identify the source draft.", nameof(sourceValidationResult));
        if (!string.Equals(sourceDraft.Version, revisionRequestMetadata.SourceDraftVersion, StringComparison.Ordinal)) throw new ArgumentException("Request metadata must identify the source version.", nameof(revisionRequestMetadata));
        if (!SameCorrelation(cycleMetadata.CorrelationId, revisionRequestMetadata.CorrelationId, executionMetadata.CorrelationId)) throw new ArgumentException("All cycle correlations must match exactly.");
        var revisionRequest = new DocumentaryNarrativeRevisionRequestBuilder().Build(sourceDraft, sourceValidationResult, revisionRequestId, revisionRequestMetadata);
        var workPackage = new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(sourceDraft, revisionRequest, executionMetadata);
        var cycleId = $"{sourceDraft.DraftId}.revision-cycle.{sourceDraft.Version}.{revisionRequest.RevisionRequestId}";
        var status = sourceValidationResult.Findings.Count == 0 ? DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired : DocumentaryNarrativeRevisionCycleStatus.AwaitingExternalRevision;
        return new(cycleId, sourceDraft, sourceValidationResult, revisionRequest, workPackage, cycleMetadata, status);
    }
    private static bool SameCorrelation(string expected, params string[] values) => values.All(value => string.Equals(expected, value, StringComparison.Ordinal));
}
