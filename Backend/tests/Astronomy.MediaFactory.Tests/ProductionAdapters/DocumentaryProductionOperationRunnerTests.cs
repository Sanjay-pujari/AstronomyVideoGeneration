using Astronomy.MediaFactory.ProductionAdapters;
using FluentAssertions;

namespace Astronomy.MediaFactory.Tests.ProductionAdapters;

public sealed class DocumentaryProductionOperationRunnerTests
{
 [Fact]
 public async Task Retryable_failure_is_retried_and_identity_is_preserved()
 {
  var attempts=new CapturingAttempts();var runner=new DocumentaryProductionOperationRunner(attempts,new DocumentaryProductionFailureNormalizer());var calls=0;
  var result=await runner.ExecuteAsync(Request(maximumAttempts:2),(_,_)=>Task.FromResult(++calls==1?new Value(false,new(DocumentaryProductionFailureCode.ProviderTimeout,"timeout",true)):new Value(true,null)),x=>x.Success,x=>x.Failure,default);
  result.Succeeded.Should().BeTrue();result.AttemptCount.Should().Be(2);attempts.Values.Select(x=>x.AttemptNumber).Should().Equal(1,2);attempts.Values.Select(x=>x.ExecutionId).Should().OnlyContain(x=>x=="execution");
 }

 [Fact]
 public async Task Non_retryable_failure_is_not_retried()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());var calls=0;
  var result=await runner.ExecuteAsync(Request(maximumAttempts:3),(_,_)=>{calls++;return Task.FromResult(new Value(false,new(DocumentaryProductionFailureCode.ProviderRejectedRequest,"bad",true)));},x=>x.Success,x=>x.Failure,default);
  result.Succeeded.Should().BeFalse();calls.Should().Be(1);
 }

 [Fact]
 public async Task Host_timeout_is_enforced_and_normalized()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());
  var result=await runner.ExecuteAsync(Request(TimeSpan.FromMilliseconds(20)),async(_,token)=>{await Task.Delay(Timeout.InfiniteTimeSpan,token);return new Value(true,null);},x=>x.Success,x=>x.Failure,default);
  result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.ProviderTimeout);
 }

 [Fact]
 public async Task Process_timeout_uses_process_failure_code()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());
  var result=await runner.ExecuteAsync(Request(TimeSpan.FromMilliseconds(20),DocumentaryProductionOperationKind.SceneComposition),async(_,token)=>{await Task.Delay(Timeout.InfiniteTimeSpan,token);return new Value(true,null);},x=>x.Success,x=>x.Failure,default);
  result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.ProcessTimedOut);
 }

 [Fact]
 public async Task Caller_cancellation_is_not_converted_to_host_timeout()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());using var cancellation=new CancellationTokenSource(20);
  var action=()=>runner.ExecuteAsync(Request(TimeSpan.FromSeconds(10)),async(_,token)=>{await Task.Delay(Timeout.InfiniteTimeSpan,token);return new Value(true,null);},x=>x.Success,x=>x.Failure,cancellation.Token);
  await action.Should().ThrowAsync<OperationCanceledException>();
 }

 [Fact]
 public async Task Unexpected_provider_exception_is_normalized()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());
  var result=await runner.ExecuteAsync<Value>(Request(),(_,_)=>throw new TimeoutException("private provider detail"),x=>x.Success,x=>x.Failure,default);
  result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.ProviderTimeout);
 }

 [Fact]
 public async Task Unexpected_composition_exception_is_normalized()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());
  var result=await runner.ExecuteAsync<Value>(Request(kind:DocumentaryProductionOperationKind.SceneComposition),(_,_)=>throw new IOException("private path"),x=>x.Success,x=>x.Failure,default);
  result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.FileSystemFailure);
 }

 [Fact]
 public async Task Private_adapter_exception_message_is_not_exposed()
 {
  const string secret="customer-secret-provider-message";
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());
  var result=await runner.ExecuteAsync<Value>(Request(),(_,_)=>throw new InvalidOperationException(secret),x=>x.Success,x=>x.Failure,default);
  result.Failure!.Code.Should().Be(DocumentaryProductionFailureCode.ProviderRejectedRequest);
  result.Failure.Message.Should().NotContain(secret);
 }

 [Fact]
 public async Task Caller_cancellation_is_not_normalized()
 {
  var runner=new DocumentaryProductionOperationRunner(new CapturingAttempts(),new DocumentaryProductionFailureNormalizer());using var cancellation=new CancellationTokenSource();cancellation.Cancel();
  var action=()=>runner.ExecuteAsync<Value>(Request(),(_,_)=>throw new OperationCanceledException(cancellation.Token),x=>x.Success,x=>x.Failure,cancellation.Token);
  await action.Should().ThrowAsync<OperationCanceledException>();
 }

 private static DocumentaryProductionOperationExecutionRequest Request(TimeSpan? timeout=null,DocumentaryProductionOperationKind kind=DocumentaryProductionOperationKind.VisualGeneration,int maximumAttempts=1)=>new(new("execution","correlation",DocumentaryProductionExecutionMode.Certified,"/tmp",DateTimeOffset.UnixEpoch,new Dictionary<string,string>()),kind,"asset","provider",timeout??TimeSpan.FromSeconds(1),maximumAttempts,"variant","scene");
 private sealed record Value(bool Success,DocumentaryProductionFailure? Failure);
 private sealed class CapturingAttempts:IDocumentaryProductionAttemptContextFactory { public List<DocumentaryProductionAttemptContext> Values{get;}=[];public DocumentaryProductionAttemptContext Create(DocumentaryProductionExecutionContext e,DocumentaryProductionOperationKind k,string asset,string provider,int attempt,TimeSpan timeout,string? variantId=null,string? sceneId=null){var value=new DocumentaryProductionAttemptContext(e.ExecutionId,e.CorrelationId,k,asset,variantId,sceneId,attempt,provider,DateTimeOffset.UnixEpoch,timeout);Values.Add(value);return value;} }
}
