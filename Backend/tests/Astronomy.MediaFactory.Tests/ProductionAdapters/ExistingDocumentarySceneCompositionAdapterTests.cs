using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;
namespace Astronomy.MediaFactory.Tests.ProductionAdapters;
public sealed class ExistingDocumentarySceneCompositionAdapterTests
{
 [Theory][InlineData(typeof(TimeoutException),DocumentaryProductionFailureCode.ProviderTimeout)][InlineData(typeof(IOException),DocumentaryProductionFailureCode.FileSystemFailure)][InlineData(typeof(InvalidOperationException),DocumentaryProductionFailureCode.ProviderRejectedRequest)]
 public void Provider_exceptions_have_stable_safe_normalization(Type exceptionType,DocumentaryProductionFailureCode expected){var exception=(Exception)Activator.CreateInstance(exceptionType)!;var result=new DocumentaryProductionFailureNormalizer().Normalize(exception,DocumentaryProductionOperationKind.SceneComposition,false);result.Code.Should().Be(expected);result.Message.Should().NotContain(exception.Message);}
 [Fact] public async Task Provider_cancellation_propagates(){using var cts=new CancellationTokenSource();var fake=new FakeSceneCompositionProviderBinding{WaitForCancellation=true};var task=fake.ComposeAsync(DocumentarySceneCompositionTestFixtures.ProviderRequest(Path.GetTempPath()),cts.Token);cts.Cancel();await FluentActions.Awaiting(()=>task).Should().ThrowAsync<OperationCanceledException>();fake.InvocationCount.Should().Be(1);fake.CapturedCancellationToken.Should().Be(cts.Token);}
 [Fact] public async Task Inspector_cancellation_propagates(){using var cts=new CancellationTokenSource();var fake=new CancellingSceneVideoInspector();var task=fake.InspectAsync("scene.mp4",cts.Token);cts.Cancel();await FluentActions.Awaiting(()=>task).Should().ThrowAsync<OperationCanceledException>();fake.InvocationCount.Should().Be(1);}
}
