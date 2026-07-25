namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionCycleCompleter
{
    public DocumentaryNarrativeRevisionCycleResult Complete(DocumentaryNarrativeRevisionCycleCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = request.Plan; var submission = request.Submission; var revisionMetadata = request.RevisionMetadata;
        static bool Eq(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Eq(submission.WorkPackageId, plan.WorkPackage.WorkPackageId) || !Eq(submission.RevisionRequestId, plan.RevisionRequest.RevisionRequestId) ||
            !Eq(submission.DraftId, plan.SourceDraftId) || !Eq(submission.DraftVersion, plan.SourceDraftVersion))
            throw new ArgumentException("Submission and plan lineage must match exactly.", nameof(request));
        if (!Eq(revisionMetadata.SourceDraftId, plan.SourceDraftId) || !Eq(revisionMetadata.SourceDraftVersion, plan.SourceDraftVersion))
            throw new ArgumentException("Revision metadata and plan lineage must match exactly.", nameof(request));
        var correlation = plan.Metadata.CorrelationId;
        if (!new[] { plan.RevisionRequest.Metadata.CorrelationId, plan.WorkPackage.Metadata.CorrelationId, submission.Metadata.CorrelationId, revisionMetadata.CorrelationId, request.CorrelationId }.All(x => Eq(correlation, x)))
            throw new ArgumentException("All cycle correlations must match exactly.", nameof(request));
        var bindingRequest = new DocumentaryNarrativeRevisionSubmissionAssembler().Assemble(plan.SourceDraft, plan.RevisionRequest, plan.WorkPackage, submission, revisionMetadata);
        var revisionResult = new DocumentaryNarrativeRevisionBinder().Bind(bindingRequest);
        var revisedValidation = new DocumentaryNarrativeDraftValidator().Validate(revisionResult.RevisedDraft);
        var comparison = new DocumentaryNarrativeRevisionValidationComparer().Compare(plan.SourceValidationResult, revisedValidation);
        var status = plan.Status == DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired && revisionResult.Status == DocumentaryNarrativeRevisionStatus.NoChangesRequired
            ? DocumentaryNarrativeRevisionCycleStatus.NoRevisionRequired
            : revisionResult.UnresolvedItemCount > 0 ? DocumentaryNarrativeRevisionCycleStatus.PartiallyCompleted
            : revisedValidation.Findings.Count > 0 ? DocumentaryNarrativeRevisionCycleStatus.CompletedWithRemainingFindings
            : DocumentaryNarrativeRevisionCycleStatus.CompletedSuccessfully;
        return new(plan, submission, bindingRequest, revisionResult, revisedValidation, comparison, request.CompletedUtc, request.CompletedBy, request.CompletionSchemaVersion, request.CorrelationId, status);
    }
}
