using System.Text.Json;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintSerializationTests
{
 [Fact] public void Json_round_trip_preserves_values_and_collection_order() { var x=OrionDocumentaryBlueprintFixture.Create(); var json=JsonSerializer.Serialize(x); var copy=JsonSerializer.Deserialize<global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint>(json)!; Assert.Equal(x.BlueprintId,copy.BlueprintId); Assert.Equal(x.Metadata,copy.Metadata); Assert.Equal(x.Scenes[0].KnowledgeReferences,copy.Scenes[0].KnowledgeReferences); Assert.Equal(json,JsonSerializer.Serialize(copy)); }
}
