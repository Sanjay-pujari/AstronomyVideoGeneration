using System.Reflection;
using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

internal static class OrionDocumentaryNarrativeDraftFixture
{
    internal static DocumentaryNarrativeDraftMetadata Metadata() => new(new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero), "narrative-editor", "editorial-draft-v1", "1", "1.0", "1.0", "correlation-orion-draft-001");
    internal static DocumentaryNarrativeDraftRequest Request(bool reverse = false)
    {
        var composition = OrionDocumentaryNarrativeCompositionFixture.Composition();
        var texts = new[] { "Look toward Orion and three nearly straight stars immediately command attention.", "The familiar line is only an apparent alignment because the stars lie at very different distances from Earth.", "Orion transforms a familiar winter pattern into a reminder of the immense depth hidden behind the night sky." };
        var inputs = composition.Sections.SelectMany(x => x.Beats).Select((x, i) => new DocumentaryNarrativePassageInput(x.BeatId, texts[i])).ToArray();
        return new("narrative-draft.orion.long.v1", "1", Metadata(), composition, reverse ? inputs.Reverse().ToArray() : inputs);
    }
}

public sealed class DocumentaryNarrativeDraftAssemblerTests
{
    [Fact] public void Assembles_exactly_one_passage_per_beat() { var r = OrionDocumentaryNarrativeDraftFixture.Request(); var d = new DocumentaryNarrativeDraftAssembler().Assemble(r); Assert.Equal(r.Composition.Sections.SelectMany(x => x.Beats).Count(), d.Sections.SelectMany(x => x.Passages).Count()); Assert.Equal(r.DraftId, d.DraftId); Assert.Equal(r.Composition.CompositionId, d.CompositionId); }
}

public sealed class DocumentaryNarrativeDraftAssemblerMappingTests
{
    [Fact] public void Preserves_every_mapped_field() { var r = OrionDocumentaryNarrativeDraftFixture.Request(); var d = new DocumentaryNarrativeDraftAssembler().Assemble(r); var beats = r.Composition.Sections.SelectMany(x => x.Beats).ToArray(); var passages = d.Sections.SelectMany(x => x.Passages).ToArray(); for (var i=0;i<beats.Length;i++) { var b=beats[i]; var p=passages[i]; Assert.Equal(b.BeatId+".passage",p.PassageId); Assert.Equal(b.BeatId,p.SourceBeatId); Assert.Equal(b.SourceSceneId,p.SourceSceneId); Assert.Equal(b.Title,p.Title); Assert.Equal(b.ViewerQuestion,p.ViewerQuestion); Assert.Equal(b.Purpose,p.Purpose); Assert.Equal(b.KnowledgeReferences,p.KnowledgeReferences); Assert.Equal(b.VisualOpportunities,p.VisualOpportunities); Assert.Equal(b.Transition,p.Transition); Assert.Equal(b.EditorialOutcome,p.EditorialOutcome); Assert.Equal(b.EstimatedDurationSeconds,p.EstimatedDurationSeconds); } }
}

public sealed class DocumentaryNarrativeDraftAssemblerValidationTests
{
    [Fact] public void Rejects_null_request() => Assert.Throws<ArgumentNullException>(() => new DocumentaryNarrativeDraftAssembler().Assemble(null!));
    [Fact] public void Rejects_missing_input() { var r=OrionDocumentaryNarrativeDraftFixture.Request(); var bad=new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,r.PassageInputs.Skip(1).ToArray()); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftAssembler().Assemble(bad)); }
    [Fact] public void Rejects_unknown_input() { var r=OrionDocumentaryNarrativeDraftFixture.Request(); var inputs=r.PassageInputs.Skip(1).Append(new DocumentaryNarrativePassageInput("unknown","text")).ToArray(); var bad=new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,inputs); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftAssembler().Assemble(bad)); }
    [Fact] public void Request_rejects_duplicates() { var r=OrionDocumentaryNarrativeDraftFixture.Request(); Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftRequest(r.DraftId,r.Version,r.Metadata,r.Composition,[r.PassageInputs[0],r.PassageInputs[0]])); }
}

public sealed class DocumentaryNarrativeDraftAssemblerOrderingTests
{
    [Fact] public void Shuffled_inputs_emit_composition_order() { var d=new DocumentaryNarrativeDraftAssembler().Assemble(OrionDocumentaryNarrativeDraftFixture.Request(true)); Assert.Equal(new[]{1,2,3},d.Sections.SelectMany(x=>x.Passages).Select(x=>x.SourceBeatNumber)); }
}

public sealed class DocumentaryNarrativeDraftAssemblerDeterminismTests
{
    [Fact] public void Shuffling_does_not_change_json() { var a=new DocumentaryNarrativeDraftAssembler(); var options=new JsonSerializerOptions(JsonSerializerDefaults.Web); Assert.Equal(JsonSerializer.Serialize(a.Assemble(OrionDocumentaryNarrativeDraftFixture.Request()),options),JsonSerializer.Serialize(a.Assemble(OrionDocumentaryNarrativeDraftFixture.Request(true)),options)); }
}

public sealed class DocumentaryNarrativeDraftMetadataTests
{
    [Fact] public void Rejects_default_timestamp() => Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftMetadata(default,"a","b","1","1.0","1.0","c"));
    [Fact] public void Rejects_wrong_schema() => Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativeDraftMetadata(new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero),"a","b","1","1.0","2.0","c"));
}

public sealed class DocumentaryNarrativePassageInputTests
{
    [Theory] [InlineData("")] [InlineData(" ")] public void Rejects_blank_text(string text)=>Assert.Throws<ArgumentException>(()=>new DocumentaryNarrativePassageInput("beat",text));
}

public sealed class DocumentaryNarrativeDraftEnumTests
{
    [Fact] public void Inventory_is_exact() => Assert.Equal(new[]{"Opening","Question","Orientation","Discovery","Explanation","Evidence","Context","Clarification","Observation","Guidance","Reflection","Transition","Closing"},Enum.GetNames<DocumentaryNarrativePassageType>());
    [Fact] public void Mapping_is_exhaustive() => Assert.Equal(Enum.GetValues<DocumentaryNarrativeBeatType>().Length,Enum.GetValues<DocumentaryNarrativeBeatType>().Select(DocumentaryNarrativeDraftMappings.PassageType).Distinct().Count());
}

public sealed class DocumentaryNarrativeDraftArchitectureTests
{
    [Fact] public void Assembler_boundary_is_exact() { var t=typeof(DocumentaryNarrativeDraftAssembler); Assert.True(t.IsSealed); Assert.Empty(t.GetConstructors().Single().GetParameters()); var method=Assert.Single(t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)); Assert.Equal("Assemble",method.Name); Assert.Equal(typeof(DocumentaryNarrativeDraft),method.ReturnType); Assert.Equal(typeof(DocumentaryNarrativeDraftRequest),Assert.Single(method.GetParameters()).ParameterType); }
    [Fact] public void Contracts_are_read_only() { var types=new[]{typeof(DocumentaryNarrativeDraft),typeof(DocumentaryNarrativeDraftSection),typeof(DocumentaryNarrativePassage),typeof(DocumentaryNarrativeDraftMetadata),typeof(DocumentaryNarrativePassageInput),typeof(DocumentaryNarrativeDraftRequest)}; Assert.All(types,t=>Assert.All(t.GetProperties(),p=>Assert.False(p.SetMethod?.IsPublic??false))); }
}
