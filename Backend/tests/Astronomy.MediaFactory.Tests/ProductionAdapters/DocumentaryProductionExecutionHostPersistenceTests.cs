using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostPersistenceTests
{
 [Fact] public async Task Artifact_manifest_and_completed_execution_record_are_persisted_after_success()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); var r = await h.ExecuteAsync(); File.Exists(r.ArtifactManifestReference).Should().BeTrue(); File.Exists(r.ExecutionDiagnosticsReference).Should().BeTrue(); h.DiagnosticsWriter.Files.Should().Contain(x => x.EndsWith("documentary-production-execution.json"));
 }
 [Fact] public async Task Required_manifest_failure_marks_successful_pipeline_failed()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { FailManifestPersistence = true }); var r = await h.ExecuteAsync(); r.Status.Should().Be(DocumentaryProductionExecutionStatus.Failed); r.ArtifactManifestReference.Should().BeNull(); r.Failures.Should().Contain(x => x.Code == DocumentaryProductionFailureCode.FileSystemFailure);
 }
 [Fact] public async Task Persistence_failure_does_not_replace_original_operation_failure()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { FailManifestPersistence = true }); h.AdapterRegistry.VisualOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.NonRetryableFailure)); var r = await h.ExecuteAsync(); r.Failures.First().Code.Should().Be(DocumentaryProductionFailureCode.ProviderRejectedRequest); r.Failures.Should().Contain(x => x.Code == DocumentaryProductionFailureCode.FileSystemFailure);
 }
 [Fact] public async Task Failed_writes_produce_null_references()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { FailDiagnosticsFileName = "execution-completed.json" }); var r = await h.ExecuteAsync(); r.ExecutionDiagnosticsReference.Should().BeNull(); r.Status.Should().Be(DocumentaryProductionExecutionStatus.Failed);
 }
}
