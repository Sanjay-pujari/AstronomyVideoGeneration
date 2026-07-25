using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderTests
{
    [Fact] public void Valid_request_builds_exactly_the_two_Orion_scenes()
    {
        var result = new DocumentaryBlueprintBuilder().Build(OrionDocumentaryBlueprintBuilderFixture.Create());
        Assert.IsType<global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint>(result);
        Assert.Equal("documentary.orion.long.v1", result.BlueprintId); Assert.Equal("knowledge.orion.v1", result.KnowledgeId);
        Assert.Equal("correlation-orion-001", result.Metadata.CorrelationId); Assert.Equal(2, result.Scenes.Count);
        Assert.Equal(["scene.orion.belt", "scene.orion.distance"], result.Scenes.Select(x => x.SceneId));
    }
}
