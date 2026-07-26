using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;

public sealed class DocumentaryNarrativeRevisionConvergenceDraftEquivalenceTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Advance_accepts_same_source_draft_instance()
    {
        var cycle = OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle();
        var state = StartFromCycleSource(cycle);

        Assert.Same(state.CurrentDraft, cycle.Plan.SourceDraft);
        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.NotStarted, state.Status);
        Assert.True(state.RequiresAnotherCycle);

        var advanced = Advance(state, cycle);

        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, advanced.Status);
    }

    [Fact]
    public void Advance_accepts_independently_reconstructed_equivalent_source_draft()
    {
        var state = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var cycle = OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle();

        Assert.NotSame(state.CurrentDraft, cycle.Plan.SourceDraft);
        Assert.Equal(JsonSerializer.Serialize(state.CurrentDraft, WebJson),
            JsonSerializer.Serialize(cycle.Plan.SourceDraft, WebJson));

        var advanced = Advance(state, cycle);

        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, advanced.Status);
    }

    [Fact]
    public void Advance_accepts_deserialized_equivalent_source_draft()
    {
        var original = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var state = JsonSerializer.Deserialize<DocumentaryNarrativeRevisionConvergenceState>(
            JsonSerializer.Serialize(original, WebJson), WebJson)!;
        var cycle = CloneCycle(OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle());

        Assert.NotSame(state.CurrentDraft, cycle.Plan.SourceDraft);
        Assert.Equal(state.CurrentDraftId, cycle.SourceDraftId, StringComparer.Ordinal);
        Assert.Equal(state.CurrentDraftVersion, cycle.SourceDraftVersion, StringComparer.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(state.CurrentDraft, WebJson),
            JsonSerializer.Serialize(cycle.Plan.SourceDraft, WebJson));

        var advanced = Advance(state, cycle);

        Assert.Equal(DocumentaryNarrativeRevisionConvergenceStatus.ConvergedSuccessfully, advanced.Status);
    }

    [Fact]
    public void Advance_rejects_different_passage_text_with_same_lineage()
    {
        var state = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var cycle = MutateCycle(root =>
            root["plan"]!["sourceDraft"]!["sections"]![0]!["passages"]![0]!["text"] = "Different passage text.");

        var exception = Assert.Throws<ArgumentException>(() => Advance(state, cycle));

        Assert.Contains("value-equivalent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Advance_rejects_different_nested_structure_with_same_lineage()
    {
        var state = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var cycle = MutateCycle(root =>
            root["plan"]!["sourceDraft"]!["sections"]![0]!["passages"]!.AsArray().RemoveAt(0));

        var exception = Assert.Throws<ArgumentException>(() => Advance(state, cycle));

        Assert.Contains("value-equivalent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Advance_rejects_case_only_source_id_difference()
    {
        var cycle = OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle();
        var state = CloneState(StartFromCycleSource(cycle));

        SetBackingField(state.CurrentDraft, "DraftId", state.CurrentDraftId.ToUpperInvariant());

        Assert.Throws<ArgumentException>(() => Advance(state, cycle));
    }

    [Fact]
    public void Advance_rejects_case_only_source_version_difference()
    {
        var cycle = OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle();
        var sourceDraft = WithVersion(cycle.Plan.SourceDraft, $"v{cycle.Plan.SourceDraft.Version}");
        var state = new DocumentaryNarrativeRevisionConvergenceStarter().Start(
            sourceDraft,
            cycle.Plan.SourceValidationResult,
            OrionDocumentaryNarrativeRevisionConvergenceFixture.DefaultPolicy(),
            OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata());

        SetBackingField(state.CurrentDraft, "Version", state.CurrentDraftVersion.ToUpperInvariant());

        Assert.Throws<ArgumentException>(() => Advance(state, cycle));
    }

    private static DocumentaryNarrativeRevisionConvergenceState StartFromCycleSource(
        DocumentaryNarrativeRevisionCycleResult cycle) =>
        new DocumentaryNarrativeRevisionConvergenceStarter().Start(
            cycle.Plan.SourceDraft,
            cycle.Plan.SourceValidationResult,
            OrionDocumentaryNarrativeRevisionConvergenceFixture.DefaultPolicy(),
            OrionDocumentaryNarrativeRevisionConvergenceFixture.Metadata());

    private static DocumentaryNarrativeRevisionConvergenceState Advance(
        DocumentaryNarrativeRevisionConvergenceState state,
        DocumentaryNarrativeRevisionCycleResult cycle) =>
        new DocumentaryNarrativeRevisionConvergenceAdvancer().Advance(
            OrionDocumentaryNarrativeRevisionConvergenceFixture.Request(state, cycle));

    private static DocumentaryNarrativeRevisionCycleResult MutateCycle(Action<JsonNode> mutation)
        => MutateCycle(OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle(), mutation);

    private static DocumentaryNarrativeRevisionCycleResult MutateCycle(
        DocumentaryNarrativeRevisionCycleResult cycle,
        Action<JsonNode> mutation)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(
            cycle, WebJson))!;
        mutation(root);
        return root.Deserialize<DocumentaryNarrativeRevisionCycleResult>(WebJson)!;
    }

    private static void SetBackingField<T>(object target, string propertyName, T value)
    {
        var field = target.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static DocumentaryNarrativeRevisionConvergenceState CloneState(
        DocumentaryNarrativeRevisionConvergenceState state) =>
        JsonSerializer.Deserialize<DocumentaryNarrativeRevisionConvergenceState>(
            JsonSerializer.Serialize(state, WebJson), WebJson)!;

    private static DocumentaryNarrativeDraft WithVersion(DocumentaryNarrativeDraft draft, string version) =>
        new(
            draft.DraftId,
            draft.CompositionId,
            draft.BlueprintId,
            draft.KnowledgeId,
            draft.SubjectId,
            draft.SubjectName,
            draft.PublicationFormat,
            draft.PrimaryLanguage,
            version,
            draft.Metadata,
            draft.Sections);

    private static DocumentaryNarrativeRevisionCycleResult CloneCycle(
        DocumentaryNarrativeRevisionCycleResult cycle) =>
        JsonSerializer.Deserialize<DocumentaryNarrativeRevisionCycleResult>(
            JsonSerializer.Serialize(cycle, WebJson), WebJson)!;
}
