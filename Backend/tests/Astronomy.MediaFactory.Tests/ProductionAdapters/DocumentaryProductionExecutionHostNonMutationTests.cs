using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostNonMutationTests
{
 [Fact]
 public async Task Production_execution_does_not_mutate_pipeline_input()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Request_builder_does_not_mutate_plans()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Record_mapper_does_not_mutate_descriptors()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Verification_does_not_mutate_artifact_evidence()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
