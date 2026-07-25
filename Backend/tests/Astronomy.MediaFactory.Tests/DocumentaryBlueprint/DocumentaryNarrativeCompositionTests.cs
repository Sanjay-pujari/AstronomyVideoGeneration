using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using DocumentaryBlueprintModel = Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeCompositionFixture
{
    internal static DocumentaryNarrativeCompositionRequest Request(params DocumentarySceneBlueprint[] scenes)
    {
        var blueprint = scenes.Length == 0 ? OrionDocumentaryBlueprintValidationFixture.Create() : OrionDocumentaryBlueprintValidationFixture.Create(scenes);
        return new("narrative.orion.long.v1", "1", Metadata(), blueprint,
            new DocumentaryBlueprintEditorialValidator().Validate(blueprint));
    }

    internal static NarrativeCompositionMetadata Metadata(DateTimeOffset? createdUtc = null) => new(
        createdUtc ?? new DateTimeOffset(2026, 1, 15, 12, 30, 0, TimeSpan.Zero), "narrative-system",
        "narrative-model-v1", "1", "1.0", "1.0", "correlation-orion-composition-001");

    internal static DocumentaryNarrativeComposition Composition(params DocumentarySceneBlueprint[] scenes) =>
        new DocumentaryNarrativeComposer().Compose(Request(scenes));

    internal static DocumentaryNarrativeBeat Beat(int number = 1, string? id = null,
        IReadOnlyList<KnowledgeReference>? knowledge = null, IReadOnlyList<VisualOpportunity>? visuals = null) =>
        new(id ?? $"scene.{number}.beat", number, $"scene.{number}", number, $"Scene {number}",
            DocumentaryNarrativeBeatType.Hook, DocumentaryNarrativeStage.Wonder, DocumentarySceneRole.OpeningHook,
            new ViewerQuestion($"Question {number}?"), $"Purpose {number}.",
            knowledge ?? [new KnowledgeReference($"knowledge.{number}", "Science", "Evidence", true)],
            visuals ?? [new VisualOpportunity($"Visual {number}", "SkyMap", $"knowledge.{number}", null, true)],
            new SceneTransition("Continue", "Next?", "Advance"),
            new EditorialOutcome("Takeaway", "Contribution", true, true, true, false, false), 42);

    internal static DocumentaryNarrativeSection Section(int number = 1, string? id = null,
        IReadOnlyList<DocumentaryNarrativeBeat>? beats = null) => new(id ?? $"section.{number}", number,
        "Opening", "Establish the documentary opening.", DocumentaryNarrativeStage.Wonder,
        DocumentaryNarrativeSectionRole.Opening, beats ?? [Beat(number)], 42);

    internal static DocumentaryNarrativeComposition Aggregate(IReadOnlyList<DocumentaryNarrativeSection>? sections = null)
    {
        var request = Request();
        return new(request.CompositionId, request.Blueprint.BlueprintId, request.Blueprint.KnowledgeId,
            request.Blueprint.SubjectId, request.Blueprint.SubjectName, request.Blueprint.PublicationFormat,
            request.Blueprint.PrimaryLanguage, request.Version, request.Metadata, sections ?? [Section()]);
    }
}

public sealed class DocumentaryNarrativeComposerTests
{
    [Fact]
    public void Produces_one_ordered_beat_per_scene()
    {
        var request = OrionDocumentaryNarrativeCompositionFixture.Request();
        var result = new DocumentaryNarrativeComposer().Compose(request);
        Assert.Equal(request.CompositionId, result.CompositionId);
        Assert.Equal(request.Blueprint.BlueprintId, result.BlueprintId);
        Assert.Equal(request.Blueprint.Scenes.Select(x => x.SceneId), result.Sections.SelectMany(x => x.Beats).Select(x => x.SourceSceneId));
        Assert.Equal(3, result.Sections.Count);
    }
}

public sealed class DocumentaryNarrativeComposerMappingTests
{
    [Fact]
    public void Maps_all_scene_roles_to_exact_beat_and_section_roles()
    {
        var roles = Enum.GetValues<DocumentarySceneRole>();
        var expectedBeat = new[] { DocumentaryNarrativeBeatType.Hook, DocumentaryNarrativeBeatType.Orientation, DocumentaryNarrativeBeatType.Discovery, DocumentaryNarrativeBeatType.Discovery, DocumentaryNarrativeBeatType.Explanation, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Clarification, DocumentaryNarrativeBeatType.Observation, DocumentaryNarrativeBeatType.Guidance, DocumentaryNarrativeBeatType.Closure };
        var expectedSection = new[] { DocumentaryNarrativeSectionRole.Opening, DocumentaryNarrativeSectionRole.Orientation, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Explanation, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Correction, DocumentaryNarrativeSectionRole.PracticalGuidance, DocumentaryNarrativeSectionRole.PracticalGuidance, DocumentaryNarrativeSectionRole.Closing };
        Assert.Equal(expectedBeat, roles.Select(DocumentaryNarrativeCompositionMappings.BeatType));
        Assert.Equal(expectedSection, roles.Select(DocumentaryNarrativeCompositionMappings.SectionRole));
    }

    [Fact]
    public void Preserves_every_field_of_every_scene()
    {
        var request = OrionDocumentaryNarrativeCompositionFixture.Request();
        var composition = new DocumentaryNarrativeComposer().Compose(request);
        var sourceScenes = request.Blueprint.Scenes;
        var beats = composition.Sections.SelectMany(section => section.Beats).ToArray();
        Assert.Equal(sourceScenes.Count, beats.Length);
        for (var index = 0; index < sourceScenes.Count; index++)
        {
            var source = sourceScenes[index]; var beat = beats[index];
            Assert.Equal(source.SceneId + ".beat", beat.BeatId);
            Assert.Equal(source.SceneNumber, beat.BeatNumber);
            Assert.Equal(source.SceneId, beat.SourceSceneId);
            Assert.Equal(source.SceneNumber, beat.SourceSceneNumber);
            Assert.Equal(source.Title, beat.Title);
            Assert.Equal(DocumentaryNarrativeCompositionMappings.BeatType(source.SceneRole), beat.BeatType);
            Assert.Equal(source.NarrativeStage, beat.NarrativeStage);
            Assert.Equal(source.SceneRole, beat.SceneRole);
            Assert.Equal(source.ViewerQuestion, beat.ViewerQuestion);
            Assert.Equal(source.SceneObjective.Summary, beat.Purpose);
            Assert.Equal(source.KnowledgeReferences, beat.KnowledgeReferences);
            Assert.Equal(source.VisualOpportunities, beat.VisualOpportunities);
            Assert.Equal(source.Transition, beat.Transition);
            Assert.Equal(source.EditorialOutcome, beat.EditorialOutcome);
            Assert.Equal(source.EstimatedDurationSeconds, beat.EstimatedDurationSeconds);
        }
    }
}

public sealed class DocumentaryNarrativeComposerValidationTests
{
    [Fact] public void Rejects_null_request() => Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeComposer().Compose(null!));
    [Fact] public void Rejects_mismatched_validation_result() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeComposer().Compose(new(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new("other", [])))); }
    [Fact] public void Rejects_invalid_validation_result() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); var finding = new DocumentaryBlueprintValidationFinding("x", DocumentaryBlueprintValidationSeverity.Error, "bad", r.Blueprint.BlueprintId); Assert.Throws<InvalidOperationException>(() => new DocumentaryNarrativeComposer().Compose(new(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new(r.Blueprint.BlueprintId, [finding])))); }
    [Fact] public void Accepts_warning_only_result() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); var warning = new DocumentaryBlueprintValidationFinding("x", DocumentaryBlueprintValidationSeverity.Warning, "review", r.Blueprint.BlueprintId); Assert.NotNull(new DocumentaryNarrativeComposer().Compose(new(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new(r.Blueprint.BlueprintId, [warning])))); }
}

public sealed class DocumentaryNarrativeComposerGroupingTests
{
    [Fact]
    public void Certifies_every_detail_of_consecutive_grouping()
    {
        var roles = new[] { DocumentarySceneRole.OpeningHook, DocumentarySceneRole.RecognitionGuide, DocumentarySceneRole.CoreDiscovery, DocumentarySceneRole.ScientificExplanation, DocumentarySceneRole.HistoricalContext, DocumentarySceneRole.CulturalContext, DocumentarySceneRole.ReflectiveClosing };
        var scenes = roles.Select((role, i) => OrionDocumentaryBlueprintValidationFixture.Scene(i + 1, role, duration: i + 10)).ToArray();
        var sections = OrionDocumentaryNarrativeCompositionFixture.Composition(scenes).Sections;
        var sectionRoles = new[] { DocumentaryNarrativeSectionRole.Opening, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Explanation, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Closing };
        Assert.Equal(5, sections.Count);
        Assert.Equal(sectionRoles, sections.Select(x => x.SectionRole));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, sections.Select(x => x.SectionNumber));
        Assert.Equal(Enumerable.Range(1, 5).Select(x => $"narrative.orion.long.v1.section.{x}"), sections.Select(x => x.SectionId));
        Assert.Equal(sectionRoles.Select(DocumentaryNarrativeCompositionMappings.Title), sections.Select(x => x.Title));
        Assert.Equal(sectionRoles.Select(DocumentaryNarrativeCompositionMappings.Purpose), sections.Select(x => x.Purpose));
        Assert.Equal(new[] { new[] { 1 }, new[] { 2, 3 }, new[] { 4 }, new[] { 5, 6 }, new[] { 7 } }, sections.Select(x => x.Beats.Select(b => b.BeatNumber).ToArray()));
        Assert.All(sections, section => Assert.Equal(section.Beats.Sum(x => x.EstimatedDurationSeconds), section.EstimatedDurationSeconds));
        Assert.All(sections, section => Assert.Equal(section.Beats[0].NarrativeStage, section.NarrativeStage));
    }

    [Fact]
    public void Nonconsecutive_equal_roles_are_not_merged()
    {
        var roles = new[] { DocumentarySceneRole.OpeningHook, DocumentarySceneRole.HistoricalContext, DocumentarySceneRole.ScientificExplanation, DocumentarySceneRole.CulturalContext, DocumentarySceneRole.ReflectiveClosing };
        var scenes = roles.Select((role, i) => OrionDocumentaryBlueprintValidationFixture.Scene(i + 1, role)).ToArray();
        var sections = OrionDocumentaryNarrativeCompositionFixture.Composition(scenes).Sections;
        Assert.Equal(5, sections.Count);
        Assert.Equal(new[] { DocumentaryNarrativeSectionRole.Opening, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Explanation, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Closing }, sections.Select(x => x.SectionRole));
        Assert.NotEqual(sections[1].SectionId, sections[3].SectionId);
        Assert.Equal(new[] { 2 }, sections[1].Beats.Select(x => x.BeatNumber));
        Assert.Equal(new[] { 4 }, sections[3].Beats.Select(x => x.BeatNumber));
    }
}

public sealed class DocumentaryNarrativeCompositionContractTests
{
    private static DocumentaryNarrativeComposition Valid() => OrionDocumentaryNarrativeCompositionFixture.Aggregate();
    private static DocumentaryNarrativeComposition Make(DocumentaryNarrativeComposition c, string? compositionId = null, string? blueprintId = null, string? knowledgeId = null, string? subjectId = null, string? subjectName = null, BlueprintPublicationFormat? format = null, string? language = null, string? version = null, NarrativeCompositionMetadata? metadata = null, IReadOnlyList<DocumentaryNarrativeSection>? sections = null) => new(compositionId ?? c.CompositionId, blueprintId ?? c.BlueprintId, knowledgeId ?? c.KnowledgeId, subjectId ?? c.SubjectId, subjectName ?? c.SubjectName, format ?? c.PublicationFormat, language ?? c.PrimaryLanguage, version ?? c.Version, metadata ?? c.Metadata, sections ?? c.Sections);
    [Theory] [InlineData("")] [InlineData(" ")] public void Rejects_blank_composition_id(string value) { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, compositionId: value)); }
    [Fact] public void Rejects_blank_blueprint_id() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, blueprintId: " ")); }
    [Fact] public void Rejects_blank_knowledge_id() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, knowledgeId: " ")); }
    [Fact] public void Rejects_blank_subject_id() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, subjectId: " ")); }
    [Fact] public void Rejects_blank_subject_name() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, subjectName: " ")); }
    [Fact] public void Rejects_blank_primary_language() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, language: " ")); }
    [Fact] public void Rejects_blank_version() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, version: " ")); }
    [Fact] public void Rejects_undefined_publication_format() { var c = Valid(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(c, format: (BlueprintPublicationFormat)999)); }
    [Fact] public void Rejects_null_metadata() { var c = Valid(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeComposition(c.CompositionId, c.BlueprintId, c.KnowledgeId, c.SubjectId, c.SubjectName, c.PublicationFormat, c.PrimaryLanguage, c.Version, null!, c.Sections)); }
    [Fact] public void Rejects_null_sections() { var c = Valid(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeComposition(c.CompositionId, c.BlueprintId, c.KnowledgeId, c.SubjectId, c.SubjectName, c.PublicationFormat, c.PrimaryLanguage, c.Version, c.Metadata, null!)); }
    [Fact] public void Rejects_null_section_element() { var c = Valid(); Assert.Throws<ArgumentException>(() => Make(c, sections: [null!])); }
    [Fact] public void Rejects_duplicate_section_ids() { var c = Valid(); var a = OrionDocumentaryNarrativeCompositionFixture.Section(1, "same"); var b = OrionDocumentaryNarrativeCompositionFixture.Section(2, "same"); Assert.Throws<ArgumentException>(() => Make(c, sections: [a, b])); }
    [Fact] public void Rejects_duplicate_section_numbers() { var c = Valid(); var a = OrionDocumentaryNarrativeCompositionFixture.Section(1, "a"); var b = OrionDocumentaryNarrativeCompositionFixture.Section(1, "b"); Assert.Throws<ArgumentException>(() => Make(c, sections: [a, b])); }
    [Fact] public void Valid_construction_preserves_values() { var c = Valid(); Assert.Equal("narrative.orion.long.v1", c.CompositionId); Assert.Single(c.Sections); }
}

public sealed class DocumentaryNarrativeSectionContractTests
{
    private static DocumentaryNarrativeSection B() => OrionDocumentaryNarrativeCompositionFixture.Section();
    private static DocumentaryNarrativeSection Make(DocumentaryNarrativeSection s, string? id = null, string? title = null, string? purpose = null, DocumentaryNarrativeStage? stage = null, DocumentaryNarrativeSectionRole? role = null, IReadOnlyList<DocumentaryNarrativeBeat>? beats = null, int? duration = null) => new(id ?? s.SectionId, s.SectionNumber, title ?? s.Title, purpose ?? s.Purpose, stage ?? s.NarrativeStage, role ?? s.SectionRole, beats ?? s.Beats, duration ?? s.EstimatedDurationSeconds);
    [Fact] public void Rejects_blank_section_id() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, id: " ")); }
    [Fact] public void Rejects_blank_title() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, title: " ")); }
    [Fact] public void Rejects_blank_purpose() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, purpose: " ")); }
    [Fact] public void Rejects_undefined_stage() { var s = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(s, stage: (DocumentaryNarrativeStage)999)); }
    [Fact] public void Rejects_undefined_role() { var s = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(s, role: (DocumentaryNarrativeSectionRole)999)); }
    [Fact] public void Rejects_null_beats() { var s = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeSection(s.SectionId, s.SectionNumber, s.Title, s.Purpose, s.NarrativeStage, s.SectionRole, null!, s.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_beat_element() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, beats: [null!])); }
    [Fact] public void Rejects_duplicate_beat_ids() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, beats: [OrionDocumentaryNarrativeCompositionFixture.Beat(1, "same"), OrionDocumentaryNarrativeCompositionFixture.Beat(2, "same")])); }
    [Fact] public void Rejects_duplicate_beat_numbers() { var s = B(); Assert.Throws<ArgumentException>(() => Make(s, beats: [OrionDocumentaryNarrativeCompositionFixture.Beat(1, "a"), OrionDocumentaryNarrativeCompositionFixture.Beat(1, "b")])); }
    [Fact] public void Rejects_negative_duration() { var s = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(s, duration: -1)); }
    [Fact] public void Valid_construction_preserves_structural_values() { var beats = new[] { OrionDocumentaryNarrativeCompositionFixture.Beat(2), OrionDocumentaryNarrativeCompositionFixture.Beat(1) }; var s = new DocumentaryNarrativeSection("s", 7, "Title", "Purpose", DocumentaryNarrativeStage.History, DocumentaryNarrativeSectionRole.Context, beats, 84); Assert.Equal(7, s.SectionNumber); Assert.Equal(DocumentaryNarrativeStage.History, s.NarrativeStage); Assert.Equal(DocumentaryNarrativeSectionRole.Context, s.SectionRole); Assert.Equal(beats, s.Beats); Assert.Equal(84, s.EstimatedDurationSeconds); }
}

public sealed class DocumentaryNarrativeBeatContractTests
{
    private static DocumentaryNarrativeBeat B() => OrionDocumentaryNarrativeCompositionFixture.Beat();
    private static DocumentaryNarrativeBeat Make(DocumentaryNarrativeBeat b, string? id = null, string? sourceId = null, string? title = null, string? purpose = null, DocumentaryNarrativeBeatType? type = null, DocumentaryNarrativeStage? stage = null, DocumentarySceneRole? role = null, IReadOnlyList<KnowledgeReference>? knowledge = null, IReadOnlyList<VisualOpportunity>? visuals = null, int? duration = null) => new(id ?? b.BeatId, b.BeatNumber, sourceId ?? b.SourceSceneId, b.SourceSceneNumber, title ?? b.Title, type ?? b.BeatType, stage ?? b.NarrativeStage, role ?? b.SceneRole, b.ViewerQuestion, purpose ?? b.Purpose, knowledge ?? b.KnowledgeReferences, visuals ?? b.VisualOpportunities, b.Transition, b.EditorialOutcome, duration ?? b.EstimatedDurationSeconds);
    [Fact] public void Rejects_blank_beat_id() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, id: " ")); }
    [Fact] public void Rejects_blank_source_scene_id() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, sourceId: " ")); }
    [Fact] public void Rejects_blank_title() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, title: " ")); }
    [Fact] public void Rejects_blank_purpose() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, purpose: " ")); }
    [Fact] public void Rejects_undefined_type() { var b = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(b, type: (DocumentaryNarrativeBeatType)999)); }
    [Fact] public void Rejects_undefined_stage() { var b = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(b, stage: (DocumentaryNarrativeStage)999)); }
    [Fact] public void Rejects_undefined_scene_role() { var b = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(b, role: (DocumentarySceneRole)999)); }
    [Fact] public void Rejects_null_viewer_question() { var b = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, null!, b.Purpose, b.KnowledgeReferences, b.VisualOpportunities, b.Transition, b.EditorialOutcome, b.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_knowledge_collection() { var b = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, null!, b.VisualOpportunities, b.Transition, b.EditorialOutcome, b.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_visual_collection() { var b = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, b.KnowledgeReferences, null!, b.Transition, b.EditorialOutcome, b.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_transition() { var b = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, b.KnowledgeReferences, b.VisualOpportunities, null!, b.EditorialOutcome, b.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_editorial_outcome() { var b = B(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, b.KnowledgeReferences, b.VisualOpportunities, b.Transition, null!, b.EstimatedDurationSeconds)); }
    [Fact] public void Rejects_null_knowledge_element() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, knowledge: [null!])); }
    [Fact] public void Rejects_null_visual_element() { var b = B(); Assert.Throws<ArgumentException>(() => Make(b, visuals: [null!])); }
    [Fact] public void Rejects_negative_duration() { var b = B(); Assert.Throws<ArgumentOutOfRangeException>(() => Make(b, duration: -1)); }
    [Fact] public void Valid_construction_preserves_every_field() { var b = B(); Assert.Equal("scene.1.beat", b.BeatId); Assert.Equal(1, b.BeatNumber); Assert.Equal("scene.1", b.SourceSceneId); Assert.Equal(1, b.SourceSceneNumber); Assert.Equal("Scene 1", b.Title); Assert.Equal(DocumentaryNarrativeBeatType.Hook, b.BeatType); Assert.Equal(DocumentaryNarrativeStage.Wonder, b.NarrativeStage); Assert.Equal(DocumentarySceneRole.OpeningHook, b.SceneRole); Assert.Equal("Question 1?", b.ViewerQuestion.Text); Assert.Equal("Purpose 1.", b.Purpose); Assert.Single(b.KnowledgeReferences); Assert.Single(b.VisualOpportunities); Assert.Equal("Continue", b.Transition.TransitionIntent); Assert.Equal("Takeaway", b.EditorialOutcome.ViewerTakeaway); Assert.Equal(42, b.EstimatedDurationSeconds); }
}

public sealed class NarrativeCompositionMetadataTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
    private static NarrativeCompositionMetadata Make(DateTimeOffset? time = null, string createdBy = "creator", string model = "model", string blueprintVersion = "1", string blueprintSchema = "1.0", string compositionSchema = "1.0", string correlation = "correlation") => new(time ?? Timestamp, createdBy, model, blueprintVersion, blueprintSchema, compositionSchema, correlation);
    [Fact] public void Rejects_default_timestamp() => Assert.Throws<ArgumentException>(() => new NarrativeCompositionMetadata(default, "creator", "model", "1", "1.0", "1.0", "correlation"));
    [Fact] public void Rejects_blank_created_by() => Assert.Throws<ArgumentException>(() => Make(createdBy: " "));
    [Fact] public void Rejects_blank_composition_model_version() => Assert.Throws<ArgumentException>(() => Make(model: " "));
    [Fact] public void Rejects_blank_blueprint_version() => Assert.Throws<ArgumentException>(() => Make(blueprintVersion: " "));
    [Fact] public void Rejects_blank_blueprint_schema_version() => Assert.Throws<ArgumentException>(() => Make(blueprintSchema: " "));
    [Fact] public void Rejects_blank_correlation_id() => Assert.Throws<ArgumentException>(() => Make(correlation: " "));
    [Theory] [InlineData("")] [InlineData("2.0")] public void Rejects_unapproved_composition_schema_version(string value) => Assert.Throws<ArgumentException>(() => Make(compositionSchema: value));
    [Fact] public void Preserves_externally_supplied_timestamp_exactly() { var value = new DateTimeOffset(2026, 2, 3, 4, 5, 6, 789, TimeSpan.FromHours(3)); Assert.Equal(value, Make(value).CreatedUtc); }
}

public sealed class DocumentaryNarrativeCompositionRequestTests
{
    private static DocumentaryNarrativeCompositionRequest R() => OrionDocumentaryNarrativeCompositionFixture.Request();
    [Fact] public void Rejects_blank_composition_id() { var r = R(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeCompositionRequest(" ", r.Version, r.Metadata, r.Blueprint, r.ValidationResult)); }
    [Fact] public void Rejects_blank_version() { var r = R(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, " ", r.Metadata, r.Blueprint, r.ValidationResult)); }
    [Fact] public void Rejects_null_metadata() { var r = R(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, null!, r.Blueprint, r.ValidationResult)); }
    [Fact] public void Rejects_null_blueprint() { var r = R(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, r.Metadata, null!, r.ValidationResult)); }
    [Fact] public void Rejects_null_validation_result() { var r = R(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, r.Metadata, r.Blueprint, null!)); }
}

public sealed class DocumentaryNarrativeDefensiveCopyingTests
{
    [Fact] public void Composition_copies_sections_and_exposure_rejects_mutation() { var list = new List<DocumentaryNarrativeSection> { OrionDocumentaryNarrativeCompositionFixture.Section() }; var value = OrionDocumentaryNarrativeCompositionFixture.Aggregate(list); list.Clear(); Assert.Single(value.Sections); Assert.Throws<NotSupportedException>(() => ((IList<DocumentaryNarrativeSection>)value.Sections).Clear()); }
    [Fact] public void Section_copies_beats_and_exposure_rejects_mutation() { var list = new List<DocumentaryNarrativeBeat> { OrionDocumentaryNarrativeCompositionFixture.Beat() }; var value = OrionDocumentaryNarrativeCompositionFixture.Section(beats: list); list.Clear(); Assert.Single(value.Beats); Assert.Throws<NotSupportedException>(() => ((IList<DocumentaryNarrativeBeat>)value.Beats).Clear()); }
    [Fact] public void Beat_copies_knowledge_references_and_exposure_rejects_mutation() { var list = new List<KnowledgeReference> { new("k", "s", "p", true) }; var value = OrionDocumentaryNarrativeCompositionFixture.Beat(knowledge: list); list.Clear(); Assert.Single(value.KnowledgeReferences); Assert.Throws<NotSupportedException>(() => ((IList<KnowledgeReference>)value.KnowledgeReferences).Clear()); }
    [Fact] public void Beat_copies_visual_opportunities_and_exposure_rejects_mutation() { var list = new List<VisualOpportunity> { new("v", "type", null, null, false) }; var value = OrionDocumentaryNarrativeCompositionFixture.Beat(visuals: list); list.Clear(); Assert.Single(value.VisualOpportunities); Assert.Throws<NotSupportedException>(() => ((IList<VisualOpportunity>)value.VisualOpportunities).Clear()); }
}

public sealed class DocumentaryNarrativeComposerImmutabilityTests
{
    [Fact]
    public void Compose_does_not_mutate_any_input_or_nested_order()
    {
        var request = OrionDocumentaryNarrativeCompositionFixture.Request();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var blueprintJson = JsonSerializer.Serialize(request.Blueprint, options);
        var validationJson = JsonSerializer.Serialize(request.ValidationResult, options);
        var sceneOrder = request.Blueprint.Scenes.Select(x => x.SceneId).ToArray();
        var referenceOrder = request.Blueprint.Scenes.Select(x => x.KnowledgeReferences.Select(k => k.KnowledgeEntryId).ToArray()).ToArray();
        var visualOrder = request.Blueprint.Scenes.Select(x => x.VisualOpportunities.Select(v => v.Description).ToArray()).ToArray();
        var findingOrder = request.ValidationResult.Findings.Select(x => x.RuleCode).ToArray();
        _ = new DocumentaryNarrativeComposer().Compose(request);
        Assert.Equal(blueprintJson, JsonSerializer.Serialize(request.Blueprint, options));
        Assert.Equal(validationJson, JsonSerializer.Serialize(request.ValidationResult, options));
        Assert.Equal(sceneOrder, request.Blueprint.Scenes.Select(x => x.SceneId));
        Assert.Equal(referenceOrder, request.Blueprint.Scenes.Select(x => x.KnowledgeReferences.Select(k => k.KnowledgeEntryId).ToArray()));
        Assert.Equal(visualOrder, request.Blueprint.Scenes.Select(x => x.VisualOpportunities.Select(v => v.Description).ToArray()));
        Assert.Equal(findingOrder, request.ValidationResult.Findings.Select(x => x.RuleCode));
    }
}

public sealed class DocumentaryNarrativeCompositionInventoryTests
{
    [Fact]
    public void Fixed_titles_are_exact()
    {
        var roles = Enum.GetValues<DocumentaryNarrativeSectionRole>();
        Assert.Equal(new[] { "Opening", "Orientation", "Exploration", "Explanation", "Context", "Clarification", "Practical Guidance", "Reflection", "Closing" }, roles.Select(DocumentaryNarrativeCompositionMappings.Title));
    }

    [Fact]
    public void Fixed_purposes_are_exact()
    {
        var roles = Enum.GetValues<DocumentaryNarrativeSectionRole>();
        Assert.Equal(new[] { "Establish the documentary opening.", "Orient the viewer to the subject.", "Guide recognition and discovery.", "Develop scientific understanding.", "Provide historical, cultural, or mythological context.", "Clarify a misconception or misunderstanding.", "Provide practical observation or astrophotography guidance.", "Encourage reflection on the subject.", "Provide documentary closure." }, roles.Select(DocumentaryNarrativeCompositionMappings.Purpose));
    }
}

public sealed class DocumentaryNarrativeCompositionSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static T RoundTrip<T>(T source, out string json) { json = JsonSerializer.Serialize(source, Options); return JsonSerializer.Deserialize<T>(json, Options)!; }

    [Fact]
    public void Metadata_round_trips_every_field_deterministically()
    {
        var source = OrionDocumentaryNarrativeCompositionFixture.Metadata(); var copy = RoundTrip(source, out var json);
        Assert.Equal(source.CreatedUtc, copy.CreatedUtc); Assert.Equal(source.CreatedBy, copy.CreatedBy); Assert.Equal(source.CompositionModelVersion, copy.CompositionModelVersion); Assert.Equal(source.BlueprintVersion, copy.BlueprintVersion); Assert.Equal(source.BlueprintSchemaVersion, copy.BlueprintSchemaVersion); Assert.Equal(source.CompositionSchemaVersion, copy.CompositionSchemaVersion); Assert.Equal(source.CorrelationId, copy.CorrelationId);
        Assert.Equal(json, JsonSerializer.Serialize(copy, Options));
    }

    [Fact]
    public void Beat_round_trips_every_field_and_nested_order_deterministically()
    {
        var knowledge = new[] { new KnowledgeReference("k2", "two", "p2", false), new KnowledgeReference("k1", "one", "p1", true) };
        var visuals = new[] { new VisualOpportunity("v2", "t2", "k2", "a2", false), new VisualOpportunity("v1", "t1", "k1", "a1", true) };
        var source = OrionDocumentaryNarrativeCompositionFixture.Beat(7, knowledge: knowledge, visuals: visuals); var copy = RoundTrip(source, out var json);
        Assert.Equal(source.BeatId, copy.BeatId); Assert.Equal(source.BeatNumber, copy.BeatNumber); Assert.Equal(source.SourceSceneId, copy.SourceSceneId); Assert.Equal(source.SourceSceneNumber, copy.SourceSceneNumber); Assert.Equal(source.Title, copy.Title); Assert.Equal(source.BeatType, copy.BeatType); Assert.Equal(source.NarrativeStage, copy.NarrativeStage); Assert.Equal(source.SceneRole, copy.SceneRole); Assert.Equal(source.ViewerQuestion, copy.ViewerQuestion); Assert.Equal(source.Purpose, copy.Purpose); Assert.Equal(source.KnowledgeReferences, copy.KnowledgeReferences); Assert.Equal(source.VisualOpportunities, copy.VisualOpportunities); Assert.Equal(source.Transition, copy.Transition); Assert.Equal(source.EditorialOutcome, copy.EditorialOutcome); Assert.Equal(source.EstimatedDurationSeconds, copy.EstimatedDurationSeconds);
        Assert.Equal(json, JsonSerializer.Serialize(copy, Options));
    }

    [Fact]
    public void Section_round_trips_metadata_beats_and_order_deterministically()
    {
        var source = new DocumentaryNarrativeSection("section", 4, "Context", "Purpose", DocumentaryNarrativeStage.History, DocumentaryNarrativeSectionRole.Context, [OrionDocumentaryNarrativeCompositionFixture.Beat(2), OrionDocumentaryNarrativeCompositionFixture.Beat(1)], 84); var copy = RoundTrip(source, out var json);
        Assert.Equal(source.SectionId, copy.SectionId); Assert.Equal(source.SectionNumber, copy.SectionNumber); Assert.Equal(source.Title, copy.Title); Assert.Equal(source.Purpose, copy.Purpose); Assert.Equal(source.NarrativeStage, copy.NarrativeStage); Assert.Equal(source.SectionRole, copy.SectionRole); Assert.Equal(source.Beats.Select(x => x.BeatId), copy.Beats.Select(x => x.BeatId)); Assert.Equal(source.Beats.Select(x => x.Purpose), copy.Beats.Select(x => x.Purpose)); Assert.Equal(source.EstimatedDurationSeconds, copy.EstimatedDurationSeconds);
        Assert.Equal(json, JsonSerializer.Serialize(copy, Options));
    }

    [Fact]
    public void Aggregate_round_trips_every_field_and_nested_order_deterministically()
    {
        var source = OrionDocumentaryNarrativeCompositionFixture.Composition(); var copy = RoundTrip(source, out var json);
        Assert.Equal(source.CompositionId, copy.CompositionId); Assert.Equal(source.BlueprintId, copy.BlueprintId); Assert.Equal(source.KnowledgeId, copy.KnowledgeId); Assert.Equal(source.SubjectId, copy.SubjectId); Assert.Equal(source.SubjectName, copy.SubjectName); Assert.Equal(source.PublicationFormat, copy.PublicationFormat); Assert.Equal(source.PrimaryLanguage, copy.PrimaryLanguage); Assert.Equal(source.Version, copy.Version); Assert.Equal(source.Metadata, copy.Metadata);
        Assert.Equal(source.Sections.Select(x => x.SectionId), copy.Sections.Select(x => x.SectionId)); Assert.Equal(source.Sections.Select(x => x.SectionRole), copy.Sections.Select(x => x.SectionRole)); Assert.Equal(source.Sections.SelectMany(x => x.Beats).Select(x => x.BeatId), copy.Sections.SelectMany(x => x.Beats).Select(x => x.BeatId)); Assert.Equal(source.Sections.SelectMany(x => x.Beats).Select(x => x.BeatType), copy.Sections.SelectMany(x => x.Beats).Select(x => x.BeatType)); Assert.Equal(source.Sections.SelectMany(x => x.Beats).SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId), copy.Sections.SelectMany(x => x.Beats).SelectMany(x => x.KnowledgeReferences).Select(x => x.KnowledgeEntryId)); Assert.Equal(source.Sections.SelectMany(x => x.Beats).SelectMany(x => x.VisualOpportunities).Select(x => x.Description), copy.Sections.SelectMany(x => x.Beats).SelectMany(x => x.VisualOpportunities).Select(x => x.Description));
        Assert.Equal(json, JsonSerializer.Serialize(copy, Options));
    }
}

public sealed class DocumentaryNarrativeComposerArchitectureTests
{
    private static string[] Properties<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Order().ToArray();
    private static void AssertProperties<T>(params string[] expected) => Assert.Equal(expected.Order(), Properties<T>());

    [Fact]
    public void O21_and_O22_property_inventories_are_exact()
    {
        AssertProperties<DocumentaryBlueprintModel>("BlueprintId", "KnowledgeId", "SubjectId", "SubjectName", "PublicationFormat", "PrimaryLanguage", "Version", "Metadata", "Scenes");
        AssertProperties<DocumentarySceneBlueprint>("SceneId", "SceneNumber", "Title", "NarrativeStage", "SceneRole", "ViewerQuestion", "SceneObjective", "EditorialOutcome", "EditorialPriority", "KnowledgeReferences", "VisualOpportunities", "Transition", "EstimatedDurationSeconds");
        AssertProperties<DocumentaryBlueprintBuildRequest>("BlueprintId", "KnowledgeId", "SubjectId", "SubjectName", "PublicationFormat", "PrimaryLanguage", "Version", "Metadata", "Scenes");
        AssertProperties<DocumentarySceneBlueprintInput>("SceneId", "SceneNumber", "Title", "NarrativeStage", "SceneRole", "ViewerQuestion", "SceneObjective", "EditorialOutcome", "EditorialPriority", "KnowledgeReferences", "VisualOpportunities", "Transition", "EstimatedDurationSeconds");
    }

    [Fact]
    public void Builder_boundary_is_sealed_parameterless_and_synchronous()
    {
        var type = typeof(DocumentaryBlueprintBuilder); var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.True(type.IsSealed); Assert.Empty(type.GetConstructors().Single().GetParameters()); Assert.Equal("Build", method.Name); Assert.Equal(typeof(DocumentaryBlueprintModel), method.ReturnType); Assert.Equal(typeof(DocumentaryBlueprintBuildRequest), Assert.Single(method.GetParameters()).ParameterType); Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }

    [Fact]
    public void O23_inventories_rules_and_validator_boundary_are_exact()
    {
        AssertProperties<DocumentaryBlueprintValidationFinding>("RuleCode", "Severity", "Message", "BlueprintId", "SceneId", "SceneNumber", "FieldName");
        AssertProperties<DocumentaryBlueprintValidationResult>("BlueprintId", "Findings", "IsValid", "ErrorCount", "WarningCount");
        var expectedCodes = Enumerable.Range(1, 16).Select(x => $"DBP-EDITORIAL-{x:000}").ToArray();
        var expectedSeverities = new[] { "Error", "Error", "Error", "Error", "Error", "Error", "Error", "Warning", "Warning", "Warning", "Warning", "Error", "Warning", "Error", "Error", "Warning" };
        Assert.Equal(expectedCodes, DocumentaryBlueprintEditorialRuleCodes.Inventory.Select(x => x.Code)); Assert.Equal(expectedSeverities, DocumentaryBlueprintEditorialRuleCodes.Inventory.Select(x => x.Severity.ToString()));
        var type = typeof(DocumentaryBlueprintEditorialValidator); var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.True(type.IsSealed); Assert.Empty(type.GetConstructors().Single().GetParameters()); Assert.Equal(typeof(DocumentaryBlueprintValidationResult), method.ReturnType); Assert.Equal(typeof(DocumentaryBlueprintModel), Assert.Single(method.GetParameters()).ParameterType); Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }

    [Fact]
    public void O24_property_inventories_and_composer_boundary_are_exact()
    {
        AssertProperties<NarrativeCompositionMetadata>("CreatedUtc", "CreatedBy", "CompositionModelVersion", "BlueprintVersion", "BlueprintSchemaVersion", "CompositionSchemaVersion", "CorrelationId");
        AssertProperties<DocumentaryNarrativeBeat>("BeatId", "BeatNumber", "SourceSceneId", "SourceSceneNumber", "Title", "BeatType", "NarrativeStage", "SceneRole", "ViewerQuestion", "Purpose", "KnowledgeReferences", "VisualOpportunities", "Transition", "EditorialOutcome", "EstimatedDurationSeconds");
        AssertProperties<DocumentaryNarrativeSection>("SectionId", "SectionNumber", "Title", "Purpose", "NarrativeStage", "SectionRole", "Beats", "EstimatedDurationSeconds");
        AssertProperties<DocumentaryNarrativeComposition>("CompositionId", "BlueprintId", "KnowledgeId", "SubjectId", "SubjectName", "PublicationFormat", "PrimaryLanguage", "Version", "Metadata", "Sections");
        AssertProperties<DocumentaryNarrativeCompositionRequest>("CompositionId", "Version", "Metadata", "Blueprint", "ValidationResult");
        var type = typeof(DocumentaryNarrativeComposer); var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.True(type.IsSealed); Assert.Empty(type.GetConstructors().Single().GetParameters()); Assert.Equal("Compose", method.Name); Assert.Equal(typeof(DocumentaryNarrativeComposition), method.ReturnType); Assert.Equal(typeof(DocumentaryNarrativeCompositionRequest), Assert.Single(method.GetParameters()).ParameterType); Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType));
    }

    [Fact]
    public void O24_contracts_expose_no_forbidden_generated_text_properties()
    {
        var types = new[] { typeof(DocumentaryNarrativeComposition), typeof(DocumentaryNarrativeSection), typeof(DocumentaryNarrativeBeat), typeof(NarrativeCompositionMetadata), typeof(DocumentaryNarrativeCompositionRequest) };
        var forbidden = new[] { "Narration", "NarrationText", "VoiceOver", "VoiceOverText", "Script", "ScriptText", "SpokenText", "Dialogue", "Paragraph", "Sentence", "Prompt", "PromptText", "GeneratedText", "LlmResponse", "TtsText", "Ssml", "ReplacementText", "SuggestedText", "AutoFix" };
        Assert.All(types.SelectMany(x => x.GetProperties()), property => Assert.False(property.CanWrite));
        Assert.Empty(types.SelectMany(x => x.GetProperties()).Where(x => forbidden.Contains(x.Name, StringComparer.Ordinal)));
        Assert.DoesNotContain(typeof(DocumentaryBlueprintEditorialValidator), typeof(DocumentaryNarrativeComposer).GetConstructors().SelectMany(x => x.GetParameters()).Select(x => x.ParameterType));
    }
}

public sealed class DocumentaryNarrativeCompositionEnumTests
{
    [Fact] public void Inventories_are_exact() { Assert.Equal(new[] { "Opening", "Orientation", "Exploration", "Explanation", "Context", "Correction", "PracticalGuidance", "Reflection", "Closing" }, Enum.GetNames<DocumentaryNarrativeSectionRole>()); Assert.Equal(new[] { "Hook", "Question", "Orientation", "Discovery", "Explanation", "Evidence", "Context", "Clarification", "Observation", "Guidance", "Reflection", "Transition", "Closure" }, Enum.GetNames<DocumentaryNarrativeBeatType>()); }
}
