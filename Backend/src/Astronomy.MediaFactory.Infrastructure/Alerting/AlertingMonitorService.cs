using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Astronomy.MediaFactory.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.MediaFactory.Infrastructure.Alerting;

public sealed class AlertingMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AlertingOptions _options;
    private readonly ILogger<AlertingMonitorService> _logger;

    public AlertingMonitorService(IServiceProvider serviceProvider, IOptions<AlertingOptions> options, ILogger<AlertingMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var monitoring = scope.ServiceProvider.GetRequiredService<IPipelineMonitoringService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IOperationalAlertNotifier>();
        var health = scope.ServiceProvider.GetRequiredService<HealthCheckService>();

        var jobSummary = await monitoring.GetJobSummaryAsync(cancellationToken);
        var backlog = jobSummary.PendingJobs + jobSummary.RetryingJobs;
        if (backlog >= _options.QueueBacklogThreshold)
        {
            await notifier.NotifyAsync(new OperationalAlert(
                AlertCategory.QueueBacklogHigh,
                string.Empty,
                QueueBacklog: backlog,
                QueueThreshold: _options.QueueBacklogThreshold,
                OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
        }

        var report = await health.CheckHealthAsync(x => x.Tags.Contains("ready"), cancellationToken);
        if (report.Status == HealthStatus.Degraded || report.Status == HealthStatus.Unhealthy)
        {
            var details = string.Join("; ", report.Entries.Where(x => x.Value.Status != HealthStatus.Healthy).Select(x => $"{x.Key}:{x.Value.Description ?? x.Value.Status.ToString()}"));
            await notifier.NotifyAsync(new OperationalAlert(AlertCategory.HealthDegraded, details, OccurredAt: DateTimeOffset.UtcNow), cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alerting monitor loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
