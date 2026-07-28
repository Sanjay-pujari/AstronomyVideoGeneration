using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostRetryTests
{
 [Theory]
 [InlineData(DocumentaryProductionOperationKind.VisualGeneration)] [InlineData(DocumentaryProductionOperationKind.NarrationSynthesis)] [InlineData(DocumentaryProductionOperationKind.SubtitleGeneration)] [InlineData(DocumentaryProductionOperationKind.SceneComposition)] [InlineData(DocumentaryProductionOperationKind.VariantComposition)] [InlineData(DocumentaryProductionOperationKind.MediaVerification)]
 public async Task Retryable_operation_failure_is_retried(DocumentaryProductionOperationKind operation)
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { MaximumAttemptsPerOperation = 2 }); var q = Queue(h, operation); q.Enqueue(new(FakeProductionAdapterOutcomeKind.RetryableFailure)); q.Enqueue(FakeProductionAdapterOutcome.Success);
  var r = await h.ExecuteAsync(); r.Succeeded.Should().BeTrue(); var first = h.AdapterRegistry.Attempts.First(x => x.OperationKind == operation && (operation != DocumentaryProductionOperationKind.MediaVerification || x.SceneId is null)); var attempts = h.AdapterRegistry.Attempts.Where(x => x.OperationKind == operation && x.AssetId == first.AssetId).Take(2).ToArray(); attempts.Select(x => x.AttemptNumber).Should().Equal(1, 2); attempts.Select(x => x.ExecutionId).Distinct().Should().ContainSingle(); attempts.Select(x => x.CorrelationId).Distinct().Should().ContainSingle(); attempts.Select(x => x.AssetId).Distinct().Should().ContainSingle();
 }
 [Fact] public async Task Non_retryable_failure_is_not_retried()
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { MaximumAttemptsPerOperation = 2 }); h.AdapterRegistry.VisualOutcomes.Enqueue(new(FakeProductionAdapterOutcomeKind.NonRetryableFailure));
  await h.ExecuteAsync(); var firstAsset = h.AdapterRegistry.VisualRequests.First().AssetPlan.AssetId; h.AdapterRegistry.VisualRequests.Count(x => x.AssetPlan.AssetId == firstAsset).Should().Be(1);
 }
 static System.Collections.Concurrent.ConcurrentQueue<FakeProductionAdapterOutcome> Queue(DocumentaryProductionExecutionHostHarness h, DocumentaryProductionOperationKind k) => k switch { DocumentaryProductionOperationKind.VisualGeneration => h.AdapterRegistry.VisualOutcomes, DocumentaryProductionOperationKind.NarrationSynthesis => h.AdapterRegistry.NarrationOutcomes, DocumentaryProductionOperationKind.SubtitleGeneration => h.AdapterRegistry.SubtitleOutcomes, DocumentaryProductionOperationKind.SceneComposition => h.AdapterRegistry.SceneCompositionOutcomes, DocumentaryProductionOperationKind.VariantComposition => h.AdapterRegistry.VariantCompositionOutcomes, _ => h.AdapterRegistry.VariantVerificationOutcomes };
}
