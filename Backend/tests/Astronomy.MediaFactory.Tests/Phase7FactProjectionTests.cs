using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;
using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7FactProjectionTests
{
    [Fact]
    public void DuplicateRequiredClaimsSameScenePreserveAllIds()
    {
        var card = BuildCard("long", "scene-1",
            Certified("claim-b", "Orion has official constellation boundaries.", "ref-b", "source-b"),
            Certified("claim-a", "Orion has official constellation boundaries.", "ref-a", "source-a"));

        Assert.Single(card.Facts);
        Assert.Equal("claim-a", Assert.Single(card.KnowledgeFacts!).ClaimId);
        Assert.Equal(["claim-a", "claim-b"], card.SelectedClaimIds);
        Assert.Equal(["claim-a", "claim-b"], card.KnowledgeFacts![0].SourceClaimIds);
    }

    [Fact]
    public void NormalizedEquivalentStatementsPreserveAllIds()
    {
        var card = BuildCard("long", "scene-1",
            Certified("claim-1", "Fact: Alnitak, Alnilam, and Mintaka form Orion's Belt", "ref-1", "source-1"),
            Certified("claim-2", "Alnitak, Alnilam, and Mintaka form Orion's Belt.", "ref-2", "source-2"));

        Assert.Single(card.Facts);
        Assert.Equal(["claim-1", "claim-2"], card.SelectedClaimIds);
    }

    [Fact]
    public void DuplicateClaimsMergeProvenanceAndRequiredDisposition()
    {
        var optional = new SceneKnowledgeFact("claim-b", "A shared certified fact.", ["ref-b"], ["source-b"], .8m, ["qual-b"], false);
        var required = new SceneKnowledgeFact("claim-a", "A shared certified fact.", ["ref-a"], ["source-a"], .99m, ["qual-a"], true);

        var merged = Assert.Single(BuildCard("long", "scene-1", optional, required).KnowledgeFacts!);

        Assert.True(merged.Required);
        Assert.Equal(.99m, merged.Confidence);
        Assert.Equal(["source-a", "source-b"], merged.SourceIds);
        Assert.Equal(["ref-a", "ref-b"], merged.KnowledgeReferenceIds);
        Assert.Equal(["qual-a", "qual-b"], merged.QualificationRequirements);
    }

    [Fact]
    public void CrossSceneDuplicatesRemainSceneScoped()
    {
        var frames = new[]
        {
            FrameWithFact("scene-4", 4, Certified("claim-4", "The Belt is nearly straight.", "ref-4", "source-4")),
            FrameWithFact("scene-9", 9, Certified("claim-9", "The Belt is nearly straight.", "ref-9", "source-9"))
        };

        var cards = SceneFactCardGenerator.Build("long", EmptyNotes(), "test", frames).Cards;

        Assert.Equal(["claim-4"], cards[0].SelectedClaimIds);
        Assert.Equal(["claim-9"], cards[1].SelectedClaimIds);
    }

    [Fact]
    public void CrossVariantDuplicatesRemainIndependent()
    {
        var statement = "Orion is visible from much of Earth.";
        var longCard = BuildCard("long", "long-scene", Certified("long-claim", statement, "long-ref", "long-source"));
        var shortCard = BuildCard("short", "short-scene", Certified("short-claim", statement, "short-ref", "short-source"));

        Assert.Equal(["long-claim"], longCard.SelectedClaimIds);
        Assert.Equal(["short-claim"], shortCard.SelectedClaimIds);
    }

    [Fact]
    public void GenuineClaimLossStillFails()
    {
        var longClaim = RequiredClaim("long-claim", "A governed long fact.", "long-ref", "long-source");
        var shortClaim = RequiredClaim("short-claim", "A governed short fact.", "short-ref", "short-source");
        var authority = new NarrationGeneratorV5AuthorityInput(
            Request("Long", SceneInput("long-scene", longClaim)),
            Request("Short", SceneInput("short-scene", shortClaim)));
        var longCards = SceneFactCardGenerator.Build("long", EmptyNotes(), "test",
            [FrameWithFact("long-scene", 1, Certified("different-claim", "A different fact.", "different-ref", "different-source"))]);
        var shortCards = SceneFactCardGenerator.Build("short", EmptyNotes(), "test",
            [FrameWithFact("short-scene", 1, Certified("short-claim", shortClaim.Fact, "short-ref", "short-source"))]);

        var lost = CommittedCompositionFactCardProjector.FindLostCommittedClaimIds(authority, longCards, shortCards);

        Assert.Equal(["long-claim"], lost);
    }
    [Fact]
    public void ResolverZeroFactsPreservesCommittedPacketFactsAndLineage()
    {
        var committed = Certified("packet-claim-orion", "Orion contains a recognizable belt of three stars.",
            "packet-reference-orion", "committed-packet");
        var frame = new StoryFrameNarrationSource("long-001", 1, "frame-001", "Recognize Orion", [committed],
            "blueprint-001", "Where is Orion?", "Recognize Orion", "Retain the pattern", "Continue", "Recognition",
            ["packet-reference-orion"]);
        var before = Assert.Single(SceneFactCardGenerator.Build("long", EmptyNotes(), "test", [frame]).Cards);

        var resolution = new RequiredSemanticFactResolutionResult([], new { resolvedBeatCount = 0 });
        var after = Assert.Single(SceneFactCardGenerator.Build("long", EmptyNotes(), "test", [frame], resolution).Cards);

        Assert.NotEmpty(before.Facts);
        Assert.True(after.Facts.Count >= before.Facts.Count);
        Assert.Equal(before.Facts, after.Facts);
        Assert.Equal(before.SelectedClaimIds, after.SelectedClaimIds);
        Assert.Equal(before.SelectedKnowledgeReferenceIds, after.SelectedKnowledgeReferenceIds);
        Assert.Contains("packet-claim-orion", after.SelectedClaimIds);
    }

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

    [Fact]
    public void BlueprintSceneRolesRemainDistinct()
    {
        var frames = new[]
        {
            Frame("opening", 1, "Hook", "Invite curiosity", "Why look up?", "Become curious"),
            Frame("science", 2, "Science", "Explain the geometry", "Why is it shaped this way?", "Understand the geometry"),
            Frame("close", 3, "Closing", "Reflect on scale", "What should remain?", "Remember the scale")
        };

        var cards = SceneFactCardGenerator.Build("long", EmptyNotes(), "test", frames).Cards;
        Assert.Equal(3, cards.Select(card => card.ScenePurpose).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(cards.Where(card => card.SceneRole == "Hook"));
    }

    [Fact]
    public void NarrationContextPreservesSemanticDiversityAfterRealization()
    {
        var source = new NarrationContextDocument("v", "v", [new NarrationFormatContext("long", [
            Beat("opening", "Hook: invite curiosity", "Become curious", "Open with wonder"),
            Beat("science", "Science: explain geometry", "Understand geometry", "Explain with restraint")
        ])]);
        var realized = new[]
        {
            Realized("opening"), Realized("science")
        };

        var projected = NarrationRealizedContextMapper.ToContext(source, realized);
        var beats = Assert.Single(projected.Formats).Beats;
        Assert.Equal(2, beats.Select(beat => beat.KnowledgeGoal).Distinct().Count());
        Assert.Equal("Science: explain geometry", beats[1].KnowledgeGoal);
        Assert.Equal("Understand geometry", beats[1].AudienceOutcome);
    }

    private static StoryFrameNarrationSource Frame(string id, int order, string role, string purpose, string question, string outcome)
        => new(id, order, $"frame-{id}", "", [Certified($"claim-{id}", $"Certified astronomy fact for {id}.", $"kr-{id}", $"source-{id}")],
            $"blueprint-{id}", question, outcome, outcome, "Continue", purpose, [$"kr-{id}"], role, role, "Required", 20);

    private static SceneFactCard BuildCard(string format, string sceneId, params SceneKnowledgeFact[] facts)
        => Assert.Single(SceneFactCardGenerator.Build(format, EmptyNotes(), "test", [FrameWithFact(sceneId, 1, facts)]).Cards);

    private static StoryFrameNarrationSource FrameWithFact(string id, int order, params SceneKnowledgeFact[] facts)
        => new(id, order, $"frame-{id}", "", facts, $"blueprint-{id}", BlueprintKnowledgeReferenceIds: facts.SelectMany(f => f.KnowledgeReferenceIds).ToArray());

    private static DocumentaryNarrativeRequiredFact RequiredClaim(string id, string fact, string reference, string source)
        => new(id, fact, [reference], [source], .99m, []);

    private static DocumentaryNarrativeSceneInput SceneInput(string id, params DocumentaryNarrativeRequiredFact[] facts)
        => new(1, id, "section", "Science", "question", "objective", "brief", facts, [], [], [], [], "visual", 20, "", "");

    private static DocumentaryNarrativeCompositionRequest Request(string variant, params DocumentaryNarrativeSceneInput[] scenes)
        => new("execution", Guid.Empty, "event", "family", "en", variant, "profile", scenes,
            new DocumentaryNarrativeDurationGuidance(1, 2, 3), [], [], []);

    private static NarrationContextBeat Beat(string id, string goal, string outcome, string intent)
        => new(goal, outcome, intent, [new("claim", "A certified astronomy fact.", "CertifiedKnowledgeClaim")], [], null,
            "Continue", "calm", "measured", [], null, id, id == "opening" ? 1 : 2, "long", 20);

    private static NarrationRealizationResult Realized(string id)
        => new("long", id, "Hook", "Constellation", "EducationalObjectProfile", "Hook", "Create curiosity about the subject.",
            [], [], [], null, "calm", "measured", 40, null, null, [], "curiosity, surprise, or wonder");

    private static SceneKnowledgeFact Certified(string claim, string statement, string reference, string source) =>
        new(claim, statement, [reference], [source], 0.99m, [], true);

    private static ProducerNotesContract EmptyNotes() => new("v", "en", []);
}
