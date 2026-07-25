using System.Collections.ObjectModel;

namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;

public enum DocumentaryNarrativeRevisionAction { ReviewDraftStructure, RevisePassageText, RevisePassageOpening, AddTerminalPunctuation, DifferentiatePassageText, DifferentiatePassageTitle, CorrectPassageType, CorrectPassageNumber, CorrectSourceIdentity, ReviewDuration }
public enum DocumentaryNarrativeRevisionStatus { NoChangesRequired, Revised, PartiallyRevised, Rejected }

/// <summary>Externally supplied provenance for a deterministic revision request.</summary>
public sealed record DocumentaryNarrativeRevisionRequestMetadata
{
    public DocumentaryNarrativeRevisionRequestMetadata(DateTimeOffset createdUtc, string createdBy, string sourceDraftVersion, string validationSchemaVersion, string revisionRequestSchemaVersion, string correlationId)
    {
        CreatedUtc=createdUtc!=default?createdUtc:throw new ArgumentException("A non-default creation timestamp is required.",nameof(createdUtc));
        CreatedBy=Guard.Required(createdBy,nameof(createdBy)); SourceDraftVersion=Guard.Required(sourceDraftVersion,nameof(sourceDraftVersion));
        ValidationSchemaVersion=Guard.Required(validationSchemaVersion,nameof(validationSchemaVersion));
        RevisionRequestSchemaVersion=revisionRequestSchemaVersion=="1.0"?revisionRequestSchemaVersion:throw new ArgumentException("Revision request schema version must be 1.0.",nameof(revisionRequestSchemaVersion));
        CorrelationId=Guard.Required(correlationId,nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public string SourceDraftVersion{get;} public string ValidationSchemaVersion{get;} public string RevisionRequestSchemaVersion{get;} public string CorrelationId{get;}
}

/// <summary>One validation finding translated into externally actionable revision work.</summary>
public sealed record DocumentaryNarrativeRevisionItem
{
    public DocumentaryNarrativeRevisionItem(string revisionItemId,int sequenceNumber,string ruleCode,DocumentaryNarrativeDraftValidationSeverity severity,DocumentaryNarrativeRevisionAction action,string message,string draftId,string? sectionId=null,int? sectionNumber=null,string? passageId=null,int? passageNumber=null,string? fieldName=null)
    {
        RevisionItemId=Guard.Required(revisionItemId,nameof(revisionItemId)); if(sequenceNumber<=0)throw new ArgumentOutOfRangeException(nameof(sequenceNumber)); SequenceNumber=sequenceNumber;
        RuleCode=Guard.Required(ruleCode,nameof(ruleCode)); Guard.Enum(severity,nameof(severity)); Severity=severity; Guard.Enum(action,nameof(action)); Action=action;
        Message=Guard.Required(message,nameof(message)); DraftId=Guard.Required(draftId,nameof(draftId)); SectionId=Guard.OptionalIdentifier(sectionId,nameof(sectionId)); SectionNumber=sectionNumber;
        PassageId=Guard.OptionalIdentifier(passageId,nameof(passageId)); PassageNumber=passageNumber; FieldName=Guard.OptionalIdentifier(fieldName,nameof(fieldName));
    }
    public string RevisionItemId{get;} public int SequenceNumber{get;} public string RuleCode{get;} public DocumentaryNarrativeDraftValidationSeverity Severity{get;} public DocumentaryNarrativeRevisionAction Action{get;} public string Message{get;} public string DraftId{get;} public string? SectionId{get;} public int? SectionNumber{get;} public string? PassageId{get;} public int? PassageNumber{get;} public string? FieldName{get;}
    public bool RequiresPassageText=>DocumentaryNarrativeRevisionMappings.RequiresPassageText(Action);
}

public sealed class DocumentaryNarrativeRevisionRequest
{
    public DocumentaryNarrativeRevisionRequest(string revisionRequestId,string draftId,string draftVersion,DocumentaryNarrativeDraftValidationResult validationResult,DocumentaryNarrativeRevisionRequestMetadata metadata,IReadOnlyList<DocumentaryNarrativeRevisionItem> items)
    {
        RevisionRequestId=Guard.Required(revisionRequestId,nameof(revisionRequestId)); DraftId=Guard.Required(draftId,nameof(draftId)); DraftVersion=Guard.Required(draftVersion,nameof(draftVersion));
        ValidationResult=validationResult??throw new ArgumentNullException(nameof(validationResult)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); Items=Guard.Copy(items,nameof(items));
        if(!string.Equals(ValidationResult.DraftId,DraftId,StringComparison.Ordinal))throw new ArgumentException("Validation result must identify the draft.",nameof(validationResult));
        if(!string.Equals(Metadata.SourceDraftVersion,DraftVersion,StringComparison.Ordinal))throw new ArgumentException("Metadata must identify the draft version.",nameof(metadata));
        if(Items.Count!=ValidationResult.Findings.Count)throw new ArgumentException("Items must correspond one-to-one with findings.",nameof(items));
        if(Items.Any(x=>!string.Equals(x.DraftId,DraftId,StringComparison.Ordinal))||Items.Select(x=>x.SequenceNumber).Distinct().Count()!=Items.Count||Items.Select(x=>x.RevisionItemId).Distinct(StringComparer.Ordinal).Count()!=Items.Count)throw new ArgumentException("Revision items must have consistent, unique identities.",nameof(items));
    }
    public string RevisionRequestId{get;} public string DraftId{get;} public string DraftVersion{get;} public DocumentaryNarrativeDraftValidationResult ValidationResult{get;} public DocumentaryNarrativeRevisionRequestMetadata Metadata{get;} public IReadOnlyList<DocumentaryNarrativeRevisionItem> Items{get;}
    public bool RequiresRevision=>Items.Count>0; public int PassageTextRevisionCount=>Items.Count(x=>x.RequiresPassageText); public int ManualReviewCount=>Items.Count-PassageTextRevisionCount;
}

/// <summary>One final replacement text resolving all text findings for one passage.</summary>
public sealed record DocumentaryNarrativePassageRevisionInput
{
    public DocumentaryNarrativePassageRevisionInput(IReadOnlyList<string> revisionItemIds,string passageId,string originalText,string revisedText)
    {
        RevisionItemIds=Guard.Copy(revisionItemIds,nameof(revisionItemIds)); if(RevisionItemIds.Count==0||RevisionItemIds.Any(string.IsNullOrWhiteSpace)||RevisionItemIds.Distinct(StringComparer.Ordinal).Count()!=RevisionItemIds.Count)throw new ArgumentException("Revision item IDs must be nonempty and unique.",nameof(revisionItemIds));
        PassageId=Guard.Required(passageId,nameof(passageId)); OriginalText=Guard.Required(originalText,nameof(originalText)); RevisedText=Guard.Required(revisedText,nameof(revisedText));
    }
    public IReadOnlyList<string> RevisionItemIds{get;} public string PassageId{get;} public string OriginalText{get;} public string RevisedText{get;}
}

public sealed record DocumentaryNarrativeRevisionMetadata
{
    public DocumentaryNarrativeRevisionMetadata(DateTimeOffset createdUtc,string createdBy,string sourceDraftId,string sourceDraftVersion,string targetDraftVersion,string revisionSchemaVersion,string correlationId)
    {
        CreatedUtc=createdUtc!=default?createdUtc:throw new ArgumentException("A non-default creation timestamp is required.",nameof(createdUtc)); CreatedBy=Guard.Required(createdBy,nameof(createdBy)); SourceDraftId=Guard.Required(sourceDraftId,nameof(sourceDraftId)); SourceDraftVersion=Guard.Required(sourceDraftVersion,nameof(sourceDraftVersion)); TargetDraftVersion=Guard.Required(targetDraftVersion,nameof(targetDraftVersion));
        if(string.Equals(SourceDraftVersion,TargetDraftVersion,StringComparison.Ordinal))throw new ArgumentException("Source and target versions must differ.",nameof(targetDraftVersion)); RevisionSchemaVersion=revisionSchemaVersion=="1.0"?revisionSchemaVersion:throw new ArgumentException("Revision schema version must be 1.0.",nameof(revisionSchemaVersion)); CorrelationId=Guard.Required(correlationId,nameof(correlationId));
    }
    public DateTimeOffset CreatedUtc{get;} public string CreatedBy{get;} public string SourceDraftId{get;} public string SourceDraftVersion{get;} public string TargetDraftVersion{get;} public string RevisionSchemaVersion{get;} public string CorrelationId{get;}
}

public sealed class DocumentaryNarrativeRevisionBindingRequest
{
    public DocumentaryNarrativeRevisionBindingRequest(DocumentaryNarrativeDraft originalDraft,DocumentaryNarrativeRevisionRequest revisionRequest,DocumentaryNarrativeRevisionMetadata metadata,IReadOnlyList<DocumentaryNarrativePassageRevisionInput> passageRevisionInputs)
    {
        OriginalDraft=originalDraft??throw new ArgumentNullException(nameof(originalDraft)); RevisionRequest=revisionRequest??throw new ArgumentNullException(nameof(revisionRequest)); Metadata=metadata??throw new ArgumentNullException(nameof(metadata)); PassageRevisionInputs=Guard.Copy(passageRevisionInputs,nameof(passageRevisionInputs));
        if(!string.Equals(OriginalDraft.DraftId,RevisionRequest.DraftId,StringComparison.Ordinal)||!string.Equals(OriginalDraft.DraftId,Metadata.SourceDraftId,StringComparison.Ordinal)||!string.Equals(OriginalDraft.Version,Metadata.SourceDraftVersion,StringComparison.Ordinal))throw new ArgumentException("Draft, request, and metadata lineage must match.");
        if(PassageRevisionInputs.Select(x=>x.PassageId).Distinct(StringComparer.Ordinal).Count()!=PassageRevisionInputs.Count||PassageRevisionInputs.SelectMany(x=>x.RevisionItemIds).Distinct(StringComparer.Ordinal).Count()!=PassageRevisionInputs.Sum(x=>x.RevisionItemIds.Count))throw new ArgumentException("Passage and revision item inputs must be unique.",nameof(passageRevisionInputs));
    }
    public DocumentaryNarrativeDraft OriginalDraft{get;} public DocumentaryNarrativeRevisionRequest RevisionRequest{get;} public DocumentaryNarrativeRevisionMetadata Metadata{get;} public IReadOnlyList<DocumentaryNarrativePassageRevisionInput> PassageRevisionInputs{get;}
}

public sealed record DocumentaryNarrativeRevisionChange
{
    public DocumentaryNarrativeRevisionChange(IReadOnlyList<string> revisionItemIds,IReadOnlyList<string> ruleCodes,string passageId,string originalText,string revisedText)
    { RevisionItemIds=Guard.Copy(revisionItemIds,nameof(revisionItemIds)); RuleCodes=Guard.Copy(ruleCodes,nameof(ruleCodes)); if(RevisionItemIds.Count==0||RevisionItemIds.Count!=RuleCodes.Count||RevisionItemIds.Any(string.IsNullOrWhiteSpace)||RuleCodes.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Ordered item IDs and rule codes must be nonempty and aligned."); PassageId=Guard.Required(passageId,nameof(passageId)); OriginalText=Guard.Required(originalText,nameof(originalText)); RevisedText=Guard.Required(revisedText,nameof(revisedText)); }
    public IReadOnlyList<string> RevisionItemIds{get;} public IReadOnlyList<string> RuleCodes{get;} public string PassageId{get;} public string OriginalText{get;} public string RevisedText{get;}
}

public sealed class DocumentaryNarrativeRevisionResult
{
    public DocumentaryNarrativeRevisionResult(string revisionRequestId,string sourceDraftId,string sourceDraftVersion,string targetDraftId,string targetDraftVersion,DocumentaryNarrativeRevisionStatus status,DocumentaryNarrativeDraft revisedDraft,IReadOnlyList<DocumentaryNarrativeRevisionChange> changes,IReadOnlyList<DocumentaryNarrativeRevisionItem> unresolvedItems)
    { RevisionRequestId=Guard.Required(revisionRequestId,nameof(revisionRequestId)); SourceDraftId=Guard.Required(sourceDraftId,nameof(sourceDraftId)); SourceDraftVersion=Guard.Required(sourceDraftVersion,nameof(sourceDraftVersion)); TargetDraftId=Guard.Required(targetDraftId,nameof(targetDraftId)); TargetDraftVersion=Guard.Required(targetDraftVersion,nameof(targetDraftVersion)); Guard.Enum(status,nameof(status)); Status=status; RevisedDraft=revisedDraft??throw new ArgumentNullException(nameof(revisedDraft)); Changes=Guard.Copy(changes,nameof(changes)); UnresolvedItems=Guard.Copy(unresolvedItems,nameof(unresolvedItems)); if(!string.Equals(RevisedDraft.DraftId,TargetDraftId,StringComparison.Ordinal)||!string.Equals(RevisedDraft.Version,TargetDraftVersion,StringComparison.Ordinal))throw new ArgumentException("Revised draft target identity is inconsistent.",nameof(revisedDraft)); }
    public string RevisionRequestId{get;} public string SourceDraftId{get;} public string SourceDraftVersion{get;} public string TargetDraftId{get;} public string TargetDraftVersion{get;} public DocumentaryNarrativeRevisionStatus Status{get;} public DocumentaryNarrativeDraft RevisedDraft{get;} public IReadOnlyList<DocumentaryNarrativeRevisionChange> Changes{get;} public IReadOnlyList<DocumentaryNarrativeRevisionItem> UnresolvedItems{get;} public int AppliedChangeCount=>Changes.Count; public int UnresolvedItemCount=>UnresolvedItems.Count;
}
