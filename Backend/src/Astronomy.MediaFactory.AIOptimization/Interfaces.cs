namespace Astronomy.MediaFactory.AIOptimization;
public interface IHookOptimizationService { Task<IReadOnlyCollection<HookScoreResult>> ScoreAsync(HookOptimizationRequest request, CancellationToken cancellationToken); Task<HookOptimizationReport> BuildReportAsync(HookOptimizationRequest request, string outputDirectory, CancellationToken cancellationToken); }
public interface ITrendSignalProvider { Task<IReadOnlyCollection<TrendSignalResult>> GetSignalsAsync(DateOnly date, CancellationToken cancellationToken); }
public interface IPublishingOptimizationService { Task<PublishingOptimizationResult> RecommendAsync(Guid pipelineRunId, string language, string eventType, CancellationToken cancellationToken); }
