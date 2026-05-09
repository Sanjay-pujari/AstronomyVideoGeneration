using System.Collections.Concurrent;
using System.Text.Json;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Scheduling;

public sealed class PipelineRunQueue : IPipelineRunQueue
{
    private static readonly PipelineRunStatus[] DuplicateStatuses = [PipelineRunStatus.Queued, PipelineRunStatus.Running, PipelineRunStatus.Succeeded];
    private readonly ConcurrentQueue<SchedulerRunQueueItem> _queue = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISchedulerAuditStore _auditStore;
    private readonly IOptionsMonitor<SchedulerOptions> _options;
    private readonly ILogger<PipelineRunQueue> _logger;
    private int _activeCount;

    public PipelineRunQueue(IServiceScopeFactory scopeFactory, ISchedulerAuditStore auditStore, IOptionsMonitor<SchedulerOptions> options, ILogger<PipelineRunQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _auditStore = auditStore;
        _options = options;
        _logger = logger;
    }

    public int QueuedCount => _queue.Count;
    public int ActiveCount => Volatile.Read(ref _activeCount);

    public async Task<SchedulerRunResult> EnqueueAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken)
    {
        if (!item.Force && await HasDuplicateAsync(item, cancellationToken))
        {
            const string reason = "Duplicate scheduler or pipeline run already exists for schedule/date/location.";
            _logger.LogInformation("Scheduler duplicate skipped for {ScheduleName} on {TargetDate}: {Reason}", item.ScheduleName, item.Request.Date, reason);
            await UpsertAuditAsync(item, "Skipped", reason, null, null, cancellationToken);
            return new SchedulerRunResult(false, "Skipped", reason, null, item.Request.Date, item.PlannedRunUtc);
        }

        _queue.Enqueue(item);
        await UpsertAuditAsync(item, "Created", null, null, null, cancellationToken);
        _logger.LogInformation("Scheduler run created for {ScheduleName} on {TargetDate}; queue depth is {QueuedRuns}", item.ScheduleName, item.Request.Date, _queue.Count);
        await DrainAsync(cancellationToken);
        return new SchedulerRunResult(true, "Created", null, null, item.Request.Date, item.PlannedRunUtc);
    }

    public Task DrainAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
            && ActiveCount < Math.Max(1, _options.CurrentValue.MaxConcurrentRuns)
            && _queue.TryDequeue(out var item))
        {
            Interlocked.Increment(ref _activeCount);
            _ = Task.Run(() => ExecuteItemAsync(item, cancellationToken), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteItemAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken)
    {
        try
        {
            if (!item.Force && await HasPipelineDuplicateAsync(item, cancellationToken))
            {
                const string reason = "Duplicate pipeline run was detected before start.";
                _logger.LogInformation("Scheduler duplicate skipped before start for {ScheduleName} on {TargetDate}: {Reason}", item.ScheduleName, item.Request.Date, reason);
                await UpsertAuditAsync(item, "Skipped", reason, null, null, cancellationToken);
                return;
            }

            var actualRunUtc = DateTimeOffset.UtcNow;
            await UpsertAuditAsync(item, "Running", null, actualRunUtc, null, cancellationToken);
            _logger.LogInformation("Scheduler run started for {ScheduleName} on {TargetDate}", item.ScheduleName, item.Request.Date);

            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IPipelineRunExecutor>();
            var run = await executor.ExecuteAsync(item.Request, cancellationToken);
            await WriteOptimizationDiagnosticsAsync(item, run, cancellationToken);
            await UpsertAuditAsync(item, run.Status == PipelineRunStatus.Succeeded ? "Completed" : run.Status.ToString(), null, actualRunUtc, run.Id, CancellationToken.None);
            _logger.LogInformation("Scheduler run completed for {ScheduleName} on {TargetDate} with pipeline {PipelineRunId} status {Status}", item.ScheduleName, item.Request.Date, run.Id, run.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduler run failed for {ScheduleName} on {TargetDate}", item.ScheduleName, item.Request.Date);
            await UpsertAuditAsync(item, "Failed", ex.Message, DateTimeOffset.UtcNow, null, CancellationToken.None);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
            await DrainAsync(CancellationToken.None);
        }
    }


    private static async Task WriteOptimizationDiagnosticsAsync(SchedulerRunQueueItem item, PipelineRun run, CancellationToken cancellationToken)
    {
        if (item.OptimizationPlan is null || string.IsNullOrWhiteSpace(run.OutputFolder))
            return;

        Directory.CreateDirectory(run.OutputFolder);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "optimization-plan.json"), JsonSerializer.Serialize(item.OptimizationPlan, options), cancellationToken);
        if (item.OriginalRequest is not null && item.OriginalRequest != item.Request)
        {
            var payload = new OptimizationApplyResult { OriginalRequest = item.OriginalRequest, ResultRequest = item.Request, Plan = item.OptimizationPlan, ChangedFields = ChangedFields(item.OriginalRequest, item.Request), Mode = OptimizationMode.ApplySafeRules.ToString() };
            await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "optimization-applied.json"), JsonSerializer.Serialize(payload, options), cancellationToken);
        }
    }

    private static IReadOnlyCollection<string> ChangedFields(RunPipelineRequest original, RunPipelineRequest result)
    {
        var changed = new List<string>();
        if (original.UseTopicPlanner != result.UseTopicPlanner) changed.Add(nameof(RunPipelineRequest.UseTopicPlanner));
        if (original.TimeZone != result.TimeZone) changed.Add(nameof(RunPipelineRequest.TimeZone));
        if (original.LocationName != result.LocationName) changed.Add(nameof(RunPipelineRequest.LocationName));
        if (original.Date != result.Date) changed.Add(nameof(RunPipelineRequest.Date));
        return changed;
    }

    private async Task<bool> HasDuplicateAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken)
        => await HasSchedulerDuplicateAsync(item, cancellationToken) || await HasPipelineDuplicateAsync(item, cancellationToken);

    private async Task<bool> HasSchedulerDuplicateAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken)
    {
        var runs = await _auditStore.GetRunsAsync(cancellationToken);
        return runs.Any(x => x.ScheduleName.Equals(item.ScheduleName, StringComparison.OrdinalIgnoreCase)
            && x.TargetDate == item.Request.Date
            && x.LocationName.Equals(item.Request.LocationName, StringComparison.OrdinalIgnoreCase)
            && x.Status is "Created" or "Running" or "Completed" or "Publishing" or "Recoverable");
    }

    private async Task<bool> HasPipelineDuplicateAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPipelineRepository>();
        return await repository.HasPipelineRunAsync(item.Request.Date, item.Request.ContentType, item.Request.LocationName, item.Request.TimeZone, DuplicateStatuses, cancellationToken);
    }

    private Task UpsertAuditAsync(SchedulerRunQueueItem item, string status, string? skipReason, DateTimeOffset? actualRunUtc, Guid? pipelineRunId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return _auditStore.UpsertAsync(new SchedulerRunRecord(
            item.ScheduleName,
            item.Request.Date,
            item.PlannedRunUtc,
            actualRunUtc,
            pipelineRunId,
            status,
            skipReason,
            item.Request.LocationName,
            item.Request.TimeZone,
            CreatedUtc: now,
            UpdatedUtc: now), cancellationToken);
    }
}
