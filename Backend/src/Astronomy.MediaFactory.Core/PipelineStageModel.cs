using Astronomy.MediaFactory.Contracts;
using System.Net;

namespace Astronomy.MediaFactory.Core;

public static class PipelineStageNames
{
    public const string Created = nameof(Created);
    public const string ObservationWindowCompleted = nameof(ObservationWindowCompleted);
    public const string SkyfieldCompleted = nameof(SkyfieldCompleted);
    public const string SceneContextCompleted = nameof(SceneContextCompleted);
    public const string NarrationCompleted = nameof(NarrationCompleted);
    public const string SpeechCompleted = nameof(SpeechCompleted);
    public const string StellariumCompleted = nameof(StellariumCompleted);
    public const string RenderingCompleted = nameof(RenderingCompleted);
    public const string ThumbnailCompleted = nameof(ThumbnailCompleted);
    public const string SeoCompleted = nameof(SeoCompleted);
    public const string ValidationCompleted = nameof(ValidationCompleted);
    public const string YouTubeLongPublished = nameof(YouTubeLongPublished);
    public const string YouTubeShortPublished = nameof(YouTubeShortPublished);
    public const string FacebookReelPublished = nameof(FacebookReelPublished);
    public const string InstagramReelPublished = nameof(InstagramReelPublished);
    public const string Completed = nameof(Completed);
    public const string Failed = nameof(Failed);

    public static readonly string[] All =
    [
        Created,
        ObservationWindowCompleted,
        SkyfieldCompleted,
        SceneContextCompleted,
        NarrationCompleted,
        SpeechCompleted,
        StellariumCompleted,
        RenderingCompleted,
        ThumbnailCompleted,
        SeoCompleted,
        ValidationCompleted,
        YouTubeLongPublished,
        YouTubeShortPublished,
        FacebookReelPublished,
        InstagramReelPublished,
        Completed,
        Failed
    ];
}

public static class PersistentStageStatuses
{
    public const string Pending = nameof(Pending);
    public const string Running = nameof(Running);
    public const string Succeeded = nameof(Succeeded);
    public const string Failed = nameof(Failed);
    public const string Skipped = nameof(Skipped);
}

public sealed class StageExecutionOptions
{
    public int MaxAttempts { get; init; } = 3;
    public int RetryDelaySeconds { get; init; } = 2;
    public double RetryBackoffMultiplier { get; init; } = 2;
    public Func<Exception, bool>? IsRetryableExceptionFunc { get; init; }
    public bool AllowSkipIfAlreadySucceeded { get; init; } = true;
    public string? OutputPath { get; init; }
    public string? DiagnosticPath { get; init; }
}

public interface IPipelineStageExecutor
{
    Task<T> ExecuteStageAsync<T>(
        Guid pipelineRunId,
        string stageName,
        Func<CancellationToken, Task<T>> action,
        StageExecutionOptions options,
        CancellationToken cancellationToken);
}

public static class PipelineRetryClassifier
{
    public static bool IsRetryable(Exception exception, string? outputPath = null)
    {
        if (exception is TimeoutException or TaskCanceledException)
            return string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath);

        if (exception is HttpRequestException http)
            return http.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

        var message = exception.Message ?? string.Empty;
        if (ContainsAny(message, "invalid credentials", "missing permissions", "validation failed", "file not found", "bad request", "scene mismatch", "invalid config"))
            return false;

        return ContainsAny(message,
            "network timeout",
            "http 429",
            "http 500",
            "http 502",
            "http 503",
            "http 504",
            "transient upload",
            "meta processing timeout",
            "youtube transient upload",
            "azure transient");
    }

    public static bool IsNonRetryable(Exception exception)
        => !IsRetryable(exception);

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}

public sealed record PipelineStageStatusDto(
    string StageName,
    string Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? LastError,
    string? OutputPath,
    string? DiagnosticPath);

public sealed record PipelineStatusResponse(
    Guid RunId,
    PipelineRunStatus RunStatus,
    IReadOnlyCollection<PipelineStageStatusDto> Stages,
    IReadOnlyCollection<string> PublishedUrls,
    string? FailedStage,
    string? LastError,
    string? OutputFolder,
    IReadOnlyCollection<string> Warnings);
