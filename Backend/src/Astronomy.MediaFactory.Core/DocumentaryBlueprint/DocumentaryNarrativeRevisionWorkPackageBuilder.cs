using System.Globalization;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionWorkPackageBuilder
{
    public DocumentaryNarrativeRevisionWorkPackage Build(DocumentaryNarrativeDraft draft,DocumentaryNarrativeRevisionRequest revisionRequest,DocumentaryNarrativeRevisionExecutionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(revisionRequest); ArgumentNullException.ThrowIfNull(metadata);
        if(!string.Equals(draft.DraftId,revisionRequest.DraftId,StringComparison.Ordinal)||!string.Equals(draft.Version,revisionRequest.DraftVersion,StringComparison.Ordinal))throw new ArgumentException("Draft and revision request lineage must match.",nameof(revisionRequest));
        var orderedPassages=draft.Sections.SelectMany(s=>s.Passages.Select(p=>(Section:s,Passage:p))).ToArray();
        foreach(var item in revisionRequest.Items)
        {
            if(!string.Equals(item.DraftId,draft.DraftId,StringComparison.Ordinal))throw new ArgumentException("Revision item draft identity must match.",nameof(revisionRequest));
            Guard.Enum(item.Action,nameof(revisionRequest));
            if(!item.RequiresPassageText)continue;
            if(string.IsNullOrWhiteSpace(item.PassageId))throw new ArgumentException("Passage text work requires a passage identity.",nameof(revisionRequest));
            var matches=orderedPassages.Where(x=>string.Equals(x.Passage.PassageId,item.PassageId,StringComparison.Ordinal)).ToArray();
            if(matches.Length!=1)throw new ArgumentException("Target passage must exist exactly once.",nameof(revisionRequest));
            var target=matches[0];
            if(item.SectionId is not null&&!string.Equals(item.SectionId,target.Section.SectionId,StringComparison.Ordinal)||item.SectionNumber is not null&&item.SectionNumber!=target.Section.SectionNumber||item.PassageNumber is not null&&item.PassageNumber!=target.Passage.PassageNumber)throw new ArgumentException("Revision item scope conflicts with its passage.",nameof(revisionRequest));
        }
        var textGroups=new List<List<DocumentaryNarrativeRevisionItem>>();
        foreach(var item in revisionRequest.Items.Where(x=>x.RequiresPassageText)) { var group=textGroups.FirstOrDefault(x=>string.Equals(x[0].PassageId,item.PassageId,StringComparison.Ordinal)); if(group is null){group=[];textGroups.Add(group);} group.Add(item); }
        var passageItems=textGroups.Select((items,index)=>
        {
            var located=orderedPassages.Select((x,i)=>(x,i)).Single(x=>string.Equals(x.x.Passage.PassageId,items[0].PassageId,StringComparison.Ordinal)); var s=located.x.Section; var p=located.x.Passage; var sequence=index+1;
            DocumentaryNarrativePassageContext Context(int i)=>new(orderedPassages[i].Passage.PassageId,orderedPassages[i].Passage.PassageNumber,orderedPassages[i].Passage.Title,orderedPassages[i].Passage.Text);
            return new DocumentaryNarrativePassageRevisionWorkItem(revisionRequest.RevisionRequestId+".passage-work."+sequence.ToString(CultureInfo.InvariantCulture),sequence,revisionRequest.RevisionRequestId,draft.DraftId,draft.Version,s.SectionId,s.SectionNumber,s.Title,p.PassageId,p.PassageNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.Text,items.Select(x=>x.RevisionItemId).ToArray(),items.Select(x=>x.RuleCode).ToArray(),items.Select(x=>x.Severity).ToArray(),items.Select(x=>x.Action).ToArray(),items.Select(x=>x.Message).ToArray(),located.i==0?null:Context(located.i-1),located.i==orderedPassages.Length-1?null:Context(located.i+1));
        }).ToArray();
        var manual=revisionRequest.Items.Where(x=>!x.RequiresPassageText).Select((x,i)=>new DocumentaryNarrativeManualReviewWorkItem(revisionRequest.RevisionRequestId+".manual-work."+(i+1).ToString(CultureInfo.InvariantCulture),i+1,revisionRequest.RevisionRequestId,x.RevisionItemId,x.RuleCode,x.Severity,x.Action,x.Message,x.DraftId,x.SectionId,x.SectionNumber,x.PassageId,x.PassageNumber,x.FieldName)).ToArray();
        var package=new DocumentaryNarrativeRevisionWorkPackage(revisionRequest.RevisionRequestId+".work-package.v1",revisionRequest.RevisionRequestId,draft.DraftId,draft.Version,draft.SubjectId,draft.SubjectName,draft.PublicationFormat,draft.PrimaryLanguage,metadata,passageItems,manual);
        if(package.RevisionItemCount!=revisionRequest.Items.Count)throw new InvalidOperationException("Every revision item must be represented exactly once."); return package;
    }
}
