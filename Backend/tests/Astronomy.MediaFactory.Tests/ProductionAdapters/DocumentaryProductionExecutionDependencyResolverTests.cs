using Astronomy.MediaFactory.Core.DocumentaryBlueprint;
using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionDependencyResolverTests
{
 [Fact]
 public async Task Scene_composition_uses_registered_dependencies()
 {
  var root=DocumentaryProductionExecutionHostTestFixtures.CreateWorkspaceRoot();var registry=new DocumentaryPhysicalArtifactRegistry();
  var registered=DocumentaryProductionExecutionHostTestFixtures.Descriptor(root,"scene-video",DocumentaryPhysicalArtifactKind.SceneVideo) with { ProviderId="registry-marker" };
  await registry.RegisterAsync(registered,DocumentaryPhysicalArtifactKind.SceneVideo,default);
  var resolved=await new DocumentaryProductionExecutionDependencyResolver(registry).ResolveAsync("scene-video",DocumentaryPhysicalArtifactKind.SceneVideo,"correlation-a3-10",default);
  resolved.ProviderId.Should().Be("registry-marker");
 }

 [Fact]
 public async Task Variant_composition_uses_registered_scene_videos_in_sequence()
 {
  var root=DocumentaryProductionExecutionHostTestFixtures.CreateWorkspaceRoot();var registry=new DocumentaryPhysicalArtifactRegistry();
  await registry.RegisterAsync(DocumentaryProductionExecutionHostTestFixtures.Descriptor(root,"second",DocumentaryPhysicalArtifactKind.SceneVideo,2),DocumentaryPhysicalArtifactKind.SceneVideo,default);
  await registry.RegisterAsync(DocumentaryProductionExecutionHostTestFixtures.Descriptor(root,"first",DocumentaryPhysicalArtifactKind.SceneVideo,1),DocumentaryPhysicalArtifactKind.SceneVideo,default);
  var resolved=await new DocumentaryProductionExecutionDependencyResolver(registry).ResolveOrderedAsync([Plan("second",2),Plan("first",1)],DocumentaryPhysicalArtifactKind.SceneVideo,"correlation-a3-10",default);
  resolved.Select(x=>x.AssetId).Should().Equal("first","second");
 }

 [Fact]
 public async Task Adapter_success_without_registry_registration_fails_dependency_resolution()
 {
  var resolver=new DocumentaryProductionExecutionDependencyResolver(new DocumentaryPhysicalArtifactRegistry());
  var action=()=>resolver.ResolveAsync("unregistered",DocumentaryPhysicalArtifactKind.VisualImage,"correlation-a3-10",default);
  await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*registered dependency is missing*");
 }

 private static DocumentaryMediaAssetPlan Plan(string id,int sequence)=>new(id,DocumentaryMediaAssetType.SceneVideo,DocumentaryMediaAssetFormat.Mp4,DocumentaryMediaVariantType.LongEnglish,"scene","source",DocumentaryMediaProviderCapability.SceneComposition,sequence,[],1920,1080,1000,30,48000,2,[],"correlation-a3-10");
}
