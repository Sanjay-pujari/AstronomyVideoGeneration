using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostFullFlowTests
{
 [Fact]
 public async Task Four_variants_execute_through_complete_fake_pipeline()
 {
  await using var harness=new DocumentaryProductionExecutionHostHarness();
  var result=await harness.Coordinator.ExecuteAsync(harness.Request,CancellationToken.None);
  result.Succeeded.Should().BeTrue();
  result.Status.Should().Be(DocumentaryProductionExecutionStatus.Succeeded);
  result.Variants.Should().HaveCount(4).And.OnlyContain(x=>x.Status==DocumentaryProductionItemStatus.Succeeded&&x.VerificationResult!.Verified);
  result.ExecutionRecord.Should().NotBeNull();
  result.ExecutionRecord!.IsComplete.Should().BeTrue();
  result.EligibleForPublishing.Should().BeFalse("A3.10 must not certify publishing");
  harness.AdapterRegistry.Attempts.Should().OnlyContain(x=>x.ExecutionId==result.ExecutionId&&x.CorrelationId==result.CorrelationId);
  var registered=await harness.ArtifactRegistry.GetAllAsync(result.CorrelationId,CancellationToken.None);
  registered.Should().NotBeEmpty().And.OnlyContain(x=>File.Exists(x.PhysicalPath)&&x.ProviderId=="registry-marker");
  File.Exists(result.ArtifactManifestReference).Should().BeTrue();
  File.Exists(result.ExecutionDiagnosticsReference).Should().BeTrue();
 }

 [Fact]
 public async Task Compatibility_host_returns_completed_execution_record()
 {
  await using var harness=new DocumentaryProductionExecutionHostHarness();
  var record=await harness.CompatibilityHost.ExecuteAsync(harness.Request,CancellationToken.None);
  record.Should().NotBeNull(); record!.IsComplete.Should().BeTrue(); record.VariantRecords.Should().HaveCount(4);
 }

 [Fact]
 public async Task Coordinator_consumes_registered_artifacts_and_preserves_semantic_order()
 {
  await using var harness=new DocumentaryProductionExecutionHostHarness();
  var result=await harness.ExecuteAsync();
  result.Succeeded.Should().BeTrue();
  var order=harness.InvocationOrder.ToList();
  order.Should().NotBeEmpty();
  order.First().Should().StartWith("VisualGeneration:");
  order.IndexOf(order.First(x=>x.StartsWith("NarrationSynthesis:"))).Should().BeGreaterThan(order.IndexOf(order.First()));
  result.ExecutionRecord!.OutputManifest.Assets.Should().OnlyContain(x=>x.ProviderId=="registry-marker");
 }
}
