using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineStageExecutor : IPipelineStageExecutor
{
    private readonly IPipelineRepository _repository;
    private readonly ILogger<PipelineStageExecutor> _logger;

    public PipelineStageExecutor(IPipelineRepository repository, ILogger<PipelineStageExecutor> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<T> ExecuteStageAsync<T>(
        Guid pipelineRunId,
        string stageName,
        Func<CancellationToken, Task<T>> action,
        StageExecutionOptions options,
        CancellationToken cancellationToken)
    {
        options ??= new StageExecutionOptions();
        var maxAttempts = Math.Max(1, options.MaxAttempts);
        var existing = await _repository.GetLatestStageExecutionAsync(pipelineRunId, stageName, cancellationToken);
        if (options.AllowSkipIfAlreadySucceeded
            && existing?.Status == PersistentStageStatuses.Succeeded
            && (string.IsNullOrWhiteSpace(existing.OutputPath) || File.Exists(existing.OutputPath)))
        {
            existing.Status = PersistentStageStatuses.Skipped;
            existing.CompletedUtc = DateTimeOffset.UtcNow;
            existing.DiagnosticPath = options.DiagnosticPath ?? existing.DiagnosticPath;
            await _repository.SaveChangesAsync(cancellationToken);
            await WriteStateAsync(pipelineRunId, stageName, retryable: false, cancellationToken);
            return default!;
        }

        Exception? lastException = null;
        for (var attempt = (existing?.AttemptCount ?? 0) + 1; attempt <= maxAttempts; attempt++)
        {
            var stage = existing ?? new PipelineStageExecution
            {
                PipelineRunId = pipelineRunId,
                StageName = stageName
            };
            stage.Status = PersistentStageStatuses.Running;
            stage.StartedUtc = DateTimeOffset.UtcNow;
            stage.CompletedUtc = null;
            stage.AttemptCount = attempt;
            stage.MaxAttempts = maxAttempts;
            stage.OutputPath = options.OutputPath ?? stage.OutputPath;
            stage.DiagnosticPath = options.DiagnosticPath ?? stage.DiagnosticPath;
            stage.LastError = lastException?.ToString();
            if (existing is null)
            {
                await _repository.AddStageExecutionAsync(stage, cancellationToken);
                existing = stage;
            }
            await _repository.SaveChangesAsync(cancellationToken);
            await WriteStateAsync(pipelineRunId, stageName, retryable: false, cancellationToken);

            try
            {
                var result = await action(cancellationToken);
                stage.Status = PersistentStageStatuses.Succeeded;
                stage.CompletedUtc = DateTimeOffset.UtcNow;
                stage.DurationMs = (long)Math.Max(0, (stage.CompletedUtc.Value - stage.StartedAt).TotalMilliseconds);
                stage.LastError = null;
                await _repository.SaveChangesAsync(cancellationToken);
                await WriteStateAsync(pipelineRunId, stageName, retryable: false, cancellationToken);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                var retryable = (options.IsRetryableExceptionFunc ?? (e => PipelineRetryClassifier.IsRetryable(e, options.OutputPath)))(ex);
                stage.Status = PersistentStageStatuses.Failed;
                stage.CompletedUtc = DateTimeOffset.UtcNow;
                stage.DurationMs = (long)Math.Max(0, (stage.CompletedUtc.Value - stage.StartedAt).TotalMilliseconds);
                stage.LastError = ex.ToString();
                await _repository.SaveChangesAsync(cancellationToken);
                await WriteStateAsync(pipelineRunId, stageName, retryable, cancellationToken);

                if (!retryable || attempt >= maxAttempts)
                {
                    _logger.LogError(ex, "Pipeline stage {StageName} failed for run {PipelineRunId} after {AttemptCount}/{MaxAttempts} attempts", stageName, pipelineRunId, attempt, maxAttempts);
                    throw;
                }

                var delaySeconds = options.RetryDelaySeconds * Math.Pow(Math.Max(1, options.RetryBackoffMultiplier), attempt - 1);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException($"Stage {stageName} failed without an exception.");
    }

    private async Task WriteStateAsync(Guid pipelineRunId, string currentStage, bool retryable, CancellationToken cancellationToken)
    {
        var run = await _repository.GetAsync(pipelineRunId, cancellationToken);
        if (string.IsNullOrWhiteSpace(run?.OutputFolder))
            return;

        Directory.CreateDirectory(run.OutputFolder);
        var stages = await _repository.GetStageExecutionsAsync(pipelineRunId, cancellationToken);
        var failed = stages.FirstOrDefault(s => s.Status == PersistentStageStatuses.Failed);
        var payload = new
        {
            runId = pipelineRunId,
            overallStatus = run.Status.ToString(),
            stages = stages.Select(s => new PipelineStageStatusDto(s.StageName, s.Status, s.AttemptCount, s.MaxAttempts, s.StartedUtc, s.CompletedUtc, s.LastError, s.OutputPath, s.DiagnosticPath)),
            currentStage,
            failedStage = failed?.StageName,
            retryable,
            resumeCommandSuggestion = $"POST /api/pipeline/resume/{pipelineRunId}"
        };
        await File.WriteAllTextAsync(Path.Combine(run.OutputFolder, "pipeline-state.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
