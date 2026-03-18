using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Alerting;
using Astronomy.MediaFactory.Infrastructure.Configuration;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Astronomy.MediaFactory.Worker;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using SchedulingOptions = Astronomy.MediaFactory.Contracts.SchedulingOptions;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddMediaFactorySecureConfiguration(builder.Environment);

var telemetryOptions = new TelemetryOptions();
builder.Configuration.GetSection(TelemetryOptions.SectionName).Bind(telemetryOptions);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
if (!string.IsNullOrWhiteSpace(telemetryOptions.ApplicationInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetryWorkerService();
}

builder.Services.AddMediaFactory(builder.Configuration);
builder.Services.AddHostedService<PipelineQueueWorker>();
builder.Services.AddHostedService<AlertingMonitorService>();

builder.Services.AddQuartz(q =>
{
    q.AddJob<EnqueueScheduledContentJob>(o => o.WithIdentity("schedule-daily-sky-guide"));
    q.AddTrigger(t => t.ForJob("schedule-daily-sky-guide").WithIdentity("schedule-daily-sky-guide-trigger")
        .WithCronSchedule(builder.Configuration[$"{SchedulingOptions.SectionName}:DailySkyGuideCron"] ?? "0 0 18 * * ?")
        .UsingJobData("contentType", nameof(ContentType.DailySkyGuide)));

    q.AddJob<EnqueueScheduledContentJob>(o => o.WithIdentity("schedule-telescope-targets"));
    q.AddTrigger(t => t.ForJob("schedule-telescope-targets").WithIdentity("schedule-telescope-targets-trigger")
        .WithCronSchedule(builder.Configuration[$"{SchedulingOptions.SectionName}:TelescopeTargetsCron"] ?? "0 0 19 * * ?")
        .UsingJobData("contentType", nameof(ContentType.TelescopeTargets)));

    q.AddJob<EnqueueScheduledContentJob>(o => o.WithIdentity("schedule-space-news"));
    q.AddTrigger(t => t.ForJob("schedule-space-news").WithIdentity("schedule-space-news-trigger")
        .WithCronSchedule(builder.Configuration[$"{SchedulingOptions.SectionName}:SpaceNewsCron"] ?? "0 0 20 * * ?")
        .UsingJobData("contentType", nameof(ContentType.SpaceNews)));

    q.AddJob<EnqueueScheduledContentJob>(o => o.WithIdentity("schedule-astrophotography-tips"));
    q.AddTrigger(t => t.ForJob("schedule-astrophotography-tips").WithIdentity("schedule-astrophotography-tips-trigger")
        .WithCronSchedule(builder.Configuration[$"{SchedulingOptions.SectionName}:AstrophotographyTipsCron"] ?? "0 0 21 * * ?")
        .UsingJobData("contentType", nameof(ContentType.AstrophotographyTips)));

    q.AddJob<FetchAnalyticsJob>(o => o.WithIdentity("fetch-analytics"));
    q.AddTrigger(t => t.ForJob("fetch-analytics").WithIdentity("fetch-analytics-trigger")
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(Math.Max(1, builder.Configuration.GetValue<int>($"{AnalyticsOptions.SectionName}:FetchIntervalMinutes", 1440)))).RepeatForever()));

    q.AddJob<RetentionCleanupJob>(o => o.WithIdentity("retention-cleanup"));
    q.AddTrigger(t => t.ForJob("retention-cleanup").WithIdentity("retention-cleanup-trigger")
        .WithCronSchedule("0 0 3 * * ?"));
});

builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("Starting Astronomy.MediaFactory.Worker in {Environment}", builder.Environment.EnvironmentName);
await host.RunAsync();

public sealed class EnqueueScheduledContentJob : IJob
{
    private readonly IPipelineJobQueue _queue;
    private readonly ILogger<EnqueueScheduledContentJob> _logger;

    public EnqueueScheduledContentJob(IPipelineJobQueue queue, ILogger<EnqueueScheduledContentJob> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var contentTypeValue = context.JobDetail.JobDataMap.GetString("contentType");
        if (!Enum.TryParse<ContentType>(contentTypeValue, out var contentType))
            throw new InvalidOperationException("Missing content type for scheduled enqueue job.");

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["jobType"] = "schedule", ["contentType"] = contentType.ToString() });
        await _queue.EnqueueAsync(new EnqueuePipelineJobRequest(
            PipelineJobType.GenerateMainVideo,
            DateOnly.FromDateTime(DateTime.UtcNow),
            contentType,
            "Udaipur, India"), context.CancellationToken);

        _logger.LogInformation("Scheduled job queued for {ContentType}", contentType);
    }
}

public sealed class RetentionCleanupJob : IJob
{
    private readonly IMaintenanceService _maintenanceService;
    private readonly ILogger<RetentionCleanupJob> _logger;

    public RetentionCleanupJob(IMaintenanceService maintenanceService, ILogger<RetentionCleanupJob> logger)
    {
        _maintenanceService = maintenanceService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var summary = await _maintenanceService.CleanupAsync(new CleanupMaintenanceRequest("system", "Scheduled daily retention cleanup.", DeleteWorkingFiles: true, DeleteDbRecords: true, DeleteAnalytics: true), context.CancellationToken);
        _logger.LogInformation("Retention cleanup job completed. Deleted {DeletedWorkingFiles} files.", summary.DeletedWorkingFiles);
    }
}

public sealed class PipelineQueueWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SchedulingOptions _options;
    private readonly ILogger<PipelineQueueWorker> _logger;

    public PipelineQueueWorker(IServiceProvider serviceProvider, IOptions<SchedulingOptions> options, ILogger<PipelineQueueWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pipeline queue worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<PipelineJobProcessor>();
            var processed = await processor.ProcessNextAsync(stoppingToken);
            if (!processed)
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.QueuePollIntervalSeconds)), stoppingToken);
        }
        _logger.LogInformation("Pipeline queue worker stopping.");
    }
}
