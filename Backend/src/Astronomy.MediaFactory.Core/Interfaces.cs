using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;


public interface IAstronomyEventDiscoveryService
{
    Task<IReadOnlyCollection<AstronomyEvent>> RefreshAsync(int? days, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(int? days, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AstronomyEvent>> GetTopAsync(int? days, CancellationToken cancellationToken);
    Task<AstronomyEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken);
    async Task<IReadOnlyCollection<AstronomyEvent>> RefreshEventsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
        => await RefreshAsync(Math.Max(1, toDate.DayNumber - fromDate.DayNumber + 1), cancellationToken);
    async Task<IReadOnlyCollection<AstronomyEvent>> DiscoverEventsForRegionAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
        => (await GetUpcomingAsync(1, cancellationToken)).Where(e => (e.TargetDate == default ? DateOnly.FromDateTime((e.PeakUtc ?? e.StartUtc).UtcDateTime) : e.TargetDate) == targetDate && (e.GlobalVisibility || e.RegionId is null || string.Equals(e.RegionId, regionId, StringComparison.OrdinalIgnoreCase) || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase)))).ToArray();
    async Task<IReadOnlyCollection<AstronomyEvent>> GetTopEventsAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken)
        => (await GetTopAsync(1, cancellationToken)).Where(e => (e.TargetDate == default ? DateOnly.FromDateTime((e.PeakUtc ?? e.StartUtc).UtcDateTime) : e.TargetDate) == targetDate && (e.GlobalVisibility || e.RegionId is null || string.Equals(e.RegionId, regionId, StringComparison.OrdinalIgnoreCase) || e.VisibilityRegions.Any(r => r.Contains(regionId, StringComparison.OrdinalIgnoreCase)))).ToArray();
}

public interface IAstronomyEventScoringService
{
    Task<IReadOnlyCollection<AstronomyEvent>> ScoreAsync(IReadOnlyCollection<AstronomyEvent> events, DateTimeOffset now, CancellationToken cancellationToken);
    AstronomyEvent Score(AstronomyEvent astronomyEvent, DateTimeOffset now);
}

public interface IAstronomyEventStore
{
    Task UpsertEventsAsync(IReadOnlyCollection<AstronomyEvent> events, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AstronomyEvent>> GetUpcomingAsync(DateOnly fromDate, DateOnly toDate, string? regionId, CancellationToken cancellationToken);
    Task<AstronomyEvent?> GetByEventIdAsync(string eventId, CancellationToken cancellationToken);
    Task<bool> HasGenerationHistoryAsync(string eventId, string regionId, DateOnly targetDate, ContentType contentType, CancellationToken cancellationToken);
    Task AddGenerationHistoryAsync(Guid astronomyEventId, Guid pipelineRunId, string regionId, DateOnly targetDate, ContentType contentType, string generationMode, CancellationToken cancellationToken);
}

public interface IAstronomyEventDecisionService
{
    Task<EventContentDecision> DecideAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken);
}

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
public interface IContentMonetizationService
{
    Task<MonetizationPlan> BuildPlanAsync(MonetizationInput input, CancellationToken cancellationToken);
}
public interface ISpeechSynthesisService { Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken); }
public interface IVideoRenderService { Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken); }
public interface IShortsVideoRenderService { Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken); }
public interface IShortFormPlatformMetadataFormatter { PlatformPublicationTarget FormatTarget(ShortFormPlatform platform, ShortFormPublicationRequest request); }
public interface IShortFormPlatformPublisher { ShortFormPlatform Platform { get; } Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken); }
public interface IShortFormPublishingService { Task<IReadOnlyCollection<PlatformPublicationTarget>> PublishAsync(ShortFormPublicationRequest request, CancellationToken cancellationToken); }

public interface IPlatformPublishService
{
    string PlatformName { get; }
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken);
}

public interface IContentPublishService
{
    Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublishResult>> PublishForPipelineRunAsync(Guid pipelineRunId, string asset, CancellationToken cancellationToken)
        => PublishForPipelineRunAsync(pipelineRunId, cancellationToken);
}

public interface IYouTubePublishService : IPlatformPublishService
{
}

public interface IMetaPublishService
{
    Task<IReadOnlyList<MetaPublishResult>> PublishForPipelineRunAsync(
        Guid pipelineRunId,
        string asset = "all",
        CancellationToken cancellationToken = default);
}

public interface IFacebookReelPublishService
{
    Task<MetaPublishResult> PublishReelAsync(
        MetaPublishRequest request,
        CancellationToken cancellationToken);
}

public interface IInstagramReelPublishService
{
    Task<MetaPublishResult> PublishReelAsync(
        MetaPublishRequest request,
        CancellationToken cancellationToken);
}



public interface ITokenHealthService
{
    Task<IReadOnlyList<TokenHealthResult>> CheckAllAsync(CancellationToken cancellationToken);
    Task<TokenHealthResult> CheckYouTubeAsync(CancellationToken cancellationToken);
    Task<TokenHealthResult> CheckMetaAsync(CancellationToken cancellationToken);
}

public interface IYouTubeAuthService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public interface IYouTubeOAuthService
{
    string BuildAuthorizationUrl();
    Task<YouTubeOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken);
}

public interface IMetaOAuthService
{
    string BuildAuthorizationUrl();
    Task<MetaOAuthSetupResult> CompleteSetupAsync(string code, CancellationToken cancellationToken);
}

public interface IYouTubeApiClient
{
    Task<YouTubeChannelInfo> GetAuthenticatedChannelAsync(string accessToken, CancellationToken cancellationToken);
    Task<string> UploadVideoAsync(PublishRequest request, string accessToken, CancellationToken cancellationToken);
    Task UploadThumbnailAsync(string videoId, string thumbnailPath, string accessToken, CancellationToken cancellationToken);
}

public interface IAzureBlobStorageService { Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken); }
public interface IPublicMediaStorageService
{
    Task<PublicMediaUploadResult> UploadForInstagramAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken);
}
public interface IYouTubePublishingService { Task<string?> UploadAsync(string videoPath, string title, string description, IReadOnlyCollection<string> tags, string visibility, CancellationToken cancellationToken); }
public interface IYouTubeThumbnailPublisher { Task<bool> UploadThumbnailAsync(string videoId, string thumbnailPath, CancellationToken cancellationToken); }
public interface IYouTubeAnalyticsService { Task<YouTubeVideoAnalyticsSnapshot?> GetVideoAnalyticsAsync(string videoId, CancellationToken cancellationToken); }

public interface IAnalyticsCollectionService
{
    Task CollectRecentAnalyticsAsync(CancellationToken cancellationToken);
    Task CollectForPipelineRunAsync(Guid pipelineRunId, CancellationToken cancellationToken);
}

public interface IPlatformAnalyticsCollector
{
    string Platform { get; }
    Task<PlatformContentAnalytics> CollectAsync(PlatformAnalyticsCollectionContext context, CancellationToken cancellationToken);
}

public interface IYouTubeAnalyticsCollector : IPlatformAnalyticsCollector { }
public interface IFacebookAnalyticsCollector : IPlatformAnalyticsCollector { }
public interface IInstagramAnalyticsCollector : IPlatformAnalyticsCollector { }
public interface IAnalyticsAggregationService
{
    Task<AnalyticsAggregationSummary> BuildSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, int topN, CancellationToken cancellationToken);
}

public interface IAnalyticsIntelligenceService
{
    Task<AnalyticsDashboardResponse> BuildDashboardAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AnalyticsTopContentItem>> GetTopContentAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AnalyticsInsight>> GetInsightsAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AnalyticsPlatformBreakdown>> GetPlatformSummaryAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AnalyticsContentTypeBreakdown>> GetContentPerformanceAsync(AnalyticsIntelligenceRequest request, CancellationToken cancellationToken);
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

public interface IThumbnailGeneratorService
{
    Task<IReadOnlyCollection<string>> GenerateAsync(AstronomyContext context, IReadOnlyCollection<string> screenshots, string outputDirectory, string narrationContext, CancellationToken cancellationToken);
}

public interface ISeoMetadataGeneratorService
{
    Task<SeoMetadataResult> GenerateAsync(SeoMetadataRequest request, CancellationToken cancellationToken);
}

public interface IPrePublishValidationService
{
    Task<PrePublishValidationReport> ValidateAsync(PrePublishValidationRequest request, CancellationToken cancellationToken);
}

public interface IContentExperimentService
{
    Task InitializeExperimentsAsync(PublishedVideo publishedVideo, OptimizedVideoMetadata metadata, ThumbnailPlan thumbnailPlan, MonetizationPlan? monetizationPlan, CancellationToken cancellationToken);
    Task<ExperimentVariantAssignment> ResolveAssignmentsAsync(Guid videoId, CancellationToken cancellationToken);
    Task EvaluateRecentExperimentsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContentExperiment>> GetRecentExperimentsAsync(int take, CancellationToken cancellationToken);
    Task<ContentExperiment?> GetExperimentAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContentExperiment>> GetTopPerformingExperimentsAsync(int take, CancellationToken cancellationToken);
    Task<ExperimentFeedbackSnapshot> GetFeedbackSnapshotAsync(CancellationToken cancellationToken);
}

public interface IOptimizationService
{
    Task<OptimizationPlan> BuildPlanAsync(string locationName, string platform, CancellationToken cancellationToken);
    Task<RunPipelineRequest> ApplyPlanAsync(RunPipelineRequest request, OptimizationPlan plan, CancellationToken cancellationToken);
}

public interface IAIOptimizationService
{
    Task<AIOptimizationRecommendations> GetRecommendationsAsync(CancellationToken cancellationToken);
    Task<AIOptimizationRecommendations> GenerateNowAsync(CancellationToken cancellationToken);
    Task<AIOptimizationRecommendations> GetPendingApprovalAsync(CancellationToken cancellationToken);
    Task<AIOptimizationApplyResult> ApplyApprovedAsync(AIOptimizationApplyRequest request, CancellationToken cancellationToken);
    Task<AIOptimizationApplyResult> RejectAsync(AIOptimizationApplyRequest request, CancellationToken cancellationToken);
    Task<AIOptimizationAppliedProfile?> GetLatestApprovedProfileAsync(CancellationToken cancellationToken);
}

public interface IPipelineRepository {
 Task<PipelineRun> CreateAsync(PipelineRun run, CancellationToken cancellationToken);
 Task<PipelineRun?> GetAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineRun>> GetRecentAsync(int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineRun>> GetGeneratedSpecialEventRunsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineRun>>([]);
 Task<bool> HasSpecialEventRunAsync(string eventId, DateOnly runDate, string regionId, ContentType contentType, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken) => Task.FromResult(false);
 Task<bool> HasPipelineRunAsync(DateOnly runDate, ContentType contentType, string locationName, string timeZone, IReadOnlyCollection<PipelineRunStatus> statuses, CancellationToken cancellationToken) => Task.FromResult(false);
 Task AddScriptAsync(GeneratedScript script, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<GeneratedScript>> GetRecentScriptsAsync(int take, CancellationToken cancellationToken);
 Task AddAssetAsync(MediaAsset asset, CancellationToken cancellationToken);
 Task AddPublishedVideoAsync(PublishedVideo publishedVideo, CancellationToken cancellationToken);
 Task AddShortVideoAsync(ShortVideo shortVideo, CancellationToken cancellationToken);
 Task AddPlatformPublicationRecordAsync(PlatformPublicationRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
 Task AddMonetizationRecordAsync(MonetizationRecord monetizationRecord, CancellationToken cancellationToken) => Task.CompletedTask;
 Task AddJobAsync(PipelineJob job, CancellationToken cancellationToken);
 Task<PipelineJob?> GetJobAsync(Guid id, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineJob>> GetRecentJobsAsync(int take, CancellationToken cancellationToken);
 Task<PipelineJob?> GetNextRunnableJobAsync(DateTimeOffset now, CancellationToken cancellationToken);
 Task<bool> HasQueuedOrCompletedMainJobAsync(DateOnly runDate, ContentType contentType, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PublishedVideo>> GetRecentPublishedVideosAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<GeneratedScript>> GetRecentGeneratedScriptsAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task AddVideoAnalyticsAsync(VideoAnalytics analytics, CancellationToken cancellationToken);
 Task UpsertPlatformContentAnalyticsAsync(PlatformContentAnalytics analytics, CancellationToken cancellationToken) => Task.CompletedTask;
 Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsAsync(PlatformAnalyticsQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformContentAnalytics>>([]);
 Task<IReadOnlyCollection<PlatformContentAnalytics>> GetPlatformContentAnalyticsByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformContentAnalytics>>([]);
 Task<AnalyticsDashboardSummary> GetAnalyticsDashboardSummaryAsync(int days, CancellationToken cancellationToken) => Task.FromResult(new AnalyticsDashboardSummary([], 0, 0, null, null, null));
 Task<IReadOnlyCollection<VideoAnalytics>> GetRecentAnalyticsAsync(int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsWindowAsync(DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByVideoIdAsync(string videoId, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetAnalyticsByContentTypeAsync(ContentType contentType, DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<VideoAnalytics>> GetTopPerformingAnalyticsAsync(DateTimeOffset? from, DateTimeOffset? to, int take, bool shortsOnly, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<ShortVideo>> GetShortVideosWithYouTubeIdAsync(DateTimeOffset from, CancellationToken cancellationToken);
 Task<PlatformPublicationRecord?> GetPlatformPublicationRecordAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<PlatformPublicationRecord?>(null);
 Task<IReadOnlyCollection<PlatformPublicationRecord>> GetRecentPlatformPublicationRecordsAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>([]);
 Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByShortIdAsync(Guid shortVideoId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>([]);
 Task<IReadOnlyCollection<PlatformPublicationRecord>> GetPlatformPublicationRecordsByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PlatformPublicationRecord>>([]);
 Task<GeneratedScript?> GetLatestScriptByTitleAsync(string title, CancellationToken cancellationToken);
 Task<IReadOnlyCollection<PipelineStageExecution>> GetStageExecutionsAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PipelineStageExecution>>([]);
 Task<PipelineStageExecution?> GetLatestStageExecutionAsync(Guid pipelineRunId, string stageName, CancellationToken cancellationToken) => Task.FromResult<PipelineStageExecution?>(null);
 Task AddStageExecutionAsync(PipelineStageExecution stageExecution, CancellationToken cancellationToken) => Task.CompletedTask;
 Task<IReadOnlyCollection<PublishedVideo>> GetPublishedVideosByRunAsync(Guid pipelineRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PublishedVideo>>([]);
 Task SaveChangesAsync(CancellationToken cancellationToken);
}


public interface ISchedulerAuditStore
{
    Task<IReadOnlyCollection<SchedulerRunRecord>> GetRunsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SchedulerRunRecord>> GetRecentRunsAsync(int take, CancellationToken cancellationToken);
    Task UpsertAsync(SchedulerRunRecord record, CancellationToken cancellationToken);
}


public interface IPipelineRunExecutor
{
    Task<PipelineRun> ExecuteAsync(RunPipelineRequest request, Guid? pipelineRunId, CancellationToken cancellationToken);
}

public interface IPipelineRunQueue
{
    int QueuedCount { get; }
    int ActiveCount { get; }
    Task<SchedulerRunResult> EnqueueAsync(SchedulerRunQueueItem item, CancellationToken cancellationToken);
    Task DrainAsync(CancellationToken cancellationToken);
}

public interface IPipelineSchedulerService
{
    Task EvaluateSchedulesAsync(CancellationToken cancellationToken);
    Task<SchedulerStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<SchedulerRunResult> RunNowAsync(string scheduleName, bool force, CancellationToken cancellationToken);
    Task<RegionStatusResponse> GetRegionsAsync(CancellationToken cancellationToken);
    Task<SchedulerRunResult> RunRegionNowAsync(string regionId, bool force, CancellationToken cancellationToken);
    Task<bool> EnableRegionAsync(string regionId, CancellationToken cancellationToken);
    Task<bool> DisableRegionAsync(string regionId, CancellationToken cancellationToken);
    Task<bool> EnableScheduleAsync(string scheduleName, CancellationToken cancellationToken);
    Task<bool> DisableScheduleAsync(string scheduleName, CancellationToken cancellationToken);
    Task RecoverStartupAsync(CancellationToken cancellationToken);
    Task<SchedulerEventPlanResponse> GetEventPlanAsync(string regionId, DateOnly targetDate, CancellationToken cancellationToken);
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



public interface IOpsDashboardService
{
    Task<OpsDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OpsPipelineRunSummary>> GetRunsAsync(DateOnly? date, string? status, CancellationToken cancellationToken);
    Task<OpsPipelineRunDetail?> GetRunAsync(Guid pipelineRunId, CancellationToken cancellationToken);
    Task<FailureOpsSummary> GetFailuresAsync(int days, CancellationToken cancellationToken);
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

public interface IPipelineRecoveryService
{
    Task<PipelineStatusResponse?> GetStatusAsync(Guid pipelineRunId, CancellationToken cancellationToken, bool includeInternal = false);
    Task<PipelineStatusResponse?> ResumeAsync(Guid pipelineRunId, string? forceStage, CancellationToken cancellationToken);
    Task<PipelineStatusResponse?> RetryPublishAsync(Guid pipelineRunId, string platform, CancellationToken cancellationToken);
}
