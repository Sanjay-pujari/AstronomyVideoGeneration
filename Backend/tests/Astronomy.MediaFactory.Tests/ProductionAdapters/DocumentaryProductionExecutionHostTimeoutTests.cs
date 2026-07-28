using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionExecutionHostTimeoutTests
{
 [Theory]
 [InlineData(DocumentaryProductionOperationKind.VisualGeneration, DocumentaryProductionFailureCode.ProviderTimeout)]
 [InlineData(DocumentaryProductionOperationKind.NarrationSynthesis, DocumentaryProductionFailureCode.ProviderTimeout)]
 [InlineData(DocumentaryProductionOperationKind.SubtitleGeneration, DocumentaryProductionFailureCode.ProviderTimeout)]
 [InlineData(DocumentaryProductionOperationKind.SceneComposition, DocumentaryProductionFailureCode.ProcessTimedOut)]
 [InlineData(DocumentaryProductionOperationKind.VariantComposition, DocumentaryProductionFailureCode.ProcessTimedOut)]
 [InlineData(DocumentaryProductionOperationKind.MediaVerification, DocumentaryProductionFailureCode.ProviderTimeout)]
 public async Task Operation_attempt_timeout_is_enforced(DocumentaryProductionOperationKind operation, DocumentaryProductionFailureCode expected)
 {
  await using var h = new DocumentaryProductionExecutionHostHarness(new() { OperationTimeoutMilliseconds = 75 }); Queue(h, operation).Enqueue(new(FakeProductionAdapterOutcomeKind.WaitUntilCancelled));
  var r = await h.ExecuteAsync(); r.Failures.Should().Contain(x => x.Code == expected);
 }
 static System.Collections.Concurrent.ConcurrentQueue<FakeProductionAdapterOutcome> Queue(DocumentaryProductionExecutionHostHarness h, DocumentaryProductionOperationKind k) => k switch { DocumentaryProductionOperationKind.VisualGeneration => h.AdapterRegistry.VisualOutcomes, DocumentaryProductionOperationKind.NarrationSynthesis => h.AdapterRegistry.NarrationOutcomes, DocumentaryProductionOperationKind.SubtitleGeneration => h.AdapterRegistry.SubtitleOutcomes, DocumentaryProductionOperationKind.SceneComposition => h.AdapterRegistry.SceneCompositionOutcomes, DocumentaryProductionOperationKind.VariantComposition => h.AdapterRegistry.VariantCompositionOutcomes, _ => h.AdapterRegistry.VariantVerificationOutcomes };
}
