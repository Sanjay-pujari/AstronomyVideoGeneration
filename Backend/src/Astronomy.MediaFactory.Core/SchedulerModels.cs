using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public sealed record SchedulerRunRecord(
    string? RegionId,
    string ScheduleName,
    ContentType ContentType,
    DateOnly TargetDate,
    DateTimeOffset PlannedRunUtc,
    DateTimeOffset? ActualRunUtc,
    Guid? PipelineRunId,
    string Status,
    string? SkipReason,
    string LocationName,
    string TimeZone,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? EventId = null,
    string? EventType = null,
    string? EventTitle = null);

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
    string? RegionId,
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

public sealed record RegionStatusResponse(
    bool Enabled,
    IReadOnlyCollection<string> DefaultPublishPlatforms,
    IReadOnlyCollection<RegionScheduleStatus> Items);

public sealed record RegionScheduleStatus(
    string RegionId,
    string DisplayName,
    double Latitude,
    double Longitude,
    string Timezone,
    string Language,
    string LocalRunTime,
    bool Enabled,
    DateTimeOffset? NextPlannedRunUtc,
    DateOnly? NextTargetDate);

public sealed record RegionBreakdownItem(
    string RegionId,
    string LocationName,
    int Runs,
    long Views,
    int Failures);
