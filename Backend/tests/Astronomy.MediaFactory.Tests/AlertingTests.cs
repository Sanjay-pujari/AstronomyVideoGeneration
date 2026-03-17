using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Alerting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Astronomy.MediaFactory.Tests;

public sealed class AlertingTests
{
    [Fact]
    public void AlertMessageFormatter_FormatsQueueBacklog()
    {
        var formatter = new AlertMessageFormatter();
        var message = formatter.Format(new OperationalAlert(AlertCategory.QueueBacklogHigh, string.Empty, QueueBacklog: 42, QueueThreshold: 25));
        Assert.Contains("42", message);
        Assert.Contains("25", message);
    }

    [Fact]
    public async Task SafeNotifier_Skips_WhenDisabled()
    {
        var publisher = new RecordingOperationalPublisher();
        var notifier = new SafeOperationalAlertNotifier(
            new AlertingRouter(Options.Create(new AlertingOptions { Enabled = false })),
            new AlertMessageFormatter(),
            new AlertNoiseSuppressor(Options.Create(new AlertingOptions { Enabled = false })),
            publisher,
            NullLogger<SafeOperationalAlertNotifier>.Instance);

        await notifier.NotifyAsync(new OperationalAlert(AlertCategory.StageFailed, string.Empty), CancellationToken.None);
        Assert.Empty(publisher.Alerts);
    }

    [Fact]
    public void SlackPublisher_BuildsPayload()
    {
        var payload = SlackWebhookOperationalAlertPublisher.BuildPayload(new OperationalAlert(AlertCategory.StageFailed, "Failure", PipelineRunId: Guid.NewGuid(), StageName: "Rendering", ErrorSummary: "boom"));
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        Assert.Contains("StageFailed", json);
        Assert.Contains("Rendering", json);
        Assert.Contains("boom", json);
    }

    [Fact]
    public async Task NoiseSuppressor_SuppressesDuplicates()
    {
        var publisher = new RecordingOperationalPublisher();
        var notifier = new SafeOperationalAlertNotifier(
            new AlertingRouter(Options.Create(new AlertingOptions { Enabled = true, NotifyOnStageFailed = true, DedupWindowSeconds = 999 })),
            new AlertMessageFormatter(),
            new AlertNoiseSuppressor(Options.Create(new AlertingOptions { DedupWindowSeconds = 999 })),
            publisher,
            NullLogger<SafeOperationalAlertNotifier>.Instance);

        var alert = new OperationalAlert(AlertCategory.StageFailed, "same", PipelineRunId: Guid.NewGuid(), StageName: "BlobUpload");
        await notifier.NotifyAsync(alert, CancellationToken.None);
        await notifier.NotifyAsync(alert, CancellationToken.None);

        Assert.Single(publisher.Alerts);
    }

    [Fact]
    public async Task MonitorService_TriggersQueueBacklogAlert()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPipelineMonitoringService>(new FakeMonitoringService(new JobOpsSummary(100, 30, 1, 12, 0, 57)));
        var recordingNotifier = new RecordingNotifier();
        services.AddSingleton<IOperationalAlertNotifier>(recordingNotifier);
        services.AddSingleton<HealthCheckService>(new FakeHealthCheckService(HealthStatus.Healthy));
        services.AddSingleton<IOptions<AlertingOptions>>(Options.Create(new AlertingOptions { Enabled = true, QueueBacklogThreshold = 25, NotifyOnQueueBacklogHigh = true }));
        services.AddSingleton<AlertingMonitorService>();

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<AlertingMonitorService>();
        await monitor.RunOnceAsync(CancellationToken.None);

        Assert.Contains(recordingNotifier.Alerts, x => x.Category == AlertCategory.QueueBacklogHigh);
    }

    private sealed class RecordingOperationalPublisher : IOperationalAlertPublisher
    {
        public List<OperationalAlert> Alerts { get; } = [];
        public Task PublishAsync(OperationalAlert alert, CancellationToken cancellationToken)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotifier : IOperationalAlertNotifier
    {
        public List<OperationalAlert> Alerts { get; } = [];
        public Task NotifyAsync(OperationalAlert alert, CancellationToken cancellationToken)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMonitoringService : IPipelineMonitoringService
    {
        private readonly JobOpsSummary _summary;
        public FakeMonitoringService(JobOpsSummary summary) => _summary = summary;
        public Task<JobOpsSummary> GetJobSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(_summary);
        public Task<RecentFailuresSnapshot> GetRecentFailuresAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PipelineRun>> GetRecentPipelinesAsync(int take, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PipelineOpsSummary> GetSummaryAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<PipelineStageExecution>> GetPipelineStagesAsync(Guid pipelineRunId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeHealthCheckService : HealthCheckService
    {
        private readonly HealthStatus _status;
        public FakeHealthCheckService(HealthStatus status) => _status = status;
        public override Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry>
            {
                ["ready"] = new HealthReportEntry(_status, "ok", TimeSpan.Zero, null, null)
            }, _status, TimeSpan.Zero));
    }
}
