namespace Astronomy.MediaFactory.ProductionAdapters;

/// <summary>Applies the host's timeout, cancellation, and retry policy uniformly to adapter calls.</summary>
public sealed class DocumentaryProductionOperationRunner(
 IDocumentaryProductionAttemptContextFactory attempts,
 IDocumentaryProductionFailureNormalizer failures) : IDocumentaryProductionOperationRunner
{
 private static readonly HashSet<DocumentaryProductionFailureCode> NeverRetry =
 [
  DocumentaryProductionFailureCode.ProviderRejectedRequest,
  DocumentaryProductionFailureCode.SourceArtifactInvalid,
  DocumentaryProductionFailureCode.OutputFormatInvalid,
  DocumentaryProductionFailureCode.DimensionMismatch,
  DocumentaryProductionFailureCode.AudioStreamMissing,
  DocumentaryProductionFailureCode.VideoStreamMissing,
  DocumentaryProductionFailureCode.SubtitleMissing,
  DocumentaryProductionFailureCode.Cancelled
 ];

 public async Task<DocumentaryProductionOperationExecutionResult<T>> ExecuteAsync<T>(
  DocumentaryProductionOperationExecutionRequest request,
  Func<DocumentaryProductionAttemptContext,CancellationToken,Task<T>> operation,
  Func<T,bool> succeeded,
  Func<T,DocumentaryProductionFailure?> failureSelector,
  CancellationToken callerToken)
 {
  ArgumentNullException.ThrowIfNull(request);
  ArgumentNullException.ThrowIfNull(operation);
  if (request.MaximumAttempts is < 1 or > 10 || request.Timeout <= TimeSpan.Zero)
   throw new ArgumentOutOfRangeException(nameof(request));

  T? last = default;
  DocumentaryProductionFailure? failure = null;
  for (var attempt = 1; attempt <= request.MaximumAttempts; attempt++)
  {
   callerToken.ThrowIfCancellationRequested();
   var context = attempts.Create(request.ExecutionContext, request.OperationKind, request.AssetId,
    request.ProviderId, attempt, request.Timeout, request.VariantId, request.SceneId);
   using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
   timeout.CancelAfter(request.Timeout);
   try
   {
    last = await operation(context, timeout.Token);
    if (succeeded(last)) return new(last, null, attempt);
    failure = failureSelector(last) ?? new(DocumentaryProductionFailureCode.ProviderInvalidResponse,
     "The adapter returned an unsuccessful result without failure evidence.");
   }
   catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
   {
    failure = failures.Normalize(new OperationCanceledException(), request.OperationKind, false);
   }
   catch (OperationCanceledException) { throw; }
   catch (Exception exception)
   {
    failure = failures.Normalize(exception, request.OperationKind, callerCancelled: false);
   }

   if (!failure.Retryable || NeverRetry.Contains(failure.Code) || attempt == request.MaximumAttempts)
    return new(last, failure, attempt);
  }
  return new(last, failure, request.MaximumAttempts);
 }
}
