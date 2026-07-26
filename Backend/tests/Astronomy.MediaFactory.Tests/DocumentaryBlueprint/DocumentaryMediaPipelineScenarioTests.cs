using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelineScenarioTests
{
 public static IEnumerable<object[]> Projects()=>[[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Orion)],[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Leo)],[new Func<DocumentaryMediaProject>(DocumentaryMediaPipelineFixture.Conjunction)]];
 [Theory][MemberData(nameof(Projects))] public void All_O217_projects_complete_four_verified_outputs(Func<DocumentaryMediaProject> factory){var result=DocumentaryMediaPipelineFixture.Run(factory());var r=result.ExecutionRecord!;Assert.Equal(DocumentaryMediaPipelineStatus.Complete,result.Status);Assert.Equal(4,r.CompletedVariantCount);Assert.Equal(0,r.FailedVariantCount);Assert.Equal(Enum.GetValues<DocumentaryMediaVariantType>(),r.VariantRecords.Select(x=>x.VariantType));Assert.All(r.VariantRecords,x=>{Assert.Equal(DocumentaryMediaPipelineStatus.Complete,x.Status);Assert.Equal(x.AssetResults.Single(a=>a.AssetId==x.OutputAssetId).Status,DocumentaryMediaAssetStatus.Verified);Assert.Equal(0,x.FailedSceneCount);});Assert.Equal(4,r.OutputManifest.Checksums.Count);Assert.True(DocumentaryMediaPipelineFixture.Summary(r).IsComplete);}
}
