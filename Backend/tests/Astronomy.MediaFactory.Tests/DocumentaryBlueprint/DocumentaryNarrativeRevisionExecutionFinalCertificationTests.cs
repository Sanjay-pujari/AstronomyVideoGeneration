using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

/// <summary>Final, requirement-shaped certification suite for the O2.8 execution boundary.</summary>
public sealed class DocumentaryNarrativeRevisionExecutionContractCertificationTests
{
    private static readonly DateTimeOffset Time = new(2026, 7, 25, 9, 8, 7, 123, TimeSpan.FromHours(5.5));

    [Fact]
    public void Passage_context_validates_preserves_whitespace_serializes_and_is_immutable()
    {
        foreach (var field in Enumerable.Range(0, 3))
        {
            var values = new[] { "id", " title ", "\r\n text \t" };
            values[field] = " ";
            Assert.Throws<ArgumentException>(() => new DocumentaryNarrativePassageContext(values[0], 7, values[1], values[2]));
        }
        var value = new DocumentaryNarrativePassageContext(" passage ", -2, " title ", "\r\n text \t");
        Assert.Equal((" passage ", -2, " title ", "\r\n text \t"), (value.PassageId, value.PassageNumber, value.Title, value.Text));
        AssertImmutable(value);
        RoundTrip(value);
    }

    [Fact]
    public void Passage_work_item_certifies_all_scalars_enums_aligned_collections_contexts_copying_and_order()
    {
        var previous = new DocumentaryNarrativePassageContext("prev", 1, " previous ", " previous text ");
        var next = new DocumentaryNarrativePassageContext("next", 3, " next ", " next text ");
        var ids = new List<string> { "z", "a" }; var rules = new List<string> { "r2", "r1" };
        var severities = new List<DocumentaryNarrativeDraftValidationSeverity> { DocumentaryNarrativeDraftValidationSeverity.Warning, DocumentaryNarrativeDraftValidationSeverity.Error };
        var actions = new List<DocumentaryNarrativeRevisionAction> { DocumentaryNarrativeRevisionAction.RevisePassageOpening, DocumentaryNarrativeRevisionAction.RevisePassageText };
        var messages = new List<string> { " second ", " first " };
        var value = Work(ids, rules, severities, actions, messages, previous, next);
        ids.Clear(); rules.Clear(); severities.Clear(); actions.Clear(); messages.Clear();
        Assert.Equal((" work ", 4, " request ", " draft ", " version ", " section ", -5, " section title ", " passage ", -7, " passage title ", " original \r\n text "),
            (value.WorkItemId, value.SequenceNumber, value.RevisionRequestId, value.DraftId, value.DraftVersion, value.SectionId, value.SectionNumber, value.SectionTitle, value.PassageId, value.PassageNumber, value.PassageTitle, value.OriginalText));
        Assert.Equal(new[] { "z", "a" }, value.RevisionItemIds); Assert.Equal(new[] { "r2", "r1" }, value.RuleCodes);
        Assert.Equal(new[] { DocumentaryNarrativeDraftValidationSeverity.Warning, DocumentaryNarrativeDraftValidationSeverity.Error }, value.Severities);
        Assert.Equal(new[] { DocumentaryNarrativeRevisionAction.RevisePassageOpening, DocumentaryNarrativeRevisionAction.RevisePassageText }, value.Actions);
        Assert.Equal(new[] { " second ", " first " }, value.Messages); Assert.Same(previous, value.PreviousPassageContext); Assert.Same(next, value.NextPassageContext);
        foreach (var collection in new object[] { value.RevisionItemIds, value.RuleCodes, value.Severities, value.Actions, value.Messages }) AssertReadOnly(collection);
        RoundTrip(value);

        foreach (var blank in Enumerable.Range(0, 11)) Assert.Throws<ArgumentException>(() => WorkScalarBlank(blank));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(sequence: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(passageType: (DocumentaryNarrativePassageType)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(stage: (DocumentaryNarrativeStage)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(role: (DocumentarySceneRole)999));
        foreach (var malformed in new[] { Array.Empty<string>(), new[] { "i", "i" }, new[] { " " } }) Assert.Throws<ArgumentException>(() => Work(ids: malformed));
        Assert.Throws<ArgumentException>(() => Work(rules: ["r"])); Assert.Throws<ArgumentException>(() => Work(messages: ["m"]));
        Assert.Throws<ArgumentException>(() => Work(severities: [DocumentaryNarrativeDraftValidationSeverity.Error])); Assert.Throws<ArgumentException>(() => Work(actions: [DocumentaryNarrativeRevisionAction.RevisePassageText]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(severities: [(DocumentaryNarrativeDraftValidationSeverity)999, DocumentaryNarrativeDraftValidationSeverity.Error]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Work(actions: [(DocumentaryNarrativeRevisionAction)999, DocumentaryNarrativeRevisionAction.RevisePassageText]));
    }

    [Fact]
    public void Manual_work_item_validates_preserves_and_serializes()
    {
        foreach (var blank in Enumerable.Range(0, 7)) Assert.Throws<ArgumentException>(() => Manual(blank));
        Assert.Throws<ArgumentOutOfRangeException>(() => Manual(sequence: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Manual(severity: (DocumentaryNarrativeDraftValidationSeverity)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Manual(action: (DocumentaryNarrativeRevisionAction)99));
        var value = Manual();
        Assert.Equal(("work", 3, "request", "item", "rule", " message ", "draft", "section", -1, "passage", -2, " field "),
            (value.WorkItemId, value.SequenceNumber, value.RevisionRequestId, value.RevisionItemId, value.RuleCode, value.Message, value.DraftId, value.SectionId, value.SectionNumber, value.PassageId, value.PassageNumber, value.FieldName));
        RoundTrip(value);
    }

    [Fact]
    public void Metadata_contract_validates_editor_schema_and_preserves_exact_values()
    {
        Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeRevisionSubmissionMetadata(default,"by","1.0",DocumentaryNarrativeRevisionEditorType.Human,"editor","c"));
        Assert.Throws<ArgumentException>(() => Metadata(schema: "1.1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Metadata(type: (DocumentaryNarrativeRevisionEditorType)99));
        foreach (var blank in Enumerable.Range(0, 4)) Assert.Throws<ArgumentException>(() => Metadata(blank: blank));
        var value = Metadata();
        Assert.Equal((Time, " created by ", "1.0", DocumentaryNarrativeRevisionEditorType.Hybrid, " editor ", " correlation "), (value.CreatedUtc, value.CreatedBy, value.SubmissionSchemaVersion, value.EditorType, value.EditorName, value.CorrelationId));
        RoundTrip(value);
        Assert.Equal(new[] { "Human", "Automated", "Hybrid" }, Enum.GetNames<DocumentaryNarrativeRevisionEditorType>());
    }

    [Fact]
    public void Passage_submission_validates_preserves_whitespace_order_copying_and_immutability()
    {
        foreach (var blank in Enumerable.Range(0, 4)) { var v = new[] { "work", "passage", " old ", " new " }; v[blank] = " "; Assert.Throws<ArgumentException>(() => new DocumentaryNarrativePassageRevisionSubmission(v[0], v[1], v[2], v[3], ["i"])); }
        Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativePassageRevisionSubmission("w", "p", "o", "n", null!));
        foreach (var ids in new[] { Array.Empty<string>(), new[] { " " }, new[] { "i", "i" } }) Assert.Throws<ArgumentException>(() => new DocumentaryNarrativePassageRevisionSubmission("w", "p", "o", "n", ids));
        var source = new List<string> { "z", "a" }; var value = new DocumentaryNarrativePassageRevisionSubmission(" work ", " passage ", "\r\n old ", " new\t", source); source.Clear();
        Assert.Equal((" work ", " passage ", "\r\n old ", " new\t"), (value.WorkItemId, value.PassageId, value.OriginalText, value.RevisedText)); Assert.Equal(new[] { "z", "a" }, value.ResolvedRevisionItemIds); AssertReadOnly(value.ResolvedRevisionItemIds); RoundTrip(value);
    }

    private static DocumentaryNarrativePassageRevisionWorkItem Work(IReadOnlyList<string>? ids=null,IReadOnlyList<string>? rules=null,IReadOnlyList<DocumentaryNarrativeDraftValidationSeverity>? severities=null,IReadOnlyList<DocumentaryNarrativeRevisionAction>? actions=null,IReadOnlyList<string>? messages=null,DocumentaryNarrativePassageContext? previous=null,DocumentaryNarrativePassageContext? next=null,int sequence=4,DocumentaryNarrativePassageType passageType=DocumentaryNarrativePassageType.Context,DocumentaryNarrativeStage stage=DocumentaryNarrativeStage.Science,DocumentarySceneRole role=DocumentarySceneRole.ScientificExplanation)
        => new(" work ",sequence," request "," draft "," version "," section ",-5," section title "," passage ",-7," passage title ",passageType,stage,role," original \r\n text ",ids??["z","a"],rules??["r2","r1"],severities??[DocumentaryNarrativeDraftValidationSeverity.Warning,DocumentaryNarrativeDraftValidationSeverity.Error],actions??[DocumentaryNarrativeRevisionAction.RevisePassageOpening,DocumentaryNarrativeRevisionAction.RevisePassageText],messages??[" second "," first "],previous,next);
    private static void WorkScalarBlank(int index) { var v = new[] { "w", "r", "d", "v", "s", "st", "p", "pt", "o", "r1", "m1" }; v[index]=" "; _=new DocumentaryNarrativePassageRevisionWorkItem(v[0],1,v[1],v[2],v[3],v[4],1,v[5],v[6],1,v[7],DocumentaryNarrativePassageType.Context,DocumentaryNarrativeStage.Science,DocumentarySceneRole.ScientificExplanation,v[8],["i"],[v[9]],[DocumentaryNarrativeDraftValidationSeverity.Error],[DocumentaryNarrativeRevisionAction.RevisePassageText],[v[10]],null,null); }
    private static DocumentaryNarrativeManualReviewWorkItem Manual(int blank=-1,int sequence=3,DocumentaryNarrativeDraftValidationSeverity severity=DocumentaryNarrativeDraftValidationSeverity.Warning,DocumentaryNarrativeRevisionAction action=DocumentaryNarrativeRevisionAction.ReviewDuration) { var v=new[]{"work","request","item","rule"," message ","draft","section","passage"," field "};if(blank>=0)v[blank]=" ";return new(v[0],sequence,v[1],v[2],v[3],severity,action,v[4],v[5],v[6],-1,v[7],-2,v[8]); }
    private static DocumentaryNarrativeRevisionSubmissionMetadata Metadata(DateTimeOffset? created=null,string schema="1.0",DocumentaryNarrativeRevisionEditorType type=DocumentaryNarrativeRevisionEditorType.Hybrid,int blank=-1) { var v=new[]{" created by "," editor "," correlation ",schema};if(blank>=0)v[blank]=" ";return new(created??Time,v[0],v[3],type,v[1],v[2]); }
    private static void AssertReadOnly(object value) { var list=Assert.IsAssignableFrom<System.Collections.IList>(value); Assert.Throws<NotSupportedException>(()=>list.Add(null)); }
    private static void AssertImmutable(object value)=>Assert.All(value.GetType().GetProperties(), p=>Assert.False(p.SetMethod?.IsPublic==true));
    private static void RoundTrip<T>(T value) { var o=new JsonSerializerOptions(JsonSerializerDefaults.Web);var json=JsonSerializer.Serialize(value,o);Assert.Equal(json,JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(json,o),o)); }
}

public sealed class DocumentaryNarrativeRevisionExecutionFlowCertificationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Builder_groups_interleaved_findings_by_first_sequence_with_complete_context_and_deterministic_ids()
    {
        var draft=OrionDocumentaryNarrativeRevisionFixture.ValidDraft(); var passages=draft.Sections.SelectMany(s=>s.Passages).ToArray();
        var findings=new[] { Finding(draft,2,"DND-QUALITY-011"), Finding(draft,0,"DND-QUALITY-014"), Finding(draft,0,"DND-QUALITY-012"), Finding(draft,2,"DND-QUALITY-018"), Finding(draft,1,"DND-QUALITY-008"), Finding(draft,0,"DND-QUALITY-013"), Finding(draft,2,"DND-QUALITY-011") };
        var request=BuildRequest(draft,findings,"cert.group"); var package=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(draft,request,OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());
        Assert.Equal(new[]{passages[2].PassageId,passages[0].PassageId,passages[1].PassageId},package.PassageWorkItems.Select(x=>x.PassageId));
        Assert.Equal(new[]{1,2,3},package.PassageWorkItems.Select(x=>x.SequenceNumber)); Assert.Equal(new[]{"cert.group.passage-work.1","cert.group.passage-work.2","cert.group.passage-work.3"},package.PassageWorkItems.Select(x=>x.WorkItemId));
        foreach(var work in package.PassageWorkItems) { var expected=request.Items.Where(x=>x.RequiresPassageText&&x.PassageId==work.PassageId).ToArray(); Assert.Equal(expected.Select(x=>x.RevisionItemId),work.RevisionItemIds);Assert.Equal(expected.Select(x=>x.RuleCode),work.RuleCodes);Assert.Equal(expected.Select(x=>x.Severity),work.Severities);Assert.Equal(expected.Select(x=>x.Action),work.Actions);Assert.Equal(expected.Select(x=>x.Message),work.Messages); }
        Assert.Equal(new[]{"cert.group.manual-work.1","cert.group.manual-work.2"},package.ManualReviewWorkItems.Select(x=>x.WorkItemId)); Assert.Equal(new[]{request.Items[1].RevisionItemId,request.Items[3].RevisionItemId},package.ManualReviewWorkItems.Select(x=>x.RevisionItemId));
        Assert.Equal(findings.Length,package.RevisionItemCount); Assert.True(package.RequiresExternalEditing);
        foreach(var work in package.PassageWorkItems) { var index=Array.FindIndex(passages,p=>p.PassageId==work.PassageId); AssertContext(index==0?null:passages[index-1],work.PreviousPassageContext);AssertContext(index==passages.Length-1?null:passages[index+1],work.NextPassageContext); }
        Assert.Equal(Json(package),Json(new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(draft,BuildRequest(OrionDocumentaryNarrativeRevisionFixture.ValidDraft(),findings,"cert.group"),OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata())));
    }

    [Fact]
    public void Builder_directly_rejects_every_null_lineage_scope_and_action_branch_without_mutation()
    {
        var d=OrionDocumentaryNarrativeRevisionFixture.Draft();var r=OrionDocumentaryNarrativeRevisionFixture.Request();var m=OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata();var b=new DocumentaryNarrativeRevisionWorkPackageBuilder();
        Assert.Throws<ArgumentNullException>(()=>b.Build(null!,r,m));Assert.Throws<ArgumentNullException>(()=>b.Build(d,null!,m));Assert.Throws<ArgumentNullException>(()=>b.Build(d,r,null!));
        foreach(var property in new[]{"DraftId","DraftVersion"}) { var copy=OrionDocumentaryNarrativeRevisionFixture.Request(); Set(copy,property,property=="DraftId"?d.DraftId.ToUpperInvariant():d.Version.ToUpperInvariant()+"X");Assert.Throws<ArgumentException>(()=>b.Build(d,copy,m)); }
        foreach(var mutation in new[]{"DraftId","PassageId","SectionId","SectionNumber","PassageNumber","Action"}) { var copy=OrionDocumentaryNarrativeRevisionFixture.Request();var item=copy.Items.First(x=>x.RequiresPassageText);Set(item,mutation,mutation switch {"DraftId"=>"other","PassageId"=>"missing","SectionId"=>"other","SectionNumber"=>999,"PassageNumber"=>999,"Action"=>(DocumentaryNarrativeRevisionAction)999,_=>null});Assert.ThrowsAny<ArgumentException>(()=>b.Build(d,copy,m)); }
        var missing=OrionDocumentaryNarrativeRevisionFixture.Request();Set(missing.Items.First(x=>x.RequiresPassageText),"PassageId",null);Assert.Throws<ArgumentException>(()=>b.Build(d,missing,m));
        var before=new[]{Json(d),Json(r),Json(r.ValidationResult),Json(r.Items),Json(m)};_=b.Build(d,r,m);Assert.Equal(before,new[]{Json(d),Json(r),Json(r.ValidationResult),Json(r.Items),Json(m)});
    }

    [Fact]
    public void Manual_only_clean_and_mixed_packages_have_exact_separation_counts_and_order()
    {
        var d=OrionDocumentaryNarrativeRevisionFixture.ValidDraft();var manual=BuildRequest(d,[Finding(d,0,"DND-QUALITY-014"),Finding(d,2,"DND-QUALITY-018")],"manual");var mp=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,manual,OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());Assert.Empty(mp.PassageWorkItems);Assert.Equal(2,mp.ManualReviewWorkItems.Count);Assert.False(mp.RequiresExternalEditing);Assert.Equal(2,mp.RevisionItemCount);
        var clean=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,BuildRequest(d,[],"clean"),OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());Assert.Empty(clean.PassageWorkItems);Assert.Empty(clean.ManualReviewWorkItems);Assert.Equal(0,clean.RevisionItemCount);Assert.False(clean.RequiresExternalEditing);
    }

    [Fact]
    public void Assembler_certifies_lineage_structural_gates_ordering_nonmutation_and_binder_scenarios()
    {
        var d=OrionDocumentaryNarrativeRevisionFixture.ValidDraft();var r=BuildRequest(d,[Finding(d,0,"DND-QUALITY-011"),Finding(d,0,"DND-QUALITY-012"),Finding(d,2,"DND-QUALITY-008")],"assemble");var p=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,r,OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());var metadata=OrionDocumentaryNarrativeRevisionFixture.MetadataFor(d);var s=Submission(p,p.PassageWorkItems.Reverse());var assembler=new DocumentaryNarrativeRevisionSubmissionAssembler();
        var before=new[]{Json(d),Json(r),Json(r.ValidationResult),Json(p),Json(p.PassageWorkItems.SelectMany(x=>new[]{x.PreviousPassageContext,x.NextPassageContext})),Json(s),Json(s.PassageSubmissions.Select(x=>x.ResolvedRevisionItemIds)),Json(s.Metadata)};var binding=assembler.Assemble(d,r,p,s,metadata);Assert.Equal(s.PassageSubmissions.Select(x=>x.PassageId),binding.PassageRevisionInputs.Select(x=>x.PassageId));Assert.Equal(before,new[]{Json(d),Json(r),Json(r.ValidationResult),Json(p),Json(p.PassageWorkItems.SelectMany(x=>new[]{x.PreviousPassageContext,x.NextPassageContext})),Json(s),Json(s.PassageSubmissions.Select(x=>x.ResolvedRevisionItemIds)),Json(s.Metadata)});
        var result=new DocumentaryNarrativeRevisionBinder().Bind(binding);Assert.Equal(DocumentaryNarrativeRevisionStatus.Revised,result.Status);Assert.Equal(r.Items.Where(x=>x.RequiresPassageText).Select(x=>x.PassageId).Distinct(),result.Changes.Select(x=>x.PassageId));
        foreach(var property in new[]{"WorkPackageId","RevisionRequestId","DraftId","DraftVersion"}) { var bad=Submission(p,p.PassageWorkItems);Set(bad,property,(string)bad.GetType().GetProperty(property)!.GetValue(bad)!+"X");Assert.Throws<ArgumentException>(()=>assembler.Assemble(d,r,p,bad,metadata)); }
        var badMeta=OrionDocumentaryNarrativeRevisionFixture.MetadataFor(d);Set(badMeta,"SourceDraftId",d.DraftId.ToUpperInvariant());Assert.Throws<ArgumentException>(()=>assembler.Assemble(d,r,p,s,badMeta));
        var w=p.PassageWorkItems[0];
        foreach(var bad in new[]{Passage(w,work:"unknown"),Passage(w,work:w.WorkItemId.ToUpperInvariant()),Passage(w,passage:"unknown"),Passage(w,passage:w.PassageId.ToUpperInvariant()),Passage(w,ids:w.RevisionItemIds.Reverse()),Passage(w,ids:w.RevisionItemIds.Take(1)),Passage(w,ids:[..w.RevisionItemIds,"extra"])}) Assert.Throws<ArgumentException>(()=>assembler.Assemble(d,r,p,SubmissionFromPassages(p,[bad]),metadata));
        Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionSubmission("s",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),[Passage(w),Passage(w)]));
    }

    [Fact]
    public void Every_execution_contract_round_trips_and_context_is_not_in_O27_binding_json()
    {
        var p=OrionDocumentaryNarrativeRevisionExecutionFixture.Package();var s=OrionDocumentaryNarrativeRevisionExecutionFixture.Submission();var binding=new DocumentaryNarrativeRevisionSubmissionAssembler().Assemble(OrionDocumentaryNarrativeRevisionFixture.Draft(),OrionDocumentaryNarrativeRevisionFixture.Request(),p,s,OrionDocumentaryNarrativeRevisionFixture.Metadata());
        foreach(var value in new object[]{p.PassageWorkItems[0].NextPassageContext!,p.PassageWorkItems[0],OrionDocumentaryNarrativeRevisionExecutionFixture.Package().ManualReviewWorkItems.FirstOrDefault()??ManualFallback(p),s.Metadata,s.PassageSubmissions[0],s,p,p.Metadata,binding}) RoundTripObject(value);
        Assert.DoesNotContain("PassageContext",Json(binding),StringComparison.Ordinal);Assert.DoesNotContain("previousPassageContext",Json(binding),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("nextPassageContext",Json(binding),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Architecture_inventory_signatures_and_provider_neutrality_are_exact()
    {
        var inventory=new Dictionary<Type,string[]> { [typeof(DocumentaryNarrativePassageContext)]=["PassageId","PassageNumber","Title","Text"], [typeof(DocumentaryNarrativePassageRevisionWorkItem)]=["WorkItemId","SequenceNumber","RevisionRequestId","DraftId","DraftVersion","SectionId","SectionNumber","SectionTitle","PassageId","PassageNumber","PassageTitle","PassageType","NarrativeStage","SceneRole","OriginalText","RevisionItemIds","RuleCodes","Severities","Actions","Messages","PreviousPassageContext","NextPassageContext"], [typeof(DocumentaryNarrativeManualReviewWorkItem)]=["WorkItemId","SequenceNumber","RevisionRequestId","RevisionItemId","RuleCode","Severity","Action","Message","DraftId","SectionId","SectionNumber","PassageId","PassageNumber","FieldName"], [typeof(DocumentaryNarrativeRevisionWorkPackage)]=["WorkPackageId","RevisionRequestId","DraftId","DraftVersion","SubjectId","SubjectName","PublicationFormat","PrimaryLanguage","Metadata","PassageWorkItems","ManualReviewWorkItems","RequiresExternalEditing","PassageWorkItemCount","ManualReviewWorkItemCount","RevisionItemCount"], [typeof(DocumentaryNarrativeRevisionSubmissionMetadata)]=["CreatedUtc","CreatedBy","SubmissionSchemaVersion","EditorType","EditorName","CorrelationId"], [typeof(DocumentaryNarrativePassageRevisionSubmission)]=["WorkItemId","PassageId","OriginalText","RevisedText","ResolvedRevisionItemIds"], [typeof(DocumentaryNarrativeRevisionSubmission)]=["SubmissionId","WorkPackageId","RevisionRequestId","DraftId","DraftVersion","Metadata","PassageSubmissions","PassageSubmissionCount","ResolvedRevisionItemCount"] };
        var forbidden=new[]{"Prompt","Provider","Model","Http","Runtime","Storage","Ai","Llm","Token","Temperature"};foreach(var pair in inventory){Assert.Equal(pair.Value.Order(),pair.Key.GetProperties().Select(x=>x.Name).Order());Assert.All(pair.Key.GetProperties(),x=>Assert.False(x.SetMethod?.IsPublic==true));Assert.Empty(pair.Key.GetProperties().Select(x=>x.Name).Where(x=>forbidden.Any(f=>x.Contains(f,StringComparison.OrdinalIgnoreCase))));}
        Assert.Equal(new[]{typeof(DocumentaryNarrativeDraft),typeof(DocumentaryNarrativeRevisionRequest),typeof(DocumentaryNarrativeRevisionExecutionMetadata)},typeof(DocumentaryNarrativeRevisionWorkPackageBuilder).GetMethod("Build")!.GetParameters().Select(x=>x.ParameterType));
        Assert.Equal(new[]{typeof(DocumentaryNarrativeDraft),typeof(DocumentaryNarrativeRevisionRequest),typeof(DocumentaryNarrativeRevisionWorkPackage),typeof(DocumentaryNarrativeRevisionSubmission),typeof(DocumentaryNarrativeRevisionMetadata)},typeof(DocumentaryNarrativeRevisionSubmissionAssembler).GetMethod("Assemble")!.GetParameters().Select(x=>x.ParameterType));
    }

    private static DocumentaryNarrativeDraftValidationFinding Finding(DocumentaryNarrativeDraft d,int index,string rule){var x=d.Sections.SelectMany(s=>s.Passages.Select(p=>(s,p))).ElementAt(index);return new(rule,DocumentaryNarrativeDraftValidationSeverity.Warning,"message "+rule,d.DraftId,x.s.SectionId,x.s.SectionNumber,x.p.PassageId,x.p.PassageNumber,"Text");}
    private static DocumentaryNarrativeRevisionRequest BuildRequest(DocumentaryNarrativeDraft d,IReadOnlyList<DocumentaryNarrativeDraftValidationFinding> f,string id)=>new DocumentaryNarrativeRevisionRequestBuilder().Build(d,new(d.DraftId,f),id,OrionDocumentaryNarrativeRevisionFixture.RequestMetadata());
    private static DocumentaryNarrativeRevisionSubmission Submission(DocumentaryNarrativeRevisionWorkPackage p,IEnumerable<DocumentaryNarrativePassageRevisionWorkItem> works)=>new("submission",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),works.Select(x=>Passage(x)).ToArray());
    private static DocumentaryNarrativeRevisionSubmission SubmissionFromPassages(DocumentaryNarrativeRevisionWorkPackage p,IReadOnlyList<DocumentaryNarrativePassageRevisionSubmission> passages)=>new("submission",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),passages);
    private static DocumentaryNarrativePassageRevisionSubmission Passage(DocumentaryNarrativePassageRevisionWorkItem w,string? work=null,string? passage=null,IEnumerable<string>? ids=null)=>new(work??w.WorkItemId,passage??w.PassageId,w.OriginalText,w.OriginalText+" revised",(ids??w.RevisionItemIds).ToArray());
    private static DocumentaryNarrativeManualReviewWorkItem ManualFallback(DocumentaryNarrativeRevisionWorkPackage p)=>new("m",1,p.RevisionRequestId,"i","r",DocumentaryNarrativeDraftValidationSeverity.Warning,DocumentaryNarrativeRevisionAction.ReviewDuration,"m",p.DraftId,null,null,null,null,null);
    private static void AssertContext(DocumentaryNarrativePassage? expected,DocumentaryNarrativePassageContext? actual){if(expected is null){Assert.Null(actual);return;}Assert.NotNull(actual);Assert.Equal((expected.PassageId,expected.PassageNumber,expected.Title,expected.Text),(actual!.PassageId,actual.PassageNumber,actual.Title,actual.Text));}
    private static void Set(object target,string property,object? value)=>target.GetType().GetField("<"+property+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    private static string Json(object value)=>JsonSerializer.Serialize(value,value.GetType(),Web);
    private static void RoundTripObject(object value){var json=Json(value);var copy=JsonSerializer.Deserialize(json,value.GetType(),Web);Assert.NotNull(copy);Assert.Equal(json,Json(copy!));}
}
