using System.Text.Json;
using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderDeterminismTests
{
    [Fact] public void Same_and_equivalent_requests_produce_identical_ordered_json()
    {
        var builder=new DocumentaryBlueprintBuilder(); var request=OrionDocumentaryBlueprintBuilderFixture.Create(); var one=builder.Build(request); var two=builder.Build(request); var three=builder.Build(OrionDocumentaryBlueprintBuilderFixture.Create()); var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(one,options),JsonSerializer.Serialize(two,options)); Assert.Equal(JsonSerializer.Serialize(one,options),JsonSerializer.Serialize(three,options));
        Assert.Equal(request.Scenes.Select(x=>x.SceneId),one.Scenes.Select(x=>x.SceneId)); Assert.Equal(request.Scenes[0].KnowledgeReferences.Select(x=>x.KnowledgeEntryId),one.Scenes[0].KnowledgeReferences.Select(x=>x.KnowledgeEntryId)); Assert.Equal(request.Scenes[0].VisualOpportunities.Select(x=>x.Description),one.Scenes[0].VisualOpportunities.Select(x=>x.Description)); Assert.Equal(request.BlueprintId,one.BlueprintId); Assert.Equal(request.Metadata.CreatedUtc,one.Metadata.CreatedUtc);
    }
}
