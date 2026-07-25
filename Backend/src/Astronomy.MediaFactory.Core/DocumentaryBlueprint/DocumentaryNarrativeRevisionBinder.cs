namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeRevisionBinder
{
    public DocumentaryNarrativeRevisionResult Bind(DocumentaryNarrativeRevisionBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); var rr=request.RevisionRequest;
        if(rr.Items.Count==0){if(request.PassageRevisionInputs.Count!=0)throw new ArgumentException("A no-change request cannot accept inputs.",nameof(request));return new(rr.RevisionRequestId,request.OriginalDraft.DraftId,request.OriginalDraft.Version,request.OriginalDraft.DraftId,request.OriginalDraft.Version,DocumentaryNarrativeRevisionStatus.NoChangesRequired,request.OriginalDraft,[],[]);}
        var byId=rr.Items.ToDictionary(x=>x.RevisionItemId,StringComparer.Ordinal); var passages=request.OriginalDraft.Sections.SelectMany(x=>x.Passages).ToArray(); var resolved=new HashSet<string>(StringComparer.Ordinal); var replacements=new Dictionary<string,DocumentaryNarrativePassageRevisionInput>(StringComparer.Ordinal);
        foreach(var input in request.PassageRevisionInputs)
        {
            var items=input.RevisionItemIds.Select(id=>byId.TryGetValue(id,out var item)?item:throw new ArgumentException("Unknown revision item ID.",nameof(request))).ToArray();
            if(items.Any(x=>!x.RequiresPassageText))throw new ArgumentException("Manual-review items cannot be passage text inputs.",nameof(request));
            if(items.Any(x=>!string.Equals(x.PassageId,input.PassageId,StringComparison.Ordinal)))throw new ArgumentException("Revision items must target the input passage.",nameof(request));
            var applicable=rr.Items.Where(x=>x.RequiresPassageText&&string.Equals(x.PassageId,input.PassageId,StringComparison.Ordinal)).Select(x=>x.RevisionItemId).ToArray();
            if(!applicable.SequenceEqual(input.RevisionItemIds,StringComparer.Ordinal))throw new ArgumentException("An input must include all applicable item identities in request order.",nameof(request));
            var matches=passages.Where(x=>string.Equals(x.PassageId,input.PassageId,StringComparison.Ordinal)).ToArray(); if(matches.Length!=1)throw new ArgumentException("Target passage must exist exactly once.",nameof(request));
            if(!string.Equals(matches[0].Text,input.OriginalText,StringComparison.Ordinal))throw new ArgumentException("Original text is stale or mismatched.",nameof(request));
            foreach(var id in input.RevisionItemIds)resolved.Add(id); replacements.Add(input.PassageId,input);
        }
        var changes=request.PassageRevisionInputs.OrderBy(x=>rr.Items.First(i=>i.RevisionItemId==x.RevisionItemIds[0]).SequenceNumber).Select(x=>new DocumentaryNarrativeRevisionChange(x.RevisionItemIds,x.RevisionItemIds.Select(id=>byId[id].RuleCode).ToArray(),x.PassageId,x.OriginalText,x.RevisedText)).ToArray();
        var unresolved=rr.Items.Where(x=>!resolved.Contains(x.RevisionItemId)).ToArray(); var targetId=request.Metadata.SourceDraftId+".revision."+request.Metadata.TargetDraftVersion;
        var sections=request.OriginalDraft.Sections.Select(s=>new DocumentaryNarrativeDraftSection(s.SectionId,s.SectionNumber,s.SourceCompositionSectionId,s.Title,s.Purpose,s.NarrativeStage,s.SectionRole,s.Passages.Select(p=>Copy(p,replacements.TryGetValue(p.PassageId,out var input)?input.RevisedText:p.Text)).ToArray(),s.EstimatedDurationSeconds)).ToArray(); var d=request.OriginalDraft;
        var revised=new DocumentaryNarrativeDraft(targetId,d.CompositionId,d.BlueprintId,d.KnowledgeId,d.SubjectId,d.SubjectName,d.PublicationFormat,d.PrimaryLanguage,request.Metadata.TargetDraftVersion,d.Metadata,sections);
        var status=unresolved.Length==0?DocumentaryNarrativeRevisionStatus.Revised:DocumentaryNarrativeRevisionStatus.PartiallyRevised;
        return new(rr.RevisionRequestId,d.DraftId,d.Version,targetId,request.Metadata.TargetDraftVersion,status,revised,changes,unresolved);
    }
    private static DocumentaryNarrativePassage Copy(DocumentaryNarrativePassage p,string text)=>new(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.ViewerQuestion,p.Purpose,text,p.KnowledgeReferences,p.VisualOpportunities,p.Transition,p.EditorialOutcome,p.EstimatedDurationSeconds);
}
