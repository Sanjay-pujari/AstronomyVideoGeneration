using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class NarrationCanonicalProjectionTests
{
    [Fact]
    public void Canonical_LongAndShortComposition_ProduceSafeContextBeats()
    {
        var longCards = Cards("Long", "long", 12);
        var shortCards = Cards("SHORT", "short", 4);

        var result = NarrationInputNormalizer.Normalize(null, null, null, null, null, null,
            new DocumentaryPerformerSceneFactCards(longCards, shortCards), "calm", "test", LanguageProfileResolver.Resolve("en"));

        Assert.Equal(12, result.Context.Formats.Single(f => f.Format == "long").Beats.Count);
        Assert.Equal(4, result.Context.Formats.Single(f => f.Format == "short").Beats.Count);
        Assert.Equal(longCards.Cards.Select(c => c.SceneId), result.SafeContexts.Where(c => c.Format == "long").Select(c => c.SceneId));
        Assert.Equal(shortCards.Cards.Select(c => c.SceneId), result.SafeContexts.Where(c => c.Format == "short").Select(c => c.SceneId));
    }

    [Fact]
    public void RawNarrativeProjection_DoesNotMixVariants()
    {
        var notes = new ProducerNotesContract("v", "en", []);
        var longRaw = RawNarrativeGenerator.Build("LONG", notes, "test", Frames("long", 12));
        var shortRaw = RawNarrativeGenerator.Build("short", notes, "test", Frames("short", 4));

        Assert.Equal(12, longRaw.Scenes.Count);
        Assert.Equal(4, shortRaw.Scenes.Count);
        Assert.DoesNotContain(longRaw.Scenes, s => s.SceneId.StartsWith("short-"));
        Assert.DoesNotContain(shortRaw.Scenes, s => s.SceneId.StartsWith("long-"));
    }

    [Fact]
    public void PlanningOnlySceneBrief_IsNotDiscarded()
    {
        var cards = new SceneFactCardSet("v", "o", "long", "en",
            [new SceneFactCard("long-001", 1, "long", ["The scene explains apparent motion"], [], [], [], [], [], [], [], ["Do not imply physical proximity"], 20, "long-001", "long-001")]);

        var context = NarrationContextBuilder.Build(null, null, null, null, null, null,
            new DocumentaryPerformerSceneFactCards(cards, Cards("short", "short", 1)), null, "calm", "test");

        var beat = Assert.Single(context.Formats.Single(f => f.Format == "long").Beats);
        Assert.Equal("long-001", beat.SceneId);
        Assert.NotEmpty(beat.VerifiedFacts);
    }

    [Fact]
    public void EmptyRequestedContext_PreventsLlmInvocation()
    {
        var empty = Cards("long", "long", 0);
        var shortCards = Cards("short", "short", 1);
        var context = NarrationContextBuilder.Build(null, null, null, null, null, null,
            new DocumentaryPerformerSceneFactCards(empty, shortCards), null, "calm", "test");

        var error = Assert.Throws<InvalidOperationException>(() => NarrationContextProjectionValidator.Validate(["Long"], empty, shortCards,
            RawNarrativeGenerator.Build("long", new("v", "en", []), "test", []),
            RawNarrativeGenerator.Build("short", new("v", "en", []), "test", Frames("short", 1)), context));

        Assert.Contains("emptyRequestedContext=True", error.Message);
        Assert.Contains("LLM invocation prevented", error.Message);
    }

    [Fact]
    public void SafeContextSceneIdentity_MatchesCompositionAuthority()
    {
        var longCards = Cards("long", "long", 2);
        var shortCards = Cards("short", "short", 1);
        var valid = NarrationContextBuilder.Build(null, null, null, null, null, null,
            new DocumentaryPerformerSceneFactCards(longCards, shortCards), null, "calm", "test");
        var longFormat = valid.Formats.Single(f => f.Format == "long");
        var reordered = valid with { Formats = [new("long", longFormat.Beats.Reverse().ToArray()), valid.Formats.Single(f => f.Format == "short")] };

        var error = Assert.Throws<InvalidOperationException>(() => NarrationContextProjectionValidator.Validate(["long", "short"], longCards, shortCards,
            RawNarrativeGenerator.Build("long", new("v", "en", []), "test", Frames("long", 2)),
            RawNarrativeGenerator.Build("short", new("v", "en", []), "test", Frames("short", 1)), reordered));

        Assert.Contains("firstLongMismatch=index=0", error.Message);
    }

    [Fact]
    public void PerformerPrompt_ContainsOnePassagePerCanonicalScene()
    {
        var context = NarrationContextBuilder.Build(null, null, null, null, null, null,
            new DocumentaryPerformerSceneFactCards(Cards("long", "long", 12), Cards("short", "short", 4)), null, "calm", "test");

        var output = new NarrationPromptComposer().Compose(new NarrationPromptComposerInput(context, [],
            "/tmp/preview.md", "/tmp/diagnostics.json", "/tmp/quality.json"));

        Assert.Equal(16, output.PromptPreviewMarkdown.Split("Beat ", StringSplitOptions.None).Length - 1);
        foreach (var fact in Enumerable.Range(1, 12).Select(i => $"Grounded fact for canonical long scene {i}"))
            Assert.Contains(fact, output.PromptPreviewMarkdown);
        foreach (var fact in Enumerable.Range(1, 4).Select(i => $"Grounded fact for canonical short scene {i}"))
            Assert.Contains(fact, output.PromptPreviewMarkdown);
    }

    private static SceneFactCardSet Cards(string declaredFormat, string prefix, int count) => new("v", "o", declaredFormat, "en",
        Enumerable.Range(1, count).Select(i => new SceneFactCard($"{prefix}-{i:000}", i, prefix,
            [$"Grounded fact for canonical {prefix} scene {i}"], [], [], [], [], [], [], [], ["Do not invent details"], 20, $"{prefix}-{i:000}", $"{prefix}-{i:000}")).ToArray());

    private static IReadOnlyList<StoryFrameNarrationSource> Frames(string prefix, int count) => Enumerable.Range(1, count)
        .Select(i => new StoryFrameNarrationSource($"{prefix}-{i:000}", i, $"{prefix}-frame-{i:000}", $"Natural narration purpose for {prefix} scene {i}."))
        .ToArray();
}
