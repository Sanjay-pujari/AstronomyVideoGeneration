using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentarySceneBlueprintContractTests
{
 [Fact] public void Valid_scene_preserves_planning_values() { var x=OrionDocumentaryBlueprintFixture.Scene(); Assert.Equal(75,x.EstimatedDurationSeconds); Assert.Equal("Why are Orion's Belt stars so famous?",x.ViewerQuestion.Text); Assert.Equal(2,x.KnowledgeReferences.Count); }
 [Fact] public void Scene_rejects_negative_duration() { var x=OrionDocumentaryBlueprintFixture.Scene(); Assert.Throws<ArgumentOutOfRangeException>(()=>new DocumentarySceneBlueprint("s",1,"t",x.NarrativeStage,x.SceneRole,x.ViewerQuestion,x.SceneObjective,x.EditorialOutcome,x.EditorialPriority,[],[],x.Transition,-1)); }
 [Fact] public void Scene_rejects_null_required_values_and_collections() { var x=OrionDocumentaryBlueprintFixture.Scene(); Assert.Throws<ArgumentNullException>(()=>new DocumentarySceneBlueprint("s",1,"t",x.NarrativeStage,x.SceneRole,null!,x.SceneObjective,x.EditorialOutcome,x.EditorialPriority,[],[],x.Transition,0)); Assert.Throws<ArgumentNullException>(()=>new DocumentarySceneBlueprint("s",1,"t",x.NarrativeStage,x.SceneRole,x.ViewerQuestion,x.SceneObjective,x.EditorialOutcome,x.EditorialPriority,null!,[],x.Transition,0)); }
 [Fact] public void Scene_contract_has_no_narration_or_generated_text_fields() { string[] forbidden=["Narration","NarrationText","VoiceOver","VoiceOverText","Script","ScriptText","Prompt","PromptText","GeneratedText","LlmResponse"]; Assert.DoesNotContain(typeof(DocumentarySceneBlueprint).GetProperties(),p=>forbidden.Contains(p.Name,StringComparer.OrdinalIgnoreCase)); }
}
