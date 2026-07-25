using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryBlueprintBuilderMappingTests
{
    [Fact] public void Every_aggregate_metadata_and_scene_value_is_mapped()
    {
        var request=OrionDocumentaryBlueprintBuilderFixture.Create(); var result=new DocumentaryBlueprintBuilder().Build(request); var input=request.Scenes[0]; var scene=result.Scenes[0];
        Assert.Equal(request.BlueprintId,result.BlueprintId); Assert.Equal(request.KnowledgeId,result.KnowledgeId); Assert.Equal(request.SubjectId,result.SubjectId); Assert.Equal(request.SubjectName,result.SubjectName); Assert.Equal(request.PublicationFormat,result.PublicationFormat); Assert.Equal(request.PrimaryLanguage,result.PrimaryLanguage); Assert.Equal(request.Version,result.Version); Assert.Equal(request.Metadata,result.Metadata);
        Assert.Equal(input.SceneId,scene.SceneId); Assert.Equal(input.SceneNumber,scene.SceneNumber); Assert.Equal(input.Title,scene.Title); Assert.Equal(input.NarrativeStage,scene.NarrativeStage); Assert.Equal(input.SceneRole,scene.SceneRole); Assert.Equal(input.ViewerQuestion,scene.ViewerQuestion); Assert.Equal(input.SceneObjective,scene.SceneObjective); Assert.Equal(input.EditorialOutcome,scene.EditorialOutcome); Assert.Equal(input.EditorialPriority,scene.EditorialPriority); Assert.Equal(input.KnowledgeReferences,scene.KnowledgeReferences); Assert.Equal(input.VisualOpportunities,scene.VisualOpportunities); Assert.Equal(input.Transition,scene.Transition); Assert.Equal(input.EstimatedDurationSeconds,scene.EstimatedDurationSeconds);
        Assert.Equal(new DateTimeOffset(2026,1,15,12,0,0,TimeSpan.Zero),result.Metadata.CreatedUtc); Assert.Equal("editorial-system",result.Metadata.CreatedBy); Assert.Equal("editorial-model-v1",result.Metadata.EditorialModelVersion); Assert.Equal("knowledge-v1",result.Metadata.KnowledgeVersion); Assert.Equal("1.0",result.Metadata.BlueprintSchemaVersion); Assert.Equal("correlation-orion-001",result.Metadata.CorrelationId);
    }
}
