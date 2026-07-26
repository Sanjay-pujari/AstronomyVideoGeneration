using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaProjectionSemanticPlanTests
{
 [Fact] public void Long_semantic_scenes_use_numeric_sequence_past_nine()
 {
  var scenes=Enumerable.Range(0,11).Select(i=>new DocumentarySemanticScene($"topic.semantic-scene.{i}",i,DocumentaryMediaSceneRole.Identity,$"scene {i}",$"दृश्य {i}",[],"intent",100-i,true,false,[],"correlation")).ToArray();
  Assert.Equal(Enumerable.Range(0,11),scenes.OrderBy(x=>x.Sequence).Select(x=>x.Sequence));
  Assert.Equal("topic.semantic-scene.10",scenes.OrderBy(x=>x.Sequence).Last().SemanticSceneId);
  Assert.True(Array.IndexOf(scenes.Select(x=>x.SemanticSceneId).ToArray(),"topic.semantic-scene.2")<Array.IndexOf(scenes.Select(x=>x.SemanticSceneId).ToArray(),"topic.semantic-scene.10"));
 }
 [Theory] [InlineData("orion")] [InlineData("leo")] [InlineData("conjunction")] public void Every_request_uses_one_shared_traceable_plan(string scenario)
 {var request=scenario=="orion"?DocumentaryMediaProjectionFixture.Orion():scenario=="leo"?DocumentaryMediaProjectionFixture.Leo():DocumentaryMediaProjectionFixture.Conjunction();var p=DocumentaryMediaProjectionFixture.Complete(request);Assert.Equal(p.Variants[0].Scenes.SelectMany(x=>x.KnowledgeReferences).Select(x=>x.JsonPointer),p.Variants[1].Scenes.SelectMany(x=>x.KnowledgeReferences).Select(x=>x.JsonPointer));}
}
