using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintImmutabilityTests
{
 [Fact] public void Aggregate_defensively_copies_and_exposes_read_only_scenes() { var list=new List<DocumentarySceneBlueprint>{OrionDocumentaryBlueprintFixture.Scene()}; var x=OrionDocumentaryBlueprintFixture.Create(list); list.Clear(); Assert.Single(x.Scenes); Assert.Throws<NotSupportedException>(()=>((IList<DocumentarySceneBlueprint>)x.Scenes).Clear()); }
 [Fact] public void Scene_defensively_copies_both_collections() { var template=OrionDocumentaryBlueprintFixture.Scene(); var refs=template.KnowledgeReferences.ToList(); var visuals=template.VisualOpportunities.ToList(); var x=new DocumentarySceneBlueprint("s",1,"t",template.NarrativeStage,template.SceneRole,template.ViewerQuestion,template.SceneObjective,template.EditorialOutcome,template.EditorialPriority,refs,visuals,template.Transition,1); refs.Clear(); visuals.Clear(); Assert.NotEmpty(x.KnowledgeReferences); Assert.NotEmpty(x.VisualOpportunities); Assert.Throws<NotSupportedException>(()=>((IList<KnowledgeReference>)x.KnowledgeReferences).Clear()); }
}
