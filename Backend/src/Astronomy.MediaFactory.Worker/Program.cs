using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Extensions;
using Quartz;
using Serilog;
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables();
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Services.AddSerilog();
builder.Services.AddMediaFactory(builder.Configuration);
builder.Services.AddQuartz(q => { q.AddJob<RunScheduledPipelineJob>(o => o.WithIdentity("daily-job")); q.AddTrigger(t => t.ForJob("daily-job").WithIdentity("daily-trigger").WithCronSchedule("0 0 3 * * ?")); });
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
var host = builder.Build(); await host.RunAsync();
public sealed class RunScheduledPipelineJob : IJob
{
    private readonly PipelineOrchestrator _orchestrator;
    public RunScheduledPipelineJob(PipelineOrchestrator orchestrator) { _orchestrator = orchestrator; }
    public async Task Execute(IJobExecutionContext context)
    {
        var request = new RunPipelineRequest(DateOnly.FromDateTime(DateTime.UtcNow.Date), ContentType.DailySkyGuide, "Udaipur, India", "Asia/Kolkata", false);
        await _orchestrator.RunAsync(request, context.CancellationToken);
    }
}
