using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderValidationTests
{
    private static DocumentaryBlueprintBuildRequest Request(string blueprintId="b", BlueprintPublicationFormat format=BlueprintPublicationFormat.Article, DocumentaryBlueprintMetadata? metadata=null, IReadOnlyList<DocumentarySceneBlueprintInput>? scenes=null) => new(blueprintId,"k","s","subject",format,"en-US","1",metadata??OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,scenes??[OrionDocumentaryBlueprintBuilderFixture.Scene()]);
    [Fact] public void Builder_rejects_null_request() => Assert.Throws<ArgumentNullException>(()=>new DocumentaryBlueprintBuilder().Build(null!));
    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")] public void Request_rejects_blank_required_values(string? value)
    {
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest(value!,"k","s","n",BlueprintPublicationFormat.Article,"en","1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest("b",value!,"s","n",BlueprintPublicationFormat.Article,"en","1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest("b","k",value!,"n",BlueprintPublicationFormat.Article,"en","1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest("b","k","s",value!,BlueprintPublicationFormat.Article,"en","1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest("b","k","s","n",BlueprintPublicationFormat.Article,value!,"1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintBuildRequest("b","k","s","n",BlueprintPublicationFormat.Article,"en",value!,OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,[]));
    }
    [Fact] public void Request_rejects_null_metadata_scenes_elements_duplicates_and_enum()
    {
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryBlueprintBuildRequest("b","k","s","n",BlueprintPublicationFormat.Article,"en","1",null!,[]));
        Assert.Throws<ArgumentNullException>(()=>new DocumentaryBlueprintBuildRequest("b","k","s","n",BlueprintPublicationFormat.Article,"en","1",OrionDocumentaryBlueprintBuilderFixture.Create().Metadata,null!));
        Assert.Throws<ArgumentException>(()=>Request(scenes:[OrionDocumentaryBlueprintBuilderFixture.Scene(),null!]));
        Assert.Throws<ArgumentException>(()=>Request(scenes:[OrionDocumentaryBlueprintBuilderFixture.Scene(),OrionDocumentaryBlueprintBuilderFixture.Scene()]));
        Assert.Throws<ArgumentException>(()=>Request(scenes:[OrionDocumentaryBlueprintBuilderFixture.Scene(),OrionDocumentaryBlueprintBuilderFixture.Scene("other",1)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Request(format:(BlueprintPublicationFormat)999));
    }
    [Fact] public void Metadata_rejects_default_timestamp_and_non_1_0_schema()
    {
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintMetadata(default,"a","e","k","1.0","c"));
        Assert.Throws<ArgumentException>(()=>new DocumentaryBlueprintMetadata(DateTimeOffset.UnixEpoch,"a","e","k","2.0","c"));
    }
    [Fact] public void Scene_input_rejects_invalid_structure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>NewScene(duration:-1)); Assert.Throws<ArgumentOutOfRangeException>(()=>NewScene(stage:(DocumentaryNarrativeStage)999)); Assert.Throws<ArgumentOutOfRangeException>(()=>NewScene(role:(DocumentarySceneRole)999)); Assert.Throws<ArgumentOutOfRangeException>(()=>NewScene(priority:(EditorialPriority)999));
        Assert.Throws<ArgumentNullException>(()=>Raw(null!,Objective(),Outcome(),[],[],Transition())); Assert.Throws<ArgumentNullException>(()=>Raw(new("q"),null!,Outcome(),[],[],Transition())); Assert.Throws<ArgumentNullException>(()=>Raw(new("q"),Objective(),null!,[],[],Transition())); Assert.Throws<ArgumentNullException>(()=>Raw(new("q"),Objective(),Outcome(),[],[],null!));
        Assert.Throws<ArgumentNullException>(()=>Raw(new("q"),Objective(),Outcome(),null!,[],Transition())); Assert.Throws<ArgumentNullException>(()=>Raw(new("q"),Objective(),Outcome(),[],null!,Transition())); Assert.Throws<ArgumentException>(()=>Raw(new("q"),Objective(),Outcome(),[null!],[],Transition())); Assert.Throws<ArgumentException>(()=>Raw(new("q"),Objective(),Outcome(),[],[null!],Transition()));
    }
    private static DocumentarySceneBlueprintInput Raw(ViewerQuestion q, SceneObjective o, EditorialOutcome e, IReadOnlyList<KnowledgeReference> r, IReadOnlyList<VisualOpportunity> v, SceneTransition t) => new("id",1,"title",DocumentaryNarrativeStage.Wonder,DocumentarySceneRole.OpeningHook,q,o,e,EditorialPriority.High,r,v,t,1);
    private static SceneObjective Objective()=>new("summary","learning","curiosity","emotion"); private static EditorialOutcome Outcome()=>new("takeaway","contribution",false,false,false,false,false); private static SceneTransition Transition()=>new("intent","seed","direction");
    private static DocumentarySceneBlueprintInput NewScene(DocumentaryNarrativeStage stage=DocumentaryNarrativeStage.Wonder, DocumentarySceneRole role=DocumentarySceneRole.OpeningHook, EditorialPriority priority=EditorialPriority.High, ViewerQuestion? question=null, SceneObjective? objective=null, EditorialOutcome? outcome=null, IReadOnlyList<KnowledgeReference>? references=null, IReadOnlyList<VisualOpportunity>? visuals=null, SceneTransition? transition=null, int duration=1) => new("id",1,"title",stage,role,question??new("question"),objective??new("summary","learning","curiosity","emotion"),outcome??new("takeaway","contribution",false,false,false,false,false),priority,references??[],visuals??[],transition??new("intent","seed","direction"),duration);
}
