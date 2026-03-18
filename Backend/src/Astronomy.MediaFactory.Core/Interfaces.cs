using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;

public interface IAstronomyContextProvider { Task<AstronomyContext> BuildContextAsync(DateOnly date, ContentType contentType, string locationName, string timeZone, CancellationToken cancellationToken); }
public interface ITopicRankingService { Task<IReadOnlyCollection<RankedTopic>> RankAsync(AstronomyContext context, ContentType contentType, CancellationToken cancellationToken); }
public interface ITopicSelectionService
{
    Task<TopicSelectionPlan> BuildPlanAsync(TopicSelectionRequest request, CancellationToken cancellationToken);
}
public interface IVisualAssetProvider { Task<IReadOnlyCollection<string>> PrepareVisualsAsync(AstronomyContext context, string outputDirectory, CancellationToken cancellationToken); }
public interface IScriptGenerationService { Task<ScriptResult> GenerateAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken); }
public interface IShortsScriptGenerationService { Task<ShortScriptResult> GenerateShortAsync(ContentType contentType, AstronomyContext context, CancellationToken cancellationToken); }
public interface IMetadataOptimizationService
{
    Task<OptimizedVideoMetadata> OptimizeForVideoAsync(MetadataOptimizationInput input, CancellationToken cancellationToken);
    Task<OptimizedVideoMetadata> OptimizeForShortAsync(MetadataOptimizationInput input, CancellationToken cancellationToken);
}
public interface ISpeechSynthesisService { Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken); }
public interface IVideoRenderService { Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken); }
public interface IShortsVideoRenderService { Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken); }
public interface IAzureBlobStorageService { Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken); }
public interface IYouTubePublishingService { Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken); }
public interface IYouTubeThumbnailPublisher { Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken); }
public interface IYouTubeAnalyticsService { Task<YouTubeVideoAnalyticsSnapshot?> GetVideoAnalyticsAsync(string videoId, CancellationToken cancellationToken); }
public interface IAnalyticsAggregationService
{
    Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken);
}
public interface IAnalyticsFeedbackProvider
{
    Task<FeedbackSignals> GetSignalsAsync(int topN, CancellationToken cancellationToken);
    Task<AnalyticsAggregationSummary> GetSummaryAsync(int topN, CancellationToken cancellationToken);
}

public interface IPromptFeedbackService
{
    Task<PromptFeedbackContext> BuildContextAsync(PromptFeedbackRequest request, CancellationToken cancellationToken);
}

public interface IFeedbackSignalExtractor
{
    void Extract(AnalyticsAggregationSummary summary, int topN, FeedbackSignalCollector collector);
}

public interface IThumbnailStrategyService
{
    ThumbnailPlan BuildPlan(ThumbnailGenerationRequest request);
}

public interface IThumbnailGenerationService
{
    Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken);
}

public interface IPipelineRepository {
 Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken);
 Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken);
 Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken);
 Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken);
 Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken);
 Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken);
 Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken);
 Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken);
 Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken);
 Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken);
 Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPipelineStageRecorder
{
    Task<PipelineStageExecution> StartStageAsync(Guid pipelineRunId, string stageName, string? metadataJson, CancellationToken cancellationToken);
    Task CompleteStageAsync(PipelineStageExecution stageExecution, string? metadataJson, CancellationToken cancellationToken);
    Task FailStageAsync(PipelineStageExecution stageExecution, string errorMessage, bool continuedWithFallback, string? metadataJson, CancellationToken cancellationToken);
}


public interface IStageAlertPublisher
{
    Task PublishSlowStageAsync(StageAlertContext context, CancellationToken cancellationToken);
    Task PublishStageFailureAsync(StageAlertContext context, CancellationToken cancellationToken);
}

public interface IPipelineMonitoringService
{
    Task<PipelineOpsSummary> GetSummaryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PipelineRun>> GetRecentPipelinesAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PipelineStageExecution>> GetPipelineStagesAsync(Guid pipelineRunId, CancellationToken cancellationToken);
    Task<RecentFailuresSnapshot> GetRecentFailuresAsync(int take, CancellationToken cancellationToken);
    Task<JobOpsSummary> GetJobSummaryAsync(CancellationToken cancellationToken);
}

public interface IPipelineJobQueue
{
    Task<PipelineJob> EnqueueAsync(EnqueuePipelineJobRequest request, CancellationToken cancellationToken);
}

public interface IPipelineJobExecutor
{
    Task ExecuteAsync(PipelineJob job, CancellationToken cancellationToken);
}


public interface IRunOperationsService
{
    Task<OpsActionResult> ReplayRunAsync(Guid runId, ReplayPipelineRequest request, CancellationToken cancellationToken);
    Task<OpsActionResult> RetryPublishAsync(Guid runId, RetryPublishRequest request, CancellationToken cancellationToken);
    Task<OpsActionResult> RetryArchiveAsync(Guid runId, RetryArchiveRequest request, CancellationToken cancellationToken);
    Task<OpsActionResult> RegenerateShortsAsync(Guid runId, RegenerateShortsRequest request, CancellationToken cancellationToken);
    Task<OpsActionResult> RerunMetadataOptimizationAsync(Guid runId, RerunMetadataOptimizationRequest request, CancellationToken cancellationToken);
    Task<OpsActionResult> RequeueJobAsync(Guid jobId, RequeueJobRequest request, CancellationToken cancellationToken);
    Task<StaleJobRecoverySummary> RecoverStaleJobsAsync(RecoverStaleJobsRequest request, CancellationToken cancellationToken);
}

public interface IMaintenanceService
{
    Task<MaintenanceCleanupSummary> CleanupAsync(CleanupMaintenanceRequest request, CancellationToken cancellationToken);
}
