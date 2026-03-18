using Astronomy.MediaFactory.Contracts;

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

public sealed record StageAlertContext(
    Guid PipelineRunId,
    string StageName,
    string Status,
    long? DurationMs,
    string? ErrorMessage,
    string? MetadataJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public enum RecoveryOperationType
{
    ReplayPipeline = 1,
    RetryPublish = 2,
    RetryArchive = 3,
    RegenerateShorts = 4,
    RerunMetadataOptimization = 5,
    RequeueJob = 6,
    RecoverStaleJobs = 7,
    CleanupRetention = 8
}

public enum RecoveryOperationStatus
{
    Requested = 1,
    Completed = 2,
    Rejected = 3,
    Failed = 4
}

public enum ReplayBehavior
{
    FailedOrIncompleteRunsOnly = 1,
    AllowCompletedRuns = 2
}

public sealed record ReplayPipelineRequest(
    string RequestedBy,
    string? Notes = null,
    bool AllowReplayOfSucceededRun = false,
    bool PublishToYouTubeOverride = false,
    bool UseTopicPlannerOverride = false,
    ReplayBehavior ReplayBehavior = ReplayBehavior.FailedOrIncompleteRunsOnly);

public sealed record RetryPublishRequest(
    string RequestedBy,
    string? Notes = null,
    bool RetryThumbnailOnly = false,
    bool ForceRepublish = false,
    bool PublishToYouTube = true);

public sealed record RetryArchiveRequest(
    string RequestedBy,
    string? Notes = null,
    bool Force = false);

public sealed record RegenerateShortsRequest(
    string RequestedBy,
    string? Notes = null,
    bool PublishToYouTube = false,
    bool Force = false);

public sealed record RerunMetadataOptimizationRequest(
    string RequestedBy,
    string? Notes = null,
    bool ApplyToPublishedVideo = true);

public sealed record RequeueJobRequest(
    string RequestedBy,
    string? Notes = null,
    bool Force = false);

public sealed record RecoverStaleJobsRequest(
    string RequestedBy,
    string? Notes = null,
    int? ThresholdMinutes = null,
    bool RequeueRecoveredJobs = true,
    bool RecoverIncompleteRuns = true);

public sealed record CleanupMaintenanceRequest(
    string RequestedBy,
    string? Notes = null,
    bool DeleteWorkingFiles = true,
    bool DeleteDbRecords = true,
    bool DeleteAnalytics = false);

public sealed record OpsActionResult(Guid RecoveryOperationId, string Message, IReadOnlyCollection<Guid>? AffectedIds = null);
public sealed record StaleJobRecoverySummary(Guid RecoveryOperationId, int MarkedStaleJobs, int RequeuedJobs, int IncompleteRunsRecovered, IReadOnlyCollection<Guid> AffectedJobIds, IReadOnlyCollection<Guid> AffectedRunIds);
public sealed record MaintenanceCleanupSummary(Guid RecoveryOperationId, int DeletedStageRecords, int DeletedJobRecords, int DeletedAnalyticsRecords, int DeletedWorkingFiles, IReadOnlyCollection<string> DeletedPaths);

public sealed record ReplayEligibility(bool CanReplay, string? RejectionReason)
{
    public static ReplayEligibility Allowed() => new(true, null);
    public static ReplayEligibility Rejected(string reason) => new(false, reason);
}

public static class RecoveryRequestValidator
{
    public static string? Validate(ReplayPipelineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return "RequestedBy is required.";

        return null;
    }

    public static ReplayEligibility GetReplayEligibility(PipelineRun run, ReplayPipelineRequest request)
    {
        if (run.Status == PipelineRunStatus.Running)
            return ReplayEligibility.Rejected("Cannot replay a run that is currently running.");

        if (run.Status == PipelineRunStatus.Succeeded
            && !AllowsCompletedRunReplay(request))
        {
            return ReplayEligibility.Rejected("Replay of a successful run requires ReplayBehavior=AllowCompletedRuns to avoid duplicate content.");
        }

        return ReplayEligibility.Allowed();
    }

    public static bool AllowsCompletedRunReplay(ReplayPipelineRequest request)
        => request.AllowReplayOfSucceededRun || request.ReplayBehavior == ReplayBehavior.AllowCompletedRuns;

    public static string? Validate(RetryPublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return "RequestedBy is required.";

        if (request.RetryThumbnailOnly && request.ForceRepublish)
            return "RetryThumbnailOnly cannot be combined with ForceRepublish.";

        if (!request.PublishToYouTube)
            return "Retry publish operations must target YouTube publishing explicitly.";

        return null;
    }

    public static string? Validate(RetryArchiveRequest request)
        => string.IsNullOrWhiteSpace(request.RequestedBy)
            ? "RequestedBy is required."
            : null;

    public static string? Validate(RegenerateShortsRequest request)
        => string.IsNullOrWhiteSpace(request.RequestedBy)
            ? "RequestedBy is required."
            : null;

    public static string? Validate(RerunMetadataOptimizationRequest request)
        => string.IsNullOrWhiteSpace(request.RequestedBy)
            ? "RequestedBy is required."
            : null;

    public static string? Validate(RequeueJobRequest request)
        => string.IsNullOrWhiteSpace(request.RequestedBy)
            ? "RequestedBy is required."
            : null;

    public static string? Validate(RecoverStaleJobsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return "RequestedBy is required.";

        if (request.ThresholdMinutes.HasValue && request.ThresholdMinutes.Value <= 0)
            return "ThresholdMinutes must be greater than zero when specified.";

        return null;
    }

    public static string? Validate(CleanupMaintenanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            return "RequestedBy is required.";

        if (!request.DeleteWorkingFiles && !request.DeleteDbRecords && !request.DeleteAnalytics)
            return "At least one cleanup target must be enabled.";

        return null;
    }
}
