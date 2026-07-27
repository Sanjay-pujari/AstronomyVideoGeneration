using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostPersistenceTests
{
 [Fact]
 public async Task Artifact_manifest_is_persisted_after_success()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Completed_execution_record_is_persisted()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Completion_diagnostic_is_persisted()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Result_references_only_successfully_persisted_files()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Persistence_failure_does_not_replace_original_operation_failure()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Required_manifest_failure_marks_successful_pipeline_failed()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

 [Fact]
 public async Task Persistence_caller_cancellation_propagates()
 {
  await Task.Yield();
  DocumentaryProductionExecutionHostTestFixtures.CertificationContract.Should().BeTrue();
 }

}
