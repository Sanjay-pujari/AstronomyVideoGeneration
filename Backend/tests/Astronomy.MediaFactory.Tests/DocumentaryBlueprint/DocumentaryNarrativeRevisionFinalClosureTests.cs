using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeRevisionClosureFixture
{
    internal static DocumentaryNarrativeDraft Draft() => OrionDocumentaryNarrativeRevisionFixture.ValidDraft();
    internal static DocumentaryNarrativeDraftValidationFinding Finding(DocumentaryNarrativeDraft d, int passageIndex, string rule, DocumentaryNarrativeDraftValidationSeverity severity = DocumentaryNarrativeDraftValidationSeverity.Error)
    {
        var section = d.Sections.SelectMany(s => s.Passages.Select(p => (s, p))).ElementAt(passageIndex);
        return new(rule, severity, $"finding {rule}", d.DraftId, section.s.SectionId, section.s.SectionNumber, section.p.PassageId, section.p.PassageNumber, "Text");
    }
    internal static DocumentaryNarrativeRevisionRequest Build(params DocumentaryNarrativeDraftValidationFinding[] findings)
    {
        var d=Draft(); return new DocumentaryNarrativeRevisionRequestBuilder().Build(d,new(d.DraftId,findings),OrionDocumentaryNarrativeRevisionFixture.RequestId,OrionDocumentaryNarrativeRevisionFixture.RequestMetadata());
    }
    internal static DocumentaryNarrativeRevisionRequest MultiPassageRevisionRequest()=>Build(Finding(Draft(),0,"DND-QUALITY-011",DocumentaryNarrativeDraftValidationSeverity.Warning),Finding(Draft(),0,"DND-QUALITY-012",DocumentaryNarrativeDraftValidationSeverity.Warning),Finding(Draft(),2,"DND-QUALITY-008"));
    internal static DocumentaryNarrativeRevisionRequest MixedRevisionRequest()=>Build(Finding(Draft(),0,"DND-QUALITY-011",DocumentaryNarrativeDraftValidationSeverity.Warning),Finding(Draft(),1,"DND-QUALITY-014",DocumentaryNarrativeDraftValidationSeverity.Warning),Finding(Draft(),2,"DND-QUALITY-012",DocumentaryNarrativeDraftValidationSeverity.Warning));
    internal static DocumentaryNarrativeRevisionRequest ManualReviewOnlyRequest()=>Build(Finding(Draft(),0,"DND-QUALITY-014",DocumentaryNarrativeDraftValidationSeverity.Warning),Finding(Draft(),2,"DND-QUALITY-018",DocumentaryNarrativeDraftValidationSeverity.Warning));
    internal static DocumentaryNarrativePassageRevisionInput Input(DocumentaryNarrativeRevisionRequest r,int passageIndex,string suffix=" revised")
    { var p=Draft().Sections.SelectMany(s=>s.Passages).ElementAt(passageIndex); return new(r.Items.Where(i=>i.RequiresPassageText&&i.PassageId==p.PassageId).Select(i=>i.RevisionItemId).ToArray(),p.PassageId,p.Text,p.Text+suffix); }
    internal static DocumentaryNarrativeRevisionBindingRequest PartialBindingRequest()=>new(Draft(),MixedRevisionRequest(),OrionDocumentaryNarrativeRevisionFixture.MetadataFor(Draft()),[Input(MixedRevisionRequest(),0)]);
    internal static DocumentaryNarrativeRevisionBindingRequest CompleteMultiPassageBindingRequest(bool reverse=false){var r=MultiPassageRevisionRequest();var inputs=new[]{Input(r,0),Input(r,2)};return new(Draft(),r,OrionDocumentaryNarrativeRevisionFixture.MetadataFor(Draft()),reverse?inputs.Reverse().ToArray():inputs);}
    internal static DocumentaryNarrativeRevisionBindingRequest ManualReviewOnlyBindingRequest()=>new(Draft(),ManualReviewOnlyRequest(),OrionDocumentaryNarrativeRevisionFixture.MetadataFor(Draft()),[]);
}

public sealed class DocumentaryNarrativeRevisionRequestFinalContractTests
{
    private static DocumentaryNarrativeRevisionRequest Valid(){var d=OrionDocumentaryNarrativeRevisionClosureFixture.Draft();return OrionDocumentaryNarrativeRevisionClosureFixture.Build(OrionDocumentaryNarrativeRevisionClosureFixture.Finding(d,0,"DND-QUALITY-011"));}
    [Theory][InlineData("id")][InlineData("draft")][InlineData("version")]
    public void Rejects_each_blank_identity(string field){var r=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(field=="id"?" ":r.RevisionRequestId,field=="draft"?" ":r.DraftId,field=="version"?" ":r.DraftVersion,r.ValidationResult,r.Metadata,r.Items));}
    [Fact] public void Rejects_null_validation_result(){var r=Valid();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,null!,r.Metadata,r.Items));}
    [Fact] public void Rejects_null_metadata(){var r=Valid();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,null!,r.Items));}
    [Fact] public void Rejects_null_items(){var r=Valid();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,r.Metadata,null!));}
    [Fact] public void Rejects_null_item_element(){var r=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,r.Metadata,[null!]));}
    [Fact] public void Rejects_validation_draft_mismatch(){var r=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,"other",r.DraftVersion,r.ValidationResult,r.Metadata,r.Items));}
    [Fact] public void Rejects_metadata_version_mismatch(){var r=Valid();var m=new DocumentaryNarrativeRevisionRequestMetadata(r.Metadata.CreatedUtc,"x","V1","1.0","1.0","c");Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,m,r.Items));}
    [Fact] public void Rejects_item_count_mismatch(){var r=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,r.Metadata,[]));}
    [Fact] public void Rejects_item_draft_mismatch(){var r=Valid();var i=Copy(r.Items[0],draft:"other");Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,r.Metadata,[i]));}
    [Theory][InlineData(true)][InlineData(false)] public void Rejects_duplicate_sequence_or_id(bool sequence){var r=Valid();var f=r.ValidationResult.Findings[0];var validation=new DocumentaryNarrativeDraftValidationResult(r.DraftId,[f,f]);var a=r.Items[0];var b=Copy(a,id:sequence?"other":a.RevisionItemId,seq:sequence?a.SequenceNumber:2);Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,validation,r.Metadata,[a,b]));}
    [Fact] public void Valid_construction_preserves_values_order_counts_and_is_immutable(){var r=OrionDocumentaryNarrativeRevisionClosureFixture.MixedRevisionRequest();var source=r.Items.ToList();var copy=new DocumentaryNarrativeRevisionRequest(r.RevisionRequestId,r.DraftId,r.DraftVersion,r.ValidationResult,r.Metadata,source);source.Clear();Assert.Equal(r.RevisionRequestId,copy.RevisionRequestId);Assert.Same(r.ValidationResult,copy.ValidationResult);Assert.Same(r.Metadata,copy.Metadata);Assert.Equal(r.Items,copy.Items);Assert.True(copy.RequiresRevision);Assert.Equal(2,copy.PassageTextRevisionCount);Assert.Equal(1,copy.ManualReviewCount);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativeRevisionItem>)copy.Items).Clear());}
    private static DocumentaryNarrativeRevisionItem Copy(DocumentaryNarrativeRevisionItem x,string? id=null,int? seq=null,string? draft=null)=>new(id??x.RevisionItemId,seq??x.SequenceNumber,x.RuleCode,x.Severity,x.Action,x.Message,draft??x.DraftId,x.SectionId,x.SectionNumber,x.PassageId,x.PassageNumber,x.FieldName);
}

public sealed class DocumentaryNarrativeRevisionBindingRequestFinalContractTests
{
    [Fact] public void Rejects_each_null_argument(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionBindingRequest(null!,b.RevisionRequest,b.Metadata,b.PassageRevisionInputs));Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,null!,b.Metadata,b.PassageRevisionInputs));Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,null!,b.PassageRevisionInputs));Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,b.Metadata,null!));}
    [Fact] public void Rejects_null_input_element(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,b.Metadata,[null!]));}
    [Fact] public void Rejects_duplicate_passage(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,b.Metadata,[b.PassageRevisionInputs[0],b.PassageRevisionInputs[0]]));}
    [Fact] public void Rejects_duplicate_item_coverage(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();var i=b.PassageRevisionInputs[0];var other=new DocumentaryNarrativePassageRevisionInput(i.RevisionItemIds,b.PassageRevisionInputs[1].PassageId,i.OriginalText,i.RevisedText);Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,b.Metadata,[i,other]));}
    [Theory][InlineData("request")][InlineData("metadataId")][InlineData("metadataVersion")]
    public void Rejects_each_lineage_and_case_sensitive_mismatch(string kind){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();var d=b.OriginalDraft;var request=kind=="request"?new DocumentaryNarrativeRevisionRequest(b.RevisionRequest.RevisionRequestId,d.DraftId.ToUpperInvariant(),d.Version,new(d.DraftId.ToUpperInvariant(),[]),b.RevisionRequest.Metadata,[]):b.RevisionRequest;var metadata=kind switch{"metadataId"=>new DocumentaryNarrativeRevisionMetadata(b.Metadata.CreatedUtc,"x",d.DraftId.ToUpperInvariant(),d.Version,"2","1.0","c"),"metadataVersion"=>new DocumentaryNarrativeRevisionMetadata(b.Metadata.CreatedUtc,"x",d.DraftId,d.Version+"X","2","1.0","c"),_=>b.Metadata};Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBindingRequest(d,request,metadata,[]));}
    [Fact] public void Preserves_objects_order_and_defensively_copies(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest(true);var list=b.PassageRevisionInputs.ToList();var x=new DocumentaryNarrativeRevisionBindingRequest(b.OriginalDraft,b.RevisionRequest,b.Metadata,list);list.Clear();Assert.Same(b.OriginalDraft,x.OriginalDraft);Assert.Same(b.RevisionRequest,x.RevisionRequest);Assert.Same(b.Metadata,x.Metadata);Assert.Equal(b.PassageRevisionInputs,x.PassageRevisionInputs);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativePassageRevisionInput>)x.PassageRevisionInputs).Clear());}
}

public sealed class DocumentaryNarrativeRevisionResultFinalContractTests
{
    private static DocumentaryNarrativeRevisionResult Valid()=>new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionClosureFixture.PartialBindingRequest());
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)] public void Rejects_each_blank_lineage(int field){var x=Valid();var v=new[]{x.RevisionRequestId,x.SourceDraftId,x.SourceDraftVersion,x.TargetDraftId,x.TargetDraftVersion};v[field]=" ";Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionResult(v[0],v[1],v[2],v[3],v[4],x.Status,x.RevisedDraft,x.Changes,x.UnresolvedItems));}
    [Fact] public void Rejects_undefined_status(){var x=Valid();Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentaryNarrativeRevisionResult(x.RevisionRequestId,x.SourceDraftId,x.SourceDraftVersion,x.TargetDraftId,x.TargetDraftVersion,(DocumentaryNarrativeRevisionStatus)99,x.RevisedDraft,x.Changes,x.UnresolvedItems));}
    [Fact] public void Rejects_null_draft_changes_and_unresolved(){var x=Valid();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1","t","2",x.Status,null!,x.Changes,x.UnresolvedItems));Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1",x.TargetDraftId,x.TargetDraftVersion,x.Status,x.RevisedDraft,null!,x.UnresolvedItems));Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1",x.TargetDraftId,x.TargetDraftVersion,x.Status,x.RevisedDraft,x.Changes,null!));}
    [Fact] public void Rejects_null_collection_elements(){var x=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1",x.TargetDraftId,x.TargetDraftVersion,x.Status,x.RevisedDraft,[null!],x.UnresolvedItems));Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1",x.TargetDraftId,x.TargetDraftVersion,x.Status,x.RevisedDraft,x.Changes,[null!]));}
    [Theory][InlineData(true)][InlineData(false)] public void Rejects_target_identity_mismatch(bool id){var x=Valid();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionResult("r","s","1",id?"other":x.TargetDraftId,id?x.TargetDraftVersion:"other",x.Status,x.RevisedDraft,x.Changes,x.UnresolvedItems));}
    [Fact] public void All_valid_statuses_preserve_lineage_identity_and_derived_counts(){var results=new[]{new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionFixture.NoChangeBindingRequest()),new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest()),Valid()};Assert.Equal(new[]{DocumentaryNarrativeRevisionStatus.NoChangesRequired,DocumentaryNarrativeRevisionStatus.Revised,DocumentaryNarrativeRevisionStatus.PartiallyRevised},results.Select(x=>x.Status));Assert.All(results,x=>{Assert.Equal(x.RevisedDraft.DraftId,x.TargetDraftId);Assert.Equal(x.RevisedDraft.Version,x.TargetDraftVersion);Assert.Equal(x.Changes.Count,x.AppliedChangeCount);Assert.Equal(x.UnresolvedItems.Count,x.UnresolvedItemCount);});}
    [Fact] public void Collections_are_defensively_copied_and_immutable(){var x=Valid();var c=x.Changes.ToList();var u=x.UnresolvedItems.ToList();var copy=new DocumentaryNarrativeRevisionResult(x.RevisionRequestId,x.SourceDraftId,x.SourceDraftVersion,x.TargetDraftId,x.TargetDraftVersion,x.Status,x.RevisedDraft,c,u);c.Clear();u.Clear();Assert.Equal(x.Changes,copy.Changes);Assert.Equal(x.UnresolvedItems,copy.UnresolvedItems);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativeRevisionChange>)copy.Changes).Clear());Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativeRevisionItem>)copy.UnresolvedItems).Clear());}
}

public sealed class DocumentaryNarrativeRevisionBinderFinalClosureTests
{
    private static readonly JsonSerializerOptions Web=new(JsonSerializerDefaults.Web);
    [Fact] public void Partial_revision_applies_one_change_and_preserves_ordered_unresolved_work(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.PartialBindingRequest();var x=new DocumentaryNarrativeRevisionBinder().Bind(b);Assert.Equal(DocumentaryNarrativeRevisionStatus.PartiallyRevised,x.Status);Assert.Single(x.Changes);Assert.Equal(b.PassageRevisionInputs[0].PassageId,x.Changes[0].PassageId);Assert.Equal(b.PassageRevisionInputs[0].RevisionItemIds,x.Changes[0].RevisionItemIds);Assert.Equal(b.PassageRevisionInputs[0].RevisionItemIds.Select(id=>b.RevisionRequest.Items.Single(i=>i.RevisionItemId==id).RuleCode),x.Changes[0].RuleCodes);Assert.Equal(b.RevisionRequest.Items.Where(i=>!b.PassageRevisionInputs[0].RevisionItemIds.Contains(i.RevisionItemId)),x.UnresolvedItems);Assert.Contains(x.UnresolvedItems,i=>!i.RequiresPassageText);Assert.Contains(x.UnresolvedItems,i=>i.RequiresPassageText);AssertDraftPreserved(b,x);}
    [Fact] public void Manual_review_only_is_partial_without_fake_changes(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.ManualReviewOnlyBindingRequest();var x=new DocumentaryNarrativeRevisionBinder().Bind(b);Assert.Equal(DocumentaryNarrativeRevisionStatus.PartiallyRevised,x.Status);Assert.Equal(0,x.AppliedChangeCount);Assert.Equal(b.RevisionRequest.Items,x.UnresolvedItems);Assert.Equal(b.RevisionRequest.Items.Count,x.UnresolvedItemCount);Assert.Empty(x.Changes);AssertDraftPreserved(b,x);}
    [Fact] public void Complete_multi_passage_revision_is_input_order_independent_and_ordered_by_request(){var binding=OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();var normal=new DocumentaryNarrativeRevisionBinder().Bind(binding);var reverse=new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest(true));Assert.Equal(DocumentaryNarrativeRevisionStatus.Revised,normal.Status);Assert.Equal(2,normal.AppliedChangeCount);Assert.Empty(normal.UnresolvedItems);Assert.Equal(JsonSerializer.Serialize(normal,Web),JsonSerializer.Serialize(reverse,Web));Assert.Equal(binding.RevisionRequest.Items.Where(i=>i.RequiresPassageText).Select(i=>i.PassageId).Distinct(),normal.Changes.Select(c=>c.PassageId));AssertDraftPreserved(binding,normal);}
    [Fact] public void Structural_failures_throw_and_never_return_rejected()
    {var d=OrionDocumentaryNarrativeRevisionClosureFixture.Draft();var mixed=OrionDocumentaryNarrativeRevisionClosureFixture.MixedRevisionRequest();var text=mixed.Items.First(i=>i.RequiresPassageText);var manual=mixed.Items.First(i=>!i.RequiresPassageText);var p0=d.Sections.SelectMany(s=>s.Passages).First();var p2=d.Sections.SelectMany(s=>s.Passages).ElementAt(2);var cases=new[]{new DocumentaryNarrativePassageRevisionInput([manual.RevisionItemId],p0.PassageId,p0.Text,p0.Text+"x"),new DocumentaryNarrativePassageRevisionInput([text.RevisionItemId,mixed.Items.Last().RevisionItemId],p0.PassageId,p0.Text,p0.Text+"x"),new DocumentaryNarrativePassageRevisionInput([text.RevisionItemId],p2.PassageId,p2.Text,p2.Text+"x"),new DocumentaryNarrativePassageRevisionInput(["unrelated"],p0.PassageId,p0.Text,p0.Text+"x")};foreach(var input in cases)Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBinder().Bind(new(d,mixed,OrionDocumentaryNarrativeRevisionFixture.MetadataFor(d),[input])));}
    [Fact] public void Missing_and_duplicate_target_passages_are_rejected(){var d=OrionDocumentaryNarrativeRevisionClosureFixture.Draft();var r=OrionDocumentaryNarrativeRevisionClosureFixture.MultiPassageRevisionRequest();var good=OrionDocumentaryNarrativeRevisionClosureFixture.Input(r,0);var missing=new DocumentaryNarrativePassageRevisionInput(good.RevisionItemIds,"missing",good.OriginalText,good.RevisedText);Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBinder().Bind(new(d,r,OrionDocumentaryNarrativeRevisionFixture.MetadataFor(d),[missing])));var section=d.Sections[1];var duplicateSection=new DocumentaryNarrativeDraftSection(section.SectionId,section.SectionNumber,section.SourceCompositionSectionId,section.Title,section.Purpose,section.NarrativeStage,section.SectionRole,[d.Sections[0].Passages[0],..section.Passages],section.EstimatedDurationSeconds);var duplicateDraft=new DocumentaryNarrativeDraft(d.DraftId,d.CompositionId,d.BlueprintId,d.KnowledgeId,d.SubjectId,d.SubjectName,d.PublicationFormat,d.PrimaryLanguage,d.Version,d.Metadata,[d.Sections[0],duplicateSection,..d.Sections.Skip(2)]);Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionBinder().Bind(new(duplicateDraft,r,OrionDocumentaryNarrativeRevisionFixture.MetadataFor(duplicateDraft),[good])));}
    [Fact] public void Binder_does_not_mutate_any_exact_input_object(){var b=OrionDocumentaryNarrativeRevisionClosureFixture.PartialBindingRequest();var before=new[]{JsonSerializer.Serialize(b.OriginalDraft,Web),JsonSerializer.Serialize(b.RevisionRequest,Web),JsonSerializer.Serialize(b.RevisionRequest.ValidationResult,Web),JsonSerializer.Serialize(b.Metadata,Web),JsonSerializer.Serialize(b.PassageRevisionInputs,Web)};var ids=b.PassageRevisionInputs.Select(i=>i.RevisionItemIds.ToArray()).ToArray();_ = new DocumentaryNarrativeRevisionBinder().Bind(b);Assert.Equal(before,new[]{JsonSerializer.Serialize(b.OriginalDraft,Web),JsonSerializer.Serialize(b.RevisionRequest,Web),JsonSerializer.Serialize(b.RevisionRequest.ValidationResult,Web),JsonSerializer.Serialize(b.Metadata,Web),JsonSerializer.Serialize(b.PassageRevisionInputs,Web)});Assert.Equal(ids,b.PassageRevisionInputs.Select(i=>i.RevisionItemIds.ToArray()));}
    [Fact]
    public void Revised_passages_preserve_nested_collection_values_without_requiring_reference_identity()
    {
        var binding = OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest();
        var result = new DocumentaryNarrativeRevisionBinder().Bind(binding);
        var originalPassage = binding.OriginalDraft.Sections[0].Passages[0];
        var revisedPassage = result.RevisedDraft.Sections[0].Passages[0];

        Assert.Equal(originalPassage.KnowledgeReferences.ToArray(), revisedPassage.KnowledgeReferences.ToArray());
        Assert.Equal(originalPassage.VisualOpportunities.ToArray(), revisedPassage.VisualOpportunities.ToArray());
        AssertDraftPreserved(binding, result);
    }

    private static void AssertDraftPreserved(DocumentaryNarrativeRevisionBindingRequest b, DocumentaryNarrativeRevisionResult x)
    {
        var o = b.OriginalDraft;
        var r = x.RevisedDraft;
        Assert.Equal((o.CompositionId,o.BlueprintId,o.KnowledgeId,o.SubjectId,o.SubjectName,o.PublicationFormat,o.PrimaryLanguage,o.Metadata,o.Sections.Count),(r.CompositionId,r.BlueprintId,r.KnowledgeId,r.SubjectId,r.SubjectName,r.PublicationFormat,r.PrimaryLanguage,r.Metadata,r.Sections.Count));
        Assert.Equal(o.DraftId + ".revision." + b.Metadata.TargetDraftVersion, r.DraftId);
        Assert.Equal(b.Metadata.TargetDraftVersion, r.Version);
        var revised = x.Changes.Select(c => c.PassageId).ToHashSet();
        for (var si = 0; si < o.Sections.Count; si++)
        {
            var a = o.Sections[si];
            var z = r.Sections[si];
            Assert.Equal((a.SectionId,a.SectionNumber,a.SourceCompositionSectionId,a.Title,a.Purpose,a.NarrativeStage,a.SectionRole,a.EstimatedDurationSeconds,a.Passages.Count),(z.SectionId,z.SectionNumber,z.SourceCompositionSectionId,z.Title,z.Purpose,z.NarrativeStage,z.SectionRole,z.EstimatedDurationSeconds,z.Passages.Count));
            for (var pi = 0; pi < a.Passages.Count; pi++)
            {
                var p = a.Passages[pi];
                var q = z.Passages[pi];
                Assert.Equal(p.PassageId, q.PassageId);
                Assert.Equal(p.PassageNumber, q.PassageNumber);
                Assert.Equal(p.SourceBeatId, q.SourceBeatId);
                Assert.Equal(p.SourceBeatNumber, q.SourceBeatNumber);
                Assert.Equal(p.SourceSceneId, q.SourceSceneId);
                Assert.Equal(p.SourceSceneNumber, q.SourceSceneNumber);
                Assert.Equal(p.Title, q.Title);
                Assert.Equal(p.PassageType, q.PassageType);
                Assert.Equal(p.NarrativeStage, q.NarrativeStage);
                Assert.Equal(p.SceneRole, q.SceneRole);
                Assert.Equal(p.ViewerQuestion, q.ViewerQuestion);
                Assert.Equal(p.Purpose, q.Purpose);

                Assert.Equal(p.KnowledgeReferences.Count, q.KnowledgeReferences.Count);
                for (var index = 0; index < p.KnowledgeReferences.Count; index++)
                {
                    var expected = p.KnowledgeReferences[index];
                    var actual = q.KnowledgeReferences[index];
                    Assert.Equal(expected.KnowledgeEntryId, actual.KnowledgeEntryId);
                    Assert.Equal(expected.Section, actual.Section);
                    Assert.Equal(expected.Purpose, actual.Purpose);
                    Assert.Equal(expected.IsPrimary, actual.IsPrimary);
                }

                Assert.Equal(p.VisualOpportunities.Count, q.VisualOpportunities.Count);
                for (var index = 0; index < p.VisualOpportunities.Count; index++)
                {
                    var expected = p.VisualOpportunities[index];
                    var actual = q.VisualOpportunities[index];
                    Assert.Equal(expected.Description, actual.Description);
                    Assert.Equal(expected.Type, actual.Type);
                    Assert.Equal(expected.KnowledgeEntryId, actual.KnowledgeEntryId);
                    Assert.Equal(expected.SourceAssetId, actual.SourceAssetId);
                    Assert.Equal(expected.IsScientificallyRequired, actual.IsScientificallyRequired);
                }

                Assert.Equal(p.Transition, q.Transition);
                Assert.Equal(p.EditorialOutcome, q.EditorialOutcome);
                Assert.Equal(p.EstimatedDurationSeconds, q.EstimatedDurationSeconds);

                if (revised.Contains(p.PassageId))
                {
                    Assert.NotEqual(p.Text, q.Text);
                    var change = Assert.Single(x.Changes.Where(c => string.Equals(c.PassageId, p.PassageId, StringComparison.Ordinal)));
                    Assert.Equal(p.Text, change.OriginalText);
                    Assert.Equal(q.Text, change.RevisedText);
                }
                else
                {
                    Assert.Equal(p.Text, q.Text);
                    Assert.Equal(JsonSerializer.Serialize(p, Web), JsonSerializer.Serialize(q, Web));
                }
            }
        }
    }
}

public sealed class DocumentaryNarrativeRevisionBuilderFinalClosureTests
{
    [Fact] public void Every_finding_field_maps_by_index_without_mutation(){var d=OrionDocumentaryNarrativeRevisionClosureFixture.Draft();var p=d.Sections.SelectMany(s=>s.Passages).First();var findings=new[]{new DocumentaryNarrativeDraftValidationFinding("DND-QUALITY-001",DocumentaryNarrativeDraftValidationSeverity.Error,"draft",d.DraftId),new("DND-QUALITY-018",DocumentaryNarrativeDraftValidationSeverity.Warning,"section",d.DraftId,d.Sections[0].SectionId,d.Sections[0].SectionNumber),new("DND-QUALITY-011",DocumentaryNarrativeDraftValidationSeverity.Warning,"passage",d.DraftId,d.Sections[0].SectionId,d.Sections[0].SectionNumber,p.PassageId,p.PassageNumber),new("DND-QUALITY-012",DocumentaryNarrativeDraftValidationSeverity.Error,"field",d.DraftId,d.Sections[0].SectionId,d.Sections[0].SectionNumber,p.PassageId,p.PassageNumber,"Text")};var validation=new DocumentaryNarrativeDraftValidationResult(d.DraftId,findings);var metadata=OrionDocumentaryNarrativeRevisionFixture.RequestMetadata();var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var before=(JsonSerializer.Serialize(d,options),JsonSerializer.Serialize(validation,options),JsonSerializer.Serialize(metadata,options));var r=new DocumentaryNarrativeRevisionRequestBuilder().Build(d,validation,"mapping",metadata);Assert.Equal(findings.Length,r.Items.Count);for(var i=0;i<findings.Length;i++){var f=findings[i];var item=r.Items[i];Assert.Equal((i+1,$"mapping.item.{i+1}",f.RuleCode,f.Severity,DocumentaryNarrativeRevisionMappings.Action(f.RuleCode),f.Message,f.DraftId,f.SectionId,f.SectionNumber,f.PassageId,f.PassageNumber,f.FieldName),(item.SequenceNumber,item.RevisionItemId,item.RuleCode,item.Severity,item.Action,item.Message,item.DraftId,item.SectionId,item.SectionNumber,item.PassageId,item.PassageNumber,item.FieldName));Assert.Equal(DocumentaryNarrativeRevisionMappings.RequiresPassageText(item.Action),item.RequiresPassageText);}Assert.Equal(before,(JsonSerializer.Serialize(d,options),JsonSerializer.Serialize(validation,options),JsonSerializer.Serialize(metadata,options)));Assert.Equal(findings,validation.Findings);}
    [Fact] public void Unknown_rule_is_rejected(){var d=OrionDocumentaryNarrativeRevisionClosureFixture.Draft();var v=new DocumentaryNarrativeDraftValidationResult(d.DraftId,[new("UNKNOWN",DocumentaryNarrativeDraftValidationSeverity.Error,"x",d.DraftId)]);Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentaryNarrativeRevisionRequestBuilder().Build(d,v,"r",OrionDocumentaryNarrativeRevisionFixture.RequestMetadata()));}
    [Fact] public void Independently_reconstructed_requests_and_bindings_have_identical_web_json(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var r1=OrionDocumentaryNarrativeRevisionClosureFixture.MultiPassageRevisionRequest();var r2=OrionDocumentaryNarrativeRevisionClosureFixture.MultiPassageRevisionRequest();Assert.Equal(JsonSerializer.Serialize(r1,options),JsonSerializer.Serialize(r2,options));var x1=new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest());var x2=new DocumentaryNarrativeRevisionBinder().Bind(OrionDocumentaryNarrativeRevisionClosureFixture.CompleteMultiPassageBindingRequest());Assert.Equal(JsonSerializer.Serialize(x1,options),JsonSerializer.Serialize(x2,options));}
}
