using Astronomy.MediaFactory.Infrastructure.DocumentaryBlueprint;
using Astronomy.MediaFactory.Infrastructure.Orchestration.RC2;

namespace Astronomy.MediaFactory.Tests;

public sealed class Phase7CompositionFactBridgeTests
{
    [Fact]
    public void CommittedFactsCreateSceneFactCardsAndPreserveDispositionAndLineage()
    {
        var input = Authority();
        var result = CommittedCompositionFactCardProjector.ValidateAndProject(input, [Frame("long-1", "lf-1")], [Frame("short-1", "sf-1")], "en", "test");

        Assert.True(result.Valid, string.Join("; ", result.Errors));
        var longFact = Assert.Single(Assert.Single(result.LongFrames!).KnowledgeFacts!);
        Assert.Equal("claim-long", longFact.ClaimId);
        Assert.True(longFact.Required);
        Assert.Equal(["knowledge-long"], longFact.KnowledgeReferenceIds);
        Assert.Equal(["source-long"], longFact.SourceIds);
        Assert.Equal(["Keep qualification"], longFact.QualificationRequirements);
        Assert.Equal("CommittedCompositionAuthority", result.Diagnostics.BridgeSource);
    }

    [Fact]
    public void CrossVariantOrMissingSceneMappingFailsClosed()
    {
        var input = Authority() with { LongRequest = Authority().LongRequest with
            { OrderedScenes = [Scene("short-1", "sf-1", "claim-long", "knowledge-long", "source-long")] } };

        var result = CommittedCompositionFactCardProjector.ValidateAndProject(input, [Frame("long-1", "lf-1")], [Frame("short-1", "sf-1")], "en", "test");

        Assert.False(result.Valid);
        Assert.Equal("P7_COMPOSITION_SCENE_MAPPING_FAILED", result.ReasonCode);
        Assert.NotEmpty(result.Diagnostics.MissingCompositionSceneIds);
    }

    private static NarrationGeneratorV5AuthorityInput Authority() => new(
        Request("Long", Scene("long-1", "lf-1", "claim-long", "knowledge-long", "source-long")),
        Request("Short", Scene("short-1", "sf-1", "claim-short", "knowledge-short", "source-short")));

    private static DocumentaryNarrativeCompositionRequest Request(string variant, DocumentaryNarrativeSceneInput scene) =>
        new("execution", Guid.Parse("11111111-1111-1111-1111-111111111111"), "orion", "Constellation", "en", variant,
            "profile", [scene], new(1, 2, 3), [], [], []);

    private static DocumentaryNarrativeSceneInput Scene(string id, string frameId, string claim, string reference, string source) =>
        new(1, id, "science", "Science", "question", "objective", "brief",
            [new(claim, "A committed astronomical fact", [reference], [source], 0.9m, ["Keep qualification"])],
            [], [], [], [], "visual", 20, "", "") { BlueprintSceneId = id, StoryFrameId = frameId, SceneRole = "Explanation" };

    private static StoryFrameNarrationSource Frame(string id, string frameId) =>
        new(id, 1, frameId, "mapping", BlueprintSceneId: id, SceneRole: "Explanation");
}
