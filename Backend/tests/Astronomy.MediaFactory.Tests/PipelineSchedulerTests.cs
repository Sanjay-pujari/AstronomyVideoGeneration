using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class PipelineSchedulerTests
{
    [Fact]
    public async Task Daily_Schedule_Triggers_Once_Per_Target_Date()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]));
        var scheduler = harness.CreateScheduler();

        await scheduler.EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);
        await scheduler.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Single(harness.Executor.CompletedRuns);
        Assert.Contains(harness.Audit.Runs, x => x.Status == "Completed" && x.ScheduleName == "UTC Daily");
    }

    [Fact]
    public async Task Timezone_Conversion_Produces_Configured_Local_Run_Time()
    {
        var schedule = new SchedulerScheduleOptions
        {
            Name = "Udaipur Daily Sky",
            Enabled = true,
            LocationName = "Udaipur, India",
            Latitude = 24.5854,
            Longitude = 73.7125,
            Timezone = "Asia/Kolkata",
            LocalRunTime = "18:00",
            PublishEnabled = true
        };
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [schedule]));
        var status = await harness.CreateScheduler().GetStatusAsync(CancellationToken.None);

        var plannedUtc = Assert.Single(status.Schedules).NextPlannedRunUtc;
        Assert.NotNull(plannedUtc);
        var local = TimeZoneInfo.ConvertTime(plannedUtc.Value, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        Assert.Equal(18, local.Hour);
        Assert.Equal(0, local.Minute);
    }

    [Fact]
    public async Task Duplicate_Schedule_Run_Is_Skipped()
    {
        var schedule = DueUtcSchedule();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [schedule]));
        await harness.Audit.UpsertAsync(new SchedulerRunRecord(schedule.RegionId, schedule.Name, ContentType.DailySkyGuide, targetDate, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), "Completed", null, schedule.LocationName, schedule.Timezone, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(harness.Executor.CompletedRuns);
        Assert.Contains(harness.Audit.Runs, x => x.Status == "Skipped");
    }

    [Fact]
    public async Task RunNow_Creates_Run()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]));

        var result = await harness.CreateScheduler().RunNowAsync("UTC Daily", force: false, CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.True(result.Accepted);
        Assert.NotNull(result.PipelineRunId);
        Assert.Equal("utc", result.RegionId);
        Assert.Equal("UTC Daily", result.ScheduleName);
        var run = Assert.Single(harness.Executor.CompletedRuns);
        Assert.Equal(result.PipelineRunId, run.Id);
    }

    [Fact]
    public async Task RunRegionNow_Returns_Region_And_Preassigned_Pipeline_Id()
    {
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true)));

        var result = await harness.CreateScheduler().RunRegionNowAsync("india-udaipur", force: false, CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.True(result.Accepted);
        Assert.NotNull(result.PipelineRunId);
        Assert.Equal("india-udaipur", result.RegionId);
        Assert.Equal("Udaipur, India", result.ScheduleName);
        var run = Assert.Single(harness.Executor.CompletedRuns);
        Assert.Equal("india-udaipur", run.RegionId);
        Assert.Equal(result.PipelineRunId, run.Id);
    }

    [Fact]
    public async Task Force_RunNow_Bypasses_Duplicate_Check()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()])) { Repository = { HasDuplicate = true } };
        var scheduler = harness.CreateScheduler();

        var skipped = await scheduler.RunNowAsync("UTC Daily", force: false, CancellationToken.None);
        var forced = await scheduler.RunNowAsync("UTC Daily", force: true, CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.False(skipped.Accepted);
        Assert.True(forced.Accepted);
        Assert.Single(harness.Executor.CompletedRuns);
    }

    [Fact]
    public async Task MaxConcurrentRuns_Is_Respected()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, maxConcurrentRuns: 1, schedules: [DueUtcSchedule()]))
        {
            Executor = { HoldRuns = true }
        };
        var queue = harness.CreateQueue();
        var request = new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "A", "UTC");

        await queue.EnqueueAsync(new SchedulerRunQueueItem("A", request, DateTimeOffset.UtcNow, Force: true), CancellationToken.None);
        await queue.EnqueueAsync(new SchedulerRunQueueItem("B", request with { LocationName = "B" }, DateTimeOffset.UtcNow, Force: true), CancellationToken.None);
        await WaitForAsync(() => queue.ActiveCount == 1 && queue.QueuedCount == 1);

        Assert.Equal(1, queue.ActiveCount);
        Assert.Equal(1, queue.QueuedCount);
        harness.Executor.ReleaseAll();
    }

    [Fact]
    public async Task Disabled_Schedule_Does_Not_Run()
    {
        var schedule = DueUtcSchedule();
        schedule.Enabled = false;
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [schedule]));

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(harness.Executor.CompletedRuns);
    }


    [Fact]
    public async Task Approved_AI_Profile_Writes_Optimization_Used_Without_Mutating_Request()
    {
        using var temp = new TempOutput();
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]))
        {
            Executor = { OutputFolder = temp.Path }
        };
        var queue = harness.CreateQueue();
        var request = new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "UTC", "UTC");
        var profile = new AIOptimizationAppliedProfile
        {
            ApprovedBy = "reviewer",
            ConfidenceScore = 0.9,
            AppliedValues = new AIOptimizationSafeValues { RecommendedHooks = ["Question-led"], RecommendedObjectsToBoost = ["Jupiter"] },
            AppliedFields = ["recommendedHooks", "recommendedObjectsToBoost"],
            SourceRecommendations = new AIOptimizationRecommendations { ConfidenceScore = 0.9, ReasoningSummary = "approved safe profile" }
        };

        await queue.EnqueueAsync(new SchedulerRunQueueItem("UTC Daily", request, DateTimeOffset.UtcNow, Force: true, AIOptimizationProfile: profile), CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.True(File.Exists(Path.Combine(temp.Path, "optimization-used.json")));
        Assert.False(harness.Executor.CompletedRuns.Single().UseTopicPlanner);
    }

    [Fact]
    public async Task Scheduler_Does_Not_Block_Manual_Pipeline_Runs()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()])) { Repository = { HasDuplicate = true } };
        var manualRun = await harness.Executor.ExecuteAsync(new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow), ContentType.DailySkyGuide, "UTC", "UTC"), CancellationToken.None);

        Assert.Equal(PipelineRunStatus.Succeeded, manualRun.Status);
        Assert.Single(harness.Executor.CompletedRuns);
    }


    [Fact]
    public async Task Multiple_Enabled_Regions_Schedule_Independent_Runs()
    {
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "UTC", enabled: true),
            Region("usa-new-york", "New York, USA", "UTC", enabled: true),
            Region("australia-sydney", "Sydney, Australia", "UTC", enabled: false)));

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 2);

        Assert.Contains(harness.Executor.CompletedRuns, x => x.RegionId == "india-udaipur");
        Assert.Contains(harness.Executor.CompletedRuns, x => x.RegionId == "usa-new-york");
        Assert.DoesNotContain(harness.Executor.CompletedRuns, x => x.RegionId == "australia-sydney");
    }


    [Fact]
    public async Task Scheduler_Passes_Region_Language_To_Pipeline_Request()
    {
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "UTC", enabled: true, language: "hi")));

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.Equal("hi", harness.Executor.CompletedRuns.Single().Language);
    }

    [Fact]
    public async Task Region_Timezone_Conversion_Uses_Each_Region_Timezone()
    {
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true),
            Region("usa-new-york", "New York, USA", "America/New_York", enabled: true)));

        var status = await harness.CreateScheduler().GetStatusAsync(CancellationToken.None);

        foreach (var schedule in status.Schedules)
        {
            Assert.NotNull(schedule.NextPlannedRunUtc);
            var local = TimeZoneInfo.ConvertTime(schedule.NextPlannedRunUtc!.Value, TimeZoneInfo.FindSystemTimeZoneById(schedule.Timezone));
            Assert.Equal(0, local.Hour);
            Assert.Equal(0, local.Minute);
        }
    }

    [Fact]
    public async Task Duplicate_Prevention_Is_Region_Specific()
    {
        var options = CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "UTC", enabled: true),
            Region("usa-new-york", "New York, USA", "UTC", enabled: true));
        var harness = new SchedulerHarness(options);
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await harness.Audit.UpsertAsync(new SchedulerRunRecord("india-udaipur", "Udaipur, India", ContentType.DailySkyGuide, targetDate, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), "Completed", null, "Udaipur, India", "UTC", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 1);

        Assert.Single(harness.Executor.CompletedRuns);
        Assert.Equal("usa-new-york", harness.Executor.CompletedRuns.Single().RegionId);
    }


    [Fact]
    public async Task High_Score_Event_Creates_SpecialEventGuide()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("perseids", 0.95)]
        };

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 2);

        Assert.Contains(harness.Executor.CompletedRuns, x => x.ContentType == ContentType.DailySkyGuide);
        Assert.Contains(harness.Executor.CompletedRuns, x => x.ContentType == ContentType.SpecialEventGuide && x.EventId == "perseids");
    }

    [Fact]
    public async Task Low_Score_Event_Is_Skipped()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("weak-meteor", 0.5)]
        };

        var plan = await harness.CreateScheduler().GetEventPlanAsync("UTC", DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        Assert.Empty(plan.SpecialEventsPlanned);
        Assert.Contains(plan.SkippedEvents, x => x.EventId == "weak-meteor" && x.Reason.Contains("below threshold", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task EventPlan_Includes_NullRegion_Global_Event_For_Requested_Region()
    {
        var targetDate = new DateOnly(2026, 5, 16);
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true)))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("global-full-moon", 0.95, targetDate, regionId: null, globalVisibility: false, eventType: "full_moon")]
        };

        var plan = await harness.CreateScheduler().GetEventPlanAsync("india-udaipur", targetDate, CancellationToken.None);

        var candidate = Assert.Single(plan.CandidateEvents!);
        Assert.Equal("global-full-moon", candidate.EventId);
        Assert.True(candidate.RegionApplied);
        Assert.Null(candidate.OriginalEventRegionId);
        Assert.Equal("india-udaipur", candidate.EffectiveRegionId);
        Assert.Equal(1, plan.GlobalEventCount);
        Assert.Contains(plan.SpecialEventsPlanned, x => x.EventId == "global-full-moon");
    }

    [Fact]
    public async Task EventPlan_Includes_GlobalVisibility_Event_For_Any_Region()
    {
        var targetDate = new DateOnly(2026, 5, 24);
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true),
            Region("usa-new-york", "New York, USA", "America/New_York", enabled: true)))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("global-visible", 0.9, targetDate, regionId: "india-udaipur", globalVisibility: true)]
        };

        var plan = await harness.CreateScheduler().GetEventPlanAsync("usa-new-york", targetDate, CancellationToken.None);

        Assert.Contains(plan.CandidateEvents!, x => x.EventId == "global-visible" && x.EffectiveRegionId == "usa-new-york");
        Assert.Equal(1, plan.GlobalEventCount);
    }

    [Fact]
    public async Task EventPlan_RegionSpecific_Event_Appears_Only_For_Matching_Region_And_Date()
    {
        var targetDate = new DateOnly(2026, 5, 16);
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true),
            Region("usa-new-york", "New York, USA", "America/New_York", enabled: true)))
        {
            EventOptions = SpecialEventOptions(),
            Events =
            [
                Event("udaipur-only", 0.95, targetDate, regionId: "india-udaipur", globalVisibility: false),
                Event("wrong-date", 0.95, targetDate.AddDays(1), regionId: null, globalVisibility: true)
            ]
        };

        var matching = await harness.CreateScheduler().GetEventPlanAsync("india-udaipur", targetDate, CancellationToken.None);
        var otherRegion = await harness.CreateScheduler().GetEventPlanAsync("usa-new-york", targetDate, CancellationToken.None);

        Assert.Contains(matching.CandidateEvents!, x => x.EventId == "udaipur-only");
        Assert.DoesNotContain(otherRegion.CandidateEvents!, x => x.EventId == "udaipur-only");
        Assert.DoesNotContain(matching.CandidateEvents!, x => x.EventId == "wrong-date");
    }

    [Fact]
    public async Task EventPlan_Global_Medium_Event_Is_Injected_And_Low_Score_Is_Skipped_With_Reason()
    {
        var targetDate = new DateOnly(2026, 5, 16);
        var harness = new SchedulerHarness(CreateRegionOptions(
            Region("india-udaipur", "Udaipur, India", "Asia/Kolkata", enabled: true)))
        {
            EventOptions = SpecialEventOptions(),
            Events =
            [
                Event("medium-global", 0.75, targetDate, regionId: null, globalVisibility: false),
                Event("low-global", 0.50, targetDate, regionId: null, globalVisibility: false)
            ]
        };

        var plan = await harness.CreateScheduler().GetEventPlanAsync("india-udaipur", targetDate, CancellationToken.None);

        Assert.Equal("InjectIntoDailyGuide", plan.DecisionType);
        Assert.Contains(plan.InjectedEvents!, x => x.EventId == "medium-global" && x.RegionId == "india-udaipur");
        Assert.Contains(plan.SkippedEvents, x => x.EventId == "low-global" && x.Reason.Contains("below threshold", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicate_Event_Is_Skipped_By_Event_Date_Region_And_ContentType()
    {
        var schedule = DueUtcSchedule();
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [schedule]))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("perseids", 0.95)]
        };
        await harness.Audit.UpsertAsync(new SchedulerRunRecord("utc", "UTC Daily special event", ContentType.SpecialEventGuide, targetDate, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid(), "Completed", null, schedule.LocationName, schedule.Timezone, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "perseids", "meteor_shower", "Perseids"), CancellationToken.None);

        var plan = await harness.CreateScheduler().GetEventPlanAsync("UTC", targetDate, CancellationToken.None);

        Assert.Empty(plan.SpecialEventsPlanned);
        Assert.Contains(plan.SkippedEvents, x => x.EventId == "perseids" && x.Reason.Contains("eventId + targetDate + regionId + contentType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaxSpecialEventVideosPerDay_Is_Enforced()
    {
        var options = SpecialEventOptions();
        options.MaxSpecialEventVideosPerDay = 2;
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]))
        {
            EventOptions = options,
            Events = [Event("meteor-1", 0.95), Event("meteor-2", 0.94), Event("meteor-3", 0.93)]
        };

        var plan = await harness.CreateScheduler().GetEventPlanAsync("UTC", DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        Assert.Equal(2, plan.SpecialEventsPlanned.Count);
        Assert.Contains(plan.SkippedEvents, x => x.EventId == "meteor-3" && x.Reason.Contains("MaxSpecialEventVideosPerDay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DailySkyGuide_Still_Runs_With_SpecialEventGuide()
    {
        var harness = new SchedulerHarness(CreateOptions(enabled: true, schedules: [DueUtcSchedule()]))
        {
            EventOptions = SpecialEventOptions(),
            Events = [Event("perseids", 0.95)]
        };

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 2);

        Assert.Single(harness.Executor.CompletedRuns.Where(x => x.ContentType == ContentType.DailySkyGuide));
        Assert.Single(harness.Executor.CompletedRuns.Where(x => x.ContentType == ContentType.SpecialEventGuide));
    }

    [Fact]
    public async Task Special_Event_Priority_Config_Is_Respected()
    {
        var eventOptions = SpecialEventOptions();
        eventOptions.RunSpecialEventsBeforeDailyGuide = true;
        var harness = new SchedulerHarness(CreateOptions(enabled: true, maxConcurrentRuns: 1, schedules: [DueUtcSchedule()]))
        {
            EventOptions = eventOptions,
            Events = [Event("perseids", 0.95)]
        };

        await harness.CreateScheduler().EvaluateSchedulesAsync(CancellationToken.None);
        await WaitForAsync(() => harness.Executor.CompletedRuns.Count == 2);

        Assert.Equal(ContentType.SpecialEventGuide, harness.Executor.CompletedRuns[0].ContentType);
        Assert.Equal(ContentType.DailySkyGuide, harness.Executor.CompletedRuns[1].ContentType);
    }

    [Fact]
    public void Output_Path_Includes_Region_Id()
    {
        var runId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var output = PipelineOrchestrator.BuildOutputDirectory("/tmp/media", ContentType.DailySkyGuide, new DateOnly(2026, 5, 9), "india-udaipur", "Udaipur, India", runId);

        Assert.EndsWith(Path.Combine("DailySkyGuide", "2026-05-09", "india-udaipur", runId.ToString("N")), output);
    }

    private static SchedulerScheduleOptions DueUtcSchedule()
        => new()
        {
            Name = "UTC Daily",
            Enabled = true,
            LocationName = "UTC",
            Latitude = 0,
            Longitude = 0,
            Timezone = "UTC",
            LocalRunTime = "00:00",
            PublishEnabled = true
        };



    private static AstronomyEvent Event(string eventId, double score, DateOnly? targetDate = null, string? regionId = null, bool globalVisibility = true, string eventType = "meteor_shower")
    {
        var date = targetDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(20))), TimeSpan.Zero);
        return new AstronomyEvent
        {
            EventId = eventId,
            EventType = eventType,
            Title = eventType == "full_moon" ? "Full Moon" : "Perseids meteor shower",
            Description = "A high-value astronomy event.",
            StartUtc = start,
            PeakUtc = start.AddHours(1),
            EndUtc = start.AddHours(2),
            TargetDate = date,
            RegionId = regionId,
            GlobalVisibility = globalVisibility,
            ContentOpportunityScore = score
        };
    }

    private static AstronomyEventsOptions SpecialEventOptions()
        => new()
        {
            EnableSpecialEventVideos = true,
            SpecialEventScoreThreshold = 0.70,
            MaxSpecialEventVideosPerDay = 2,
            RunSpecialEventsBeforeDailyGuide = false
        };

    private static SchedulerOptions CreateRegionOptions(params RegionScheduleOptions[] regions)
        => new()
        {
            Enabled = true,
            RunOnStartup = false,
            MaxConcurrentRuns = 3,
            DefaultContentType = ContentType.DailySkyGuide,
            Schedules = [],
            Regions = new RegionSchedulingOptions { Enabled = true, Items = regions.ToList() }
        };

    private static RegionScheduleOptions Region(string id, string name, string timezone, bool enabled, string language = "en")
        => new()
        {
            RegionId = id,
            DisplayName = name,
            Latitude = 0,
            Longitude = 0,
            Timezone = timezone,
            LocalRunTime = "00:00",
            Language = language,
            Enabled = enabled
        };

    private static SchedulerOptions CreateOptions(bool enabled, int maxConcurrentRuns = 2, IReadOnlyCollection<SchedulerScheduleOptions>? schedules = null)
        => new()
        {
            Enabled = enabled,
            RunOnStartup = false,
            MaxConcurrentRuns = maxConcurrentRuns,
            DefaultContentType = ContentType.DailySkyGuide,
            Schedules = schedules?.ToList() ?? [],
            Regions = new RegionSchedulingOptions { Enabled = false, Items = [] }
        };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class SchedulerHarness
    {
        private readonly TestOptionsMonitor<SchedulerOptions> _options;
        private readonly TestOptionsMonitor<OptimizationOptions> _optimizationOptions = new(new OptimizationOptions { Enabled = false, Mode = OptimizationMode.Disabled });
        public AstronomyEventsOptions EventOptions { get; init; } = new() { EnableSpecialEventVideos = false };
        public IReadOnlyCollection<AstronomyEvent> Events { get; init; } = [];
        public InMemoryAuditStore Audit { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeExecutor Executor { get; init; } = new();

        public SchedulerHarness(SchedulerOptions options)
        {
            _options = new TestOptionsMonitor<SchedulerOptions>(options);
        }

        public PipelineRunQueue CreateQueue()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPipelineRepository>(Repository);
            services.AddSingleton<IPipelineRunExecutor>(Executor);
            var serviceProvider = services.BuildServiceProvider();
            return new PipelineRunQueue(serviceProvider.GetRequiredService<IServiceScopeFactory>(), Audit, _options, NullLogger<PipelineRunQueue>.Instance);
        }

        public PipelineSchedulerService CreateScheduler()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPipelineRepository>(Repository);
            services.AddSingleton<IPipelineRunExecutor>(Executor);
            services.AddSingleton<IAstronomyEventDiscoveryService>(new FakeAstronomyEventDiscoveryService(Events));
            var serviceProvider = services.BuildServiceProvider();
            return new PipelineSchedulerService(_options, CreateQueue(), Audit, _optimizationOptions, new TestOptionsMonitor<AstronomyEventsOptions>(EventOptions), serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PipelineSchedulerService>.Instance);
        }
    }

    private sealed class InMemoryAuditStore : ISchedulerAuditStore
    {
        public List<SchedulerRunRecord> Runs { get; } = [];

        public Task<IReadOnlyCollection<SchedulerRunRecord>> GetRunsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<SchedulerRunRecord>>(Runs.ToList());

        public Task<IReadOnlyCollection<SchedulerRunRecord>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<SchedulerRunRecord>>(Runs.OrderByDescending(x => x.UpdatedUtc).Take(take).ToList());

        public Task UpsertAsync(SchedulerRunRecord record, CancellationToken cancellationToken)
        {
            Runs.RemoveAll(x => x.Status == record.Status && x.ScheduleName == record.ScheduleName && x.TargetDate == record.TargetDate && x.ContentType == record.ContentType && x.RegionId == record.RegionId && x.EventId == record.EventId && x.PlannedRunUtc == record.PlannedRunUtc);
            if (record.Status != "Skipped")
                Runs.RemoveAll(x => x.Status != "Skipped" && x.ScheduleName == record.ScheduleName && x.TargetDate == record.TargetDate && x.ContentType == record.ContentType && x.RegionId == record.RegionId && x.EventId == record.EventId);
            Runs.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IPipelineRepository
    {
        public bool HasDuplicate { get; set; }
        public Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken) => Task.FromResult(run);
        public Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineRun?>(null);
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task<IReadOnlyCollection<PipelineRun>> GetGeneratedSpecialEventRunsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
        public Task<bool> HasSpecialEventRunAsync(string eventId, DateOnly runDate, string regionId, ContentType contentType, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken) => Task.FromResult(HasDuplicate);
        public Task<bool> HasPipelineRunAsync(DateOnly runDate, ContentType contentType, string locationName, string timeZone, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken) => Task.FromResult(HasDuplicate);
        public Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineJob>>([]);
        public Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<PipelineJob?>(null);
        public Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<GeneratedScript>>([]);
        public Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<VideoAnalytics>>([]);
        public Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
        public Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ShortVideo>>([]);
        public Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken) => Task.FromResult<GeneratedScript?>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }


    private sealed class FakeAstronomyEventDiscoveryService : IAstronomyEventDiscoveryService
    {
        private readonly IReadOnlyCollection<AstronomyEvent> _events;
        public FakeAstronomyEventDiscoveryService(IReadOnlyCollection<AstronomyEvent> events) => _events = events;
        public Task<IReadOnlyCollection<AstronomyEvent>> RefreshAsync(int? days, CancellationToken cancellationToken) => Task.FromResult(_events);
        public Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(int? days, CancellationToken cancellationToken) => Task.FromResult(_events);
        public Task<IReadOnlyCollection<AstronomyEvent>> GetTopAsync(int? days, CancellationToken cancellationToken) => Task.FromResult(_events);
        public Task<AstronomyEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult(_events.FirstOrDefault(x => x.EventId == eventId));
    }

    private sealed class FakeExecutor : IPipelineRunExecutor
    {
        private readonly List<TaskCompletionSource<bool>> _holds = [];
        public List<PipelineRun> CompletedRuns { get; } = [];
        public bool HoldRuns { get; set; }
        public string? OutputFolder { get; set; }

        public async Task<PipelineRun> ExecuteAsync(RunPipelineRequest request, Guid? pipelineRunId, CancellationToken cancellationToken)
        {
            if (HoldRuns)
            {
                var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _holds.Add(hold);
                await hold.Task.WaitAsync(cancellationToken);
            }

            var run = new PipelineRun();
            if (pipelineRunId.HasValue)
                run.AssignId(pipelineRunId.Value);
            run.RunDate = request.Date;
            run.ContentType = request.ContentType;
            run.RegionId = request.RegionId;
            run.LocationName = request.LocationName;
            run.TimeZone = request.TimeZone;
            run.Language = request.Language ?? "en";
            run.UseTopicPlanner = request.UseTopicPlanner;
            run.Status = PipelineRunStatus.Succeeded;
            run.OutputFolder = OutputFolder;
            run.EventId = request.EventId;
            run.EventType = request.EventType;
            run.EventTitle = request.EventTitle;
            run.EventDescription = request.EventDescription;
            CompletedRuns.Add(run);
            return run;
        }

        public void ReleaseAll()
        {
            foreach (var hold in _holds)
                hold.TrySetResult(true);
        }
    }

    private sealed class TempOutput : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scheduler-ai-optimization-tests-" + Guid.NewGuid().ToString("N"));
        public TempOutput() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
