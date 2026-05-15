using System.Diagnostics;
using Azure;
using Google;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Publishing;

internal static class TransientRetryHelper
{
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        ILogger logger,
        string operationName,
        string targetName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logger);

        var attempts = Math.Clamp(maxAttempts, 1, 5);
        var normalizedBaseDelay = NormalizeDelay(baseDelay);
        var normalizedMaxDelay = NormalizeDelay(maxDelay, normalizedBaseDelay);
        var correlationId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "n/a";

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (attempt < attempts && IsTransient(ex, cancellationToken))
            {
                var delay = GetRetryDelay(ex, attempt, normalizedBaseDelay, normalizedMaxDelay);
                logger.LogWarning(
                    ex,
                    "Transient failure during {OperationName} for {TargetName}. Retrying attempt {Attempt}/{MaxAttempts} after {DelayMs} ms. CorrelationId: {CorrelationId}",
                    operationName,
                    targetName,
                    attempt + 1,
                    attempts,
                    delay.TotalMilliseconds,
                    correlationId);

                await Task.Delay(delay, cancellationToken);
            }
        }

        return await operation(cancellationToken);
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
        => exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested => false,
            TimeoutException => true,
            IOException => true,
            HttpRequestException => true,
            TaskCanceledException => true,
            YouTubeThumbnailUploadException thumbnailUploadException => IsThumbnailUploadFailureTransient(thumbnailUploadException, cancellationToken),
            InvalidOperationException invalidOperationException when IsIncompleteUploadFailure(invalidOperationException) => IsIncompleteUploadFailureTransient(invalidOperationException, cancellationToken),
            RequestFailedException requestFailedException => requestFailedException.Status is 408 or 429 or 500 or 502 or 503 or 504,
            GoogleApiException googleApiException => (int?)googleApiException.HttpStatusCode is 408 or 429 or 500 or 502 or 503 or 504,
            _ => false
        };

    private static bool IsThumbnailUploadFailureTransient(YouTubeThumbnailUploadException exception, CancellationToken cancellationToken)
        => !YouTubeThumbnailUploadFailureClassifier.IsPermanentPermissionFailure(exception)
            && IsIncompleteUploadFailure(exception)
            && IsIncompleteUploadFailureTransient(exception, cancellationToken);

    private static bool IsIncompleteUploadFailure(InvalidOperationException exception)
        => exception.Message.Contains("did not complete successfully", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("Status: Failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncompleteUploadFailureTransient(InvalidOperationException exception, CancellationToken cancellationToken)
    {
        if (exception.InnerException is null)
        {
            return true;
        }

        return IsTransient(exception.InnerException, cancellationToken);
    }

    private static TimeSpan GetRetryDelay(Exception exception, int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var retryAfter = TryGetRetryAfter(exception);
        var exponentialMs = Math.Min(
            maxDelay.TotalMilliseconds,
            baseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        var computedDelay = TimeSpan.FromMilliseconds(Math.Max(baseDelay.TotalMilliseconds, exponentialMs));

        return retryAfter.HasValue && retryAfter.Value > computedDelay
            ? retryAfter.Value
            : computedDelay;
    }

    private static TimeSpan? TryGetRetryAfter(Exception exception)
    {
        if (exception.Data.Contains("RetryAfter") && exception.Data["RetryAfter"] is TimeSpan explicitDelay && explicitDelay > TimeSpan.Zero)
        {
            return explicitDelay;
        }

        return null;
    }

    private static TimeSpan NormalizeDelay(TimeSpan delay, TimeSpan? minimum = null)
    {
        var normalized = delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
        if (minimum.HasValue && normalized < minimum.Value)
        {
            normalized = minimum.Value;
        }

        return normalized;
    }
}
