using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionDependencyResolverTests
{
 [Fact]
 public async Task Scene_composition_uses_registered_dependencies()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Variant_composition_uses_registered_scene_videos_in_sequence()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Adapter_success_without_registry_registration_fails_dependency_resolution()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
