using System.Collections.Concurrent;
using System.Net.Http.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Alerting;

public sealed class AlertingRouter
{
    private readonly AlertingOptions _options;

    public AlertingRouter(IOptions<AlertingOptions> options) => _options = options.Value;

    public bool ShouldNotify(AlertCategory category)
        => _options.Enabled && category switch
        {
            AlertCategory.StageFailed => _options.NotifyOnStageFailed,
            AlertCategory.StageSlow => _options.NotifyOnStageSlow,
            AlertCategory.PipelineFailed => _options.NotifyOnPipelineFailed,
            AlertCategory.PublishFailed => _options.NotifyOnPublishFailed,
            AlertCategory.QueueBacklogHigh => _options.NotifyOnQueueBacklogHigh,
            AlertCategory.HealthDegraded => _options.NotifyOnHealthDegraded,
            AlertCategory.PublishSucceeded => _options.NotifyOnPublishSucceeded,
            _ => false
        };
}

public sealed class AlertMessageFormatter
{
    public string Format(OperationalAlert alert) => alert.Category switch
    {
        AlertCategory.StageFailed => $"Pipeline {alert.PipelineRunId} failed during {alert.StageName} for {alert.ContentType} {alert.RunDate:yyyy-MM-dd} ({alert.LocationName}). Error: {alert.ErrorSummary}",
        AlertCategory.StageSlow => $"{alert.StageName} stage exceeded {alert.DurationMs:N0} ms for run {alert.PipelineRunId} ({alert.ContentType} {alert.RunDate:yyyy-MM-dd})",
        AlertCategory.PipelineFailed => $"Pipeline {alert.PipelineRunId} failed for {alert.ContentType} {alert.RunDate:yyyy-MM-dd} ({alert.LocationName}). Error: {alert.ErrorSummary}",
        AlertCategory.PublishFailed => $"Publish failed for run {alert.PipelineRunId} ({alert.ContentType} {alert.RunDate:yyyy-MM-dd}). Error: {alert.ErrorSummary}",
        AlertCategory.QueueBacklogHigh => $"Queue backlog is {alert.QueueBacklog} jobs, above threshold {alert.QueueThreshold}",
        AlertCategory.HealthDegraded => $"Health degraded: {alert.Message}",
        AlertCategory.PublishSucceeded => $"Publish succeeded for run {alert.PipelineRunId} ({alert.ContentType} {alert.RunDate:yyyy-MM-dd})",
        _ => alert.Message
    };
}

public sealed class AlertNoiseSuppressor
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();
    private readonly AlertingOptions _options;

    public AlertNoiseSuppressor(IOptions<AlertingOptions> options) => _options = options.Value;

    public bool ShouldSuppress(OperationalAlert alert)
    {
        var key = $"{alert.Category}:{alert.PipelineRunId}:{alert.StageName}:{alert.JobId}:{alert.Message}";
        var now = DateTimeOffset.UtcNow;
        if (_lastSent.TryGetValue(key, out var previous) && now - previous < TimeSpan.FromSeconds(Math.Max(1, _options.DedupWindowSeconds)))
        {
            return true;
        }

        _lastSent[key] = now;
        return false;
    }
}

public sealed class NoOpOperationalAlertPublisher : IOperationalAlertPublisher
{
    public Task PublishAsync(OperationalAlert alert, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CompositeOperationalAlertPublisher : IOperationalAlertPublisher
{
    private readonly IReadOnlyCollection<IOperationalAlertPublisher> _publishers;

    public CompositeOperationalAlertPublisher(IEnumerable<IOperationalAlertPublisher> publishers)
        => _publishers = publishers.ToArray();

    public async Task PublishAsync(OperationalAlert alert, CancellationToken cancellationToken)
    {
        foreach (var publisher in _publishers)
        {
            await publisher.PublishAsync(alert, cancellationToken);
        }
    }
}

public sealed class SlackWebhookOperationalAlertPublisher : IOperationalAlertPublisher
{
    private readonly HttpClient _httpClient;
    private readonly AlertingOptions _options;

    public SlackWebhookOperationalAlertPublisher(HttpClient httpClient, IOptions<AlertingOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public static object BuildPayload(OperationalAlert alert)
        => new
        {
            text = $"[{alert.Category}] {alert.Message}",
            category = alert.Category.ToString(),
            pipelineRunId = alert.PipelineRunId,
            stageName = alert.StageName,
            contentType = alert.ContentType?.ToString(),
            runDate = alert.RunDate?.ToString("yyyy-MM-dd"),
            error = alert.ErrorSummary,
            durationMs = alert.DurationMs,
            queueBacklog = alert.QueueBacklog,
            queueThreshold = alert.QueueThreshold,
            occurredAt = (alert.OccurredAt ?? DateTimeOffset.UtcNow).ToString("O")
        };

    public async Task PublishAsync(OperationalAlert alert, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SlackWebhookUrl))
            return;

        var response = await _httpClient.PostAsJsonAsync(_options.SlackWebhookUrl, BuildPayload(alert), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class SafeOperationalAlertNotifier : IOperationalAlertNotifier
{
    private readonly AlertingRouter _router;
    private readonly AlertMessageFormatter _formatter;
    private readonly AlertNoiseSuppressor _noiseSuppressor;
    private readonly IOperationalAlertPublisher _publisher;
    private readonly ILogger<SafeOperationalAlertNotifier> _logger;

    public SafeOperationalAlertNotifier(
        AlertingRouter router,
        AlertMessageFormatter formatter,
        AlertNoiseSuppressor noiseSuppressor,
        IOperationalAlertPublisher publisher,
        ILogger<SafeOperationalAlertNotifier> logger)
    {
        _router = router;
        _formatter = formatter;
        _noiseSuppressor = noiseSuppressor;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task NotifyAsync(OperationalAlert alert, CancellationToken cancellationToken)
    {
        if (!_router.ShouldNotify(alert.Category))
            return;

        if (_noiseSuppressor.ShouldSuppress(alert))
            return;

        var enriched = alert with { Message = string.IsNullOrWhiteSpace(alert.Message) ? _formatter.Format(alert) : alert.Message, OccurredAt = alert.OccurredAt ?? DateTimeOffset.UtcNow };

        try
        {
            await _publisher.PublishAsync(enriched, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operational alert publish failed for category {Category} and run {PipelineRunId}", alert.Category, alert.PipelineRunId);
        }
    }
}

public sealed class RoutingStageAlertPublisher : IStageAlertPublisher
{
    private readonly IPipelineRepository _repository;
    private readonly IOperationalAlertNotifier _notifier;

    public RoutingStageAlertPublisher(IPipelineRepository repository, IOperationalAlertNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task PublishSlowStageAsync(StageAlertContext context, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(context.PipelineRunId, cancellationToken);
        var alert = new OperationalAlert(
            AlertCategory.StageSlow,
            string.Empty,
            context.PipelineRunId,
            context.StageName,
            run?.ContentType,
            run?.RunDate,
            run?.LocationName,
            context.DurationMs,
            context.ErrorMessage,
            OccurredAt: context.FinishedAt ?? DateTimeOffset.UtcNow);
        await _notifier.NotifyAsync(alert, cancellationToken);
    }

    public async Task PublishStageFailureAsync(StageAlertContext context, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(context.PipelineRunId, cancellationToken);
        var alert = new OperationalAlert(
            AlertCategory.StageFailed,
            string.Empty,
            context.PipelineRunId,
            context.StageName,
            run?.ContentType,
            run?.RunDate,
            run?.LocationName,
            context.DurationMs,
            context.ErrorMessage,
            OccurredAt: context.FinishedAt ?? DateTimeOffset.UtcNow);
        await _notifier.NotifyAsync(alert, cancellationToken);
    }
}
