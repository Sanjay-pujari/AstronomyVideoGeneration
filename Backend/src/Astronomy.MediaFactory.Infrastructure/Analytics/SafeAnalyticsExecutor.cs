using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Astronomy.MediaFactory.Infrastructure.Analytics;

public sealed class SafeAnalyticsExecutor : ISafeAnalyticsExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SafeAnalyticsExecutor> _logger;

    public SafeAnalyticsExecutor(IServiceScopeFactory scopeFactory, ILogger<SafeAnalyticsExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<SafeAnalyticsExecutionResult> ExecuteInitializationAsync(AnalyticsPipelineInitializationRequest request, string outputDirectory, CancellationToken cancellationToken)
    {
        var report = new SafeAnalyticsExecutionResult(false, false, false, false, true, 0, null, false);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            report = report with { AnalyticsStarted = true };
            using var scope = _scopeFactory.CreateScope();
            report = report with { ScopeCreated = true };

            var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
            _logger.LogInformation("Analytics scope created. DbContextHash={DbContextHash}; PipelineRunId={PipelineRunId}", db.GetHashCode(), request.PipelineRunId);

            var ingestion = scope.ServiceProvider.GetRequiredService<IAnalyticsIngestionService>();
            await ingestion.InitializeForPipelineRunAsync(request, timeoutCts.Token);
            report = report with { AnalyticsCompleted = true, QueriesMaterialized = 1 };
            _logger.LogInformation("Analytics completed safely for pipeline run {PipelineRunId}", request.PipelineRunId);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            report = report with { AnalyticsFailed = true, TimedOut = true, Exception = ex.Message };
            _logger.LogWarning(ex, "Analytics initialization timed out and was canceled safely for pipeline run {PipelineRunId}", request.PipelineRunId);
        }
        catch (Exception ex)
        {
            report = report with { AnalyticsFailed = true, Exception = ex.ToString() };
            _logger.LogWarning(ex, "Analytics initialization failed safely for pipeline run {PipelineRunId}", request.PipelineRunId);
        }

        var reportPayload = new
        {
            analyticsStarted = report.AnalyticsStarted,
            analyticsCompleted = report.AnalyticsCompleted,
            analyticsFailed = report.AnalyticsFailed,
            exception = report.Exception,
            scopeCreated = report.ScopeCreated,
            dbContextIsolated = report.DbContextIsolated,
            queriesMaterialized = report.QueriesMaterialized,
            pipelineImpacted = false
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "analytics-post-processing-report.json"), JsonSerializer.Serialize(reportPayload, JsonOptions), CancellationToken.None);
        return report;
    }
}
