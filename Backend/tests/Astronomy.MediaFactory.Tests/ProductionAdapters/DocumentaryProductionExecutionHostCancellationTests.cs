using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostCancellationTests
{
 [Theory] [InlineData("visual")] [InlineData("narration")] [InlineData("scene")] [InlineData("verification")]
 public async Task Caller_cancellation_propagates(string stage)
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(); var (queue, started) = stage switch { "visual" => (h.AdapterRegistry.VisualOutcomes, h.AdapterRegistry.VisualStarted.Task), "narration" => (h.AdapterRegistry.NarrationOutcomes, h.AdapterRegistry.NarrationStarted.Task), "scene" => (h.AdapterRegistry.SceneCompositionOutcomes, h.AdapterRegistry.SceneCompositionStarted.Task), _ => (h.AdapterRegistry.VariantVerificationOutcomes, h.AdapterRegistry.VariantVerificationStarted.Task) }; queue.Enqueue(new(FakeProductionAdapterOutcomeKind.WaitUntilCancelled));
  using var cts = new CancellationTokenSource(); var execution = h.ExecuteAsync(cts.Token); await started.WaitAsync(TimeSpan.FromSeconds(10)); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
 }
}
