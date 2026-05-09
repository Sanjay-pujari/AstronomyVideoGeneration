using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record SchedulerRunRecord(
    string ScheduleName,
    DateOnly TargetDate,
    DateTimeOffset PlannedRunUtc,
    DateTimeOffset? ActualRunUtc,
    Guid? PipelineRunId,
    string Status,
    string? SkipReason,
    string LocationName,
    string TimeZone,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SchedulerRunQueueItem(
    string ScheduleName,
    RunPipelineRequest Request,
    DateTimeOffset PlannedRunUtc,
    bool Force,
    OptimizationPlan? OptimizationPlan = null,
    RunPipelineRequest? OriginalRequest = null,
    AIOptimizationAppliedProfile? AIOptimizationProfile = null);

public sealed record SchedulerRunResult(
    bool Accepted,
    string Status,
    string? Reason,
    Guid? PipelineRunId,
    DateOnly TargetDate,
    DateTimeOffset PlannedRunUtc);

public sealed record SchedulerStatusResponse(
    bool Enabled,
    int MaxConcurrentRuns,
    int QueuedRuns,
    int ActiveRuns,
    IReadOnlyCollection<SchedulerScheduleStatus> Schedules,
    IReadOnlyCollection<SchedulerRunRecord> RecentRuns);

public sealed record SchedulerScheduleStatus(
    string Name,
    bool Enabled,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    string LocalRunTime,
    bool PublishEnabled,
    DateTimeOffset? NextPlannedRunUtc,
    DateOnly? NextTargetDate);
