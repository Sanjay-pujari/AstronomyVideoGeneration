namespace Astronomy.MediaFactory.Tests.DocumentaryBlueprint;
public sealed class DocumentaryMediaPipelineDocumentationTests
{
 [Fact] public void O218_documentation_certifies_scope_and_schema_behavior(){var path=Path.Combine(FindRoot(),"docs","documentary-media-pipeline-orchestration.md");var text=File.ReadAllText(path);foreach(var phrase in new[]{"without reinterpretation","four canonical variants","PlanOnly` returns `Planned","invokes no provider","all six","Measured TTS duration","Effective timing","Subtitle text","Scene composition","variant composition","render verification","failure isolation","Partial completion","invented astronomy facts","no vendor SDK","direct FFmpeg","publishing","upload","O2.19","exactly one attempt"})Assert.Contains(phrase,text,StringComparison.OrdinalIgnoreCase);}
 static string FindRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null){if(File.Exists(Path.Combine(d.FullName,"Astronomy.MediaFactory.slnx")))return d.FullName;d=d.Parent;}throw new InvalidOperationException("Backend root not found");}
}
