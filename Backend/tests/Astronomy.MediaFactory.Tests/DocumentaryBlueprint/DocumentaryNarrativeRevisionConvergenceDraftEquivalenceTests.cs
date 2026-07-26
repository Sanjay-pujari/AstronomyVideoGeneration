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
        var state = Start(cycle.Plan.SourceDraft);

        Assert.Same(state.CurrentDraft, cycle.Plan.SourceDraft);

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
        var state = OrionDocumentaryNarrativeRevisionConvergenceFixture.InitiallyInvalidState();
        var cycle = MutateCycle(root =>
        {
            root["sourceDraftId"] = state.CurrentDraftId.ToUpperInvariant();
            root["plan"]!["sourceDraft"]!["draftId"] = state.CurrentDraftId.ToUpperInvariant();
        });

        Assert.Throws<ArgumentException>(() => Advance(state, cycle));
    }

    [Fact]
    public void Advance_rejects_case_only_source_version_difference()
    {
        var cycleRoot = JsonNode.Parse(JsonSerializer.Serialize(
            OrionDocumentaryNarrativeRevisionConvergenceFixture.SuccessfulCycle(), WebJson))!;
        ReplaceStringValues(cycleRoot, "1", "vA");
        var baselineCycle = cycleRoot.Deserialize<DocumentaryNarrativeRevisionCycleResult>(WebJson)!;
        var state = Start(baselineCycle.Plan.SourceDraft);
        var cycle = MutateCycle(baselineCycle, root =>
        {
            root["sourceDraftVersion"] = state.CurrentDraftVersion.ToUpperInvariant();
            root["plan"]!["sourceDraft"]!["version"] = state.CurrentDraftVersion.ToUpperInvariant();
        });

        Assert.Throws<ArgumentException>(() => Advance(state, cycle));
    }

    private static DocumentaryNarrativeRevisionConvergenceState Start(DocumentaryNarrativeDraft draft) =>
        new DocumentaryNarrativeRevisionConvergenceStarter().Start(
            draft,
            new DocumentaryNarrativeDraftValidator().Validate(draft),
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

    private static void ReplaceStringValues(JsonNode node, string oldValue, string newValue)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && text == oldValue)
                    obj[property.Key] = newValue;
                else if (property.Value is not null)
                    ReplaceStringValues(property.Value, oldValue, newValue);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(x => x is not null))
                ReplaceStringValues(child!, oldValue, newValue);
        }
    }

    private static DocumentaryNarrativeRevisionCycleResult CloneCycle(
        DocumentaryNarrativeRevisionCycleResult cycle) =>
        JsonSerializer.Deserialize<DocumentaryNarrativeRevisionCycleResult>(
            JsonSerializer.Serialize(cycle, WebJson), WebJson)!;
}
