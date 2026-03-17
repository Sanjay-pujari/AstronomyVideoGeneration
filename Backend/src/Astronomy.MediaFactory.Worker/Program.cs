using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables();
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Services.AddSerilog();
builder.Services.AddMediaFactory(builder.Configuration);
builder.Services.AddHostedService<PipelineQueueWorker>();

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
});

builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

var host = builder.Build();
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

        await _queue.EnqueueAsync(new EnqueuePipelineJobRequest(
            PipelineJobType.GenerateMainVideo,
            DateOnly.FromDateTime(DateTime.UtcNow),
            contentType,
            "Udaipur, India"), context.CancellationToken);

        _logger.LogInformation("Scheduled job queued for {ContentType}", contentType);
    }
}

public sealed class PipelineQueueWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SchedulingOptions _options;

    public PipelineQueueWorker(IServiceProvider serviceProvider, IOptions<SchedulingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<PipelineJobProcessor>();
            var processed = await processor.ProcessNextAsync(stoppingToken);
            if (!processed)
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.QueuePollIntervalSeconds)), stoppingToken);
        }
    }
}
