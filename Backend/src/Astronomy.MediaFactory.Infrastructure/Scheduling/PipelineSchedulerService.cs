using System.Collections.Concurrent;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Scheduling;

public sealed class PipelineSchedulerService : BackgroundService, IPipelineSchedulerService
{
    private readonly IOptionsMonitor<SchedulerOptions> _options;
    private readonly IPipelineRunQueue _queue;
    private readonly ISchedulerAuditStore _auditStore;
    private readonly IOptionsMonitor<OptimizationOptions> _optimizationOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PipelineSchedulerService> _logger;
    private readonly ConcurrentDictionary<string, bool> _scheduleEnabledOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);

    public PipelineSchedulerService(IOptionsMonitor<SchedulerOptions> options, IPipelineRunQueue queue, ISchedulerAuditStore auditStore, IOptionsMonitor<OptimizationOptions> optimizationOptions, IServiceScopeFactory scopeFactory, ILogger<PipelineSchedulerService> logger)
    {
        _options = options;
        _queue = queue;
        _auditStore = auditStore;
        _optimizationOptions = optimizationOptions;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStartupAsync(stoppingToken);

        if (_options.CurrentValue.RunOnStartup)
            await EvaluateSchedulesAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EvaluateSchedulesAsync(stoppingToken);
            await _queue.DrainAsync(stoppingToken);
        }
    }

    public async Task EvaluateSchedulesAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
            return;

        if (!await _evaluationGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            foreach (var schedule in GetEffectiveSchedules(options))
            {
                var scheduleEnabled = IsScheduleEnabled(schedule);
                var planned = GetPlannedRunUtc(schedule, nowUtc);
                _logger.LogInformation("Scheduler evaluated {ScheduleName}: enabled={Enabled}, targetDate={TargetDate}, plannedRunUtc={PlannedRunUtc}", schedule.Name, scheduleEnabled, planned.TargetDate, planned.PlannedRunUtc);

                if (!scheduleEnabled || planned.PlannedRunUtc > nowUtc)
                    continue;

                var optimized = await BuildOptimizedRequestAsync(options.DefaultContentType, schedule, planned.TargetDate, cancellationToken);
                await _queue.EnqueueAsync(new SchedulerRunQueueItem(schedule.Name, optimized.Request, planned.PlannedRunUtc, Force: false, optimized.Plan, optimized.OriginalRequest, optimized.AIProfile), cancellationToken);
            }
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    public async Task<SchedulerStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var nowUtc = DateTimeOffset.UtcNow;
        var schedules = GetEffectiveSchedules(options).Select(schedule =>
        {
            var next = GetNextPlannedRunUtc(schedule, nowUtc);
            return new SchedulerScheduleStatus(
                schedule.RegionId,
                schedule.Name,
                IsScheduleEnabled(schedule),
                schedule.LocationName,
                schedule.Latitude,
                schedule.Longitude,
                schedule.Timezone,
                schedule.LocalRunTime,
                schedule.PublishEnabled,
                next.PlannedRunUtc,
                next.TargetDate);
        }).ToList();

        return new SchedulerStatusResponse(
            options.Enabled,
            Math.Max(1, options.MaxConcurrentRuns),
            _queue.QueuedCount,
            _queue.ActiveCount,
            schedules,
            await _auditStore.GetRecentRunsAsync(50, cancellationToken));
    }

    public async Task<SchedulerRunResult> RunNowAsync(string scheduleName, bool force, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var schedule = FindSchedule(scheduleName);
        if (schedule is null)
            return new SchedulerRunResult(false, "NotFound", $"Schedule '{scheduleName}' was not found.", null, DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);

        var nowUtc = DateTimeOffset.UtcNow;
        var targetDate = GetLocalDate(schedule, nowUtc);
        var optimized = await BuildOptimizedRequestAsync(options.DefaultContentType, schedule, targetDate, cancellationToken);
        _logger.LogInformation("Scheduler run-now requested for {ScheduleName} on {TargetDate}; force={Force}", schedule.Name, targetDate, force);
        return await _queue.EnqueueAsync(new SchedulerRunQueueItem(schedule.Name, optimized.Request, nowUtc, force, optimized.Plan, optimized.OriginalRequest, optimized.AIProfile), cancellationToken);
    }

    public async Task<RegionStatusResponse> GetRegionsAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var options = _options.CurrentValue;
        var regions = GetRegionSchedules(options).Select(schedule =>
        {
            var next = GetNextPlannedRunUtc(schedule, DateTimeOffset.UtcNow);
            return new RegionScheduleStatus(
                schedule.RegionId ?? Slugify(schedule.LocationName),
                schedule.LocationName,
                schedule.Latitude,
                schedule.Longitude,
                schedule.Timezone,
                ResolveRegionLanguage(options, schedule.RegionId),
                schedule.LocalRunTime,
                IsScheduleEnabled(schedule),
                next.PlannedRunUtc,
                next.TargetDate);
        }).ToArray();

        return new RegionStatusResponse(options.Regions.Enabled, options.Regions.DefaultPublishPlatforms, regions);
    }

    public async Task<SchedulerRunResult> RunRegionNowAsync(string regionId, bool force, CancellationToken cancellationToken)
    {
        var schedule = FindRegionSchedule(regionId);
        if (schedule is null)
            return new SchedulerRunResult(false, "NotFound", $"Region '{regionId}' was not found.", null, DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);

        return await RunNowAsync(schedule.Name, force, cancellationToken);
    }

    public Task<bool> EnableRegionAsync(string regionId, CancellationToken cancellationToken)
    {
        var schedule = FindRegionSchedule(regionId);
        if (schedule is null)
            return Task.FromResult(false);

        _scheduleEnabledOverrides[schedule.Name] = true;
        _logger.LogInformation("Scheduler region {RegionId} enabled", regionId);
        return Task.FromResult(true);
    }

    public Task<bool> DisableRegionAsync(string regionId, CancellationToken cancellationToken)
    {
        var schedule = FindRegionSchedule(regionId);
        if (schedule is null)
            return Task.FromResult(false);

        _scheduleEnabledOverrides[schedule.Name] = false;
        _logger.LogInformation("Scheduler region {RegionId} disabled", regionId);
        return Task.FromResult(true);
    }

    public Task<bool> EnableScheduleAsync(string scheduleName, CancellationToken cancellationToken)
    {
        var schedule = FindSchedule(scheduleName);
        if (schedule is null)
            return Task.FromResult(false);

        _scheduleEnabledOverrides[schedule.Name] = true;
        _logger.LogInformation("Scheduler schedule {ScheduleName} enabled", schedule.Name);
        return Task.FromResult(true);
    }

    public Task<bool> DisableScheduleAsync(string scheduleName, CancellationToken cancellationToken)
    {
        var schedule = FindSchedule(scheduleName);
        if (schedule is null)
            return Task.FromResult(false);

        _scheduleEnabledOverrides[schedule.Name] = false;
        _logger.LogInformation("Scheduler schedule {ScheduleName} disabled", schedule.Name);
        return Task.FromResult(true);
    }

    public async Task RecoverStartupAsync(CancellationToken cancellationToken)
    {
        var runs = await _auditStore.GetRunsAsync(cancellationToken);
        var staleCutoff = DateTimeOffset.UtcNow.AddHours(-12);
        foreach (var run in runs.Where(x => x.Status == "Running" && x.ActualRunUtc < staleCutoff))
        {
            _logger.LogWarning("Scheduler-created run for {ScheduleName} on {TargetDate} is still marked Running since {ActualRunUtc}; leaving it non-duplicated for recovery/resume", run.ScheduleName, run.TargetDate, run.ActualRunUtc);
            await _auditStore.UpsertAsync(run with { Status = "Recoverable", SkipReason = "Detected as stale Running on API startup; not auto-duplicated.", UpdatedUtc = DateTimeOffset.UtcNow }, cancellationToken);
        }
    }

    private bool IsScheduleEnabled(SchedulerScheduleOptions schedule)
        => _scheduleEnabledOverrides.TryGetValue(schedule.Name, out var enabled) ? enabled : schedule.Enabled;

    private SchedulerScheduleOptions? FindSchedule(string scheduleName)
        => GetEffectiveSchedules(_options.CurrentValue).FirstOrDefault(x => x.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

    private SchedulerScheduleOptions? FindRegionSchedule(string regionId)
        => GetRegionSchedules(_options.CurrentValue).FirstOrDefault(x => (x.RegionId ?? "").Equals(regionId, StringComparison.OrdinalIgnoreCase));

    private static RunPipelineRequest BuildRequest(ContentType contentType, SchedulerScheduleOptions schedule, DateOnly targetDate)
        => new(
            targetDate,
            contentType,
            schedule.LocationName,
            schedule.Timezone,
            schedule.PublishEnabled,
            UseTopicPlanner: false,
            schedule.Latitude,
            schedule.Longitude,
            schedule.Timezone,
            schedule.LocationName,
            targetDate,
            schedule.RegionId);

    private async Task<(RunPipelineRequest Request, OptimizationPlan? Plan, RunPipelineRequest? OriginalRequest, AIOptimizationAppliedProfile? AIProfile)> BuildOptimizedRequestAsync(ContentType contentType, SchedulerScheduleOptions schedule, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var request = BuildRequest(contentType, schedule, targetDate);
        var optimization = _optimizationOptions.CurrentValue;
        using var scope = _scopeFactory.CreateScope();
        var aiProfile = await LoadApprovedAIProfileAsync(scope.ServiceProvider, cancellationToken);

        if (!optimization.Enabled || optimization.Mode == OptimizationMode.Disabled || !optimization.ApplyToSchedulerRunsOnly)
        {
            var aiOnlyPlan = aiProfile is null ? null : ApplyAIProfileToPlan(new OptimizationPlan { LocationName = schedule.LocationName, Platform = "YouTube" }, aiProfile);
            return (request, aiOnlyPlan, request, aiProfile);
        }

        var service = scope.ServiceProvider.GetRequiredService<IOptimizationService>();
        var plan = await service.BuildPlanAsync(schedule.LocationName, "YouTube", cancellationToken);
        plan = ApplyAIProfileToPlan(plan, aiProfile);
        var optimizedRequest = optimization.Mode == OptimizationMode.ApplySafeRules
            ? await service.ApplyPlanAsync(request, plan, cancellationToken)
            : request;
        return (optimizedRequest, plan, request, aiProfile);
    }

    private async Task<AIOptimizationAppliedProfile?> LoadApprovedAIProfileAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var service = serviceProvider.GetService<IAIOptimizationService>();
        return service is null ? null : await service.GetLatestApprovedProfileAsync(cancellationToken);
    }

    private static OptimizationPlan ApplyAIProfileToPlan(OptimizationPlan plan, AIOptimizationAppliedProfile? profile)
    {
        if (profile is null)
            return plan;

        var values = profile.AppliedValues;
        if (values.RecommendedPublishTimes.Count > 0)
            plan.RecommendedPublishTimeLocal = values.RecommendedPublishTimes.First();
        if (values.RecommendedObjectsToBoost.Count > 0)
            plan.PreferredContentObjects = values.RecommendedObjectsToBoost;
        if (values.RecommendedHooks.Count > 0)
            plan.RecommendedHookStyle = values.RecommendedHooks.First();
        if (values.RecommendedThumbnailText.Count > 0)
            plan.RecommendedThumbnailStyle = values.RecommendedThumbnailText.First();
        if (values.RecommendedHashtagSets.Count > 0)
            plan.RecommendedHashtags = values.RecommendedHashtagSets.First();

        plan.ConfidenceScore = Math.Max(plan.ConfidenceScore, profile.ConfidenceScore);
        plan.AppliedRules = plan.AppliedRules.Concat(["AIOptimizationApprovedProfile"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        plan.Reasons = plan.Reasons.Concat([$"Approved AI optimization profile {profile.Id} applied to scheduler-safe fields only."]).ToArray();
        return plan;
    }

    private static (DateOnly TargetDate, DateTimeOffset PlannedRunUtc) GetPlannedRunUtc(SchedulerScheduleOptions schedule, DateTimeOffset nowUtc)
    {
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, ResolveTimeZone(schedule.Timezone));
        var targetDate = DateOnly.FromDateTime(localNow.DateTime);
        return (targetDate, ToUtc(schedule, targetDate));
    }

    private static IReadOnlyCollection<SchedulerScheduleOptions> GetEffectiveSchedules(SchedulerOptions options)
        => options.Regions.Enabled ? GetRegionSchedules(options) : options.Schedules;

    private static IReadOnlyCollection<SchedulerScheduleOptions> GetRegionSchedules(SchedulerOptions options)
        => options.Regions.Items.Select(region => ToSchedule(region, options.Regions.DefaultPublishPlatforms)).ToArray();

    private static SchedulerScheduleOptions ToSchedule(RegionScheduleOptions region, IReadOnlyCollection<string> defaultPublishPlatforms)
        => new()
        {
            RegionId = Slugify(region.RegionId),
            Name = string.IsNullOrWhiteSpace(region.DisplayName) ? region.RegionId : region.DisplayName,
            Enabled = region.Enabled,
            LocationName = region.DisplayName,
            Latitude = region.Latitude,
            Longitude = region.Longitude,
            Timezone = region.Timezone,
            LocalRunTime = region.LocalRunTime,
            PublishEnabled = defaultPublishPlatforms.Contains("YouTube", StringComparer.OrdinalIgnoreCase)
        };

    private static string ResolveRegionLanguage(SchedulerOptions options, string? regionId)
        => options.Regions.Items.FirstOrDefault(x => Slugify(x.RegionId).Equals(regionId, StringComparison.OrdinalIgnoreCase))?.Language ?? "en";

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "default-region";
    }

    private static (DateOnly TargetDate, DateTimeOffset PlannedRunUtc) GetNextPlannedRunUtc(SchedulerScheduleOptions schedule, DateTimeOffset nowUtc)
    {
        var planned = GetPlannedRunUtc(schedule, nowUtc);
        if (planned.PlannedRunUtc <= nowUtc)
        {
            var nextDate = planned.TargetDate.AddDays(1);
            return (nextDate, ToUtc(schedule, nextDate));
        }

        return planned;
    }

    private static DateOnly GetLocalDate(SchedulerScheduleOptions schedule, DateTimeOffset nowUtc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, ResolveTimeZone(schedule.Timezone)).DateTime);

    private static DateTimeOffset ToUtc(SchedulerScheduleOptions schedule, DateOnly targetDate)
    {
        if (!TimeOnly.TryParse(schedule.LocalRunTime, out var localRunTime))
            throw new InvalidOperationException($"Scheduler schedule '{schedule.Name}' has invalid LocalRunTime '{schedule.LocalRunTime}'.");

        var localDateTime = targetDate.ToDateTime(localRunTime, DateTimeKind.Unspecified);
        var timezone = ResolveTimeZone(schedule.Timezone);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDateTime, timezone), TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
        => TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
}
