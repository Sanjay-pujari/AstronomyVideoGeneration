using System.Text.Json;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7DocumentaryPerformerCertificationTests
{
    [Fact]
    public void VisualStoryFrameInstructionSuppliedAsVerifiedFact_FailsPurityValidation()
    {
        var context = ContextWithFact("visualGoal", "Create a visual-only hook frame with camera motion.");
        var result = NarrationContextPurityValidator.Validate(context);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void BeatIdSuppliedAsSpeakableFact_FailsPurityValidation()
    {
        var context = ContextWithFact("Source", "Use only source facts attached to long-beat-001.");
        var result = NarrationContextPurityValidator.Validate(context);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RawIsoTimestampInFactFormatter_IsConvertedSafely()
    {
        var text = NarrationSafeFactFormatter.TryFormat("EventDate", "2026-08-12T18:30:00.000Z", null, out var warning);
        Assert.Null(warning);
        Assert.NotNull(text);
        Assert.DoesNotContain("2026-08-12T18:30:00.000Z", text);
        Assert.Contains("2026", text);
    }

    [Fact]
    public void ProperDocumentaryContractBeat_PreservesSpecificGoals()
    {
        using var longContract = JsonDocument.Parse("""
        { "beats": [ { "sceneNumber": 1, "sceneId": "scene-001", "documentaryBeatId": "long-beat-001", "narrativeRole": "Science", "knowledgeGoal": "Explain why Jupiter and Venus appear close together from Earth.", "audienceOutcome": "The viewer understands apparent alignment and does not assume physical proximity.", "allocatedFacts": [ { "status": "allocated", "factKey": "PrimaryObjects", "value": "Jupiter", "semanticPurpose": "Identify the primary planet", "sourceBeatId": "long-beat-001" } ] } ] }
        """);
        var cards = EmptyCards();
        var context = NarrationContextBuilder.Build(longContract.RootElement, longContract.RootElement, null, null, null, null, cards, "Calm", "test");
        var beat = context.Formats[0].Beats[0];
        Assert.Equal("Explain why Jupiter and Venus appear close together from Earth.", beat.KnowledgeGoal);
        Assert.Equal("The viewer understands apparent alignment and does not assume physical proximity.", beat.AudienceOutcome);
    }

    [Fact]
    public void OrdinaryUseOfUnderstand_DoesNotFailPurityValidation()
    {
        var context = ContextWithGoal("The viewer understands the timing without confusion.");
        Assert.True(NarrationContextPurityValidator.Validate(context).IsValid);
    }

    private static DocumentaryPerformerSceneFactCards EmptyCards()
    {
        var set = new SceneFactCardSet("test", "test", "long", "en", []);
        return new DocumentaryPerformerSceneFactCards(set, set with { Format = "short" });
    }

    private static NarrationContextDocument ContextWithFact(string key, string value) => new("test", "test", [new NarrationFormatContext("long", [new NarrationContextBeat(1, "scene-001", "long-beat-001", [], "Hook", "Goal.", "Outcome.", "Intent.", [new NarrationVerifiedFact(key, value, "purpose", null, null, value)], [], "Transition.", "Tone.", "Rhythm.", [], null)])]);
    private static NarrationContextDocument ContextWithGoal(string goal) => new("test", "test", [new NarrationFormatContext("long", [new NarrationContextBeat(1, "scene-001", "long-beat-001", [], "Hook", goal, "Outcome.", "Intent.", [], [], "Transition.", "Tone.", "Rhythm.", [], null)])]);
}
