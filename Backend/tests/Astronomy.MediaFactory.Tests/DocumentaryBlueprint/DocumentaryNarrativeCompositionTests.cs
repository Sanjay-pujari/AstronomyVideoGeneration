using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeCompositionFixture
{
    internal static DocumentaryNarrativeCompositionRequest Request(params DocumentarySceneBlueprint[] scenes)
    {
        var blueprint = scenes.Length == 0 ? OrionDocumentaryBlueprintValidationFixture.Create() : OrionDocumentaryBlueprintValidationFixture.Create(scenes);
        return new("narrative.orion.long.v1", "1", new(new DateTimeOffset(2026, 1, 15, 12, 30, 0, TimeSpan.Zero),
            "narrative-system", "narrative-model-v1", "1", "1.0", "1.0", "correlation-orion-composition-001"),
            blueprint, new DocumentaryBlueprintEditorialValidator().Validate(blueprint));
    }
    internal static DocumentaryNarrativeComposition Composition(params DocumentarySceneBlueprint[] scenes) => new DocumentaryNarrativeComposer().Compose(Request(scenes));
    internal static DocumentaryNarrativeBeat Beat() => Composition().Sections[0].Beats[0];
}

public sealed class DocumentaryNarrativeComposerTests
{
    [Fact] public void Produces_one_ordered_beat_per_scene() { var request = OrionDocumentaryNarrativeCompositionFixture.Request(); var result = new DocumentaryNarrativeComposer().Compose(request); Assert.Equal(request.CompositionId, result.CompositionId); Assert.Equal(request.Blueprint.BlueprintId, result.BlueprintId); Assert.Equal(request.Blueprint.Scenes.Select(x => x.SceneId), result.Sections.SelectMany(x => x.Beats).Select(x => x.SourceSceneId)); Assert.Equal(3, result.Sections.Count); }
}

public sealed class DocumentaryNarrativeComposerMappingTests
{
    [Fact] public void Maps_all_scene_roles_exhaustively()
    {
        var expectedBeat = new[] { DocumentaryNarrativeBeatType.Hook, DocumentaryNarrativeBeatType.Orientation, DocumentaryNarrativeBeatType.Discovery, DocumentaryNarrativeBeatType.Discovery, DocumentaryNarrativeBeatType.Explanation, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Context, DocumentaryNarrativeBeatType.Clarification, DocumentaryNarrativeBeatType.Observation, DocumentaryNarrativeBeatType.Guidance, DocumentaryNarrativeBeatType.Closure };
        var expectedSection = new[] { DocumentaryNarrativeSectionRole.Opening, DocumentaryNarrativeSectionRole.Orientation, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Explanation, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Correction, DocumentaryNarrativeSectionRole.PracticalGuidance, DocumentaryNarrativeSectionRole.PracticalGuidance, DocumentaryNarrativeSectionRole.Closing };
        var roles = Enum.GetValues<DocumentarySceneRole>();
        Assert.Equal(expectedBeat, roles.Select(DocumentaryNarrativeCompositionMappings.BeatType)); Assert.Equal(expectedSection, roles.Select(DocumentaryNarrativeCompositionMappings.SectionRole));
    }
    [Fact] public void Preserves_every_scene_value() { var request = OrionDocumentaryNarrativeCompositionFixture.Request(); var source = request.Blueprint.Scenes[0]; var beat = new DocumentaryNarrativeComposer().Compose(request).Sections[0].Beats[0]; Assert.Equal(source.SceneId + ".beat", beat.BeatId); Assert.Equal(source.SceneNumber, beat.BeatNumber); Assert.Equal(source.SceneId, beat.SourceSceneId); Assert.Equal(source.SceneNumber, beat.SourceSceneNumber); Assert.Equal(source.Title, beat.Title); Assert.Same(source.ViewerQuestion, beat.ViewerQuestion); Assert.Equal(source.SceneObjective.Summary, beat.Purpose); Assert.Equal(source.KnowledgeReferences, beat.KnowledgeReferences); Assert.Equal(source.VisualOpportunities, beat.VisualOpportunities); Assert.Same(source.Transition, beat.Transition); Assert.Same(source.EditorialOutcome, beat.EditorialOutcome); Assert.Equal(source.EstimatedDurationSeconds, beat.EstimatedDurationSeconds); }
}

public sealed class DocumentaryNarrativeComposerValidationTests
{
    [Fact] public void Rejects_null_request() => Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeComposer().Compose(null!));
    [Fact] public void Request_rejects_null_inputs() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, r.Metadata, null!, r.ValidationResult)); Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, r.Metadata, r.Blueprint, null!)); }
    [Fact] public void Rejects_mismatch_and_invalid_result() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeComposer().Compose(new(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new("other", [])))); var finding = new DocumentaryBlueprintValidationFinding("x", DocumentaryBlueprintValidationSeverity.Error, "bad", r.Blueprint.BlueprintId); Assert.Throws<InvalidOperationException>(() => new DocumentaryNarrativeComposer().Compose(new(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new(r.Blueprint.BlueprintId, [finding])))); }
    [Fact] public void Accepts_warning_only_result() { var r = OrionDocumentaryNarrativeCompositionFixture.Request(); var warning = new DocumentaryBlueprintValidationFinding("x", DocumentaryBlueprintValidationSeverity.Warning, "review", r.Blueprint.BlueprintId); var changed = new DocumentaryNarrativeCompositionRequest(r.CompositionId, r.Version, r.Metadata, r.Blueprint, new(r.Blueprint.BlueprintId, [warning])); Assert.NotNull(new DocumentaryNarrativeComposer().Compose(changed)); }
}

public sealed class DocumentaryNarrativeComposerGroupingTests
{
    [Fact] public void Groups_only_consecutive_equal_roles_and_sums_duration() { var roles = new[] { DocumentarySceneRole.OpeningHook, DocumentarySceneRole.RecognitionGuide, DocumentarySceneRole.CoreDiscovery, DocumentarySceneRole.ScientificExplanation, DocumentarySceneRole.HistoricalContext, DocumentarySceneRole.CulturalContext, DocumentarySceneRole.ReflectiveClosing }; var scenes = roles.Select((r, i) => OrionDocumentaryBlueprintValidationFixture.Scene(i + 1, r, duration: i + 10)).ToArray(); var c = OrionDocumentaryNarrativeCompositionFixture.Composition(scenes); Assert.Equal(new[] { DocumentaryNarrativeSectionRole.Opening, DocumentaryNarrativeSectionRole.Exploration, DocumentaryNarrativeSectionRole.Explanation, DocumentaryNarrativeSectionRole.Context, DocumentaryNarrativeSectionRole.Closing }, c.Sections.Select(x => x.SectionRole)); Assert.Equal(new[] { 1, 2, 3, 4, 5 }, c.Sections.Select(x => x.SectionNumber)); Assert.Equal("narrative.orion.long.v1.section.2", c.Sections[1].SectionId); Assert.Equal(23, c.Sections[1].EstimatedDurationSeconds); Assert.Equal(7, c.Sections.Sum(x => x.Beats.Count)); }
}

public sealed class DocumentaryNarrativeComposerDeterminismTests
{
    [Fact] public void Equivalent_requests_have_identical_json() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var a = OrionDocumentaryNarrativeCompositionFixture.Composition(); var b = OrionDocumentaryNarrativeCompositionFixture.Composition(); Assert.Equal(JsonSerializer.Serialize(a, options), JsonSerializer.Serialize(b, options)); }
}

public sealed class DocumentaryNarrativeComposerImmutabilityTests
{
    [Fact] public void Output_collections_are_read_only_and_input_is_unchanged() { var request = OrionDocumentaryNarrativeCompositionFixture.Request(); var ids = request.Blueprint.Scenes.Select(x => x.SceneId).ToArray(); var result = new DocumentaryNarrativeComposer().Compose(request); Assert.Throws<NotSupportedException>(() => ((IList<DocumentaryNarrativeSection>)result.Sections).Add(result.Sections[0])); Assert.Throws<NotSupportedException>(() => ((IList<DocumentaryNarrativeBeat>)result.Sections[0].Beats).Add(result.Sections[0].Beats[0])); Assert.Equal(ids, request.Blueprint.Scenes.Select(x => x.SceneId)); Assert.Empty(request.ValidationResult.Findings); }
}

public sealed class DocumentaryNarrativeCompositionSerializationTests
{
    [Fact] public void Aggregate_round_trips_deterministically() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); var source = OrionDocumentaryNarrativeCompositionFixture.Composition(); var json = JsonSerializer.Serialize(source, options); var copy = JsonSerializer.Deserialize<DocumentaryNarrativeComposition>(json, options)!; Assert.Equal(source.Metadata, copy.Metadata); Assert.Equal(source.Sections.SelectMany(x => x.Beats).Select(x => x.SourceSceneId), copy.Sections.SelectMany(x => x.Beats).Select(x => x.SourceSceneId)); Assert.Equal(json, JsonSerializer.Serialize(copy, options)); }
}

public sealed class DocumentaryNarrativeCompositionEnumTests
{
    [Fact] public void Inventories_are_exact() { Assert.Equal(new[] { "Opening", "Orientation", "Exploration", "Explanation", "Context", "Correction", "PracticalGuidance", "Reflection", "Closing" }, Enum.GetNames<DocumentaryNarrativeSectionRole>()); Assert.Equal(new[] { "Hook", "Question", "Orientation", "Discovery", "Explanation", "Evidence", "Context", "Clarification", "Observation", "Guidance", "Reflection", "Transition", "Closure" }, Enum.GetNames<DocumentaryNarrativeBeatType>()); }
}

public sealed class DocumentaryNarrativeComposerArchitectureTests
{
    [Fact] public void Composer_and_contracts_have_approved_shape() { var composer = typeof(DocumentaryNarrativeComposer); Assert.True(composer.IsSealed); Assert.Empty(composer.GetConstructors().Single().GetParameters()); Assert.False(composer.GetMethod("Compose")!.ReturnType.IsGenericType); var types = new[] { typeof(DocumentaryNarrativeComposition), typeof(DocumentaryNarrativeSection), typeof(DocumentaryNarrativeBeat), typeof(NarrativeCompositionMetadata), typeof(DocumentaryNarrativeCompositionRequest) }; Assert.All(types.SelectMany(x => x.GetProperties()), p => Assert.False(p.CanWrite)); var forbidden = new[] { "Narration", "NarrationText", "VoiceOver", "VoiceOverText", "Script", "ScriptText", "SpokenText", "Dialogue", "Paragraph", "Sentence", "Prompt", "PromptText", "GeneratedText", "LlmResponse", "TtsText", "Ssml", "ReplacementText", "SuggestedText", "AutoFix" }; Assert.Empty(types.SelectMany(x => x.GetProperties()).Where(p => forbidden.Contains(p.Name, StringComparer.Ordinal))); Assert.Equal(16, DocumentaryBlueprintEditorialRuleCodes.Inventory.Count); }
}

public sealed class DocumentaryNarrativeCompositionContractTests
{
    [Fact] public void Rejects_invalid_metadata_and_duplicate_sections() { Assert.Throws<ArgumentException>(() => new NarrativeCompositionMetadata(default, "a", "b", "1", "1.0", "1.0", "c")); Assert.Throws<ArgumentException>(() => new NarrativeCompositionMetadata(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "a", "b", "1", "1.0", "2.0", "c")); var c = OrionDocumentaryNarrativeCompositionFixture.Composition(); var s = c.Sections[0]; Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeComposition(c.CompositionId, c.BlueprintId, c.KnowledgeId, c.SubjectId, c.SubjectName, c.PublicationFormat, c.PrimaryLanguage, c.Version, c.Metadata, [s, s])); }
}
public sealed class DocumentaryNarrativeSectionContractTests { [Fact] public void Rejects_duplicates_and_negative_duration() { var b = OrionDocumentaryNarrativeCompositionFixture.Beat(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeSection("s", 1, "t", "p", b.NarrativeStage, DocumentaryNarrativeSectionRole.Opening, [b, b], 1)); Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentaryNarrativeSection("s", 1, "t", "p", b.NarrativeStage, DocumentaryNarrativeSectionRole.Opening, [b], -1)); } }
public sealed class DocumentaryNarrativeBeatContractTests { [Fact] public void Rejects_blank_and_undefined_values() { var b = OrionDocumentaryNarrativeCompositionFixture.Beat(); Assert.Throws<ArgumentException>(() => new DocumentaryNarrativeBeat(" ", b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, b.BeatType, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, b.KnowledgeReferences, b.VisualOpportunities, b.Transition, b.EditorialOutcome, b.EstimatedDurationSeconds)); Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentaryNarrativeBeat(b.BeatId, b.BeatNumber, b.SourceSceneId, b.SourceSceneNumber, b.Title, (DocumentaryNarrativeBeatType)999, b.NarrativeStage, b.SceneRole, b.ViewerQuestion, b.Purpose, b.KnowledgeReferences, b.VisualOpportunities, b.Transition, b.EditorialOutcome, b.EstimatedDurationSeconds)); } }
public sealed class DocumentaryNarrativeCompositionInventoryTests { [Fact] public void Fixed_titles_and_purposes_cover_every_section_role() { foreach (var role in Enum.GetValues<DocumentaryNarrativeSectionRole>()) { Assert.False(string.IsNullOrWhiteSpace(DocumentaryNarrativeCompositionMappings.Title(role))); Assert.EndsWith(".", DocumentaryNarrativeCompositionMappings.Purpose(role)); } } }
