using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeRevisionExecutionFixture
{
    internal static DocumentaryNarrativeRevisionExecutionMetadata ExecutionMetadata()=>new(DateTimeOffset.Parse("2026-01-15T14:30:00Z"),"revision-coordinator","1.0","correlation-orion-execution-001");
    internal static DocumentaryNarrativeRevisionSubmissionMetadata SubmissionMetadata()=>new(DateTimeOffset.Parse("2026-01-15T14:45:00Z"),"external-editor","1.0",DocumentaryNarrativeRevisionEditorType.Human,"Orion editor","correlation-orion-execution-001");
    internal static DocumentaryNarrativeRevisionWorkPackage Package(){var d=OrionDocumentaryNarrativeRevisionFixture.Draft();return new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,OrionDocumentaryNarrativeRevisionFixture.Request(),ExecutionMetadata());}
    internal static DocumentaryNarrativeRevisionSubmission Submission()
    { var p=Package(); var w=p.PassageWorkItems[0]; return new("submission.orion.1",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,SubmissionMetadata(),[new(w.WorkItemId,w.PassageId,w.OriginalText,"Orion commands attention above the eastern winter horizon tonight.",w.RevisionItemIds)]); }
}

public sealed class DocumentaryNarrativeRevisionExecutionMetadataTests
{
    [Fact] public void Requires_external_deterministic_values(){Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionExecutionMetadata(default,"editor","1.0","c"));Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionExecutionMetadata(DateTimeOffset.Parse("2026-01-01Z"),"editor","2.0","c"));}
}
public sealed class DocumentaryNarrativeRevisionWorkPackageBuilderTests
{
    [Fact] public void Groups_findings_and_calculates_context(){var package=OrionDocumentaryNarrativeRevisionExecutionFixture.Package();var request=OrionDocumentaryNarrativeRevisionFixture.Request();Assert.Single(package.PassageWorkItems);Assert.Equal(request.Items.Where(x=>x.RequiresPassageText).Select(x=>x.RevisionItemId),package.PassageWorkItems[0].RevisionItemIds);Assert.Null(package.PassageWorkItems[0].PreviousPassageContext);Assert.NotNull(package.PassageWorkItems[0].NextPassageContext);Assert.Equal(request.Items.Count,package.RevisionItemCount);}
    [Fact] public void Clean_request_produces_empty_package(){var d=OrionDocumentaryNarrativeRevisionFixture.ValidDraft();var package=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,OrionDocumentaryNarrativeRevisionFixture.NoChangeRequest(),OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());Assert.Empty(package.PassageWorkItems);Assert.Empty(package.ManualReviewWorkItems);Assert.False(package.RequiresExternalEditing);}
    [Fact] public void Is_deterministic(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);Assert.Equal(JsonSerializer.Serialize(OrionDocumentaryNarrativeRevisionExecutionFixture.Package(),options),JsonSerializer.Serialize(OrionDocumentaryNarrativeRevisionExecutionFixture.Package(),options));}
}
public sealed class DocumentaryNarrativeRevisionSubmissionTests
{
    [Fact] public void Defensively_copies_submissions(){var source=new List<DocumentaryNarrativePassageRevisionSubmission>();var p=OrionDocumentaryNarrativeRevisionExecutionFixture.Package();var submission=new DocumentaryNarrativeRevisionSubmission("s",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),source);source.Add(new("w","p","old","new",["i"]));Assert.Empty(submission.PassageSubmissions);}
    [Fact] public void Rejects_duplicate_coverage(){var m=OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeRevisionSubmission("s","w","r","d","1",m,[new("w1","p1","o","n",["i"]),new("w2","p2","o","n",["i"])]));}
}
public sealed class DocumentaryNarrativeRevisionSubmissionAssemblerTests
{
    [Fact] public void Maps_external_submission_without_binding(){var d=OrionDocumentaryNarrativeRevisionFixture.Draft();var request=OrionDocumentaryNarrativeRevisionFixture.Request();var binding=new DocumentaryNarrativeRevisionSubmissionAssembler().Assemble(d,request,OrionDocumentaryNarrativeRevisionExecutionFixture.Package(),OrionDocumentaryNarrativeRevisionExecutionFixture.Submission(),OrionDocumentaryNarrativeRevisionFixture.Metadata());Assert.Single(binding.PassageRevisionInputs);Assert.Equal(OrionDocumentaryNarrativeRevisionExecutionFixture.Submission().PassageSubmissions[0].ResolvedRevisionItemIds,binding.PassageRevisionInputs[0].RevisionItemIds);}
    [Fact] public void Rejects_partial_group_and_stale_text(){var d=OrionDocumentaryNarrativeRevisionFixture.Draft();var r=OrionDocumentaryNarrativeRevisionFixture.Request();var p=OrionDocumentaryNarrativeRevisionExecutionFixture.Package();var w=p.PassageWorkItems[0];DocumentaryNarrativeRevisionSubmission Make(string text,IReadOnlyList<string> ids)=>new("s",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),[new(w.WorkItemId,w.PassageId,text,"new",ids)]);var assembler=new DocumentaryNarrativeRevisionSubmissionAssembler();Assert.Throws<ArgumentException>(()=>assembler.Assemble(d,r,p,Make("stale",w.RevisionItemIds),OrionDocumentaryNarrativeRevisionFixture.Metadata()));Assert.Throws<ArgumentException>(()=>assembler.Assemble(d,r,p,Make(w.OriginalText,[w.RevisionItemIds[0]]),OrionDocumentaryNarrativeRevisionFixture.Metadata()));}
    [Fact] public void Empty_clean_submission_assembles_empty_request(){var d=OrionDocumentaryNarrativeRevisionFixture.ValidDraft();var r=OrionDocumentaryNarrativeRevisionFixture.NoChangeRequest();var p=new DocumentaryNarrativeRevisionWorkPackageBuilder().Build(d,r,OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata());var s=new DocumentaryNarrativeRevisionSubmission("s",p.WorkPackageId,p.RevisionRequestId,p.DraftId,p.DraftVersion,OrionDocumentaryNarrativeRevisionExecutionFixture.SubmissionMetadata(),[]);var binding=new DocumentaryNarrativeRevisionSubmissionAssembler().Assemble(d,r,p,s,OrionDocumentaryNarrativeRevisionFixture.MetadataFor(d));Assert.Empty(binding.PassageRevisionInputs);}
}
public sealed class DocumentaryNarrativeRevisionExecutionSerializationTests
{
    [Fact] public void Contracts_round_trip_byte_identically(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);RoundTrip(OrionDocumentaryNarrativeRevisionExecutionFixture.ExecutionMetadata(),options);RoundTrip(OrionDocumentaryNarrativeRevisionExecutionFixture.Package(),options);RoundTrip(OrionDocumentaryNarrativeRevisionExecutionFixture.Submission(),options);}
    private static void RoundTrip<T>(T value,JsonSerializerOptions options){var json=JsonSerializer.Serialize(value,options);var copy=JsonSerializer.Deserialize<T>(json,options);Assert.Equal(json,JsonSerializer.Serialize(copy,options));}
}
public sealed class DocumentaryNarrativeRevisionExecutionArchitectureTests
{
    [Fact] public void Operations_are_exact_sealed_stateless_boundaries(){foreach(var type in new[]{typeof(DocumentaryNarrativeRevisionWorkPackageBuilder),typeof(DocumentaryNarrativeRevisionSubmissionAssembler)}){Assert.True(type.IsSealed);Assert.NotNull(type.GetConstructor(Type.EmptyTypes));Assert.Empty(type.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic));Assert.Single(type.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly));}}
    [Fact] public void Contracts_expose_no_public_setters(){var contracts=new[]{typeof(DocumentaryNarrativeRevisionExecutionMetadata),typeof(DocumentaryNarrativePassageContext),typeof(DocumentaryNarrativePassageRevisionWorkItem),typeof(DocumentaryNarrativeManualReviewWorkItem),typeof(DocumentaryNarrativeRevisionWorkPackage),typeof(DocumentaryNarrativeRevisionSubmissionMetadata),typeof(DocumentaryNarrativePassageRevisionSubmission),typeof(DocumentaryNarrativeRevisionSubmission)};Assert.All(contracts.SelectMany(x=>x.GetProperties()),property=>Assert.False(property.SetMethod?.IsPublic==true));}
}
