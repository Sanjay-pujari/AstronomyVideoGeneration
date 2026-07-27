using System.Text.Json;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class DocumentarySceneCompositionNonMutationTests
{
 [Fact] public async Task Provider_binding_does_not_mutate_request_or_options(){using var t=new Temp();var runner=new RecordingProcessRunner{OnExecute=(_,_)=>File.WriteAllBytesAsync(Path.Combine(t.Path,"provider-scene.mp4"),[1])};var request=DocumentarySceneCompositionTestFixtures.ProviderRequest(t.Path);var before=JsonSerializer.Serialize(request);var options=new Astronomy.MediaFactory.Contracts.RenderingOptions{FfmpegPath="fake"};var optionBefore=JsonSerializer.Serialize(options);await new ExistingFFmpegDocumentarySceneProviderBinding(runner,new Astronomy.MediaFactory.Rendering.FfmpegArgumentBuilder(),Microsoft.Extensions.Options.Options.Create(options)).ComposeAsync(request,default);JsonSerializer.Serialize(request).Should().Be(before);JsonSerializer.Serialize(options).Should().Be(optionBefore);}
 [Fact] public async Task Registry_snapshot_is_immutable_and_replay_is_idempotent(){using var t=new Temp();var p=Path.Combine(t.Path,"scene.mp4");await File.WriteAllBytesAsync(p,[1]);var d=DocumentarySceneCompositionTestFixtures.Descriptor("scene",p,"video/mp4");var registry=new DocumentaryPhysicalArtifactRegistry();await registry.RegisterAsync(d,DocumentaryPhysicalArtifactKind.SceneVideo,default);await registry.RegisterAsync(d,DocumentaryPhysicalArtifactKind.SceneVideo,default);var all=await registry.GetAllAsync(DocumentarySceneCompositionTestFixtures.Correlation,default);all.Should().ContainSingle().Which.Should().Be(d);all.Should().BeAssignableTo<IReadOnlyCollection<DocumentaryPhysicalArtifactDescriptor>>();}
 sealed class Temp:IDisposable{public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString("N"));public Temp()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
}
