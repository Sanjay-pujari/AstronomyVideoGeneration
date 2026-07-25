using System.Globalization;
namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryNarrativeRevisionRequestBuilder
{
    public DocumentaryNarrativeRevisionRequest Build(DocumentaryNarrativeDraft draft,DocumentaryNarrativeDraftValidationResult validationResult,string revisionRequestId,DocumentaryNarrativeRevisionRequestMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(validationResult); Guard.Required(revisionRequestId,nameof(revisionRequestId)); ArgumentNullException.ThrowIfNull(metadata);
        if(!string.Equals(draft.DraftId,validationResult.DraftId,StringComparison.Ordinal))throw new ArgumentException("Validation result must identify the draft.",nameof(validationResult));
        if(!string.Equals(draft.Version,metadata.SourceDraftVersion,StringComparison.Ordinal))throw new ArgumentException("Metadata must identify the draft version.",nameof(metadata));
        var items=validationResult.Findings.Select((f,i)=>{var n=i+1;var a=DocumentaryNarrativeRevisionMappings.Action(f.RuleCode);return new DocumentaryNarrativeRevisionItem(revisionRequestId+".item."+n.ToString(CultureInfo.InvariantCulture),n,f.RuleCode,f.Severity,a,f.Message,f.DraftId,f.SectionId,f.SectionNumber,f.PassageId,f.PassageNumber,f.FieldName);}).ToArray();
        return new(revisionRequestId,draft.DraftId,draft.Version,validationResult,metadata,items);
    }
}
