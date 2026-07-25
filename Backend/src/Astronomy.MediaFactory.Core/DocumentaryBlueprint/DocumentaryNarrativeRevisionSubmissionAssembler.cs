namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionSubmissionAssembler
{
    public DocumentaryNarrativeRevisionBindingRequest Assemble(DocumentaryNarrativeDraft draft,DocumentaryNarrativeRevisionRequest revisionRequest,DocumentaryNarrativeRevisionWorkPackage workPackage,DocumentaryNarrativeRevisionSubmission submission,DocumentaryNarrativeRevisionMetadata revisionMetadata)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(revisionRequest); ArgumentNullException.ThrowIfNull(workPackage); ArgumentNullException.ThrowIfNull(submission); ArgumentNullException.ThrowIfNull(revisionMetadata);
        static bool Eq(string a,string b)=>string.Equals(a,b,StringComparison.Ordinal);
        if(!Eq(draft.DraftId,revisionRequest.DraftId)||!Eq(draft.Version,revisionRequest.DraftVersion)||!Eq(workPackage.WorkPackageId,revisionRequest.RevisionRequestId+".work-package.v1")||!Eq(workPackage.RevisionRequestId,revisionRequest.RevisionRequestId)||!Eq(workPackage.DraftId,draft.DraftId)||!Eq(workPackage.DraftVersion,draft.Version)||!Eq(submission.WorkPackageId,workPackage.WorkPackageId)||!Eq(submission.RevisionRequestId,revisionRequest.RevisionRequestId)||!Eq(submission.DraftId,draft.DraftId)||!Eq(submission.DraftVersion,draft.Version)||!Eq(revisionMetadata.SourceDraftId,draft.DraftId)||!Eq(revisionMetadata.SourceDraftVersion,draft.Version))throw new ArgumentException("Draft, request, work package, submission, and metadata lineage must match exactly.");
        var inputs=new List<DocumentaryNarrativePassageRevisionInput>();
        foreach(var submitted in submission.PassageSubmissions)
        {
            var work=workPackage.PassageWorkItems.SingleOrDefault(x=>Eq(x.WorkItemId,submitted.WorkItemId))??throw new ArgumentException("Unknown passage work-item identity.",nameof(submission));
            if(!Eq(work.PassageId,submitted.PassageId))throw new ArgumentException("Work item and passage identities do not match.",nameof(submission));
            var sourcePassages=draft.Sections.SelectMany(x=>x.Passages).Where(x=>Eq(x.PassageId,submitted.PassageId)).ToArray();
            if(sourcePassages.Length!=1)throw new ArgumentException("Unknown or duplicated passage identity.",nameof(submission));
            var sourcePassage=sourcePassages[0];
            if(!Eq(sourcePassage.Text,work.OriginalText))throw new ArgumentException("Work-package original text does not match the source draft passage.",nameof(workPackage));
            if(!Eq(sourcePassage.Text,submitted.OriginalText))throw new ArgumentException("Submitted original text does not match the source draft passage.",nameof(submission));
            if(!Eq(work.OriginalText,submitted.OriginalText))throw new ArgumentException("Original text is stale or mismatched.",nameof(submission));
            if(!work.RevisionItemIds.SequenceEqual(submitted.ResolvedRevisionItemIds,StringComparer.Ordinal))throw new ArgumentException("A passage submission must resolve every applicable item in request order.",nameof(submission));
            var applicable=revisionRequest.Items.Where(x=>x.RequiresPassageText&&x.PassageId is not null&&Eq(x.PassageId,submitted.PassageId)).Select(x=>x.RevisionItemId);
            if(!applicable.SequenceEqual(submitted.ResolvedRevisionItemIds,StringComparer.Ordinal))throw new ArgumentException("Resolved identities must be the exact applicable request items.",nameof(submission));
            inputs.Add(new DocumentaryNarrativePassageRevisionInput(submitted.ResolvedRevisionItemIds,submitted.PassageId,submitted.OriginalText,submitted.RevisedText));
        }
        return new DocumentaryNarrativeRevisionBindingRequest(draft,revisionRequest,revisionMetadata,inputs);
    }
}
