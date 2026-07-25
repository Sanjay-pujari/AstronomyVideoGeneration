using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderArchitectureTests
{
    [Fact] public void Inputs_have_only_getters_and_no_forbidden_generated_text_contracts()
    {
        string[] forbidden=["Narration","NarrationText","VoiceOver","VoiceOverText","Script","ScriptText","Prompt","PromptText","GeneratedText","LlmResponse"];
        foreach(var type in new[]{typeof(DocumentaryBlueprintBuildRequest),typeof(DocumentarySceneBlueprintInput)}) { Assert.All(type.GetProperties(),property=>Assert.False(property.CanWrite)); Assert.DoesNotContain(type.GetProperties(),property=>forbidden.Contains(property.Name,StringComparer.OrdinalIgnoreCase)); }
    }
    [Fact] public void Builder_is_sealed_synchronous_and_has_no_dependencies()
    {
        var type=typeof(DocumentaryBlueprintBuilder); Assert.True(type.IsSealed); Assert.Single(type.GetConstructors()); Assert.Empty(type.GetConstructors().Single().GetParameters()); var build=Assert.Single(type.GetMethods().Where(x=>x.DeclaringType==type)); Assert.Equal(typeof(global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint),build.ReturnType); Assert.False(typeof(Task).IsAssignableFrom(build.ReturnType));
        Assert.Equal("Astronomy.MediaFactory.Core",type.Assembly.GetName().Name);
    }
    [Fact] public void O21_contract_inventory_remains_certified()
    {
        Assert.Equal(["BlueprintId","KnowledgeId","Metadata","PrimaryLanguage","PublicationFormat","Scenes","SubjectId","SubjectName","Version"],typeof(global::Astronomy.MediaFactory.Core.DocumentaryBlueprint.DocumentaryBlueprint).GetProperties().Select(x=>x.Name).Order());
        Assert.Equal(["EditorialOutcome","EditorialPriority","EstimatedDurationSeconds","KnowledgeReferences","NarrativeStage","SceneId","SceneNumber","SceneObjective","SceneRole","Title","Transition","ViewerQuestion","VisualOpportunities"],typeof(DocumentarySceneBlueprint).GetProperties().Select(x=>x.Name).Order());
    }
}
