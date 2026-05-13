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
    private readonly IOptionsMonitor<AstronomyEventsOptions> _astronomyEventsOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PipelineSchedulerService> _logger;
    private readonly ConcurrentDictionary<string, bool> _scheduleEnabledOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);

    public PipelineSchedulerService(IOptionsMonitor<SchedulerOptions> options, IPipelineRunQueue queue, ISchedulerAuditStore auditStore, IOptionsMonitor<OptimizationOptions> optimizationOptions, IOptionsMonitor<AstronomyEventsOptions> astronomyEventsOptions, IServiceScopeFactory scopeFactory, ILogger<PipelineSchedulerService> logger)
    {
        _options = options;
        _queue = queue;
        _auditStore = auditStore;
        _optimizationOptions = optimizationOptions;
        _astronomyEventsOptions = astronomyEventsOptions;
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
                await EnqueuePlannedRunsAsync(schedule, planned.TargetDate, planned.PlannedRunUtc, false, optimized, cancellationToken);
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
        return await EnqueuePlannedRunsAsync(schedule, targetDate, nowUtc, force, optimized, cancellationToken);
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


    public async Task<SchedulerEventPlanResponse> GetEventPlanAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var schedule = FindRegionSchedule(regionId)
            ?? GetEffectiveSchedules(_options.CurrentValue).FirstOrDefault(x => string.Equals(Slugify(x.RegionId ?? x.LocationName), Slugify(regionId), StringComparison.OrdinalIgnoreCase));
        if (schedule is null)
        {
            return new SchedulerEventPlanResponse(Slugify(regionId), targetDate, false, [], [], [$"Region '{regionId}' was not found."]);
        }

        return await BuildEventPlanAsync(schedule, targetDate, cancellationToken);
    }

    private async Task<SchedulerRunResult> EnqueuePlannedRunsAsync(
        SchedulerScheduleOptions schedule,
        DateOnly targetDate,
        DateTimeOffset plannedRunUtc,
        bool force,
        (RunPipelineRequest Request, OptimizationPlan? Plan, RunPipelineRequest? OriginalRequest, AIOptimizationAppliedProfile? AIProfile) optimized,
        CancellationToken cancellationToken)
    {
        var eventPlan = await BuildEventPlanAsync(schedule, targetDate, cancellationToken);
        var dailyItem = new SchedulerRunQueueItem(schedule.Name, optimized.Request, plannedRunUtc, force, optimized.Plan, optimized.OriginalRequest, optimized.AIProfile, eventPlan);
        var eventItems = eventPlan.SpecialEventsPlanned
            .Select(plannedEvent =>
            {
                var astronomyEvent = _lastPlannedEvents.TryGetValue(plannedEvent.EventId, out var cached) ? cached : null;
                var request = BuildSpecialEventRequest(schedule, targetDate, plannedEvent, astronomyEvent);
                return new SchedulerRunQueueItem($"{schedule.Name} special event", request, plannedRunUtc, force, EventPlan: eventPlan);
            })
            .ToArray();

        SchedulerRunResult dailyResult;
        if (_astronomyEventsOptions.CurrentValue.RunSpecialEventsBeforeDailyGuide)
        {
            foreach (var item in eventItems)
                await _queue.EnqueueAsync(item, cancellationToken);
            dailyResult = await _queue.EnqueueAsync(dailyItem, cancellationToken);
        }
        else
        {
            dailyResult = await _queue.EnqueueAsync(dailyItem, cancellationToken);
            foreach (var item in eventItems)
                await _queue.EnqueueAsync(item, cancellationToken);
        }

        return dailyResult;
    }

    private readonly ConcurrentDictionary<string, AstronomyEvent> _lastPlannedEvents = new(StringComparer.OrdinalIgnoreCase);

    private async Task<SchedulerEventPlanResponse> BuildEventPlanAsync(SchedulerScheduleOptions schedule, DateOnly targetDate, CancellationToken cancellationToken)
    {
        var eventOptions = _astronomyEventsOptions.CurrentValue;
        var regionId = Slugify(schedule.RegionId ?? schedule.LocationName);
        var reasons = new List<string> { $"DailySkyGuide planned for {regionId} on {targetDate:yyyy-MM-dd}." };
        var planned = new List<SchedulerSpecialEventPlanItem>();
        var skipped = new List<SchedulerSkippedEventPlanItem>();
        EventContentDecision decision;
        var candidateEvents = Array.Empty<AstronomyEvent>();
        var originalRegionByEventId = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<IAstronomyEventDiscoveryService>();
            await discovery.RefreshEventsAsync(targetDate, targetDate.AddDays(eventOptions.LookAheadDays), cancellationToken);
            var discoveredEvents = (await discovery.DiscoverEventsForRegionAsync(regionId, targetDate, cancellationToken)).ToArray();
            originalRegionByEventId = discoveredEvents.ToDictionary(e => e.EventId, e => e.RegionId, StringComparer.OrdinalIgnoreCase);
            candidateEvents = discoveredEvents
                .Select(e => ApplyRegionContext(e, schedule, regionId))
                .OrderByDescending(e => e.ContentOpportunityScore)
                .ThenBy(e => e.PeakUtc ?? e.StartUtc)
                .ToArray();

            decision = BuildDecisionFromCandidates(candidateEvents, eventOptions, regionId, targetDate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event planning failed for {RegionId} on {TargetDate}; scheduling DailySkyGuide only.", regionId, targetDate);
            decision = new EventContentDecision { DecisionType = "None", Reason = $"Event planning failed: {ex.Message}" };
        }

        reasons.Add(decision.Reason);
        foreach (var astronomyEvent in decision.SpecialEventCandidates)
        {
            var duplicate = await HasSpecialEventDuplicateAsync(astronomyEvent.EventId, targetDate, regionId, ContentType.SpecialEventGuide, cancellationToken);
            if (duplicate)
            {
                skipped.Add(ToSkipped(astronomyEvent, "Duplicate event video already exists for eventId + targetDate + regionId + contentType."));
                continue;
            }

            if (planned.Count >= eventOptions.MaxSpecialEventVideosPerDay)
            {
                skipped.Add(ToSkipped(astronomyEvent, $"MaxSpecialEventVideosPerDay limit of {eventOptions.MaxSpecialEventVideosPerDay} reached."));
                continue;
            }

            _lastPlannedEvents[astronomyEvent.EventId] = astronomyEvent;
            planned.Add(new SchedulerSpecialEventPlanItem(astronomyEvent.EventId, astronomyEvent.EventType, astronomyEvent.Title, astronomyEvent.ContentOpportunityScore, ContentType.SpecialEventGuide, astronomyEvent.PeakUtc, $"Decision {decision.DecisionType}; score {astronomyEvent.ContentOpportunityScore:0.00}."));
        }

        foreach (var skippedEvent in decision.SkippedEvents)
        {
            if (!skipped.Any(x => string.Equals(x.EventId, skippedEvent.EventId, StringComparison.OrdinalIgnoreCase)))
                skipped.Add(ToSkipped(skippedEvent, GetScoreSkipReason(skippedEvent, eventOptions)));
        }

        var candidatePlan = candidateEvents
            .Select(e => ToCandidatePlanItem(e, decision, planned, skipped, eventOptions, regionId, targetDate, originalRegionByEventId.TryGetValue(e.EventId, out var originalRegionId) ? originalRegionId : null))
            .ToArray();
        var globalEventCount = candidateEvents.Count(e => e.GlobalVisibility || (originalRegionByEventId.TryGetValue(e.EventId, out var originalRegionId) && originalRegionId is null));
        var regionSpecificEventCount = candidateEvents.Length - globalEventCount;

        reasons.Add($"candidateEventCount={candidateEvents.Length}; globalEventCount={globalEventCount}; regionSpecificEventCount={regionSpecificEventCount}.");
        reasons.Add(planned.Count == 0 ? "No SpecialEventGuide runs planned." : $"{planned.Count} SpecialEventGuide run(s) planned.");
        return new SchedulerEventPlanResponse(regionId, targetDate, true, planned, skipped, reasons, decision.DecisionType, decision.InjectedEvents, decision.SpecialEventCandidates, candidatePlan, candidateEvents.Length, globalEventCount, regionSpecificEventCount);
    }

    private static EventContentDecision BuildDecisionFromCandidates(IReadOnlyCollection<AstronomyEvent> candidates, AstronomyEventsOptions options, string regionId, DateOnly targetDate)
    {
        var eligible = candidates
            .Where(e => e.ContentOpportunityScore >= options.MinimumContentOpportunityScore)
            .OrderByDescending(e => e.ContentOpportunityScore)
            .ThenBy(e => e.PeakUtc ?? e.StartUtc)
            .ToArray();
        var skipped = candidates
            .Where(e => e.ContentOpportunityScore < options.MinimumContentOpportunityScore)
            .OrderByDescending(e => e.ContentOpportunityScore)
            .ToArray();
        var special = options.EnableSpecialEventVideos
            ? eligible.Where(e => e.ContentOpportunityScore >= options.MajorEventThreshold).ToArray()
            : [];
        var injectable = options.EnableDailyGuideEventInjection
            ? eligible.Where(e => e.ContentOpportunityScore >= options.MediumEventThreshold).Take(options.MaxInjectedEventsPerDailyGuide).ToArray()
            : [];
        var primary = special.FirstOrDefault() ?? injectable.FirstOrDefault() ?? eligible.FirstOrDefault();
        var decisionType = primary is null || primary.ContentOpportunityScore < options.MediumEventThreshold
            ? (primary is null ? "None" : "MentionOnly")
            : special.Length > 0 ? "GenerateBoth" : "InjectIntoDailyGuide";

        return new EventContentDecision
        {
            HasEvent = primary is not null,
            PrimaryEvent = primary,
            DecisionType = decisionType,
            InjectedEvents = decisionType == "GenerateBoth" && primary is not null ? [primary] : injectable,
            SpecialEventCandidates = special,
            SkippedEvents = skipped,
            Reason = primary is null
                ? $"No astronomy event met score {options.MinimumContentOpportunityScore:0.00} for {regionId} on {targetDate:yyyy-MM-dd}."
                : $"Top event score {primary.ContentOpportunityScore:0.00}; decision={decisionType}."
        };
    }

    private static AstronomyEvent ApplyRegionContext(AstronomyEvent astronomyEvent, SchedulerScheduleOptions schedule, string effectiveRegionId)
        => new()
        {
            EventId = astronomyEvent.EventId,
            EventType = astronomyEvent.EventType,
            Title = astronomyEvent.Title,
            Description = astronomyEvent.Description,
            StartUtc = astronomyEvent.StartUtc,
            PeakUtc = astronomyEvent.PeakUtc,
            EndUtc = astronomyEvent.EndUtc,
            TargetDate = astronomyEvent.TargetDate == default ? DateOnly.FromDateTime((astronomyEvent.PeakUtc ?? astronomyEvent.StartUtc).UtcDateTime) : astronomyEvent.TargetDate,
            RegionId = effectiveRegionId,
            LocationName = schedule.LocationName,
            Latitude = schedule.Latitude,
            Longitude = schedule.Longitude,
            Timezone = schedule.Timezone,
            GlobalVisibility = astronomyEvent.GlobalVisibility || astronomyEvent.RegionId is null,
            VisibilityRegions = astronomyEvent.VisibilityRegions,
            RelatedObjects = astronomyEvent.RelatedObjects,
            Source = astronomyEvent.Source,
            ConfidenceScore = astronomyEvent.ConfidenceScore,
            RarityScore = astronomyEvent.RarityScore,
            VisibilityScore = astronomyEvent.VisibilityScore,
            AudienceInterestScore = astronomyEvent.AudienceInterestScore,
            TimingUrgencyScore = astronomyEvent.TimingUrgencyScore,
            ContentOpportunityScore = astronomyEvent.ContentOpportunityScore,
            RecommendedContentType = astronomyEvent.RecommendedContentType,
            Status = astronomyEvent.Status
        };

    private static SchedulerEventCandidatePlanItem ToCandidatePlanItem(AstronomyEvent astronomyEvent, EventContentDecision decision, IReadOnlyCollection<SchedulerSpecialEventPlanItem> planned, IReadOnlyCollection<SchedulerSkippedEventPlanItem> skipped, AstronomyEventsOptions options, string regionId, DateOnly targetDate, string? originalRegionId)
    {
        var selected = decision.PrimaryEvent is not null && string.Equals(decision.PrimaryEvent.EventId, astronomyEvent.EventId, StringComparison.OrdinalIgnoreCase);
        var injected = decision.InjectedEvents.Any(e => string.Equals(e.EventId, astronomyEvent.EventId, StringComparison.OrdinalIgnoreCase));
        var special = decision.SpecialEventCandidates.Any(e => string.Equals(e.EventId, astronomyEvent.EventId, StringComparison.OrdinalIgnoreCase));
        var skippedItem = skipped.FirstOrDefault(e => string.Equals(e.EventId, astronomyEvent.EventId, StringComparison.OrdinalIgnoreCase));
        var candidateDecisionType = planned.Any(e => string.Equals(e.EventId, astronomyEvent.EventId, StringComparison.OrdinalIgnoreCase))
            ? "SelectedSpecialEvent"
            : skippedItem is not null ? "Skipped"
            : special ? "SpecialEventCandidate"
            : injected ? "InjectedEventCandidate"
            : selected ? decision.DecisionType
            : astronomyEvent.ContentOpportunityScore < options.MinimumContentOpportunityScore ? "Skipped"
            : "Candidate";
        var reason = skippedItem?.Reason
            ?? (astronomyEvent.ContentOpportunityScore < options.MinimumContentOpportunityScore ? GetScoreSkipReason(astronomyEvent, options)
            : $"Included for {regionId} on {targetDate:yyyy-MM-dd}; score {astronomyEvent.ContentOpportunityScore:0.00}.");

        return new SchedulerEventCandidatePlanItem(
            astronomyEvent.EventId,
            astronomyEvent.EventType,
            astronomyEvent.Title,
            astronomyEvent.ContentOpportunityScore,
            astronomyEvent.TargetDate,
            !string.Equals(originalRegionId, regionId, StringComparison.OrdinalIgnoreCase),
            originalRegionId,
            regionId,
            astronomyEvent.GlobalVisibility,
            candidateDecisionType,
            reason);
    }

    private static string GetScoreSkipReason(AstronomyEvent astronomyEvent, AstronomyEventsOptions options)
        => $"Score {astronomyEvent.ContentOpportunityScore:0.00} is below threshold {options.MinimumContentOpportunityScore:0.00}.";

    private async Task<bool> HasSpecialEventDuplicateAsync(string eventId, DateOnly targetDate, string regionId, ContentType contentType, CancellationToken cancellationToken)
    {
        var auditRuns = await _auditStore.GetRunsAsync(cancellationToken);
        if (auditRuns.Any(x => x.TargetDate == targetDate
            && x.ContentType == contentType
            && string.Equals(x.EventId, eventId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Slugify(x.RegionId ?? x.LocationName), regionId, StringComparison.OrdinalIgnoreCase)
            && x.Status is "Created" or "Running" or "Completed" or "Publishing" or "Recoverable"))
            return true;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();
        return await repository.HasSpecialEventRunAsync(eventId, targetDate, regionId, contentType, [PipelineRunStatus.Queued, PipelineRunStatus.Running, PipelineRunStatus.Succeeded], cancellationToken);
    }

    private static SchedulerSkippedEventPlanItem ToSkipped(AstronomyEvent astronomyEvent, string reason)
        => new(astronomyEvent.EventId, astronomyEvent.EventType, astronomyEvent.Title, astronomyEvent.ContentOpportunityScore, ContentType.SpecialEventGuide, reason);

    private static RunPipelineRequest BuildSpecialEventRequest(SchedulerScheduleOptions schedule, DateOnly targetDate, SchedulerSpecialEventPlanItem plannedEvent, AstronomyEvent? astronomyEvent)
        => BuildRequest(ContentType.SpecialEventGuide, schedule, targetDate) with
        {
            EventId = plannedEvent.EventId,
            EventType = plannedEvent.EventType,
            EventTitle = plannedEvent.Title,
            EventDescription = astronomyEvent?.Description,
            UseTopicPlanner = false
        };

    private static bool IsEventOnDate(AstronomyEvent astronomyEvent, DateOnly targetDate)
    {
        var peakDate = astronomyEvent.PeakUtc is null ? (DateOnly?)null : DateOnly.FromDateTime(astronomyEvent.PeakUtc.Value.UtcDateTime);
        return peakDate == targetDate
            || DateOnly.FromDateTime(astronomyEvent.StartUtc.UtcDateTime) == targetDate
            || DateOnly.FromDateTime(astronomyEvent.EndUtc.UtcDateTime) == targetDate;
    }

    private static bool IsVisibleInRegion(AstronomyEvent astronomyEvent, string regionId)
        => astronomyEvent.GlobalVisibility
            || astronomyEvent.RegionId is null
            || string.Equals(astronomyEvent.RegionId, regionId, StringComparison.OrdinalIgnoreCase)
            || astronomyEvent.VisibilityRegions.Any(region => string.Equals(Slugify(region), regionId, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedSpecialEventType(AstronomyEvent astronomyEvent)
    {
        var eventType = astronomyEvent.EventType.Replace("_", " ", StringComparison.OrdinalIgnoreCase);
        var title = astronomyEvent.Title;
        return eventType.Contains("full moon", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("supermoon", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("eclipse", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Full Moon", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Supermoon", StringComparison.OrdinalIgnoreCase)
            || title.Contains("meteor", StringComparison.OrdinalIgnoreCase)
            || title.Contains("conjunction", StringComparison.OrdinalIgnoreCase)
            || title.Contains("eclipse", StringComparison.OrdinalIgnoreCase);
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
            schedule.RegionId,
            Language: schedule.Language);

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
            PublishEnabled = defaultPublishPlatforms.Contains("YouTube", StringComparer.OrdinalIgnoreCase),
            Language = string.IsNullOrWhiteSpace(region.Language) ? "en" : region.Language
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
