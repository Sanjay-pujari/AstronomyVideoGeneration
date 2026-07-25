using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeDraftFixture
{
    internal static DocumentaryNarrativeDraftMetadata Metadata(DateTimeOffset? time = null) => new(
        time ?? new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero), "narrative-editor",
        "editorial-draft-v1", "1", "1.0", "1.0", "correlation-orion-draft-001");

    internal static DocumentaryNarrativeDraftRequest Request(bool reverse = false)
    {
        var composition = OrionDocumentaryNarrativeCompositionFixture.Composition();
        var texts = new[]
        {
            "Look toward Orion and three nearly straight stars immediately command attention.",
            "The familiar line is only an apparent alignment because the stars lie at very different distances from Earth.",
            "Orion transforms a familiar winter pattern into a reminder of the immense depth hidden behind the night sky."
        };
        var inputs = composition.Sections.SelectMany(x => x.Beats)
            .Select((x, i) => new DocumentaryNarrativePassageInput(x.BeatId, texts[i])).ToArray();
        return new("narrative-draft.orion.long.v1", "1", Metadata(), composition,
            reverse ? inputs.Reverse().ToArray() : inputs);
    }

    internal static DocumentaryNarrativePassage Passage(int number = 1, string? id = null,
        IReadOnlyList<KnowledgeReference>? knowledge = null, IReadOnlyList<VisualOpportunity>? visuals = null,
        string text = "  Exact externally supplied text.  ") => new(
        id ?? $"passage.{number}", number, $"beat.{number}", number, $"scene.{number}", number,
        $"Title {number}", DocumentaryNarrativePassageType.Opening, DocumentaryNarrativeStage.Wonder,
        DocumentarySceneRole.OpeningHook, new ViewerQuestion($"Question {number}?"), $"Purpose {number}.", text,
        knowledge ?? [new($"knowledge.{number}", "Science", "Evidence", true)],
        visuals ?? [new($"Visual {number}", "SkyMap", $"knowledge.{number}", null, true)],
        new SceneTransition("Continue", "Next?", "Advance"),
        new EditorialOutcome("Takeaway", "Contribution", true, true, true, false, false), 42);

    internal static DocumentaryNarrativeDraftSection Section(int number = 1, string? id = null,
        IReadOnlyList<DocumentaryNarrativePassage>? passages = null) => new(id ?? $"draft.section.{number}", number,
        $"composition.section.{number}", "Opening", "Establish the documentary opening.",
        DocumentaryNarrativeStage.Wonder, DocumentaryNarrativeSectionRole.Opening,
        passages ?? [Passage(number)], 42);

    internal static DocumentaryNarrativeDraft Draft(IReadOnlyList<DocumentaryNarrativeDraftSection>? sections = null) =>
        new("draft", "composition", "blueprint", "knowledge", "subject", "Orion",
            BlueprintPublicationFormat.LongDocumentary, "en", "1", Metadata(), sections ?? [Section()]);
}

public sealed class DocumentaryNarrativeDraftAssemblerMappingTests
{
    [Fact]
    public void Maps_every_aggregate_section_and_passage_field_for_complete_orion_draft()
    {
        var request = OrionDocumentaryNarrativeDraftFixture.Request();
        var draft = new DocumentaryNarrativeDraftAssembler().Assemble(request);
        var composition = request.Composition;
        Assert.Equal(request.DraftId, draft.DraftId); Assert.Equal(composition.CompositionId, draft.CompositionId);
        Assert.Equal(composition.BlueprintId, draft.BlueprintId); Assert.Equal(composition.KnowledgeId, draft.KnowledgeId);
        Assert.Equal(composition.SubjectId, draft.SubjectId); Assert.Equal(composition.SubjectName, draft.SubjectName);
        Assert.Equal(composition.PublicationFormat, draft.PublicationFormat); Assert.Equal(composition.PrimaryLanguage, draft.PrimaryLanguage);
        Assert.Equal(request.Version, draft.Version); Assert.Same(request.Metadata, draft.Metadata);
        Assert.Equal(composition.Sections.Count, draft.Sections.Count);
        for (var i = 0; i < composition.Sections.Count; i++)
        {
            var source = composition.Sections[i]; var target = draft.Sections[i];
            Assert.Equal($"{request.DraftId}.section.{source.SectionNumber}", target.SectionId);
            Assert.Equal(source.SectionId, target.SourceCompositionSectionId); Assert.Equal(source.SectionNumber, target.SectionNumber);
            Assert.Equal(source.Title, target.Title); Assert.Equal(source.Purpose, target.Purpose);
            Assert.Equal(source.NarrativeStage, target.NarrativeStage); Assert.Equal(source.SectionRole, target.SectionRole);
            Assert.Equal(source.EstimatedDurationSeconds, target.EstimatedDurationSeconds); Assert.Equal(source.Beats.Count, target.Passages.Count);
        }
        var beats = composition.Sections.SelectMany(x => x.Beats).ToArray();
        var passages = draft.Sections.SelectMany(x => x.Passages).ToArray();
        var textByBeat = request.PassageInputs.ToDictionary(x => x.SourceBeatId, x => x.Text, StringComparer.Ordinal);
        Assert.Equal(beats.Length, passages.Length);
        for (var i = 0; i < beats.Length; i++)
        {
            var b = beats[i]; var p = passages[i];
            Assert.Equal(b.BeatId + ".passage", p.PassageId); Assert.Equal(b.BeatNumber, p.PassageNumber);
            Assert.Equal(b.BeatId, p.SourceBeatId); Assert.Equal(b.BeatNumber, p.SourceBeatNumber);
            Assert.Equal(b.SourceSceneId, p.SourceSceneId); Assert.Equal(b.SourceSceneNumber, p.SourceSceneNumber);
            Assert.Equal(b.Title, p.Title); Assert.Equal(DocumentaryNarrativeDraftMappings.PassageType(b.BeatType), p.PassageType);
            Assert.Equal(b.NarrativeStage, p.NarrativeStage); Assert.Equal(b.SceneRole, p.SceneRole);
            Assert.Equal(b.ViewerQuestion, p.ViewerQuestion); Assert.Equal(b.Purpose, p.Purpose);
            Assert.Equal(textByBeat[b.BeatId], p.Text); Assert.Equal(b.KnowledgeReferences, p.KnowledgeReferences);
            Assert.Equal(b.VisualOpportunities, p.VisualOpportunities); Assert.Equal(b.Transition, p.Transition);
            Assert.Equal(b.EditorialOutcome, p.EditorialOutcome); Assert.Equal(b.EstimatedDurationSeconds, p.EstimatedDurationSeconds);
        }
    }

    [Fact]
    public void Beat_type_mapping_inventory_is_exact_in_enum_order()
    {
        var expected = new[] { DocumentaryNarrativePassageType.Opening, DocumentaryNarrativePassageType.Question,
            DocumentaryNarrativePassageType.Orientation, DocumentaryNarrativePassageType.Discovery,
            DocumentaryNarrativePassageType.Explanation, DocumentaryNarrativePassageType.Evidence,
            DocumentaryNarrativePassageType.Context, DocumentaryNarrativePassageType.Clarification,
            DocumentaryNarrativePassageType.Observation, DocumentaryNarrativePassageType.Guidance,
            DocumentaryNarrativePassageType.Reflection, DocumentaryNarrativePassageType.Transition,
            DocumentaryNarrativePassageType.Closing };
        Assert.Equal(expected, Enum.GetValues<DocumentaryNarrativeBeatType>().Select(DocumentaryNarrativeDraftMappings.PassageType));
    }

    [Fact] public void Undefined_beat_type_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentaryNarrativeDraftMappings.PassageType((DocumentaryNarrativeBeatType)999));
}

public sealed class DocumentaryNarrativeDraftContractTests
{
    private static DocumentaryNarrativeDraft D() => OrionDocumentaryNarrativeDraftFixture.Draft();
    private static DocumentaryNarrativeDraft Make(DocumentaryNarrativeDraft d, string? draft = null, string? composition = null, string? blueprint = null, string? knowledge = null, string? subject = null, string? name = null, BlueprintPublicationFormat? format = null, string? language = null, string? version = null, IReadOnlyList<DocumentaryNarrativeDraftSection>? sections = null) => new(draft ?? d.DraftId, composition ?? d.CompositionId, blueprint ?? d.BlueprintId, knowledge ?? d.KnowledgeId, subject ?? d.SubjectId, name ?? d.SubjectName, format ?? d.PublicationFormat, language ?? d.PrimaryLanguage, version ?? d.Version, d.Metadata, sections ?? d.Sections);
    [Fact] public void Rejects_blank_draft_id() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,draft:" ")); }
    [Fact] public void Rejects_blank_composition_id() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,composition:" ")); }
    [Fact] public void Rejects_blank_blueprint_id() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,blueprint:" ")); }
    [Fact] public void Rejects_blank_knowledge_id() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,knowledge:" ")); }
    [Fact] public void Rejects_blank_subject_id() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,subject:" ")); }
    [Fact] public void Rejects_blank_subject_name() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,name:" ")); }
    [Fact] public void Rejects_blank_language() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,language:" ")); }
    [Fact] public void Rejects_blank_version() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,version:" ")); }
    [Fact] public void Rejects_undefined_format() { var d=D(); Assert.Throws<ArgumentOutOfRangeException>(()=>Make(d,format:(BlueprintPublicationFormat)999)); }
    [Fact] public void Rejects_null_metadata() { var d=D(); Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraft(d.DraftId,d.CompositionId,d.BlueprintId,d.KnowledgeId,d.SubjectId,d.SubjectName,d.PublicationFormat,d.PrimaryLanguage,d.Version,null!,d.Sections)); }
    [Fact] public void Rejects_null_sections() { var d=D(); Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraft(d.DraftId,d.CompositionId,d.BlueprintId,d.KnowledgeId,d.SubjectId,d.SubjectName,d.PublicationFormat,d.PrimaryLanguage,d.Version,d.Metadata,null!)); }
    [Fact] public void Rejects_null_section_element() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,sections:[null!])); }
    [Fact] public void Rejects_duplicate_section_ids() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,sections:[OrionDocumentaryNarrativeDraftFixture.Section(1,"same"),OrionDocumentaryNarrativeDraftFixture.Section(2,"same")])); }
    [Fact] public void Rejects_duplicate_section_numbers() { var d=D(); Assert.Throws<ArgumentException>(()=>Make(d,sections:[OrionDocumentaryNarrativeDraftFixture.Section(1,"a"),OrionDocumentaryNarrativeDraftFixture.Section(1,"b")])); }
    [Fact] public void Valid_construction_preserves_every_value() { var d=D(); Assert.Equal("draft",d.DraftId); Assert.Equal("composition",d.CompositionId); Assert.Equal("blueprint",d.BlueprintId); Assert.Equal("knowledge",d.KnowledgeId); Assert.Equal("subject",d.SubjectId); Assert.Equal("Orion",d.SubjectName); Assert.Equal(BlueprintPublicationFormat.LongDocumentary,d.PublicationFormat); Assert.Equal("en",d.PrimaryLanguage); Assert.Equal("1",d.Version); Assert.NotNull(d.Metadata); Assert.Single(d.Sections); }
}

public sealed class DocumentaryNarrativeDraftSectionContractTests
{
    private static DocumentaryNarrativeDraftSection S()=>OrionDocumentaryNarrativeDraftFixture.Section();
    private static DocumentaryNarrativeDraftSection Make(DocumentaryNarrativeDraftSection s,string? id=null,string? source=null,string? title=null,string? purpose=null,DocumentaryNarrativeStage? stage=null,DocumentaryNarrativeSectionRole? role=null,IReadOnlyList<DocumentaryNarrativePassage>? passages=null,int? duration=null)=>new(id??s.SectionId,s.SectionNumber,source??s.SourceCompositionSectionId,title??s.Title,purpose??s.Purpose,stage??s.NarrativeStage,role??s.SectionRole,passages??s.Passages,duration??s.EstimatedDurationSeconds);
    [Fact] public void Rejects_blank_section_id(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,id:" "));}
    [Fact] public void Rejects_blank_source_id(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,source:" "));}
    [Fact] public void Rejects_blank_title(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,title:" "));}
    [Fact] public void Rejects_blank_purpose(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,purpose:" "));}
    [Fact] public void Rejects_undefined_stage(){var s=S();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(s,stage:(DocumentaryNarrativeStage)999));}
    [Fact] public void Rejects_undefined_role(){var s=S();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(s,role:(DocumentaryNarrativeSectionRole)999));}
    [Fact] public void Rejects_null_passages(){var s=S();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftSection(s.SectionId,s.SectionNumber,s.SourceCompositionSectionId,s.Title,s.Purpose,s.NarrativeStage,s.SectionRole,null!,s.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_element(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,passages:[null!]));}
    [Fact] public void Rejects_duplicate_ids(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,passages:[OrionDocumentaryNarrativeDraftFixture.Passage(1,"same"),OrionDocumentaryNarrativeDraftFixture.Passage(2,"same")]));}
    [Fact] public void Rejects_duplicate_numbers(){var s=S();Assert.Throws<ArgumentException>(()=>Make(s,passages:[OrionDocumentaryNarrativeDraftFixture.Passage(1,"a"),OrionDocumentaryNarrativeDraftFixture.Passage(1,"b")]));}
    [Fact] public void Rejects_negative_duration(){var s=S();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(s,duration:-1));}
    [Fact] public void Valid_construction_preserves_every_value(){var passages=new[]{OrionDocumentaryNarrativeDraftFixture.Passage(2),OrionDocumentaryNarrativeDraftFixture.Passage(1)};var s=new DocumentaryNarrativeDraftSection("id",7,"source","Title","Purpose",DocumentaryNarrativeStage.History,DocumentaryNarrativeSectionRole.Context,passages,84);Assert.Equal("id",s.SectionId);Assert.Equal(7,s.SectionNumber);Assert.Equal("source",s.SourceCompositionSectionId);Assert.Equal("Title",s.Title);Assert.Equal("Purpose",s.Purpose);Assert.Equal(DocumentaryNarrativeStage.History,s.NarrativeStage);Assert.Equal(DocumentaryNarrativeSectionRole.Context,s.SectionRole);Assert.Equal(passages,s.Passages);Assert.Equal(84,s.EstimatedDurationSeconds);}
}

public sealed class DocumentaryNarrativePassageContractTests
{
    private static DocumentaryNarrativePassage P()=>OrionDocumentaryNarrativeDraftFixture.Passage();
    private static DocumentaryNarrativePassage Make(DocumentaryNarrativePassage p,string? id=null,string? beat=null,string? scene=null,string? title=null,string? purpose=null,string? text=null,DocumentaryNarrativePassageType? type=null,DocumentaryNarrativeStage? stage=null,DocumentarySceneRole? role=null,IReadOnlyList<KnowledgeReference>? knowledge=null,IReadOnlyList<VisualOpportunity>? visuals=null,int? duration=null)=>new(id??p.PassageId,p.PassageNumber,beat??p.SourceBeatId,p.SourceBeatNumber,scene??p.SourceSceneId,p.SourceSceneNumber,title??p.Title,type??p.PassageType,stage??p.NarrativeStage,role??p.SceneRole,p.ViewerQuestion,purpose??p.Purpose,text??p.Text,knowledge??p.KnowledgeReferences,visuals??p.VisualOpportunities,p.Transition,p.EditorialOutcome,duration??p.EstimatedDurationSeconds);
    [Fact] public void Rejects_blank_id(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,id:" "));}
    [Fact] public void Rejects_blank_beat_id(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,beat:" "));}
    [Fact] public void Rejects_blank_scene_id(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,scene:" "));}
    [Fact] public void Rejects_blank_title(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,title:" "));}
    [Fact] public void Rejects_blank_purpose(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,purpose:" "));}
    [Fact] public void Rejects_blank_text(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,text:" "));}
    [Fact] public void Rejects_undefined_type(){var p=P();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(p,type:(DocumentaryNarrativePassageType)999));}
    [Fact] public void Rejects_undefined_stage(){var p=P();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(p,stage:(DocumentaryNarrativeStage)999));}
    [Fact] public void Rejects_undefined_role(){var p=P();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(p,role:(DocumentarySceneRole)999));}
    [Fact] public void Rejects_null_viewer_question(){var p=P();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativePassage(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,null!,p.Purpose,p.Text,p.KnowledgeReferences,p.VisualOpportunities,p.Transition,p.EditorialOutcome,p.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_knowledge(){var p=P();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativePassage(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.ViewerQuestion,p.Purpose,p.Text,null!,p.VisualOpportunities,p.Transition,p.EditorialOutcome,p.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_visuals(){var p=P();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativePassage(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.ViewerQuestion,p.Purpose,p.Text,p.KnowledgeReferences,null!,p.Transition,p.EditorialOutcome,p.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_transition(){var p=P();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativePassage(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.ViewerQuestion,p.Purpose,p.Text,p.KnowledgeReferences,p.VisualOpportunities,null!,p.EditorialOutcome,p.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_outcome(){var p=P();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativePassage(p.PassageId,p.PassageNumber,p.SourceBeatId,p.SourceBeatNumber,p.SourceSceneId,p.SourceSceneNumber,p.Title,p.PassageType,p.NarrativeStage,p.SceneRole,p.ViewerQuestion,p.Purpose,p.Text,p.KnowledgeReferences,p.VisualOpportunities,p.Transition,null!,p.EstimatedDurationSeconds));}
    [Fact] public void Rejects_null_knowledge_element(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,knowledge:[null!]));}
    [Fact] public void Rejects_null_visual_element(){var p=P();Assert.Throws<ArgumentException>(()=>Make(p,visuals:[null!]));}
    [Fact] public void Rejects_negative_duration(){var p=P();Assert.Throws<ArgumentOutOfRangeException>(()=>Make(p,duration:-1));}
    [Fact] public void Valid_construction_preserves_every_field_and_exact_text(){var p=P();Assert.Equal("passage.1",p.PassageId);Assert.Equal(1,p.PassageNumber);Assert.Equal("beat.1",p.SourceBeatId);Assert.Equal(1,p.SourceBeatNumber);Assert.Equal("scene.1",p.SourceSceneId);Assert.Equal(1,p.SourceSceneNumber);Assert.Equal("Title 1",p.Title);Assert.Equal(DocumentaryNarrativePassageType.Opening,p.PassageType);Assert.Equal(DocumentaryNarrativeStage.Wonder,p.NarrativeStage);Assert.Equal(DocumentarySceneRole.OpeningHook,p.SceneRole);Assert.Equal("Question 1?",p.ViewerQuestion.Text);Assert.Equal("Purpose 1.",p.Purpose);Assert.Equal("  Exact externally supplied text.  ",p.Text);Assert.Single(p.KnowledgeReferences);Assert.Single(p.VisualOpportunities);Assert.Equal("Continue",p.Transition.TransitionIntent);Assert.Equal("Takeaway",p.EditorialOutcome.ViewerTakeaway);Assert.Equal(42,p.EstimatedDurationSeconds);}
}

public sealed class DocumentaryNarrativeDraftMetadataTests
{
    private static readonly DateTimeOffset Time=new(2026,2,3,4,5,6,789,TimeSpan.FromHours(3));
    private static DocumentaryNarrativeDraftMetadata Make(DateTimeOffset? time=null,string created="creator",string model="model",string composition="1",string compositionSchema="1.0",string draftSchema="1.0",string correlation="correlation")=>new(time??Time,created,model,composition,compositionSchema,draftSchema,correlation);
    [Fact] public void Rejects_default_timestamp()=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftMetadata(default,"a","b","1","1.0","1.0","c"));
    [Fact] public void Rejects_blank_created_by()=>Assert.Throws<ArgumentException>(()=>Make(created:" "));
    [Fact] public void Rejects_blank_model()=>Assert.Throws<ArgumentException>(()=>Make(model:" "));
    [Fact] public void Rejects_blank_composition_version()=>Assert.Throws<ArgumentException>(()=>Make(composition:" "));
    [Fact] public void Rejects_blank_composition_schema()=>Assert.Throws<ArgumentException>(()=>Make(compositionSchema:" "));
    [Fact] public void Rejects_blank_correlation()=>Assert.Throws<ArgumentException>(()=>Make(correlation:" "));
    [Theory][InlineData("")][InlineData("2.0")] public void Rejects_unapproved_draft_schema(string value)=>Assert.Throws<ArgumentException>(()=>Make(draftSchema:value));
    [Fact] public void Preserves_timestamp_offset_and_precision()=>Assert.Equal(Time,Make().CreatedUtc);
}

public sealed class DocumentaryNarrativePassageInputTests
{
    [Fact] public void Rejects_blank_source_id()=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativePassageInput(" ","text"));
    [Theory][InlineData("")][InlineData(" ")] public void Rejects_blank_text(string text)=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativePassageInput("beat",text));
    [Fact] public void Preserves_text_exactly(){const string text="  Exact externally supplied text.  ";Assert.Equal(text,new DocumentaryNarrativePassageInput("beat",text).Text);}
}

public sealed class DocumentaryNarrativeDraftRequestTests
{
    private static DocumentaryNarrativeDraftRequest R()=>OrionDocumentaryNarrativeDraftFixture.Request();
    [Fact] public void Rejects_blank_draft_id(){var r=R();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftRequest(" ",r.Version,r.Metadata,r.Composition,r.PassageInputs));}
    [Fact] public void Rejects_blank_version(){var r=R();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId," ",r.Metadata,r.Composition,r.PassageInputs));}
    [Fact] public void Rejects_null_metadata(){var r=R();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,null!,r.Composition,r.PassageInputs));}
    [Fact] public void Rejects_null_composition(){var r=R();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,null!,r.PassageInputs));}
    [Fact] public void Rejects_null_inputs(){var r=R();Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,null!));}
    [Fact] public void Rejects_null_input_element(){var r=R();Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,[null!]));}
    [Fact] public void Rejects_ordinal_duplicate_ids(){var r=R();var a=new DocumentaryNarrativePassageInput("Beat","one");var b=new DocumentaryNarrativePassageInput("Beat","two");Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,[a,b]));}
    [Fact] public void Preserves_caller_order(){var r=R();var reversed=r.PassageInputs.Reverse().ToArray();var copy=new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,reversed);Assert.Equal(reversed,copy.PassageInputs);}
}

public sealed class DocumentaryNarrativeDraftDefensiveCopyingTests
{
    [Fact] public void Draft_copies_sections_and_exposure_rejects_mutation(){var list=new List<DocumentaryNarrativeDraftSection>{OrionDocumentaryNarrativeDraftFixture.Section()};var value=OrionDocumentaryNarrativeDraftFixture.Draft(list);list.Clear();Assert.Single(value.Sections);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativeDraftSection>)value.Sections).Clear());}
    [Fact] public void Section_copies_passages_and_exposure_rejects_mutation(){var list=new List<DocumentaryNarrativePassage>{OrionDocumentaryNarrativeDraftFixture.Passage()};var value=OrionDocumentaryNarrativeDraftFixture.Section(passages:list);list.Clear();Assert.Single(value.Passages);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativePassage>)value.Passages).Clear());}
    [Fact] public void Passage_copies_references_and_exposure_rejects_mutation(){var list=new List<KnowledgeReference>{new("k","s","p",true)};var value=OrionDocumentaryNarrativeDraftFixture.Passage(knowledge:list);list.Clear();Assert.Single(value.KnowledgeReferences);Assert.Throws<NotSupportedException>(()=>((IList<KnowledgeReference>)value.KnowledgeReferences).Clear());}
    [Fact] public void Passage_copies_visuals_and_exposure_rejects_mutation(){var list=new List<VisualOpportunity>{new("v","t",null,null,false)};var value=OrionDocumentaryNarrativeDraftFixture.Passage(visuals:list);list.Clear();Assert.Single(value.VisualOpportunities);Assert.Throws<NotSupportedException>(()=>((IList<VisualOpportunity>)value.VisualOpportunities).Clear());}
    [Fact] public void Request_copies_inputs_and_exposure_rejects_mutation(){var r=OrionDocumentaryNarrativeDraftFixture.Request();var list=r.PassageInputs.ToList();var value=new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,list);var count=list.Count;list.Clear();Assert.Equal(count,value.PassageInputs.Count);Assert.Throws<NotSupportedException>(()=>((IList<DocumentaryNarrativePassageInput>)value.PassageInputs).Clear());}
}

public sealed class DocumentaryNarrativeDraftAssemblerValidationTests
{
    private static void Rejected(DocumentaryNarrativeDraftRequest r,IReadOnlyList<DocumentaryNarrativePassageInput> inputs)=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftAssembler().Assemble(new(r.DraftId,r.Version,r.Metadata,r.Composition,inputs)));
    [Fact] public void Rejects_null_request()=>Assert.Throws<ArgumentNullException>(()=>new DocumentaryNarrativeDraftAssembler().Assemble(null!));
    [Fact] public void Rejects_missing_input(){var r=OrionDocumentaryNarrativeDraftFixture.Request();Rejected(r,r.PassageInputs.Take(r.PassageInputs.Count-1).ToArray());}
    [Fact] public void Rejects_unknown_input_while_required_inputs_remain(){var r=OrionDocumentaryNarrativeDraftFixture.Request();Rejected(r,r.PassageInputs.Append(new("unknown","unknown text")).ToArray());}
    [Fact] public void Rejects_extra_input_at_exact_match_gate(){var r=OrionDocumentaryNarrativeDraftFixture.Request();Rejected(r,r.PassageInputs.Append(new("extra","extra text")).ToArray());}
    [Fact] public void Rejects_empty_input_set(){var r=OrionDocumentaryNarrativeDraftFixture.Request();Rejected(r,[]);}
    [Fact] public void Matching_is_case_sensitive_and_ordinal(){var r=OrionDocumentaryNarrativeDraftFixture.Request();var first=r.PassageInputs[0];var changed=new DocumentaryNarrativePassageInput(first.SourceBeatId.ToUpperInvariant(),first.Text);Rejected(r,[changed,..r.PassageInputs.Skip(1)]);}
}

public sealed class DocumentaryNarrativeDraftAssemblerOrderingTests
{
    private static void AssertBinding(DocumentaryNarrativeDraftRequest source,IReadOnlyList<DocumentaryNarrativePassageInput> order)
    {
        var request=new DocumentaryNarrativeDraftRequest(source.DraftId,source.Version,source.Metadata,source.Composition,order);
        var draft=new DocumentaryNarrativeDraftAssembler().Assemble(request);var expectedText=source.PassageInputs.ToDictionary(x=>x.SourceBeatId,x=>x.Text,StringComparer.Ordinal);
        Assert.Equal(source.Composition.Sections.Select(x=>x.SectionId),draft.Sections.Select(x=>x.SourceCompositionSectionId));
        Assert.Equal(source.Composition.Sections.Select(x=>x.Beats.Select(b=>b.BeatId).ToArray()),draft.Sections.Select(x=>x.Passages.Select(p=>p.SourceBeatId).ToArray()));
        var beats=source.Composition.Sections.SelectMany(x=>x.Beats).ToArray();var passages=draft.Sections.SelectMany(x=>x.Passages).ToArray();
        Assert.Equal(beats.Select(x=>x.SourceSceneId),passages.Select(x=>x.SourceSceneId));Assert.Equal(beats.Select(x=>x.BeatId),passages.Select(x=>x.SourceBeatId));
        Assert.All(passages,p=>Assert.Equal(expectedText[p.SourceBeatId],p.Text));
    }
    [Fact] public void Reversed_inputs_bind_text_and_emit_composition_order(){var r=OrionDocumentaryNarrativeDraftFixture.Request();AssertBinding(r,r.PassageInputs.Reverse().ToArray());}
    [Fact] public void Arbitrary_inputs_bind_text_and_emit_composition_order(){var r=OrionDocumentaryNarrativeDraftFixture.Request();AssertBinding(r,[r.PassageInputs[1],r.PassageInputs[2],r.PassageInputs[0]]);}
}

public sealed class DocumentaryNarrativeDraftAssemblerImmutabilityTests
{
    [Fact] public void Assembly_does_not_mutate_any_input_or_nested_order()
    {
        var r=OrionDocumentaryNarrativeDraftFixture.Request(true);var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json=JsonSerializer.Serialize(r.Composition,options);var sections=r.Composition.Sections.Select(x=>x.SectionId).ToArray();
        var beats=r.Composition.Sections.Select(x=>x.Beats.Select(b=>b.BeatId).ToArray()).ToArray();
        var refs=r.Composition.Sections.SelectMany(x=>x.Beats).Select(x=>x.KnowledgeReferences.Select(k=>k.KnowledgeEntryId).ToArray()).ToArray();
        var visuals=r.Composition.Sections.SelectMany(x=>x.Beats).Select(x=>x.VisualOpportunities.Select(v=>v.Description).ToArray()).ToArray();
        var inputs=r.PassageInputs.Select(x=>(x.SourceBeatId,x.Text)).ToArray();_ = new DocumentaryNarrativeDraftAssembler().Assemble(r);
        Assert.Equal(json,JsonSerializer.Serialize(r.Composition,options));Assert.Equal(sections,r.Composition.Sections.Select(x=>x.SectionId));
        Assert.Equal(beats,r.Composition.Sections.Select(x=>x.Beats.Select(b=>b.BeatId).ToArray()));Assert.Equal(refs,r.Composition.Sections.SelectMany(x=>x.Beats).Select(x=>x.KnowledgeReferences.Select(k=>k.KnowledgeEntryId).ToArray()));
        Assert.Equal(visuals,r.Composition.Sections.SelectMany(x=>x.Beats).Select(x=>x.VisualOpportunities.Select(v=>v.Description).ToArray()));Assert.Equal(inputs,r.PassageInputs.Select(x=>(x.SourceBeatId,x.Text)));
    }
}

public sealed class DocumentaryNarrativeDraftSerializationTests
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    private static T RoundTrip<T>(T value,out string json){json=JsonSerializer.Serialize(value,Options);return JsonSerializer.Deserialize<T>(json,Options)!;}
    [Fact] public void Metadata_round_trips_every_property(){var s=OrionDocumentaryNarrativeDraftFixture.Metadata(new DateTimeOffset(2026,1,2,3,4,5,678,TimeSpan.FromHours(2)));var c=RoundTrip(s,out var j);Assert.Equal(s,c);Assert.Equal(j,JsonSerializer.Serialize(c,Options));}
    [Fact] public void Passage_input_round_trips_exact_text(){var s=new DocumentaryNarrativePassageInput("beat","  Exact externally supplied text.  ");var c=RoundTrip(s,out var j);Assert.Equal(s.SourceBeatId,c.SourceBeatId);Assert.Equal(s.Text,c.Text);Assert.Equal(j,JsonSerializer.Serialize(c,Options));}
    [Fact] public void Passage_round_trips_every_field_and_nested_order(){var s=OrionDocumentaryNarrativeDraftFixture.Passage(7,knowledge:[new("k2","s2","p2",false),new("k1","s1","p1",true)],visuals:[new("v2","t2","k2",null,false),new("v1","t1","k1",null,true)]);var c=RoundTrip(s,out var j);Assert.Equal(s.PassageId,c.PassageId);Assert.Equal(s.PassageNumber,c.PassageNumber);Assert.Equal(s.SourceBeatId,c.SourceBeatId);Assert.Equal(s.SourceBeatNumber,c.SourceBeatNumber);Assert.Equal(s.SourceSceneId,c.SourceSceneId);Assert.Equal(s.SourceSceneNumber,c.SourceSceneNumber);Assert.Equal(s.Title,c.Title);Assert.Equal(s.PassageType,c.PassageType);Assert.Equal(s.NarrativeStage,c.NarrativeStage);Assert.Equal(s.SceneRole,c.SceneRole);Assert.Equal(s.ViewerQuestion,c.ViewerQuestion);Assert.Equal(s.Purpose,c.Purpose);Assert.Equal(s.Text,c.Text);Assert.Equal(s.KnowledgeReferences,c.KnowledgeReferences);Assert.Equal(s.VisualOpportunities,c.VisualOpportunities);Assert.Equal(s.Transition,c.Transition);Assert.Equal(s.EditorialOutcome,c.EditorialOutcome);Assert.Equal(s.EstimatedDurationSeconds,c.EstimatedDurationSeconds);Assert.Equal(j,JsonSerializer.Serialize(c,Options));}
    [Fact] public void Section_round_trips_fields_passage_order_and_text(){var s=OrionDocumentaryNarrativeDraftFixture.Section(3,passages:[OrionDocumentaryNarrativeDraftFixture.Passage(2),OrionDocumentaryNarrativeDraftFixture.Passage(1)]);var c=RoundTrip(s,out var j);Assert.Equal(s.SectionId,c.SectionId);Assert.Equal(s.SectionNumber,c.SectionNumber);Assert.Equal(s.SourceCompositionSectionId,c.SourceCompositionSectionId);Assert.Equal(s.Title,c.Title);Assert.Equal(s.Purpose,c.Purpose);Assert.Equal(s.NarrativeStage,c.NarrativeStage);Assert.Equal(s.SectionRole,c.SectionRole);Assert.Equal(s.Passages.Select(x=>(x.PassageId,x.SourceBeatId,x.Text)),c.Passages.Select(x=>(x.PassageId,x.SourceBeatId,x.Text)));Assert.Equal(s.EstimatedDurationSeconds,c.EstimatedDurationSeconds);Assert.Equal(j,JsonSerializer.Serialize(c,Options));}
    [Fact] public void Draft_round_trips_every_aggregate_and_nested_value_deterministically(){var s=new DocumentaryNarrativeDraftAssembler().Assemble(OrionDocumentaryNarrativeDraftFixture.Request());var c=RoundTrip(s,out var j);Assert.Equal(s.DraftId,c.DraftId);Assert.Equal(s.CompositionId,c.CompositionId);Assert.Equal(s.BlueprintId,c.BlueprintId);Assert.Equal(s.KnowledgeId,c.KnowledgeId);Assert.Equal(s.SubjectId,c.SubjectId);Assert.Equal(s.SubjectName,c.SubjectName);Assert.Equal(s.PublicationFormat,c.PublicationFormat);Assert.Equal(s.PrimaryLanguage,c.PrimaryLanguage);Assert.Equal(s.Version,c.Version);Assert.Equal(s.Metadata,c.Metadata);Assert.Equal(s.Sections.Select(x=>x.SectionId),c.Sections.Select(x=>x.SectionId));Assert.Equal(s.Sections.SelectMany(x=>x.Passages).Select(x=>(x.PassageId,x.SourceBeatId,x.Text)),c.Sections.SelectMany(x=>x.Passages).Select(x=>(x.PassageId,x.SourceBeatId,x.Text)));Assert.Equal(s.Sections.SelectMany(x=>x.Passages).SelectMany(x=>x.KnowledgeReferences).Select(x=>x.KnowledgeEntryId),c.Sections.SelectMany(x=>x.Passages).SelectMany(x=>x.KnowledgeReferences).Select(x=>x.KnowledgeEntryId));Assert.Equal(s.Sections.SelectMany(x=>x.Passages).SelectMany(x=>x.VisualOpportunities).Select(x=>x.Description),c.Sections.SelectMany(x=>x.Passages).SelectMany(x=>x.VisualOpportunities).Select(x=>x.Description));Assert.Equal(j,JsonSerializer.Serialize(c,Options));}
}

public sealed class DocumentaryNarrativeDraftArchitectureTests
{
    private static void Properties<T>(params string[] expected)=>Assert.Equal(expected.Order(),typeof(T).GetProperties(BindingFlags.Public|BindingFlags.Instance).Select(x=>x.Name).Order());
    [Fact] public void O25_property_inventories_are_exact(){Properties<DocumentaryNarrativeDraftMetadata>("CreatedUtc","CreatedBy","NarrativeModelVersion","CompositionVersion","CompositionSchemaVersion","DraftSchemaVersion","CorrelationId");Properties<DocumentaryNarrativePassageInput>("SourceBeatId","Text");Properties<DocumentaryNarrativePassage>("PassageId","PassageNumber","SourceBeatId","SourceBeatNumber","SourceSceneId","SourceSceneNumber","Title","PassageType","NarrativeStage","SceneRole","ViewerQuestion","Purpose","Text","KnowledgeReferences","VisualOpportunities","Transition","EditorialOutcome","EstimatedDurationSeconds");Properties<DocumentaryNarrativeDraftSection>("SectionId","SectionNumber","SourceCompositionSectionId","Title","Purpose","NarrativeStage","SectionRole","Passages","EstimatedDurationSeconds");Properties<DocumentaryNarrativeDraft>("DraftId","CompositionId","BlueprintId","KnowledgeId","SubjectId","SubjectName","PublicationFormat","PrimaryLanguage","Version","Metadata","Sections");Properties<DocumentaryNarrativeDraftRequest>("DraftId","Version","Metadata","Composition","PassageInputs");}
    [Fact] public void Assembler_boundary_is_exact_and_dependency_free(){var t=typeof(DocumentaryNarrativeDraftAssembler);var ctor=Assert.Single(t.GetConstructors());var method=Assert.Single(t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly));Assert.True(t.IsSealed);Assert.Empty(ctor.GetParameters());Assert.Equal("Assemble",method.Name);Assert.Equal(typeof(DocumentaryNarrativeDraft),method.ReturnType);Assert.Equal(typeof(DocumentaryNarrativeDraftRequest),Assert.Single(method.GetParameters()).ParameterType);Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));Assert.DoesNotContain(t.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public),_=>true);}
    [Fact] public void Contracts_are_read_only_and_forbidden_properties_absent(){var types=new[]{typeof(DocumentaryNarrativeDraft),typeof(DocumentaryNarrativeDraftSection),typeof(DocumentaryNarrativePassage),typeof(DocumentaryNarrativeDraftMetadata),typeof(DocumentaryNarrativePassageInput),typeof(DocumentaryNarrativeDraftRequest)};var forbidden=new[]{"Prompt","PromptText","SystemPrompt","UserPrompt","LlmResponse","RawModelResponse","ModelRequest","ModelParameters","Temperature","TopP","TokenCount","Ssml","Audio","AudioUrl","VoiceId","SpeechRate","Subtitle","Srt","Vtt","TtsText"};var properties=types.SelectMany(x=>x.GetProperties()).ToArray();Assert.All(properties,p=>Assert.False(p.SetMethod?.IsPublic??false));Assert.Empty(properties.Where(p=>forbidden.Contains(p.Name,StringComparer.Ordinal)));Assert.Equal(new[]{typeof(DocumentaryNarrativePassage),typeof(DocumentaryNarrativePassageInput)},types.Where(t=>t.GetProperty("Text") is not null));}
    [Fact] public void Passage_type_inventory_is_exact()=>Assert.Equal(new[]{"Opening","Question","Orientation","Discovery","Explanation","Evidence","Context","Clarification","Observation","Guidance","Reflection","Transition","Closing"},Enum.GetNames<DocumentaryNarrativePassageType>());
}
