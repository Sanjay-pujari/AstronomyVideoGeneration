using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostFailureTests
{
 [Fact] public async Task Adapter_success_without_registry_registration_fails_dependency_resolution()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); h.AdapterRegistry.VisualOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration));
  var r = await h.ExecuteAsync(); r.Failures.Should().Contain(x => x.Code == DocumentaryProductionFailureCode.SourceArtifactMissing); var failed = r.Variants[0].VariantType; h.AdapterRegistry.NarrationRequests.Should().NotContain(x => x.AssetPlan.VariantType == failed); h.AdapterRegistry.SceneCompositionRequests.Should().NotContain(x => x.AssetPlan.VariantType == failed);
 }
 [Fact] public async Task Scene_success_without_registration_prevents_variant_composition()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); h.AdapterRegistry.SceneCompositionOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.SuccessWithoutRegistration));
  var r = await h.ExecuteAsync(); r.Failures.Should().Contain(x => x.Code == DocumentaryProductionFailureCode.SourceArtifactMissing); var failed = r.Variants[0].VariantId; h.AdapterRegistry.VariantCompositionRequests.Should().NotContain(x => x.MediaVariant.VariantId == failed);
 }
 [Fact] public async Task Failed_scene_verification_prevents_variant_composition()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); h.AdapterRegistry.SceneVerificationOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.VerificationRejected));
  var r = await h.ExecuteAsync(); r.Status.Should().Be(DocumentaryProductionExecutionStatus.VerificationFailed); r.Variants[0].SceneResults[0].SceneVideoArtifact.Should().NotBeNull(); var failed = r.Variants[0].VariantId; h.AdapterRegistry.VariantCompositionRequests.Should().NotContain(x => x.MediaVariant.VariantId == failed);
 }
 [Fact] public async Task Failed_variant_verification_marks_execution_verification_failed()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); h.AdapterRegistry.VariantVerificationOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.VerificationRejected));
  var r = await h.ExecuteAsync(); r.Status.Should().Be(DocumentaryProductionExecutionStatus.VerificationFailed); r.Variants[0].FinalVariantArtifact.Should().NotBeNull(); r.EligibleForPublishing.Should().BeFalse();
 }
 [Fact] public async Task Failed_scene_preserves_all_completed_upstream_artifacts()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); h.AdapterRegistry.SubtitleOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.NonRetryableFailure));
  var r = await h.ExecuteAsync(); var s = r.Variants[0].SceneResults[0]; s.VisualArtifacts.Should().NotBeEmpty(); s.NarrationArtifact.Should().NotBeNull(); s.SubtitleArtifact.Should().BeNull(); s.SceneVideoArtifact.Should().BeNull();
 }
 [Fact] public async Task Disabled_host_performs_no_pipeline_work()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { HostEnabled = false }); var r = await h.ExecuteAsync();
  r.Status.Should().Be(DocumentaryProductionExecutionStatus.NotStarted); h.WorkspaceManager.CreateCount.Should().Be(0); h.DiagnosticsWriter.Files.Should().BeEmpty(); h.ArtifactRegistry.AccessCount.Should().Be(0); h.AdapterRegistry.Attempts.Should().BeEmpty();
 }
 [Fact] public async Task Missing_required_adapter_fails_before_execution()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { IncludeVisualAdapter = false }); var r = await h.ExecuteAsync();
  r.Failures.Should().Contain(x => x.Code == DocumentaryProductionFailureCode.AdapterUnavailable); h.AdapterRegistry.Attempts.Should().BeEmpty();
 }
}
