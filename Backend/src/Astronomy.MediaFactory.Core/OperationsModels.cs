namespace Astronomy.MediaFactory.Core;

public sealed record PipelineOpsSummary(
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    double AverageDurationMs,
    string? MostCommonFailingStage,
    IReadOnlyCollection<PublishedVideoStatusSnapshot> LatestPublishResults,
    QueueHealthSnapshot LatestQueueHealth,
    IReadOnlyCollection<SlowStageSnapshot> SlowStages);

public sealed record PublishedVideoStatusSnapshot(string Title, string Status, DateTimeOffset CreatedAt, string? YouTubeVideoId);
public sealed record QueueHealthSnapshot(int PendingJobs, int RunningJobs, int RetryingJobs, int FailedJobs);
public sealed record SlowStageSnapshot(Guid PipelineRunId, string StageName, long DurationMs, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

public sealed record RecentFailuresSnapshot(
    IReadOnlyCollection<PipelineRun> FailedPipelineRuns,
    IReadOnlyCollection<PipelineJob> FailedJobs,
    IReadOnlyCollection<PipelineStageExecution> FailedStages,
    IReadOnlyCollection<StageFailureDigest> LatestErrorByStage);

public sealed record StageFailureDigest(string StageName, string ErrorMessage, DateTimeOffset OccurredAt, Guid PipelineRunId);
public sealed record JobOpsSummary(int TotalJobs, int PendingJobs, int RunningJobs, int RetryingJobs, int FailedJobs, int SucceededJobs);
