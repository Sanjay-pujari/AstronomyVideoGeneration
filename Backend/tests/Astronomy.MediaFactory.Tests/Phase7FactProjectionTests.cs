using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7FactProjectionTests
{
    [Fact]
    public void CertifiedCompositionFactsReachSceneFactCardsWithLineage()
    {
        var fact = Certified("claim-belt", "Three named stars form the familiar belt pattern.", "kr-belt", "source-catalog");
        var frame = new StoryFrameNarrationSource("long-001", 1, "frame-001", "Viewer goal", [fact],
            "blueprint-001", "What will the viewer see?", "Recognize the pattern", "Remember the stars", "Advance01", "Recognition", ["kr-belt"]);

        var cards = SceneFactCardGenerator.Build("long", EmptyNotes(), "test", [frame]);

        var card = Assert.Single(cards.Cards);
        Assert.Equal("blueprint-001", card.BlueprintSceneId);
        Assert.Equal("frame-001", card.SourceStoryFrameId);
        Assert.Equal(["claim-belt"], card.SelectedClaimIds);
        Assert.Equal(["kr-belt"], card.SelectedKnowledgeReferenceIds);
        Assert.Equal(["Three named stars form the familiar belt pattern."], card.Facts);
        Assert.DoesNotContain(card.Facts, value => value.Contains("Viewer goal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(card.Facts, value => value.Contains("Advance01", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlueprintKnowledgeReferencesDriveSelectionWithoutEditorialProjection()
    {
        var governed = Certified("claim-1", "The governed object is a constellation pattern.", "kr-governed", "source-1");
        var frame = new StoryFrameNarrationSource("short-001", 1, "short-frame-001", "Takeaway", [governed],
            "blueprint-short-001", "Viewer goal", "Learning objective", "Takeaway", "Close", "Reflection", ["kr-governed"]);

        var card = Assert.Single(SceneFactCardGenerator.Build("short", EmptyNotes(), "test", [frame]).Cards);

        Assert.Single(card.Facts);
        Assert.Equal("Viewer goal", card.ViewerQuestion);
        Assert.Equal("Takeaway", card.EditorialOutcome);
        Assert.DoesNotContain(card.Facts, SceneFactCardGenerator.IsPlaceholderFact);
    }

    [Theory]
    [InlineData("Advance01")]
    [InlineData("Observation remains grounded in the confirmed story details")]
    [InlineData("Viewer goal")]
    [InlineData("Takeaway")]
    public void PlaceholderCandidateIsRejectedBeforeNarrationContext(string statement)
    {
        var frame = new StoryFrameNarrationSource("long-001", 1, "frame-001", "purpose",
            [Certified("claim-1", statement, "kr-1", "source-1")]);

        var error = Assert.Throws<InvalidOperationException>(() =>
            SceneFactCardGenerator.Build("long", EmptyNotes(), "test", [frame]));

        Assert.Contains("P7_SCENE_FACT_PLACEHOLDER_DETECTED", error.Message);
    }

    [Fact]
    public void LongAndShortFactCardsRemainIndependent()
    {
        var longCard = SceneFactCardGenerator.Build("long", EmptyNotes(), "test",
            [new("long-001", 1, "long-frame", "", [Certified("long-claim", "A long governed astronomy fact.", "long-ref", "long-source")])]);
        var shortCard = SceneFactCardGenerator.Build("short", EmptyNotes(), "test",
            [new("short-001", 1, "short-frame", "", [Certified("short-claim", "A short governed astronomy fact.", "short-ref", "short-source")])]);

        Assert.Equal("long", Assert.Single(longCard.Cards).Format);
        Assert.Equal("short", Assert.Single(shortCard.Cards).Format);
        Assert.False(Assert.Single(longCard.Cards).Facts.Single().Contains("short", StringComparison.OrdinalIgnoreCase));
        Assert.False(Assert.Single(shortCard.Cards).Facts.Single().Contains("long", StringComparison.OrdinalIgnoreCase));
    }

    private static SceneKnowledgeFact Certified(string claim, string statement, string reference, string source) =>
        new(claim, statement, [reference], [source], 0.99m, [], true);

    private static ProducerNotesContract EmptyNotes() => new("v", "en", []);
}
