using System.Text.Json;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintSerializationTests
{
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
 [Fact] public void Json_round_trip_preserves_values_and_collection_order() { var x=OrionDocumentaryBlueprintFixture.CreateOrdered(); var json=JsonSerializer.Serialize(x,JsonOptions); var copy=JsonSerializer.Deserialize<global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint>(json,JsonOptions)!; Assert.Equal(x.BlueprintId,copy.BlueprintId); Assert.Equal(x.Metadata,copy.Metadata); Assert.Equal(x.Scenes.Select(s=>s.SceneId),copy.Scenes.Select(s=>s.SceneId)); Assert.Equal(x.Scenes.Select(s=>s.Title),copy.Scenes.Select(s=>s.Title)); for(var i=0;i<x.Scenes.Count;i++){ Assert.Equal(x.Scenes[i].KnowledgeReferences,copy.Scenes[i].KnowledgeReferences); Assert.Equal(x.Scenes[i].VisualOpportunities,copy.Scenes[i].VisualOpportunities); } Assert.Equal(json,JsonSerializer.Serialize(copy,JsonOptions)); }
}
