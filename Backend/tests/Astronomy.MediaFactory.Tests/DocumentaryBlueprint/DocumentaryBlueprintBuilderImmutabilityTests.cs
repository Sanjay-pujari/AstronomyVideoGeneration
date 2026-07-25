using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderImmutabilityTests
{
    [Fact] public void Inputs_copy_collections_and_build_does_not_mutate_them()
    {
        var refs=new List<KnowledgeReference>{new("one","section","purpose",true)}; var visuals=new List<VisualOpportunity>{new("description","type",null,null,false)}; var scene=OrionDocumentaryBlueprintBuilderFixture.Scene(references:refs,visuals:visuals); refs.Add(new("two","section","purpose",false)); visuals.Clear(); Assert.Single(scene.KnowledgeReferences); Assert.Single(scene.VisualOpportunities);
        var scenes=new List<DocumentarySceneBlueprintInput>{scene}; var request=OrionDocumentaryBlueprintBuilderFixture.Create(scenes); scenes.Clear(); Assert.Single(request.Scenes); Assert.Throws<NotSupportedException>(()=>((IList<DocumentarySceneBlueprintInput>)request.Scenes).Add(scene)); Assert.Throws<NotSupportedException>(()=>((IList<KnowledgeReference>)scene.KnowledgeReferences).Clear());
        var before=request.Scenes.Select(x=>x.SceneId).ToArray(); var result=new DocumentaryBlueprintBuilder().Build(request); Assert.Equal(before,request.Scenes.Select(x=>x.SceneId)); refs.Clear(); Assert.Single(result.Scenes[0].KnowledgeReferences);
    }
}
