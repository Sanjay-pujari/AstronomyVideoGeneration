using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationContextPurityTests
{
    [Fact]
    public void VisualStoryFrameInstructionSuppliedAsVerifiedFact_FailsBeforeLlmCall()
    {
        var context = ContextWithFact("VisualGoal", "Create a visual-only hook frame for a landscape composition.");

        var failures = NarrationContextPurityValidator.Validate(context);

        Assert.Contains(failures, f => f.Contains("visual-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BeatIdSuppliedAsSpeakableFact_FailsContextValidation()
    {
        var context = ContextWithFact("SourceBeat", "Use only source facts attached to long-beat-001.");

        var failures = NarrationContextPurityValidator.Validate(context);

        Assert.Contains(failures, f => f.Contains("Internal beat ID", StringComparison.OrdinalIgnoreCase) || f.Contains("long-beat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RawIsoTimestampFact_IsConvertedSafely()
    {
        var formatted = NarrationSafeFactFormatter.Format("EventDate", "2026-07-10T18:30:45.000Z", null, out var warning);

        Assert.Null(warning);
        Assert.Equal("July 10, 2026, 18:30 UTC.", formatted);
        Assert.DoesNotContain("T18:30:45", formatted);
    }

    [Fact]
    public void ProperDocumentaryContractBeat_PreservesSpecificGoals()
    {
        var contract = JsonDocument.Parse("""
        { "beats": [ { "beatId": "long-beat-001", "sceneId": "scene-001", "beatOrder": 1, "narrativeRole": "Science", "knowledgeGoal": "Explain why Jupiter and Venus appear close together from Earth", "audienceOutcome": "The viewer understands apparent alignment and does not assume physical proximity", "editorialIntent": "Connect geometry to observation", "transitionGoal": "Move from observing opportunity to apparent alignment", "allocatedFacts": { "PrimaryObjects": { "value": "Jupiter", "status": "allocated", "semanticPurpose": "Identify the primary planet" } } } ] }
        """).RootElement;
        var cards = new SceneFactCardSet("v", "o", "long", "en", [new SceneFactCard("scene-001", 1, "long", [], [], [], [], [], [], [], [], [], 10, "scene-001", "frame-001")]);

        var context = NarrationContextBuilder.Build(contract, contract, null, null, null, null, new DocumentaryPerformerSceneFactCards(cards, cards), "calm", "test");
        var beat = context.Formats.First().Beats.Single();

        Assert.Equal("Explain why Jupiter and Venus appear close together from Earth.", beat.KnowledgeGoal);
        Assert.Equal("The viewer understands apparent alignment and does not assume physical proximity.", beat.AudienceOutcome);
    }

    [Fact]
    public void OrdinaryUseOfUnderstand_DoesNotFailPurityValidation()
    {
        var context = new NarrationContextDocument("v", "o", [new NarrationFormatContext("long", [new NarrationContextBeat(1, "scene-001", "long-beat-001", [], "Science", "Help the audience understand apparent alignment.", "The audience can understand the geometry safely.", "Explain the idea clearly.", [new NarrationVerifiedFact("PrimaryObjects", "Jupiter.", null, null)], [], "Connect to the next idea.", "calm", "measured", [], null)])]);

        var failures = NarrationContextPurityValidator.Validate(context);

        Assert.Empty(failures);
    }

    private static NarrationContextDocument ContextWithFact(string key, string value) =>
        new("v", "o", [new NarrationFormatContext("long", [new NarrationContextBeat(1, "scene-001", "long-beat-001", [], "Hook", "Notice the event.", "The audience recognizes it.", "Open cleanly.", [new NarrationVerifiedFact(key, value, null, null)], [], "Continue.", "calm", "measured", [], null)])]);
}
