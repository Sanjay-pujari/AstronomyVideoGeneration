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
public interface ISpeechSynthesisService
{
    Task<string> SynthesizeAsync(string script, string outputDirectory, CancellationToken cancellationToken);
    Task SynthesizeToFileAsync(string script, string outputPath, CancellationToken cancellationToken);
}
public interface IVideoRenderService { Task<string> RenderAsync(RenderManifest manifest, CancellationToken cancellationToken); }
public interface IShortsVideoRenderService { Task<ShortVideoRenderResult> RenderAsync(ContentType contentType, AstronomyContext context, IReadOnlyCollection<string> sourceVisuals, string outputDirectory, bool publishToYouTube, CancellationToken cancellationToken); }
public interface IShortFormPlatformMetadataFormatter { PlatformPublicationTarget FormatTarget(ShortFormPlatform platform, ShortFormPublicationRequest request); }
public interface IShortFormPlatformPublisher { ShortFormPlatform Platform { get; } Task<PlatformPublicationTarget> PublishAsync(PlatformPublicationTarget target, CancellationToken cancellationToken); }
public interface IShortFormPublishingService { Task<IReadOnlyCollection<PlatformPublicationTarget>> PublishAsync(ShortFormPublicationRequest request, CancellationToken cancellationToken); }


public interface IPlatformThumbnailResolver
{
    Task<PlatformThumbnailResolution> ResolveAsync(
        string outputDirectory,
        string platform,
        string contentType,
        CancellationToken cancellationToken);
}

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

public interface IMetaPosterFrameFallbackService
{
    Task<MetaPosterFrameFallbackResult> ApplyAsync(
        string outputDirectory,
        string inputShortVideoPath,
        string posterFrameImagePath,
        double durationSeconds,
        CancellationToken cancellationToken);
}

public sealed record MetaPosterFrameFallbackResult(
    bool PosterFrameApplied,
    string PosterFrameImagePath,
    double PosterFrameDurationSeconds,
    string InputShortVideoPath,
    string OutputMetaVideoPath,
    string Reason);

public interface IFacebookVideoPublishService
{
    Task<MetaPublishResult> PublishVideoAsync(MetaPublishRequest request, CancellationToken cancellationToken);
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
    Task<YouTubeVideoPostUploadStatus?> GetVideoPostUploadStatusAsync(string videoId, string accessToken, CancellationToken cancellationToken);
}

public interface IAzureBlobStorageService { Task<BlobUploadResult> UploadAsync(BlobUploadRequest request, CancellationToken cancellationToken); }
public interface IPublicMediaStorageService
{
    Task<PublicMediaUploadResult> UploadForInstagramAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken);
    Task<PublicMediaUploadResult> UploadPublicAssetAsync(string localFilePath, Guid pipelineRunId, string assetFileName, string contentType, CancellationToken cancellationToken);
}

public interface IMetaThumbnailAssetPublisher
{
    Task<PublicMediaUploadResult> UploadThumbnailAsync(string localFilePath, Guid pipelineRunId, CancellationToken cancellationToken);
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

public interface IContentVarietyGuard
{
    Task<bool> CanUseCelestialObjectAsync(string categoryCode, string objectCode, DateTimeOffset date, CancellationToken cancellationToken);
    Task<bool> CanUseStyleAsync(string categoryCode, string styleCode, string styleType, DateTimeOffset date, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContentVarietyBlockedItem>> GetBlockedItemsAsync(string categoryCode, DateTimeOffset date, CancellationToken cancellationToken);
}

public sealed record ContentVarietyBlockedItem(string RuleType, string RuleKey, string Reason);

public interface IContentPlanningService
{
    Task<GenerateContentPlanResponse> GeneratePlanAsync(
        GenerateContentPlanRequest request,
        CancellationToken cancellationToken);
    Task<ContentGenerationPlan> GenerateDailyPlanAsync(
        string contentCategoryCode,
        string language,
        string regionId,
        DateTimeOffset scheduledUtc,
        string? primaryCelestialObjectCode,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContentGenerationPlan>> GetPendingPlansAsync(string? status, CancellationToken cancellationToken);
    Task<ContentGenerationPlan?> GetPlanByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DailySkyGuideContext> BuildDailySkyGuideContextPreviewAsync(Guid id, CancellationToken cancellationToken);
    Task<AstronomyVisibilityResult> BuildAstronomyVisibilityPreviewAsync(Guid id, CancellationToken cancellationToken);
    Task<StellariumSceneCapturePlan> BuildStellariumScenePlanPreviewAsync(Guid id, CancellationToken cancellationToken);
    Task<PipelineBuildResult> BuildPipelineRequestPreviewAsync(Guid id, CancellationToken cancellationToken);
    Task<PrepareManualRunResponse?> PrepareManualRunAsync(Guid id, CancellationToken cancellationToken);
    Task<ContentGenerationPlan?> MarkPlanReadyForManualRunAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanAsInProgressAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanAsCompletedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> MarkPlanAsFailedAsync(Guid id, CancellationToken cancellationToken);
    Task<ManualExecutionStartResponse?> StartManualExecutionAsync(Guid id, CancellationToken cancellationToken);
    Task<ContentPipelineExecution?> CompleteExecutionAsync(Guid executionId, CompleteContentPlanningExecutionRequest request, CancellationToken cancellationToken);
    Task<ContentPipelineExecution?> FailExecutionAsync(Guid executionId, FailContentPlanningExecutionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContentPipelineExecution>> GetExecutionsAsync(string? status, CancellationToken cancellationToken);
    Task<ContentPipelineExecution?> GetExecutionByIdAsync(Guid executionId, CancellationToken cancellationToken);
}

public sealed record CategoryPipelineRequirement(
    string ContentCategoryCode,
    bool RequiresSkyfield,
    bool RequiresStellarium,
    bool RequiresSscScript,
    bool RequiresAiImages,
    bool RequiresNasaImages,
    bool RequiresEducationalDiagrams,
    bool RequiresVoiceNarration,
    bool RequiresThumbnail,
    string PrimaryInformationSource,
    string PrimaryVisualSource,
    string NarrationSource,
    string ThumbnailStrategy,
    IReadOnlyList<string> RequiredDataPoints,
    IReadOnlyList<string> VisualAssetTypes,
    IReadOnlyList<string> Warnings);

public interface ICategoryRequirementResolver
{
    Task<CategoryPipelineRequirement> ResolveAsync(string contentCategoryCode, CancellationToken cancellationToken);
}

public sealed record VisualStrategyPlan(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string PrimaryVisualSource,
    bool UseStellariumCapture,
    bool UseSscScript,
    bool UseAiImageGeneration,
    bool UseNasaImageSearch,
    bool UseEducationalDiagramGenerator,
    bool UseVoiceNarration,
    bool UseThumbnailGeneration,
    IReadOnlyList<string> AssetTypesToGenerate,
    IReadOnlyList<string> RequiredDataPoints,
    IReadOnlyList<string> Warnings);

public interface IVisualStrategyResolver
{
    Task<VisualStrategyPlan> ResolveAsync(ContentGenerationPlan plan, CancellationToken cancellationToken);
}


public sealed record AstronomyVisibilityRequest(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly TargetDate,
    string? PreferredObjectCode,
    string Language = "en");

public sealed record VisibleCelestialObjectResult(
    string ObjectCode,
    string ObjectName,
    string ObjectType,
    bool Visible,
    DateTime? RiseUtc,
    DateTime? SetUtc,
    DateTime? TransitUtc,
    DateTime? BestViewingStartUtc,
    DateTime? BestViewingEndUtc,
    double AltitudeScore,
    double VisibilityScore,
    double PhotographyScore,
    double EducationalScore,
    double ViralityScore,
    string? Reason);

public sealed record AstronomyVisibilityResult(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly TargetDate,
    DateTime SunsetUtc,
    DateTime SunriseUtc,
    DateTime BestViewingStartUtc,
    DateTime BestViewingEndUtc,
    string MoonPhase,
    double MoonIlluminationPercent,
    IReadOnlyList<VisibleCelestialObjectResult> VisibleObjects,
    IReadOnlyList<string> Warnings);

public interface IAstronomyVisibilityService
{
    Task<AstronomyVisibilityResult> CalculateVisibilityAsync(AstronomyVisibilityRequest request, CancellationToken cancellationToken);
}

public sealed record SkyfieldVisibilityRequest(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly TargetDate,
    IReadOnlyList<string> ObjectCodes);

public sealed record SkyfieldVisibilityObjectResult(
    string ObjectCode,
    bool Visible,
    DateTime? RiseUtc,
    DateTime? SetUtc,
    DateTime? TransitUtc,
    double MaxAltitudeDegrees,
    DateTime? BestViewingStartUtc,
    DateTime? BestViewingEndUtc,
    double AltitudeScore,
    string? Reason);

public sealed record SkyfieldVisibilityResponse(
    bool Success,
    DateTime? SunsetUtc,
    DateTime? SunriseUtc,
    string? MoonPhase,
    double? MoonIlluminationPercent,
    IReadOnlyList<SkyfieldVisibilityObjectResult> Objects,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage);

public interface ISkyfieldVisibilityClient
{
    Task<SkyfieldVisibilityResponse> CalculateAsync(SkyfieldVisibilityRequest request, CancellationToken cancellationToken);
}

public interface IDailySkyGuideContextBuilder
{
    Task<DailySkyGuideContext> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken);
}

public sealed record DailySkyGuideContext(
    Guid ContentGenerationPlanId,
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly TargetDate,
    DateTimeOffset BestViewingStartLocal,
    DateTimeOffset BestViewingEndLocal,
    string? PrimaryCelestialObjectCode,
    string? PrimaryCelestialObjectName,
    IReadOnlyList<string> VisibleObjectCodes,
    IReadOnlyList<DateTimeOffset> SceneCaptureTimesUtc,
    string ImageInputSource,
    string AudioSource,
    string ThumbnailStrategy,
    StellariumSceneCapturePlan? SceneCapturePlan,
    int SceneCount,
    IReadOnlyList<string> Warnings);

public sealed record StellariumSceneCapturePlan(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly TargetDate,
    List<StellariumSceneCaptureItem> Scenes,
    List<string> Warnings);

public sealed record StellariumSceneCaptureItem(
    string SceneCode,
    string SceneType,
    string Title,
    string? TargetObjectCode,
    string? TargetObjectName,
    DateTime CaptureTimeUtc,
    string FramingMode,
    double? FieldOfViewDegrees,
    bool ShowConstellationLines,
    bool ShowConstellationLabels,
    bool ShowPlanetLabels,
    bool ShowAzimuthGrid,
    bool ShowEquatorialGrid,
    string OutputImageRole,
    int SortOrder,
    Dictionary<string, string>? Metadata);

public enum SceneGenerationMode
{
    ObjectFocused = 0,
    CompositionFocused = 1,
    Hybrid = 2
}

public sealed record CompositionScene(
    string SceneId,
    string SceneTitle,
    string CompositionType,
    IReadOnlyList<string> IncludedObjects,
    double CameraAzimuth,
    double CameraAltitude,
    double FieldOfView,
    double ZoomLevel,
    bool LabelsEnabled,
    string HighlightStrategy,
    string CapturePath,
    string ScriptPath,
    string MetadataPath);

public interface IStellariumScenePlanner
{
    Task<StellariumSceneCapturePlan> BuildScenePlanAsync(
        ContentGenerationPlan plan,
        AstronomyVisibilityResult visibilityResult,
        CancellationToken cancellationToken);
}

public interface IStellariumScenePlannerResolver
{
    IStellariumScenePlanner? Resolve(string contentCategoryCode);
}

public sealed record StellariumCaptureExecutionRequest(
    Guid ContentGenerationPlanId,
    bool DryRun = false,
    bool OverwriteExisting = false,
    bool Diagnostics = true);
public sealed record StellariumCaptureExecutionApiRequest(
    bool DryRun = false,
    bool OverwriteExisting = false,
    bool Diagnostics = true);

public sealed record StellariumCapturedImageResult(
    string SceneCode,
    string SceneType,
    string OutputImageRole,
    string? TargetObjectCode,
    DateTime CaptureTimeUtc,
    string? ImagePath,
    bool Success,
    string? ErrorMessage,
    string? ScriptPath,
    string? CommandLine,
    int? ExitCode,
    string? StandardOutput,
    string? StandardError,
    string? ScriptContent);

public sealed record StellariumCaptureDiagnosticsResponse(
    Guid ContentGenerationPlanId,
    bool StellariumEnabled,
    string? ExecutablePath,
    bool ExecutableExists,
    string? ScriptsDirectory,
    bool ScriptsDirectoryExists,
    string? CaptureDirectory,
    bool CaptureDirectoryExists,
    int CaptureTimeoutSeconds,
    string? LastExpectedOutputFolder,
    bool CanStartProcess);

public sealed record StellariumCaptureExecutionResponse(
    Guid ContentGenerationPlanId,
    bool Success,
    int RequestedSceneCount,
    int CapturedSceneCount,
    string? OutputFolder,
    List<StellariumCapturedImageResult> Images,
    List<string> Warnings,
    string? ErrorMessage);

public interface IStellariumImageCaptureExecutor
{
    Task<StellariumCaptureExecutionResponse> CaptureAsync(
        StellariumSceneCapturePlan scenePlan,
        StellariumCaptureExecutionRequest request,
        CancellationToken cancellationToken);

    Task<StellariumCaptureDiagnosticsResponse> GetDiagnosticsAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}

public sealed record StellariumScriptGenerationResult(
    Guid ContentGenerationPlanId,
    string SceneCode,
    string SceneType,
    string ScriptPath,
    string OutputImagePath,
    bool Success,
    string? ScriptContent,
    List<string> Warnings,
    string? ErrorMessage);

public interface IStellariumScriptGenerator
{
    Task<StellariumScriptGenerationResult> GenerateAsync(
        StellariumSceneCapturePlan plan,
        StellariumSceneCaptureItem scene,
        CancellationToken cancellationToken);
}

public sealed record DailySkyGuideVisualAssetItem(
    string Role,
    string Path,
    bool Exists);

public sealed record DailySkyGuideVisualAssetPackageResponse(
    Guid ContentGenerationPlanId,
    bool Success,
    string AssetRoot,
    IReadOnlyCollection<DailySkyGuideVisualAssetItem> Assets,
    IReadOnlyCollection<string> Warnings);

public interface IDailySkyGuideVisualAssetPackager
{
    Task<DailySkyGuideVisualAssetPackageResponse> BuildPackageAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}


public sealed record DailySkyGuideVisualAsset(
    string Role,
    string Path,
    bool Exists,
    int SortOrder,
    string? SceneCode,
    string? SceneType,
    string? TargetObjectCode);

public sealed record DailySkyGuideAssetAwareExecutionContext(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string RegionId,
    string LocationName,
    DateOnly TargetDate,
    string Language,
    string? Title,
    string? PrimaryCelestialObjectCode,
    string? ThumbnailCandidatePath,
    List<DailySkyGuideVisualAsset> VisualAssets,
    List<string> RecommendedImageSequence,
    List<string> Warnings);

public sealed record AssetAwareVideoSegment(
    int SortOrder,
    string SegmentCode,
    string SegmentType,
    string VisualRole,
    string? ImagePath,
    bool ImageExists,
    string? SuggestedNarrationPurpose,
    double SuggestedDurationSeconds,
    string? TransitionType,
    Dictionary<string, string>? Metadata);

public sealed record AssetAwareVideoCompositionPlan(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string LocationName,
    DateOnly TargetDate,
    string Language,
    string? Title,
    int TotalSegments,
    List<AssetAwareVideoSegment> Segments,
    List<string> Warnings,
    bool ReadyForComposition);

public sealed record AssetAwarePreviewVideoRequest(
    bool OverwriteExisting = false,
    bool IncludeTransitions = true,
    bool IncludePlaceholderNarration = false,
    bool IncludeTextOverlay = true,
    bool Diagnostics = true);

public sealed record AssetAwarePreviewSegmentResult(
    int SortOrder,
    string SegmentCode,
    string SegmentType,
    string? ImagePath,
    bool ImageExists,
    double DurationSeconds,
    bool IncludedInVideo,
    string? FilterChain,
    string? ErrorMessage);

public sealed record AssetAwarePreviewVideoResponse(
    Guid ContentGenerationPlanId,
    bool Success,
    string? OutputVideoPath,
    string? ThumbnailPath,
    int SegmentCount,
    double EstimatedDurationSeconds,
    List<AssetAwarePreviewSegmentResult> Segments,
    List<string> Warnings,
    string? ErrorMessage,
    string? FfmpegCommandLine = null,
    int? FfmpegExitCode = null,
    string? FfmpegStandardError = null,
    string? FfmpegStandardOutput = null,
    string? ResolvedFfmpegPath = null,
    string? OutputFolder = null);

public sealed record AssetAwarePreviewVideoComposeResult(
    string? OutputVideoPath,
    string? ThumbnailPath,
    string? FfmpegCommandLine,
    int? FfmpegExitCode,
    string? FfmpegStandardError,
    string? FfmpegStandardOutput,
    string? ResolvedFfmpegPath);

public interface IDailySkyGuideVisualAssetProvider
{
    Task<IReadOnlyList<DailySkyGuideVisualAsset>> GetAssetsAsync(
        Guid contentGenerationPlanId,
        CancellationToken cancellationToken);
}

public interface IDailySkyGuideAssetAwareContextService
{
    Task<DailySkyGuideAssetAwareExecutionContext> BuildAsync(
        Guid contentGenerationPlanId,
        CancellationToken cancellationToken);
}

public interface IDailySkyGuideAssetAwareCompositionPlanner
{
    Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}

public interface IAssetAwareCompositionPlanner
{
    string ContentCategoryCode { get; }
    Task<AssetAwareVideoCompositionPlan> BuildAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}

public interface IAssetAwareCompositionPlannerResolver
{
    IAssetAwareCompositionPlanner? Resolve(string contentCategoryCode);
}

public interface IDailySkyGuidePreviewVideoGenerator
{
    Task<AssetAwarePreviewVideoResponse> GenerateAsync(
        Guid contentGenerationPlanId,
        AssetAwarePreviewVideoRequest request,
        CancellationToken cancellationToken);

    Task<AssetAwarePreviewVideoResponse> GetPreviewInfoAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}

public interface IAssetAwarePreviewVideoComposer
{
    Task<AssetAwarePreviewVideoComposeResult> ComposeAsync(
        AssetAwareVideoCompositionPlan plan,
        AssetAwarePreviewVideoRequest request,
        string outputVideoPath,
        CancellationToken cancellationToken);
}

public interface IDailySkyGuideVisualAssetConsumer
{
    Task<bool> CanConsumeAsync(
        DailySkyGuideAssetAwareExecutionContext context,
        CancellationToken cancellationToken);

    Task ConsumeAsync(
        DailySkyGuideAssetAwareExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record ManualExecutionStartResponse(Guid ContentGenerationPlanId, Guid ContentPipelineExecutionId, string Status);
public sealed record CompleteContentPlanningExecutionRequest(
    Guid? PipelineRunId,
    string? OutputFolder,
    string? LongVideoPath,
    string? ShortVideoPath,
    string? ThumbnailLongPath,
    string? ThumbnailShortPath,
    bool PublishingCompleted,
    bool AnalyticsInitialized);
public sealed record FailContentPlanningExecutionRequest(string ErrorMessage);

public sealed record GenerateContentPlanRequest(
    string ContentCategoryCode,
    string Language = "en",
    string RegionId = "",
    string RegionName = "",
    DateTime? ScheduledUtc = null,
    string? PrimaryCelestialObjectCode = null,
    string? PrimaryAstronomyEventTypeCode = null,
    bool GeneratedByAi = false);

public sealed record GenerateContentPlanResponse(
    Guid ContentGenerationPlanId,
    string Status,
    string? Title,
    string? PlanningReason);

public sealed record ContentPlanningPipelineRequestPreview(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string Status,
    string? Title,
    object PipelineRequest,
    IReadOnlyList<string> Warnings);



public interface IContentCategoryPipelineStrategy
{
    string CategoryCode { get; }
    Task<PipelineBuildResult> BuildAsync(ContentGenerationPlan plan, CancellationToken cancellationToken);
}

public interface IContentCategoryPipelineStrategyResolver
{
    IContentCategoryPipelineStrategy? Resolve(string contentCategoryCode);
}

public sealed record PipelineBuildResult(
    bool Success,
    string ContentCategoryCode,
    Guid ContentGenerationPlanId,
    object? PipelineRequest,
    IReadOnlyList<DailySkyGuideVisualAssetItem> VisualAssets,
    DailySkyGuideAssetAwareMetadata? AssetAwareMetadata,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage);

public sealed record DailySkyGuideAssetAwareMetadata(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    DailySkyGuideContext AstronomyContext,
    IReadOnlyList<DailySkyGuideVisualAssetItem> VisualAssets,
    StellariumSceneCapturePlan? SceneCapturePlan,
    string? ThumbnailCandidatePath,
    IReadOnlyList<string> RecommendedImageSequence,
    IReadOnlyList<string> Warnings);

public sealed record AssetAwareManualRunPackage(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    string Status,
    object? RunPipelineRequest,
    AssetAwareMetadata? AssetAwareMetadata,
    bool AssetsReady,
    bool CanRunManually,
    IReadOnlyList<string> RequiredManualSteps,
    IReadOnlyList<string> Warnings);

public sealed record VisualAssetItem(
    string Role,
    string Path,
    bool Exists);

public sealed record AssetAwareMetadata(
    Guid ContentGenerationPlanId,
    string ContentCategoryCode,
    object? AstronomyContext,
    object? SceneCapturePlan,
    IReadOnlyList<VisualAssetItem> VisualAssets,
    string? ThumbnailCandidatePath,
    IReadOnlyList<string> RecommendedImageSequence,
    IReadOnlyList<string> Warnings);

public interface IAssetAwareManualRunPreparationService
{
    Task<AssetAwareManualRunPackage> PrepareAsync(Guid contentGenerationPlanId, CancellationToken cancellationToken);
}

public sealed record PrepareManualRunResponse(
    Guid ContentGenerationPlanId,
    string Status,
    object? PipelineRequest,
    IReadOnlyList<string> Warnings);


public sealed record ManualCategoryPreparationRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    string? PrimaryCelestialObjectCode,
    bool OverwriteExisting = false,
    bool GeneratePreviewVideo = true,
    bool CaptureStellariumScenes = true,
    bool Diagnostics = true);

public sealed record ManualCategoryPreparationStepResult(
    string StepName,
    string Status,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    long? DurationMs,
    string? Message,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);

public sealed record ManualCategoryPreparationResponse(
    Guid? ContentGenerationPlanId,
    string ContentCategoryCode,
    bool Success,
    IReadOnlyList<ManualCategoryPreparationStepResult> Steps,
    RunPipelineRequest? RunPipelineRequest,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    bool PublishingEnabled = false,
    bool PublishToYouTube = false,
    bool PublishToFacebook = false,
    bool PublishToInstagram = false);


public sealed record CategoryProductionPreviewRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTime ScheduledUtc,
    string? PrimaryCelestialObjectCode,
    bool PublishToYouTube = false,
    bool PublishToFacebook = false,
    bool PublishToInstagram = false,
    bool UseAssetAwareVisuals = false,
    bool Diagnostics = true);

public sealed record CategoryProductionStepResult(
    string StepName,
    string Status,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    long DurationMs,
    string? Message,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);

public sealed record CategoryProductionPreviewResponse(
    Guid? ContentGenerationPlanId,
    string ContentCategoryCode,
    bool Success,
    bool PublishingEnabled,
    bool PublishToYouTube,
    bool PublishToFacebook,
    bool PublishToInstagram,
    bool AnalyticsEnabled,
    string? LongAudioPath,
    string? ShortAudioPath,
    string? LongVideoPath,
    string? ShortVideoPath,
    string? LongThumbnailPath,
    string? ShortThumbnailPath,
    IReadOnlyList<string>? ShortAudioSegments,
    CategoryProductionPreviewDiagnostics? Diagnostics,
    CategoryProductionExecutionSummary? ExecutionSummary,
    object? Metadata,
    IReadOnlyList<CategoryProductionStepResult> Steps,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    RunPipelineRequest? RunPipelineRequest);

public sealed record CategoryProductionPreviewDiagnostics(
    string? ShortAudioManifestPath,
    string? ShortVideoDiagnosticsPath,
    string? RenderManifestPath,
    string? NarrationContextPath,
    string? SeoMetadataPath,
    string? ValidationReportPath,
    string? ObservationWindowPath,
    string? SkyfieldResponsePath);

public sealed record CategoryProductionExecutionSummary(
    double TotalDurationSeconds,
    bool GeneratedLongVideo,
    bool GeneratedShortVideo,
    bool GeneratedLongThumbnail,
    bool GeneratedShortThumbnail,
    bool PublishingAttempted,
    bool AnalyticsAttempted,
    int TotalCompletedSteps,
    int TotalFailedSteps,
    int TotalSkippedSteps);

public sealed record ProductionPreviewValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string? ValidationReportPath);

public interface IProductionPreviewOutputValidator
{
    Task<ProductionPreviewValidationResult> ValidateAsync(string? outputFolder, string? longAudioPath, string? longVideoPath, string? shortVideoPath, string? longThumbnailPath, string? shortThumbnailPath, CancellationToken cancellationToken);
}

public interface ICategoryProductionPipelineStrategy
{
    string ContentCategoryCode { get; }
    Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken);
}

public sealed record WeeklySkyForecastSkyfieldRequest(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly WeekStartDate,
    int Days,
    string Language,
    IReadOnlyList<string>? PreferredObjectCodes,
    bool IncludeMoonPhases = true,
    bool IncludePlanets = true,
    bool IncludeDeepSkyObjects = true,
    bool IncludeMeteorShowers = true,
    bool IncludeConjunctions = true,
    bool IncludeBestViewingWindows = true);

public sealed record WeeklySkyForecastProductionRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    bool GenerateNarration = false,
    bool GenerateAudio = false,
    bool GenerateSscScripts = false,
    bool CaptureStellariumScenes = false,
    bool GenerateSegmentVideos = false,
    bool GenerateFinalVideos = false,
    bool DryRun = true,
    bool OverwriteExisting = false,
    bool PublishToYouTube = false,
    bool PublishToFacebook = false,
    bool PublishToInstagram = false,
    bool Diagnostics = true);

public sealed record DailySkyForecastContextItem(
    DateOnly Date,
    DateTime SunsetUtc,
    DateTime SunriseUtc,
    string MoonPhase,
    double MoonIlluminationPercent,
    DateTime? MoonRiseUtc,
    DateTime? MoonSetUtc,
    IReadOnlyList<WeeklySkyForecastVisibleObjectItem> VisibleObjects,
    IReadOnlyList<WeeklySkyForecastEventItem> Events,
    DateTime BestViewingStartUtc,
    DateTime BestViewingEndUtc,
    double OverallViewingScore,
    string ViewingSummary);

public sealed record WeeklySkyForecastVisibleObjectItem(string ObjectCode, string ObjectName, string ObjectType, bool Visible, DateTime? RiseUtc, DateTime? SetUtc, DateTime? TransitUtc, double? MaxAltitudeDegrees, double? BestViewingAzimuthDegrees, DateTime? BestViewingTimeUtc, double VisibilityScore, double PhotographyScore, string ViewingDirection, string Reason);
public sealed record WeeklySkyForecastEventItem(string EventType, string Title, string Description, DateTime EventTimeUtc, double ImportanceScore, double ViralityScore, string? PrimaryObjectCode, string ViewingDirection, string ViewingTip);
public sealed record WeeklySkyForecastHighlightItem(int Order, string HighlightType, string Title, string Description, DateOnly Date, DateTime? BestTimeUtc, string? ObjectCode, double Score, string SuggestedSceneType);

public sealed record WeeklySkyForecastContext(
    string RegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string Language,
    IReadOnlyList<DailySkyForecastContextItem> DailyForecasts,
    IReadOnlyList<WeeklySkyForecastHighlightItem> WeeklyHighlights,
    IReadOnlyList<RecommendedObservationNight> RecommendedNights,
    string? BestPlanetOfWeek,
    DateOnly? BestMoonNight,
    DateOnly? BestPhotographyNight,
    IReadOnlyList<string> Warnings);

public sealed record RecommendedObservationNight(DateOnly Date, double Score, string Reason, IReadOnlyList<string> BestObjects, DateTime BestStartUtc, DateTime BestEndUtc);
public sealed record WeeklySkyForecastSegmentPlanItem(string SegmentCode, string SegmentType, int SortOrder, string Title, string NarrationPurpose, DateOnly? TargetDate, IReadOnlyList<string> TargetObjectCodes, string VisualRole, string SuggestedSceneType, int EstimatedDurationSeconds, double PriorityScore);
public sealed record WeeklySkyForecastSegmentPlan(IReadOnlyList<WeeklySkyForecastSegmentPlanItem> LongSegments, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> ShortSegments);
public sealed record WeeklySkyForecastNarrationPlanItem(string SegmentCode, string NarrationStyle, string NarrationTone, int EstimatedWords, int EstimatedDurationSeconds, double NarrationPriority);
public sealed record WeeklySkyForecastNarrationPlan(IReadOnlyList<WeeklySkyForecastNarrationPlanItem> LongSegments, IReadOnlyList<WeeklySkyForecastNarrationPlanItem> ShortSegments);
public sealed record WeeklyNarrationAudioSegment(string SegmentCode, string SegmentType, string NarrationText, int EstimatedDurationSeconds, string Language, string VoiceStyle, string OutputFileName);
public sealed record WeeklyNarrationManifest(
    IReadOnlyList<WeeklyNarrationAudioSegment> Segments,
    DateTime GeneratedUtc,
    string Language,
    int EstimatedTotalRuntimeSeconds,
    int NarrationSegmentCount,
    int TotalNarrationDuration,
    int GeneratedAudioCount,
    int FailedNarrationCount);
public sealed record WeeklySkyForecastSscScenePlanItem(string SceneCode, string SceneType, string? TargetObjectCode, DateTime CaptureTimeUtc, DateOnly TargetDate, double FieldOfViewDegrees, string OutputRole, bool IsThumbnailCandidate, string LinkedSegmentCode);
public sealed record WeeklySkyForecastSscScenePlan(IReadOnlyList<WeeklySkyForecastSscScenePlanItem> Scenes);
public sealed record CategoryOutputPaths(string RootDirectory, string NarrationDirectory, string ShortsDirectory, string ThumbnailsDirectory, string StellariumScenesDirectory, string StellariumScriptsDirectory, string ManifestsDirectory, string MetadataDirectory);
public sealed record WeeklySkyForecastMetadataSkeleton(IReadOnlyList<string> TitleCandidates, IReadOnlyList<string> ShortTitleCandidates, string DescriptionSkeleton, IReadOnlyList<string> Tags, IReadOnlyList<string> Hashtags, IReadOnlyList<string> KeyObjects, IReadOnlyList<string> KeyDates, string WeekRange, string RegionName, string Language);
public sealed record WeeklySkyForecastPreparationValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int LongSegmentCount,
    int ShortSegmentCount,
    int SscSceneCount,
    bool HasWeeklyContext,
    bool HasMetadataSkeleton,
    bool HasOutputPaths);

public sealed record WeeklyForecastDebugSummary(
    string ResolvedRegionId,
    string RequestedRegionId,
    string SkyfieldEndpoint,
    int SkyfieldDaysReturned,
    int VisibleObjectCount,
    int RecommendedNightCount,
    int WeeklyHighlightCount,
    int NormalizedObjectCount,
    int CorrectedHighlightCount,
    int ExcludedObjectCount,
    string? BestPlanetOfWeek,
    DateOnly? BestMoonNight,
    DateOnly? BestPhotographyNight);

public sealed record WeeklySkyForecastAudioSegmentResult(string SegmentCode, string AudioPath, int EstimatedDurationSeconds, bool Success, string? ErrorMessage);
public sealed record WeeklySkyForecastSscScriptResult(string SceneCode, string ScriptPath, string ExpectedImagePath, bool Success, string? ErrorMessage);
public sealed record WeeklySkyForecastVisualAssetResult(string SceneCode, string ExpectedImagePath, string OutputRole, string LinkedSegmentCode, string? TargetObjectCode);
public sealed record WeeklySkyForecastCaptureResult(string SceneCode, string ImagePath, string Status, bool Exists, string? ErrorMessage);
public sealed record WeeklySkyForecastExecutionFlags(bool GenerateNarration, bool GenerateAudio, bool GenerateSscScripts, bool CaptureStellariumScenes, bool GenerateSegmentVideos, bool GenerateFinalVideos, bool DryRun, bool OverwriteExisting);
public sealed record WeeklySkyForecastFinalVideoResult(string LongVideoPath, string ShortVideoPath, IReadOnlyList<string> SegmentVideoPaths, double DurationSeconds, string Resolution, string RenderStatus, string FfmpegCommandSummary, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record WeeklySkyForecastFinalVideoValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, bool LongVideoExists, bool ShortVideoExists, double DurationSeconds);

public sealed record WeeklySkyForecastPreparationResponse(Guid? ContentGenerationPlanId, string Category, DateOnly WeekStartDate, DateOnly WeekEndDate, WeeklySkyForecastContext ContextSummary, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> LongSegments, IReadOnlyList<WeeklySkyForecastSegmentPlanItem> ShortSegments, IReadOnlyList<WeeklySkyForecastSscScenePlanItem> SscScenes, CategoryOutputPaths OutputPaths, WeeklySkyForecastMetadataSkeleton MetadataSkeleton, WeeklySkyForecastPreparationValidation PreparationValidation, WeeklyForecastDebugSummary DebugSummary, IReadOnlyList<string> Warnings, IReadOnlyList<CategoryProductionStepResult> StepResults, bool PublishingEnabled, bool AnalyticsEnabled, string? NarrationManifestPath, IReadOnlyList<WeeklySkyForecastAudioSegmentResult> AudioSegments, IReadOnlyList<WeeklySkyForecastSscScriptResult> SscScripts, IReadOnlyList<WeeklySkyForecastVisualAssetResult> VisualAssets, IReadOnlyList<WeeklySkyForecastCaptureResult> CaptureResults, string? LongVideoPath, string? ShortVideoPath, string? FinalVideoManifestPath, WeeklySkyForecastFinalVideoResult? FinalVideoResults, WeeklySkyForecastFinalVideoValidation? FinalVideoValidation, string ExecutionMode, WeeklySkyForecastExecutionFlags FlagsUsed);

public interface IWeeklySkyForecastContextBuilder { Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken); }
public interface IWeeklySkyForecastContextBuilderV2 : IWeeklySkyForecastContextBuilder
{
    Task<WeeklySkyForecastContext> BuildAsync(WeeklySkyForecastV2OrchestrationContext context, CancellationToken cancellationToken);
}
public interface IRegionResolutionService
{
    Task<RegionResolutionResult?> TryResolveAsync(string regionId, string? regionName, CancellationToken cancellationToken);
}

public sealed record RegionResolutionResult(
    string CanonicalRegionId,
    string RequestedRegionId,
    string LocationName,
    double Latitude,
    double Longitude,
    string Timezone,
    IReadOnlyList<string> Aliases,
    string OutputFolderRegionSegment);
public interface IWeeklySkyForecastSegmentPlanner { Task<WeeklySkyForecastSegmentPlan> BuildAsync(WeeklySkyForecastContext context, CancellationToken cancellationToken); }
public interface IWeeklySkyForecastSscScenePlanner { Task<WeeklySkyForecastSscScenePlan> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken); }
public interface ICategoryOutputPathResolver { CategoryOutputPaths Resolve(string categoryName, DateOnly date, string regionId, Guid pipelineRunId); }
public interface IWeeklySkyForecastMetadataBuilder { Task<WeeklySkyForecastMetadataSkeleton> BuildAsync(WeeklySkyForecastContext context, WeeklySkyForecastSegmentPlan segmentPlan, CancellationToken cancellationToken); }
public interface IWeeklySkyForecastPreparationOrchestrator { Task<WeeklySkyForecastPreparationResponse> RunAsync(WeeklySkyForecastProductionRequest request, CancellationToken cancellationToken); }

public sealed record WeeklySkyForecastVisualAssetsGenerateRequest(
    bool DryRun = false,
    bool OverwriteExisting = true,
    bool CaptureStellariumScenes = true,
    bool Diagnostics = true,
    bool AllowExtraScenes = false);

public sealed record WeeklySkyForecastVisualAssetScriptResult(
    string SceneCode,
    string ScriptPath,
    string ExpectedImagePath,
    bool Success,
    string? ErrorMessage);

public sealed record WeeklySkyForecastVisualAssetManifestItem(
    string SegmentCode,
    string NarrationAudioPath,
    string SceneCode,
    string SscScriptPath,
    string CapturedImagePath,
    bool ReuseAllowed,
    string VisualPurpose,
    string OutputRole,
    string? TargetObjectCode,
    DateTime CaptureTimeUtc);

public sealed record WeeklySkyForecastVisualAssetImageResult(
    string SceneCode,
    string ImagePath,
    bool Exists,
    string OutputRole,
    string LinkedSegmentCode,
    string? TargetObjectCode);

public sealed record WeeklySkyForecastVisualAssetsResponse(
    Guid ContentGenerationPlanId,
    bool Success,
    int ScriptCount,
    int CapturedImageCount,
    string CanonicalSscScriptsDirectory,
    string CanonicalStellariumCapturesDirectory,
    string VisualAssetManifestPath,
    IReadOnlyList<WeeklySkyForecastVisualAssetScriptResult> Scripts,
    IReadOnlyList<WeeklySkyForecastVisualAssetImageResult> Images,
    IReadOnlyList<WeeklySkyForecastVisualAssetManifestItem> VisualAssetManifest,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CategoryProductionStepResult> StepResults);


public sealed record WeeklySkyForecastV2IntelligenceRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    DateOnly? WeekStartDate = null,
    bool GenerateNarration = false,
    bool GenerateAudio = false,
    bool GenerateSscScripts = false,
    bool CaptureStellariumScenes = false,
    bool GenerateSegmentVideos = false,
    bool GenerateFinalVideos = false,
    bool DryRun = true,
    bool OverwriteExisting = false,
    bool PublishToYouTube = false,
    bool PublishToFacebook = false,
    bool PublishToInstagram = false,
    bool Diagnostics = true,
    Guid? PipelineRunId = null,
    Guid? ContentGenerationPlanId = null);
public sealed record WeeklySkyForecastV2RenderScenesRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    DateOnly? WeekStartDate = null,
    bool Diagnostics = true,
    Guid? ContentGenerationPlanId = null,
    Guid? PipelineRunId = null);
public sealed record WeeklySkyForecastV2GenerateWeeklyScenesRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    DateOnly? WeekStartDate = null,
    bool Diagnostics = false,
    int? StellariumTimeoutSeconds = null,
    int? MaxScriptCount = null,
    bool ContinueOnFailure = true,
    Guid? ContentGenerationPlanId = null,
    Guid? PipelineRunId = null);

public enum CinematicFrameType
{
    EstablishingWide,
    BalancedStoryFrame,
    HeroCloseup,
    NegativeSpaceFrame,
    EducationalContext,
    HorizonContext,
    AlignmentWide,
    DirectionGuide,
    DetailFocus
}

public sealed record CinematicFramePlan(
    string FrameId,
    string SourceSceneCode,
    string RenderSceneCode,
    CinematicFrameType FrameType,
    int FrameIndex,
    IReadOnlyList<string> TargetObjects,
    string? PrimaryObject,
    double CameraAzimuth,
    double CameraAltitude,
    double Fov,
    bool PreserveHorizon,
    bool PreserveConstellationLabels,
    bool PreserveConstellationLines,
    double SubjectScreenX,
    double SubjectScreenY,
    string VisualPurpose,
    string NarrationUse,
    string OutputScriptName,
    string OutputImageName,
    string ScriptPath,
    string ImagePath,
    string RelativeScriptPath,
    string RelativeImagePath,
    IReadOnlyList<string> SafetyWarnings,
    bool FrameGenerationUsedFallback = false,
    string? FallbackReason = null);

public sealed record CinematicSceneFramePlan(
    string RenderSceneCode,
    string SourceSceneCode,
    IReadOnlyList<CinematicFramePlan> FramePlans);

public sealed record ImageSequenceImageValidation(
    bool ImageExists,
    long FileSizeBytes,
    int Width,
    int Height,
    string ValidationStatus,
    IReadOnlyList<string> ValidationWarnings,
    string? PerceptualHash = null);

public sealed record ImageSequencePlan(
    Guid PipelineRunId,
    string ContentCategoryCode,
    string RegionId,
    string Language,
    DateOnly WeekStartDate,
    int TotalImages,
    int EstimatedDurationSeconds,
    IReadOnlyList<ImageSequenceItem> Sequences,
    string ValidationStatus = "NotValidated",
    int SelectedImageCount = 0,
    int ExpectedImageCount = 6,
    int TotalDurationSeconds = 0,
    bool ProductionReady = false,
    string ProductionImageSource = "frameScreenshots",
    bool PrimaryScreenshotsDeprecated = true,
    bool DuplicateImagesDetected = false,
    IReadOnlyList<string>? ValidationWarnings = null);

public sealed record ImageSequenceItem(
    int SequenceIndex,
    string SourceSceneCode,
    string RenderSceneCode,
    string FrameId,
    string FrameType,
    string ImagePath,
    string VisualPurpose,
    string NarrationUse,
    int SuggestedDurationSeconds,
    string TransitionIntent,
    string MotionIntentForFutureVideo,
    double ImportanceScore,
    string SelectionReason,
    IReadOnlyList<string> Warnings,
    bool ImageExists = false,
    long FileSizeBytes = 0,
    int Width = 0,
    int Height = 0,
    string ValidationStatus = "NotValidated",
    IReadOnlyList<string>? ValidationWarnings = null,
    ImageSequenceImageValidation? ImageValidation = null,
    string SequenceRole = "production_frame",
    bool IsProductionSelected = true,
    string? PerceptualHash = null);

public sealed record WeeklySkyForecastV2GenerateWeeklyScenesResponse(
    Guid PipelineRunId,
    string WorkingDirectoryRoot,
    string SkyfieldResponsePath,
    string StoryBeatPath,
    string ScenePlanPath,
    int GeneratedScenes,
    int GeneratedSscScripts,
    int GeneratedFrameScreenshots,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<string> Warnings,
    int GeneratedFramePlans = 0,
    IReadOnlyList<string>? FrameScreenshots = null,
    IReadOnlyList<string>? PrimaryScreenshots = null,
    string? CinematicFramePlanPath = null,
    string? CinematicQualityReportPath = null,
    string? ImageSequencePlanPath = null,
    int SelectedImageCount = 0,
    int EstimatedImageSequenceDurationSeconds = 0,
    bool ImagePipelineProductionReady = false,
    string ImageSequenceValidationStatus = "NotValidated",
    bool AllSelectedImagesValid = false,
    bool DuplicateImagesDetected = false,
    bool PrimaryScreenshotsDeprecated = true,
    string ProductionImageSource = "frameScreenshots",
    string? WeeklyEpisodePlanPath = null,
    string? WeeklyLongformPlanPath = null,
    string? WeeklyShortformPlanPath = null,
    int LongformTargetDurationSeconds = 0,
    int ShortformTargetDurationSeconds = 0,
    bool EpisodeArchitectureReady = false,
    string? WeeklySegmentClassificationPlanPath = null,
    bool SegmentClassificationReady = false,
    int ClassifiedLongformSegmentCount = 0,
    int ClassifiedShortformSegmentCount = 0,
    string? HeroEventSegmentType = null,
    IReadOnlyList<string>? HeroEventObjects = null,
    string? WeeklySegmentDiversificationPlanPath = null,
    bool SegmentDiversificationReady = false,
    int DiversifiedLongformSegmentCount = 0,
    int DiversifiedShortformSegmentCount = 0,
    bool AssetExpansionRequired = false,
    int HighestRetentionRiskScore = 0,
    int HighestRepetitionRiskScore = 0,
    string? WeeklyVisualAssetPlanPath = null,
    string? WeeklyVisualBalanceReportPath = null,
    bool VisualAssetPlanningReady = false,
    int PlannedVisualAssetCount = 0,
    int PlannedMotionGraphicsCount = 0,
    int PlannedEducationalOverlayCount = 0,
    int PlannedAICinematicCount = 0,
    int PlannedNASAAssetCount = 0,
    int PlannedJWSTAssetCount = 0,
    bool VisualBalanceHealthy = false,
    string? AICinematicAssetPlanPath = null,
    string? AICinematicAssetResultsPath = null,
    string? AICinematicAssetRealizationReportPath = null,
    bool AICinematicAssetGenerationReady = false,
    int PlannedAICinematicAssetCount = 0,
    int SelectedAICinematicAssetCount = 0,
    int GeneratedAICinematicAssetCount = 0,
    int DeferredAICinematicAssetCount = 0,
    int FailedAICinematicAssetCount = 0,
    int SkippedExistingValidAICinematicAssetCount = 0,
    int ProductionReadyAICinematicAssetCount = 0,
    bool AICinematicGenerationPartial = false,
    int AICinematicMaxAssetsPerRun = 0,
    bool AICinematicProviderConfigured = false,
    string AzureImageDeploymentUsed = "",
    string? WeeklyAssetExpansionPlanPath = null,
    string? WeeklySegmentCoverageReportPath = null,
    string? WeeklyExpandedRenderScenePlanPath = null,
    bool AssetExpansionPlanningReady = false,
    int LongformVisualPackageCount = 0,
    int ShortformVisualPackageCount = 0,
    int ExpandedRenderSceneRequirementCount = 0,
    int UniqueAstronomySceneRequirementCount = 0,
    int ReadyForVideoPlanningSegmentCount = 0,
    int NeedsAssetGenerationSegmentCount = 0,
    string AssetExpansionPlanningMode = "PlanningOnly",
    string? WeeklyExpandedStellariumExecutionReportPath = null,
    bool ExpandedStellariumExecutionReady = false,
    bool ExpandedStellariumExecutionPartial = false,
    bool ExpandedStellariumExecutionTimedOut = false,
    int ExpandedStellariumMaxScenesPerRun = 0,
    int ExpandedStellariumMaxFramesPerScene = 0,
    int ExecutedExpandedSceneCount = 0,
    int SkippedExpandedSceneCount = 0,
    int GeneratedExpandedSscScriptCount = 0,
    int GeneratedExpandedScreenshotCount = 0,
    int TotalGeneratedSscScriptsIncludingExpanded = 0,
    int TotalGeneratedScreenshotsIncludingExpanded = 0,
    string AssetExpansionExecutionMode = "PlanningOnly",
    IReadOnlyList<string>? ExpandedFrameScreenshots = null,
    IReadOnlyList<string>? AllProductionFrameScreenshots = null,
    IReadOnlyList<string>? AICinematicImagePaths = null,
    IReadOnlyList<string>? AllProductionImageAssets = null,
    string? WeeklyProductionAssetManifestPath = null,
    string? WeeklyAssetRealizationReportPath = null,
    string? WeeklyVideoReadinessReportPath = null,
    bool AssetRealizationReady = false,
    int TotalProductionImageAssetCount = 0,
    int StellariumBaseAssetCount = 0,
    int ExpandedStellariumAssetCount = 0,
    int AICinematicImageCount = 0,
    int NasaImageCount = 0,
    string? NasaAssetPlanPath = null,
    string? NasaAssetResultsPath = null,
    string? NasaAssetRealizationReportPath = null,
    int PlannedRealizedNASAAssetCount = 0,
    int GeneratedNASAAssetCount = 0,
    int ProductionReadyNASAAssetCount = 0,
    int FailedNASAAssetCount = 0,
    IReadOnlyList<string>? NasaImagePaths = null,
    string? JwstAssetPlanPath = null,
    string? JwstAssetResultsPath = null,
    string? JwstAssetRealizationReportPath = null,
    int PlannedRealizedJWSTAssetCount = 0,
    int GeneratedJWSTAssetCount = 0,
    int ProductionReadyJWSTAssetCount = 0,
    int FailedJWSTAssetCount = 0,
    IReadOnlyList<string>? JwstImagePaths = null,
    bool NasaProviderConfigured = false,
    int JwstImageCount = 0,
    int MotionGraphicsImageCount = 0,
    int EducationalOverlayImageCount = 0,
    int PlannedMotionGraphicCount = 0,
    int GeneratedMotionGraphicCount = 0,
    int ProductionReadyMotionGraphicCount = 0,
    IReadOnlyList<string>? MotionGraphicPaths = null,
    int GeneratedEducationalOverlayCount = 0,
    int ProductionReadyEducationalOverlayCount = 0,
    IReadOnlyList<string>? EducationalOverlayPaths = null,
    bool TestVideoPipelineReady = false,
    bool FinalVideoPipelineReady = false,
    int ReadySegmentCountForTest = 0,
    int ReadySegmentCountForFinal = 0,
    int NotReadySegmentCount = 0,
    string? WeeklyNarrationVisualTimelinePath = null,
    string? WeeklyTimelineValidationReportPath = null,
    bool NarrationVisualTimelineReady = false,
    bool LongformTimelineReadyForTest = false,
    bool ShortformTimelineReadyForTest = false,
    bool LongformTimelineReadyForFinalVideo = false,
    bool ShortformTimelineReadyForFinalVideo = false,
    int TotalTimelineShotCount = 0,
    int TotalTimelineDurationSeconds = 0,
    string TimelineValidationStatus = "NotValidated",
    string? AssetQualityReportPath = null,
    int TotalValidatedAssets = 0,
    int ProductionReadyAssetCount = 0,
    int ProductionWarningAssetCount = 0,
    int ProductionFailedAssetCount = 0,
    bool QualityGatePassed = false,
    IReadOnlyList<string>? FailedAssetPaths = null);

public enum WeeklySkyForecastV2DiagnosticsPhase
{
    AstronomyEvents,
    StoryBeats,
    VisualSources,
    StellariumBlueprints,
    CinematicShots,
    SceneComposition,
    StellariumScripts,
    StellariumScreenshots,
    StellariumBasicSmoke,
    StellariumExecutionSmokeTest,
    MotionRenderPlan,
    NarrationSceneSync,
    CinematicTimeline,
    ThumbnailStoryboard,
    ShortsPlan,
    AllPlanning
}

public sealed record WeeklySkyForecastV2PhaseDiagnosticsRequest(
    string ContentCategoryCode,
    string Language,
    string RegionId,
    string RegionName,
    DateTimeOffset ScheduledUtc,
    DateOnly? WeekStartDate = null,
    bool Diagnostics = true,
    string Phase = nameof(WeeklySkyForecastV2DiagnosticsPhase.AllPlanning),
    bool RenderPreviewClips = false,
    int PreviewClipCount = 0,
    string? ExecuteShotCode = null,
    string? TestMode = null,
    int? StellariumTimeoutSeconds = null,
    int? MaxScriptCount = null,
    bool? ExecuteAllScripts = null,
    bool? ConfirmFullBatch = null,
    bool? ContinueOnFailure = null,
    Guid? ContentGenerationPlanId = null,
    Guid? PipelineRunId = null);

public enum ScreenshotTestMode
{
    All,
    Single,
    Grouping,
    Conjunction,
    Panorama
}

public sealed record WeeklyStellariumScreenshotScriptResult(
    string ShotCode,
    string ScriptPath,
    string ExpectedScreenshotPath,
    bool ScreenshotExists,
    long ScreenshotSizeBytes,
    long ElapsedMs,
    bool TimedOut,
    int? ExitCode,
    string? Error,
    string? SelectedScriptContentPreview = null,
    string? SelectedScriptLastWriteUtc = null,
    string? LaunchedExecutable = null,
    string? LaunchedArguments = null,
    string? LaunchedWorkingDirectory = null,
    double? WarmupSeconds = null,
    double? CameraSettleSeconds = null,
    double? PreScreenshotWaitSeconds = null,
    long? ScreenshotDetectedAtMs = null,
    long? ScreenshotStableAtMs = null,
    string? ActualScriptContentPath = null);

public sealed record WeeklyStellariumScreenshotGenerationResult(
    bool Success,
    int AttemptedScripts,
    int SuccessfulScreenshots,
    int FailedScreenshots,
    long ElapsedMs,
    int TimeoutCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<WeeklyStellariumScreenshotScriptResult> Scripts,
    string DiagnosticsPath,
    string? WorkingDirectoryRoot = null,
    string? PipelineRunId = null,
    string? SelectedShotCode = null,
    string? SelectedScriptPath = null,
    string? SelectedScriptSource = null,
    IReadOnlyList<string>? IgnoredDiagnosticScripts = null);

public sealed record WeeklyStellariumScriptExecutionResult(
    string ScriptPath,
    string ExpectedScreenshotPath,
    bool ScreenshotExists,
    long ScreenshotSizeBytes,
    long ElapsedMs,
    bool TimedOut,
    int? ExitCode,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string DiagnosticsPath,
    bool Success);

public sealed record WeeklySkyForecastV2OrchestrationContext(
    Guid ContentGenerationPlanId,
    Guid PipelineRunId,
    string? WorkingDirectoryRoot,
    WeeklySkyForecastV2IntelligenceRequest Request,
    RegionResolutionResult? ResolvedRegion,
    WeeklySkyForecastContext? WeeklyForecast,
    WeeklySkyForecastV2SkyfieldSummary? SkyfieldSummary,
    IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem>? EventIntelligence,
    WeeklyAstronomyEventExtractionResult? EventExtractionResult,
    WeeklyStoryboard? Storyboard,
    DateTime GeneratedAtUtc,
    int SkyfieldWeeklyForecastCalls = 0,
    int RegionResolveCalls = 0,
    bool ContextReusedAcrossPhases = false,
    int IntelligencePreviewCalls = 0,
    WeeklySkyForecastV2IntelligenceResponse? IntelligencePreviewResult = null,
    RenderPreparationPackage? RenderPreparationPackage = null,
    SceneRenderingPackage? SceneRenderingPackage = null,
    TimelineCompositionPackage? TimelineCompositionPackage = null);


public enum WeeklyAstronomyEventType
{
    HeroObject,
    Conjunction,
    Grouping,
    RareEvent,
    BestViewingWindow,
    DirectionalObservation,
    TelescopeOpportunity,
    DeepSkyHighlight
}

public sealed record WeeklyAstronomyEventObject(
    string ObjectCode,
    string ObjectName,
    double? AltitudeDegrees,
    double? AzimuthDegrees,
    double? Magnitude,
    double VisibilityScore);

public sealed record WeeklyAstronomyEvent(
    string EventId,
    WeeklyAstronomyEventType EventType,
    string Title,
    string Summary,
    IReadOnlyList<WeeklyAstronomyEventObject> Objects,
    string? PrimaryObject,
    int ObjectCount,
    DateOnly? BestDateLocal,
    TimeOnly? BestTimeLocal,
    string? Direction,
    double? AltitudeDegrees,
    double? AzimuthDegrees,
    double? AngularSeparationDegrees,
    double? Magnitude,
    double VisibilityScore,
    double ImportanceScore,
    double RarityScore,
    string RecommendedVisualSource,
    string? RecommendedSceneType,
    string? RecommendedNarrationAngle,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyAstronomyEventExtractionResult(
    bool IsValid,
    string? Message,
    IReadOnlyList<WeeklyAstronomyEvent> ExtractedEvents,
    string SourceForecastSummary,
    IReadOnlyDictionary<string, int> EventCountsByType,
    WeeklyAstronomyEvent? SelectedPrimaryEvent,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingData);


public enum WeeklyStoryboardSegmentType
{
    OpeningHook,
    WeeklyOverview,
    MainAstronomyEvent,
    GroupingFocus,
    HeroObjectFocus,
    BestViewingNight,
    ViewingDirectionGuide,
    TelescopeRecommendation,
    AstrophotographyMoment,
    EducationalInsight,
    EmotionalHighlight,
    ClosingSequence,
    CTAOutro
}

public sealed record WeeklyStoryboardNarrationSection(
    string NarrationPurpose,
    string NarrationTone,
    string NarrationSummary,
    int EstimatedDurationSeconds,
    int NarrationPriority);

public sealed record WeeklyStoryboardVisualPlan(
    string RecommendedVisualSource,
    string RecommendedSceneType,
    string RecommendedCameraStyle,
    string RecommendedMotionStyle,
    string RecommendedTransition);

public sealed record WeeklyStoryboardTransition(
    string FromSegmentCode,
    string ToSegmentCode,
    string TransitionType,
    string Purpose);

public sealed record WeeklyStoryboardSegment(
    string SegmentCode,
    WeeklyStoryboardSegmentType SegmentType,
    string Title,
    string Purpose,
    IReadOnlyList<string> TargetObjects,
    int EstimatedDurationSeconds,
    WeeklyStoryboardNarrationSection Narration,
    WeeklyStoryboardVisualPlan VisualPlan);

public sealed record WeeklyStoryboard(
    bool IsValid,
    string EmotionalArc,
    WeeklyAstronomyEvent? SelectedPrimaryEvent,
    IReadOnlyList<WeeklyStoryboardSegment> OrderedSegments,
    IReadOnlyList<WeeklyStoryboardTransition> Transitions,
    string PacingAnalysis,
    string NarrationFlowAnalysis,
    string VisualEscalationAnalysis,
    int EstimatedVideoDurationSeconds,
    IReadOnlyList<string> Warnings);

public sealed record WeeklySkyForecastV2EventIntelligenceItem(
    string EventId,
    string EventType,
    string Title,
    string Description,
    DateOnly PrimaryDate,
    DateTime? BestTimeUtc,
    IReadOnlyList<string> ObjectCodes,
    IReadOnlyList<string> VisibleObjectNames,
    double ImportanceScore,
    double VisualScore,
    double StoryScore,
    double RarityScore,
    string RecommendedVisualStrategy,
    string RecommendedScenePurpose,
    string Reason,
    string Source);

public sealed record WeeklyStoryArc(
    string Headline,
    string Subtitle,
    string StoryTheme,
    string OpeningHook,
    IReadOnlyList<string> NarrativeBeats,
    string ClosingRecommendation,
    IReadOnlyList<string> PrimaryObjects,
    IReadOnlyList<string> PrimaryDates,
    IReadOnlyList<string> SuggestedShorts);

public sealed record WeeklySkyForecastV2SkyfieldSummary(
    int DailyForecastCount,
    int VisibleObjectCount,
    int WeeklyHighlightsCount,
    int RecommendedNightsCount,
    string? BestPlanetOfWeek,
    DateOnly? BestMoonNight,
    DateOnly? BestPhotographyNight);

public sealed record WeeklySkyForecastV2IntelligenceResponse(
    Guid? ContentGenerationPlanId,
    string Category,
    bool Success,
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    string Region,
    WeeklySkyForecastV2SkyfieldSummary SkyfieldSummary,
    IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> EventIntelligence,
    WeeklyAstronomyEventExtractionResult? EventExtractionResult,
    WeeklyStoryboard? Storyboard,
    WeeklyStellariumBlueprintPackage? StellariumBlueprintPackage,
    WeeklyStoryArc WeeklyStoryArc,
    WeeklyEditorialStoryPackage EditorialStoryPackage,
    WeeklyCinematicStoryBlueprint? CinematicStoryBlueprint,
    WeeklyNarrativeAbstractionPackage? NarrativeAbstractionPackage,
    WeeklyNarrationPlan? NarrationPlan,
    WeeklyGeneratedNarrationPackage? GeneratedNarrationPackage,
    WeeklyNarrationQualityReport? NarrationQuality,
    WeeklyVisualRequirementPackage? VisualRequirementPackage,
    WeeklyHybridScenePlanPackage? HybridScenePlanPackage,
    WeeklyNormalizedEditorialPackage? NormalizedEditorialPackage,
    WeeklySceneChoreographyPackage? SceneChoreographyPackage,
    WeeklyCinematicChoreographyPackage? CinematicChoreographyPackage,
    WeeklyRenderExecutionPackage? RenderExecutionPackage,
    RenderPreparationPackage? RenderPreparationPackage,
    WeeklyExecutionValidationReport? ExecutionValidation,
    WeeklyPreviewStabilityReport? PreviewStability,
    WeeklyPhase5FoundationStatus? Phase5FoundationStatus,
    RenderPreparationFreezeStatus? RenderPreparationFreezeStatus,
    bool ReadyForRenderPreparation,
    bool ReadyForSceneRendering,
    bool ReadyForRendering,
    bool LegacyEditorialPackageDeprecated,
    IReadOnlyList<string> RecommendedVisualStrategies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CategoryProductionStepResult> StepResults);

public sealed record WeeklyStellariumBlueprintPackage(
    bool IsValid,
    string Region,
    double Latitude,
    double Longitude,
    string Timezone,
    IReadOnlyList<WeeklyStellariumSceneBlueprint> SceneBlueprints,
    IReadOnlyList<string> ValidationIssues,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyStellariumSceneBlueprint(
    string SegmentCode,
    string SceneCode,
    string SceneType,
    string RecommendedVisualSource,
    DateOnly DateLocal,
    TimeOnly TimeLocal,
    string Timezone,
    double Latitude,
    double Longitude,
    string CameraDirection,
    double ZoomLevel,
    double FieldOfViewDegrees,
    bool ShowHorizon,
    bool ShowAtmosphere,
    bool ShowLabels,
    IReadOnlyList<WeeklyStellariumHighlightObject> HighlightObjects,
    IReadOnlyList<string> OverlayText,
    string MotionStyle,
    string TransitionIn,
    string TransitionOut,
    string ExpectedOutputImagePath,
    string ExpectedSscScriptPath,
    WeeklyStellariumCameraPlan CameraPlan,
    WeeklyStellariumOverlayPlan OverlayPlan,
    IReadOnlyList<WeeklyStellariumShot> Shots,
    IReadOnlyList<string> PlannedSscCommands);

public sealed record WeeklyStellariumShot(string ShotCode, string ShotType, DateOnly DateLocal, TimeOnly TimeLocal, int DurationSeconds, string ExpectedOutputImagePath);
public sealed record WeeklyStellariumHighlightObject(string ObjectCode, string ObjectName, string LabelText, string HighlightStyle, string MarkerColor, int Priority, string ExpectedVisibility);
public sealed record WeeklyStellariumCameraPlan(string CameraStyle, string CameraDirection, string MotionStyle, double ZoomLevel, double FieldOfViewDegrees, bool ShowHorizon, bool ShowAtmosphere, bool ShowLabels);
public sealed record WeeklyStellariumOverlayPlan(IReadOnlyList<string> OverlayText, bool ShowDateLabel, bool ShowDirectionArrow, bool ShowAltitudeOverlay, bool ShowPracticalGuidanceLabels);

public sealed record WeeklyCameraMovementPlan(string MovementType, string MotionStyle, string Direction, double? StartFovDegrees, double? EndFovDegrees, bool TrackPrimaryObject);
public sealed record WeeklyShotTransitionPlan(string TransitionIn, string TransitionOut, string TransitionPurpose);
public sealed record WeeklyShotNarrationSync(string NarrationBeat, double EstimatedStartSecond, double EstimatedEndSecond, string SyncedNarrationPurpose, string? EmphasisObject, IReadOnlyList<string> EmphasisWords);
public sealed record WeeklyDynamicFovCalculation(string SceneCode, IReadOnlyList<string> TargetObjects, double? SourceSeparationDegrees, double CalculatedFovDegrees, string Reason);
public sealed record WeeklyCinematicShot(
    string ShotCode,
    string ShotType,
    string ShotPurpose,
    IReadOnlyList<string> TargetObjects,
    string? PrimaryObject,
    DateOnly DateLocal,
    TimeOnly TimeLocal,
    int DurationSeconds,
    string CameraDirection,
    double FieldOfViewDegrees,
    double StartFovDegrees,
    double EndFovDegrees,
    WeeklyCameraMovementPlan CameraMovement,
    WeeklyShotTransitionPlan TransitionIn,
    WeeklyShotTransitionPlan TransitionOut,
    string MotionStyle,
    string ExpectedOutputImagePath,
    string ExpectedOutputVideoPath,
    string ExpectedSscScriptPath,
    IReadOnlyList<string> PlannedSscCommands,
    WeeklyShotNarrationSync NarrationSync,
    string TitleText = "",
    string SubtitleText = "",
    IReadOnlyList<string>? LabelObjects = null,
    double? ShowLabelsFromSecond = null,
    double? HideLabelsAtSecond = null,
    string OverlayStyle = "cinematic_minimal");

public sealed record WeeklyCinematicSceneSequence(
    string SegmentCode,
    string SceneCode,
    string SceneType,
    string SourceBlueprintSceneCode,
    int DurationSeconds,
    IReadOnlyList<WeeklyCinematicShot> Shots,
    string SequencePurpose,
    WeeklyShotTransitionPlan TransitionIn,
    WeeklyShotTransitionPlan TransitionOut);

public sealed record WeeklyCinematicShotPackage(
    bool IsValid,
    string StoryboardId,
    string PipelineRunId,
    int TotalScenes,
    int TotalShots,
    int EstimatedDurationSeconds,
    IReadOnlyList<WeeklyCinematicSceneSequence> SceneSequences,
    IReadOnlyList<WeeklyDynamicFovCalculation> DynamicFovCalculations,
    IReadOnlyList<string> ValidationIssues,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyEditorialStoryPackage(
    WeeklyHeroEvent HeroEvent,
    IReadOnlyList<WeeklyHeroEvent> SecondaryEvents,
    string Headline,
    string Subtitle,
    string OpeningHook,
    string StoryTheme,
    IReadOnlyList<WeeklyNarrativeBeat> NarrativeArc,
    IReadOnlyList<WeeklyCinematicMoment> CinematicMoments,
    WeeklyThumbnailDirection ThumbnailDirection,
    IReadOnlyList<WeeklyShortCandidate> ShortsCandidates,
    string VisualStrategySummary,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyHeroEvent(
    string EventId,
    string EventType,
    string Title,
    string Description,
    DateOnly PeakDate,
    DateTime? BestTimeUtc,
    IReadOnlyList<string> ObjectCodes,
    IReadOnlyList<string> ObjectNames,
    double SignificanceScore,
    double EmotionalScore,
    double VisualScore,
    string RecommendedVisualStrategy,
    string WhyThisIsHero,
    IReadOnlyList<DateOnly>? SupportingDates = null);

public sealed record WeeklyNarrativeBeat(
    int BeatOrder,
    string BeatType,
    string Title,
    string Purpose,
    string SourceEventId,
    IReadOnlyList<string> TargetObjects,
    DateOnly TargetDate,
    string EmotionalTone,
    string SuggestedVisualStrategy,
    string SuggestedScenePurpose);

public sealed record WeeklyCinematicMoment(
    string MomentId,
    string MomentType,
    string Title,
    string Description,
    IReadOnlyList<string> ObjectCodes,
    DateOnly TargetDate,
    DateTime? BestTimeUtc,
    int VisualPriority,
    string RecommendedVisualStrategy,
    bool ReuseAllowed,
    string SuggestedScenePurpose);

public sealed record WeeklyThumbnailDirection(
    IReadOnlyList<string> TitleTextCandidates,
    IReadOnlyList<string> PrimaryObjects,
    IReadOnlyList<string> SecondaryObjects,
    string Emotion,
    string RecommendedVisualStrategy,
    string CompositionIdea,
    string BackgroundSuggestion,
    string OverlayTextSuggestion);

public sealed record WeeklyShortCandidate(
    string ShortCode,
    string Title,
    string Hook,
    string SourceEventId,
    IReadOnlyList<string> ObjectCodes,
    DateOnly TargetDate,
    int RecommendedDurationSeconds,
    string RecommendedVisualStrategy,
    double PriorityScore);

public interface IWeeklyAstronomyEventExtractor
{
    WeeklyAstronomyEventExtractionResult Extract(WeeklySkyForecastContext context, string region, DateOnly weekStartDate, DateOnly weekEndDate, string language, string? workingDirectoryRoot);
}

public interface IWeeklySkyForecastV2EventIntelligenceBuilder
{
    IReadOnlyList<WeeklySkyForecastV2EventIntelligenceItem> Build(WeeklySkyForecastContext context);
}

public interface IWeeklyStoryboardComposer
{
    WeeklyStoryboard Compose(WeeklyAstronomyEventExtractionResult extractionResult, string region, string language, string forecastSummary, string narrationStyle, string? workingDirectoryRoot);
}

public interface IWeeklyCinematicShotExpansionEngine
{
    WeeklyCinematicShotPackage Expand(WeeklyStoryboard storyboard, WeeklyStellariumBlueprintPackage stellariumBlueprintPackage, WeeklyAstronomyEventExtractionResult eventExtractionResult, string region, string workingDirectoryRoot, string pipelineRunId);
}
public sealed record WeeklyCameraPathPlan(
    string ShotCode,
    double StartSecond,
    double EndSecond,
    double StartFov,
    double EndFov,
    double StartAzimuth,
    double EndAzimuth,
    double StartAltitude,
    double EndAltitude,
    string EasingType,
    double DriftAmount,
    double HoldStartSecond,
    double HoldDurationSeconds,
    double CameraIntensity,
    string MovementType);
public sealed record WeeklyShotEmotionPlan(string VisualEmotion, string MusicBeat, double CameraIntensity, double NarrationIntensity, double TransitionEnergy);
public sealed record WeeklyMotionRenderShotPlan(
    string ShotCode,
    int DurationSeconds,
    string CompositionFramePath,
    string ClipOutputPath,
    WeeklyCameraPathPlan CameraPath,
    WeeklyShotEmotionPlan EmotionPlan,
    IReadOnlyList<string> Warnings);
public sealed record WeeklyMotionRenderValidation(string ShotCode, bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, string? ClipPath = null, double? ActualDurationSeconds = null, int? Width = null, int? Height = null);
public sealed record WeeklyStellariumScriptInfo(
    string ShotCode,
    string ScriptPath,
    string ExpectedScreenshotPath,
    int CommandCount,
    bool IsValid,
    int ShotOrder = int.MaxValue,
    bool IsDiagnostic = false);

public sealed record WeeklyStellariumScriptPackage(
    bool IsValid,
    int ScriptCount,
    IReadOnlyList<WeeklyStellariumScriptInfo> Scripts,
    IReadOnlyList<string> ValidationIssues,
    IReadOnlyList<string> Warnings,
    string DiagnosticsPath);

public interface IWeeklyStellariumScriptWriter
{
    Task<WeeklyStellariumScriptPackage> WriteAsync(WeeklyCinematicShotPackage cinematicShotPackage, string workingDirectoryRoot, CancellationToken cancellationToken);
}

public interface IWeeklyStellariumScriptExecutor
{
    Task<WeeklyStellariumScriptExecutionResult> ExecuteAsync(string workingDirectoryRoot, string scriptPath, string expectedScreenshotPath, int timeoutSeconds = 45, CancellationToken cancellationToken = default);
}

public interface IStellariumScriptExecutionService
{
    Task<WeeklyStellariumScriptExecutionResult> ExecuteAsync(string workingDirectoryRoot, string scriptPath, string expectedScreenshotPath, int timeoutSeconds = 45, CancellationToken cancellationToken = default);
}

public interface IWeeklyStellariumScreenshotGenerator
{
    Task<WeeklyStellariumScreenshotGenerationResult> GenerateAsync(string workingDirectoryRoot, WeeklyStellariumScriptPackage scriptPackage, string? executeShotCode = null, string? testMode = null, int? maxScriptCount = null, bool executeAllScripts = false, bool confirmFullBatch = false, bool continueOnFailure = true, int timeoutSeconds = 90, CancellationToken cancellationToken = default);
}

public sealed record WeeklyMotionRenderManifest(
    IReadOnlyList<WeeklyMotionRenderShotPlan> Shots,
    IReadOnlyList<WeeklyCameraPathPlan> CameraPaths,
    IReadOnlyList<string> ClipPaths,
    IReadOnlyList<string> CompositionFrames,
    IReadOnlyList<string> FfmpegCommands,
    IReadOnlyList<WeeklyMotionRenderValidation> Validation,
    IReadOnlyList<string> FailedShots,
    IReadOnlyList<string> Warnings,
    string ManifestPath);
public interface IWeeklyCameraPathEngine { WeeklyCameraPathPlan Build(WeeklyCinematicShot shot, string sequencePurpose); }
public interface IWeeklyCinematicCompositionEngine { Task<string> ComposeAsync(WeeklyCinematicShot shot, string outputPath, CancellationToken cancellationToken); }
public interface IWeeklyMotionClipRenderer
{
    Task<(string Command, WeeklyMotionRenderValidation Validation)> RenderAsync(WeeklyCinematicShot shot, WeeklyCameraPathPlan cameraPath, string composedFramePath, string clipOutputPath, CancellationToken cancellationToken);
}
public interface IWeeklyMotionRenderManifestBuilder
{
    Task<WeeklyMotionRenderManifest> BuildAsync(
        WeeklyCinematicShotPackage cinematicShotPackage,
        string rootPath,
        string pipelineRunId,
        bool renderPreviewClips,
        int previewClipCount,
        CancellationToken cancellationToken);
}

public interface IWeeklySkyForecastV2IntelligenceService
{
    Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2IntelligenceRequest request, CancellationToken cancellationToken);
    Task<WeeklySkyForecastV2IntelligenceResponse> PreviewAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken);
}
public interface IWeeklySkyForecastSceneRenderingOrchestrator
{
    Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken);
    Task<SceneRenderingPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken);
}

public interface IWeeklySkyForecastTimelineCompositionOrchestrator
{
    Task<TimelineCompositionPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken);
    Task<TimelineCompositionPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken);
}


public interface IWeeklySkyForecastFinalMediaOrchestrator
{
    Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2IntelligenceRequest request, Guid? contentGenerationPlanId, CancellationToken cancellationToken);
    Task<FinalMediaPackage> RunAsync(WeeklySkyForecastV2OrchestrationContext orchestrationContext, CancellationToken cancellationToken);
}

public sealed record ExternalProcessExecutionResult(string ExecutablePath, string Arguments, string WorkingDirectory, DateTime StartedUtc, DateTime CompletedUtc, long ElapsedMs, int ExitCode, string StdOut, string StdErr, string? OutputPath, long OutputFileSizeBytes);
public sealed record FfprobeMediaInfo(double DurationSeconds, int Width, int Height, string? VideoCodec, bool HasAudioStream, bool HasVideoStream);
public sealed record MediaValidationResult(bool IsValid, string Path, string MediaType, IReadOnlyList<string> BlockingIssues, FfprobeMediaInfo? ProbeInfo = null);

public interface IExternalProcessRunner
{
    Task<ExternalProcessExecutionResult> RunAsync(string executablePath, string arguments, string workingDirectory, string? outputPath, CancellationToken cancellationToken);
}

public interface IFFmpegService
{
    Task<ExternalProcessExecutionResult> ExecuteAsync(string arguments, string workingDirectory, string? outputPath, CancellationToken cancellationToken);
}

public interface IFFprobeService
{
    Task<FfprobeMediaInfo?> ProbeAsync(string path, CancellationToken cancellationToken);
}

public interface IMediaValidationService
{
    Task<MediaValidationResult> ValidateMp4Async(string path, long minBytes, CancellationToken cancellationToken);
    Task<MediaValidationResult> ValidateWavAsync(string path, CancellationToken cancellationToken);
    MediaValidationResult ValidateImage(string path, long minBytes, string mediaType);
}

public sealed record FinalMediaPackage(
    FinalLongFormVideoResult LongFormFinalVideo,
    NarrationAudioResult NarrationAudioResult,
    BackgroundMusicResult BackgroundMusicResult,
    FinalAudioMixResult FinalAudioMixResult,
    IReadOnlyList<ShortFinalResult> ShortsFinalResults,
    ThumbnailFinalResult ThumbnailFinalResult,
    SubtitleResult SubtitleResult,
    FinalMediaValidation FinalMediaValidation,
    FinalMediaFreezeStatus FinalMediaFreezeStatus);
public sealed record FinalLongFormVideoResult(string OutputPath, double DurationSeconds, string Resolution, int Fps, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record NarrationAudioResult(string NarrationAudioPath, string Language, string VoiceCode, double DurationSeconds, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record BackgroundMusicResult(string? MusicPath, string Mode, double DurationSeconds, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record FinalAudioMixResult(string FinalMixedAudioPath, double DurationSeconds, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record ShortFinalResult(string ShortCode, string OutputPath, double DurationSeconds, string AspectRatio, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record ThumbnailFinalResult(string OutputPath, string Status, bool ReusedFromPhase6B, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record SubtitleResult(string SrtPath, string VttPath, string Status, bool CaptionsRendered, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record FinalMediaValidation(bool IsValid, bool NarrationAudioRendered, bool FinalAudioMixed, bool LongFormVideoRendered, bool ShortsRendered, bool ThumbnailAvailable, bool SubtitlesReady, bool DurationValid, bool OutputFilesExist, bool ReadyForHumanReview, bool ReadyForPublishing, bool SinglePipelineRunIdUsed, bool OutputRootConsistent, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings, bool OutputFilesAreValidMedia, bool FfmpegExecuted, bool StellariumCaptureExecuted, bool OverlaysAreValidImages, bool ThumbnailIsValidImage, bool ShortsAreValidMedia, bool LongFormIsValidMedia, bool RealMediaOutputsGenerated = false, bool FfprobeExecuted = false, bool ThumbnailContainsObjects = false, bool SceneVisualsContainObjects = false, bool VisualAssetsResolved = false);
public sealed record FinalMediaFreezeStatus(bool IsFrozen, bool IsReadyForPhase7, IReadOnlyList<string> VerifiedChecks, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);

public interface IWeeklySkyForecastV2EditorialIntelligenceBuilder
{
    Task<WeeklyEditorialStoryPackage> BuildAsync(WeeklySkyForecastV2IntelligenceResponse intelligence, CancellationToken cancellationToken);
}

public sealed record WeeklyCinematicStoryBlueprint(
    string StoryId,
    string Headline,
    string Subtitle,
    string OpeningHook,
    string StoryPromise,
    WeeklyHeroStory HeroStory,
    IReadOnlyList<WeeklySupportingStory> SupportingStories,
    IReadOnlyList<WeeklyCinematicNarrativeBeat> NarrativeBeats,
    IReadOnlyList<WeeklyCinematicMomentBlueprint> CinematicMoments,
    IReadOnlyList<WeeklyShortBlueprint> ShortsBlueprints,
    WeeklyThumbnailBlueprint ThumbnailBlueprint,
    string NarrationTone,
    string VisualTone,
    IReadOnlyList<string> Warnings);

public sealed record WeeklyHeroStory(string Title, string Description, DateOnly PeakDate, IReadOnlyList<DateOnly> SupportingDates, DateTime? BestTimeUtc, IReadOnlyList<string> ObjectCodes, IReadOnlyList<string> ObjectNames, string StoryAngle, string ViewerBenefit, string VisualPromise, string RecommendedVisualStrategy, string SourceEventId);
public sealed record WeeklySupportingStory(string StoryCode, string Title, string Description, DateOnly TargetDate, IReadOnlyList<string> ObjectCodes, string Purpose, string RecommendedVisualStrategy, string SourceEventId);
public sealed record WeeklyCinematicNarrativeBeat(int BeatOrder, string BeatCode, string Title, string NarrationIntent, string EmotionalTone, string SourceStoryCode, DateOnly TargetDate, IReadOnlyList<string> ObjectCodes, string RecommendedVisualStrategy, string SuggestedVisualPurpose, bool ShouldReuseVisual, int EstimatedNarrationSeconds);
public sealed record WeeklyCinematicMomentBlueprint(string MomentCode, string Title, string Description, IReadOnlyList<string> ObjectCodes, DateOnly TargetDate, DateTime? BestTimeUtc, string VisualType, string RecommendedVisualStrategy, string SuggestedAssetRole, bool ReuseAllowed, string VisualUniquenessKey);
public sealed record WeeklyShortBlueprint(string ShortCode, string Title, string Hook, string StoryAngle, IReadOnlyList<string> ObjectCodes, DateOnly TargetDate, string RecommendedVisualStrategy, int SuggestedDurationSeconds, double PriorityScore);
public sealed record WeeklyThumbnailBlueprint(IReadOnlyList<string> TitleTextCandidates, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string Emotion, string CompositionIdea, string BackgroundSuggestion, string OverlayTextSuggestion, string RecommendedVisualStrategy);

public interface IWeeklySkyForecastV2CinematicEditorialRefiner
{
    Task<WeeklyCinematicStoryBlueprint> RefineAsync(
        WeeklyEditorialStoryPackage editorialPackage,
        WeeklySkyForecastV2IntelligenceResponse intelligence,
        CancellationToken cancellationToken);
}

public sealed record WeeklyNarrativeAbstractionPackage(
    string AbstractionId,
    string StoryHeadline,
    string StorySubtitle,
    string OpeningNarrationHook,
    NarrativeHeroConcept HeroNarrative,
    IReadOnlyList<NarrativeSupportConcept> SupportingNarratives,
    IReadOnlyList<NarrativeFlowBeat> NarrativeFlow,
    IReadOnlyList<NarrativeVisualConcept> CinematicVisualPlan,
    IReadOnlyList<NarrativeShortConcept> ShortsNarrativePlan,
    NarrativeThumbnailConcept ThumbnailNarrativeDirection,
    string EmotionalTone,
    string ViewerPromise,
    IReadOnlyList<string> Warnings);

public sealed record NarrativeHeroConcept(string ConceptCode, string Title, string HumanNarrative, string ViewerExperience, IReadOnlyList<string> ObjectCodes, IReadOnlyList<string> ObjectNames, DateOnly PeakDate, IReadOnlyList<DateOnly> SupportingDates, string RecommendedVisualStrategy, double EmotionalWeight, double CinematicImportance, IReadOnlyList<string> SourceEventIds);
public sealed record NarrativeSupportConcept(string SupportCode, string Title, string NarrativePurpose, DateOnly TargetDate, IReadOnlyList<string> ObjectCodes, string ViewerValue, string RecommendedVisualStrategy, IReadOnlyList<string> SourceEventIds);
public sealed record NarrativeFlowBeat(int BeatOrder, string BeatCode, string BeatTitle, string NarrationPurpose, string EmotionalIntent, string VisualIntent, IReadOnlyList<string> TargetObjects, DateOnly TargetDate, int EstimatedNarrationSeconds, string RecommendedVisualStrategy, bool ShouldReuseVisual);
public sealed record NarrativeVisualConcept(string VisualCode, string VisualPurpose, string VisualNarrativeRole, IReadOnlyList<string> ObjectCodes, string VisualUniquenessKey, string RecommendedVisualStrategy, int CinematicPriority, bool ReuseAllowed);
public sealed record NarrativeShortConcept(string ShortCode, string Title, string NarrationHook, string ViewerPromise, IReadOnlyList<string> ObjectCodes, DateOnly TargetDate, string DistinctStoryAngle, string RecommendedVisualStrategy, int EstimatedDurationSeconds);
public sealed record NarrativeThumbnailConcept(string EmotionalGoal, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string VisualStory, string CompositionNarrative, IReadOnlyList<string> TitleTextCandidates, string OverlayTextSuggestion, string RecommendedVisualStrategy);

public sealed record WeeklyNarrationPlan(
    string Language,
    string NarrationTone,
    WeeklyLongFormNarrationPlan LongFormPlan,
    WeeklyShortNarrationPlan ShortsPlan,
    IReadOnlyList<string> NarrationWarnings);
public sealed record WeeklyLongFormNarrationPlan(int TargetDurationSeconds, int SegmentCount, IReadOnlyList<WeeklyNarrationSegment> Segments);
public sealed record WeeklyNarrationSegment(string SegmentCode, int SegmentOrder, string SegmentTitle, string NarrationIntent, string EmotionalTone, string SourceBeatCode, IReadOnlyList<string> TargetObjects, DateOnly TargetDate, int EstimatedDurationSeconds, string RecommendedVisualStrategy, string VisualPurpose, IReadOnlyList<string> NarrationPromptHints);
public sealed record WeeklyShortNarrationPlan(IReadOnlyList<WeeklyShortNarrationItem> Shorts);
public sealed record WeeklyShortNarrationItem(string ShortCode, string Title, string Hook, IReadOnlyList<string> TargetObjects, DateOnly TargetDate, int EstimatedDurationSeconds, string NarrationIntent, string RecommendedVisualStrategy, double PriorityScore);
public sealed record WeeklyGeneratedNarrationPackage(string Language, string NarrationStyle, WeeklyGeneratedLongNarration LongFormNarration, IReadOnlyList<WeeklyGeneratedShortNarration> ShortNarrations, IReadOnlyList<string> Warnings);
public sealed record WeeklyGeneratedLongNarration(string FullNarration, int EstimatedDurationSeconds, IReadOnlyList<WeeklyGeneratedNarrationSegment> Segments);
public sealed record WeeklyGeneratedNarrationSegment(string SegmentCode, string SegmentTitle, string NarrationText, int EstimatedDurationSeconds, IReadOnlyList<string> TargetObjects, string RecommendedVisualStrategy, string VisualPurpose);
public sealed record WeeklyGeneratedShortNarration(string ShortCode, string Title, string NarrationText, int EstimatedDurationSeconds, string RecommendedVisualStrategy);
public sealed record WeeklyNarrationQualityReport(bool IsValid, IReadOnlyList<string> Warnings, IReadOnlyList<string> ForbiddenPhraseHits, IReadOnlyList<string> RepeatedPhraseWarnings, int WordCount, int EstimatedDurationSeconds, int TargetDurationSeconds, bool EmotionalProgressionDetected, bool ShortCtaUniquenessValid);
public sealed record WeeklyPreviewStabilityReport(bool IsStable, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings, IReadOnlyList<string> AffectedFieldPaths, bool ReadyForAssetResolution, bool ReadyForSceneChoreography, bool ReadyForRenderPreparation, bool ReadyForRendering);
public sealed record WeeklyNormalizedEditorialEvent(string NormalizedEventId, string NormalizedEventType, string Title, string HumanDescription, IReadOnlyList<string> PrimaryObjects, DateOnly PeakDate, IReadOnlyList<DateOnly> SupportingDates, string HumanTimeWindow, IReadOnlyList<string> SourceEventIds, int EditorialImportance, string RecommendedVisualStrategy);
public sealed record WeeklyNormalizedStoryArc(string Headline, string Hook, string StoryTheme, string HeroStory, IReadOnlyList<string> SupportingStoryPoints, string BestNightRecommendation, string EmotionalProgression, string ViewerPromise);
public sealed record WeeklyNormalizedTimeWindow(DateOnly Date, string HumanLabel, DateTime? RawBestTimeUtc, double Confidence);
public sealed record WeeklyNormalizedVisualStoryInput(string VisualCode, string StoryRole, string HumanScenePurpose, IReadOnlyList<string> PrimaryObjects, DateOnly TargetDate, string HumanTimeWindow, string RecommendedVisualStrategy);
public sealed record WeeklyNormalizedEditorialPackage(IReadOnlyList<WeeklyNormalizedEditorialEvent> NormalizedEvents, WeeklyNormalizedEditorialEvent HeroNormalizedEvent, WeeklyNormalizedStoryArc NormalizedStoryArc, IReadOnlyList<WeeklyNormalizedTimeWindow> NormalizedTimeWindows, IReadOnlyList<WeeklyNormalizedVisualStoryInput> NormalizedVisualStoryInputs, IReadOnlyList<string> NormalizationWarnings);
public sealed record WeeklyVisualRequirementPackage(IReadOnlyList<WeeklyVisualRequirement> VisualRequirements, IReadOnlyList<SegmentVisualMapping> SegmentVisualMappings, IReadOnlyList<VisualReusePlan> VisualReusePlan, ThumbnailVisualRequirement ThumbnailVisualRequirement, IReadOnlyList<string> VisualWarnings);
public sealed record WeeklyVisualRequirement(string VisualRequirementId, string VisualCode, string VisualPurpose, IReadOnlyList<string> SourceSegmentCodes, IReadOnlyList<string> ObjectCodes, DateOnly TargetDate, DateTime? BestTimeUtc, string EmotionalTone, string VisualStrategy, string VisualSourceType, string SceneType, string CompositionDescription, string MotionStyle, IReadOnlyList<string> OverlayNeeds, bool ReuseAllowed, int Priority, string ExpectedAssetRole, string VisualUniquenessKey);
public sealed record SegmentVisualMapping(string SegmentCode, string VisualCode, string UsageType, string TimingHint, bool ShouldReuse, string TransitionIn, string TransitionOut);
public sealed record VisualReusePlan(string ReusedVisualCode, IReadOnlyList<string> ReusedBySegments, string ReuseReason);
public sealed record WeeklyHybridScenePlanPackage(IReadOnlyList<WeeklyScenePlan> ScenePlans, IReadOnlyList<WeeklySegmentSceneMapping> SegmentSceneMappings, IReadOnlyList<WeeklyAssetNeed> AssetNeeds, IReadOnlyList<WeeklyStellariumNeed> StellariumNeeds, IReadOnlyList<WeeklyOverlayPlan> OverlayPlan, IReadOnlyList<WeeklyTransitionPlan> TransitionPlan, IReadOnlyList<string> SceneWarnings);
public sealed record WeeklyScenePlan(string SceneCode, string VisualCode, int SceneOrder, string SceneType, string VisualSourceType, string VisualStrategy, DateOnly TargetDate, DateTime? BestTimeUtc, IReadOnlyList<string> ObjectCodes, int DurationSeconds, string CompositionDescription, string CinematicMotion, string CameraBehavior, IReadOnlyList<string> OverlayInstructions, string TransitionIn, string TransitionOut, bool ReuseAllowed, string RenderIntent, IReadOnlyList<string> RequiredAssets, bool RequiresStellarium, bool RequiresCelestialAssets, bool RequiresOverlayComposite);
public sealed record WeeklySegmentSceneMapping(string SegmentCode, string SceneCode, string TimingHint, bool ReuseAllowed);
public sealed record WeeklyAssetNeed(string AssetCode, string ObjectCode, string AssetRole, string PreferredAssetType, string FallbackStrategy, IReadOnlyList<string> RequiredForSceneCodes);
public sealed record WeeklyStellariumNeed(string SceneCode, DateOnly TargetDate, DateTime? BestTimeUtc, string LocationRegionId, IReadOnlyList<string> ObjectCodes, string ScenePurpose, int FieldOfViewDegrees, string CaptureMode, string ExpectedOutputRole, string? SourceSceneCode = null, bool IsDynamicSplitScene = false);
public sealed record WeeklyOverlayPlan(string SceneCode, IReadOnlyList<string> Overlays, string LabelStyle, string Timing, string SafeArea);
public sealed record WeeklyTransitionPlan(string FromSceneCode, string ToSceneCode, string TransitionType, int DurationSeconds);
public sealed record ThumbnailVisualRequirement(string VisualCode, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string CompositionDescription, string OverlayText, string VisualStrategy, string VisualSourceType);
public sealed record WeeklySceneChoreographyPackage(IReadOnlyList<ResolvedWeeklyScene> ResolvedScenes, IReadOnlyList<ResolvedWeeklyAsset> ResolvedAssets, IReadOnlyList<WeeklySceneTimeline> SceneTimeline, IReadOnlyList<WeeklyTransitionPlan> SceneTransitions, IReadOnlyList<WeeklyOverlayTimeline> OverlayTimeline, IReadOnlyList<WeeklyRenderContract> RenderContracts, IReadOnlyList<string> ChoreographyWarnings);
public sealed record ResolvedWeeklyScene(string SceneCode, int SceneOrder, string SceneTitle, string SceneType, int DurationSeconds, IReadOnlyList<string> NarrationSegmentCodes, string VisualSourceType, string RenderIntent, string EmotionalTone, string CinematicPurpose, IReadOnlyList<string> TargetObjects, DateOnly TargetDate, DateTime? BestTimeUtc, string MotionPlan, WeeklyCameraPlan CameraPlan, string OverlayPlan, string TransitionIn, string TransitionOut, IReadOnlyList<string> ResolvedAssetIds, IReadOnlyList<string> ResolvedAssetRoles, bool RequiresStellarium, string? StellariumPlanId, string RenderStrategy, int ReusePriority);
public sealed record WeeklyCameraPlan(string PrimaryBehavior, string? SecondaryBehavior);
public sealed record ResolvedWeeklyAsset(string AssetId, string AssetCode, string AssetRole, string AssetType, string SourceType, string ObjectCode, int UsagePriority, string PreferredPath, string FallbackPath, bool SupportsTransparency, bool SupportsAnimation, IReadOnlyList<string> UsableForScenes);
public sealed record WeeklySceneTimeline(string SceneCode, int StartSecond, int EndSecond, int NarrationStartSecond, int NarrationEndSecond, int TransitionLeadSeconds, IReadOnlyList<string>? NarrationSegmentCodes = null, int TransitionInSeconds = 0, int TransitionOutSeconds = 0, bool IsThumbnailOnly = false, bool HasGapBefore = false, bool HasOverlap = false);
public sealed record WeeklyOverlayTimeline(string SceneCode, string OverlayType, string OverlayText, int StartSecond, int EndSecond, string AnimationStyle, string SafeArea);
public sealed record WeeklyRenderContract(string SceneCode, string RendererType, string RenderMode, IReadOnlyList<string> ExpectedInputs, IReadOnlyList<string> ExpectedOutputs, bool SupportsReuse, bool RequiresCompositing);
public sealed record WeeklyRenderExecutionPackage(
    string ExecutionId,
    IReadOnlyList<WeeklyRenderExecutionScene> ExecutionScenes,
    IReadOnlyList<WeeklySceneTimeline> ExecutionTimeline,
    IReadOnlyList<RenderSourceDecision> RenderSourceDecisions,
    IReadOnlyList<AssetResolutionDirective> AssetResolutionDirectives,
    IReadOnlyList<StellariumExecutionDirective> StellariumExecutionDirectives,
    IReadOnlyList<OverlayExecutionDirective> OverlayExecutionDirectives,
    IReadOnlyList<MotionExecutionDirective> MotionExecutionDirectives,
    IReadOnlyList<TransitionExecutionDirective> TransitionExecutionDirectives,
    IReadOnlyList<RendererExecutionContract> RendererExecutionContracts,
    ThumbnailExecutionContract ThumbnailExecutionContract,
    IReadOnlyList<string> ExecutionWarnings);
public sealed record WeeklyRenderExecutionScene(string SceneCode, int SceneOrder, string RendererType, string VisualSourceType, string SceneType, int DurationSeconds, int StartSecond, int EndSecond, IReadOnlyList<string> NarrationSegmentCodes, DateOnly TargetDate, string HumanTimeWindow, DateTime? TechnicalBestTimeUtc, IReadOnlyList<string> InputContracts, IReadOnlyList<string> OutputContract, int ExecutionPriority, string ReusePolicy);
public sealed record RenderSourceDecision(string SceneCode, string SelectedSourceType, string DecisionReason, IReadOnlyList<string> FallbackSourceTypes, bool RequiresAssetResolution, bool RequiresStellarium, bool RequiresOverlayComposite, bool CanRenderWithoutStellarium);
public sealed record AssetResolutionDirective(string SceneCode, IReadOnlyList<string> RequiredAssets, IReadOnlyList<string> OptionalAssets, string FallbackPolicy, bool AllowPublicImageFallback, bool AllowGeneratedImageFallback);
public sealed record StellariumExecutionDirective(string SceneCode, string RegionId, DateOnly TargetDate, DateTime? TechnicalBestTimeUtc, string HumanTimeWindow, IReadOnlyList<string> ObjectCodes, int FieldOfViewDegrees, string CapturePurpose, string FutureSscScriptRole, bool Required);
public sealed record OverlayExecutionDirective(string SceneCode, string OverlayType, string OverlayText, int StartSecond, int EndSecond, int ZIndex, string Animation, string SafeArea, string TypographyRole, int Priority, string DirectiveId = "", bool Required = true);
public sealed record MotionExecutionDirective(string SceneCode, string CameraBehavior, string MotionStyle, double ZoomStart, double ZoomEnd, string PanDirection, bool ParallaxEnabled, string EmotionalPurpose, string DirectiveId = "");
public sealed record TransitionExecutionDirective(string FromSceneCode, string ToSceneCode, string TransitionType, int StartSecond, int DurationSeconds, string EmotionalPurpose, string DirectiveId = "");
public sealed record RendererExecutionContract(string ContractId, string SceneCode, string RendererType, string SelectedSourceType, IReadOnlyList<string> RequiredInputs, IReadOnlyList<string> ExpectedOutputs, string MotionDirectiveCode, IReadOnlyList<string> OverlayDirectiveCodes, IReadOnlyList<string> TransitionDirectiveCodes, string FallbackPolicy, int RenderPriority, bool RendererDecisionLocked);
public sealed record ThumbnailExecutionContract(string RendererType, string VisualSourceType, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string FocalHierarchy, string EyeFlowDirection, string EmotionalFocus, string OverlaySafeArea, string MobileSafeFraming, string ShortsCropStrategy, IReadOnlyList<string> RequiredAssets, string FallbackPolicy, string OutputRole);
public sealed record CinematicCameraIntent(string FramingRule, double SuggestedFovMultiplier, string VerticalBias, string MotionIntent, bool PreserveHorizon);
public sealed record CinematicSceneDirection(string SceneCode, string SourceSceneCode, string CinematicStyle, string EmotionalTone, string SceneMood, int VisualPriority, string PrimarySubject, IReadOnlyList<string> SecondarySubjects, bool PreserveConstellationContext, bool PreserveHorizon, bool AllowNegativeSpace, double SkyWeight, double HorizonWeight, string FramingRule, double SuggestedFovMultiplier, string VerticalBias, string MotionIntent, IReadOnlyList<string> CompositionHints, IReadOnlyList<string> SafetyWarnings);
public sealed record CinematicDirectorResponse(IReadOnlyList<CinematicSceneDirection> SceneDirections);
public sealed record WeeklyPhase5FoundationStatus(bool IsFrozen, bool IsReadyForPhase6, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings, IReadOnlyList<string> VerifiedChecks);
public sealed record RenderPreparationPackage(
    string PreparationId,
    RenderWorkingDirectoryPlan WorkingDirectoryPlan,
    IReadOnlyList<SceneRenderRequest> SceneRenderRequests,
    AssetResolutionPlan AssetResolutionPlan,
    StellariumRenderPlan StellariumRenderPlan,
    OverlayRenderPlan OverlayRenderPlan,
    TimelineRenderPlan TimelineRenderPlan,
    ThumbnailRenderPlan ThumbnailRenderPlan,
    RenderPreparationValidation RenderPreparationValidation,
    RenderPreparationFreezeStatus RenderPreparationFreezeStatus,
    CinematicDirectorResponse? CinematicDirectorResponse = null);
public sealed record RenderWorkingDirectoryPlan(string RootPath, string SceneRendersPath, string AudioPath, string OverlaysPath, string ThumbnailsPath, string TimelinePath, string FinalPath, string MetadataPath, string DebugPath, string StellariumPath, string AssetsPath, string PathConventionVersion, string WorkingDirectorySource);
public sealed record SceneRenderRequest(string RequestId, string SceneCode, string RendererType, string SelectedSourceType, DateOnly TargetDate, DateTime? BestTimeUtc, int DurationSeconds, IReadOnlyList<string> NarrationSegmentCodes, IReadOnlyList<SceneRenderRequestInput> RequiredInputs, IReadOnlyList<SceneRenderExpectedOutput> ExpectedOutputs, IReadOnlyList<string> RequiredAssets, MotionExecutionDirective? MotionDirective, IReadOnlyList<OverlayExecutionDirective> OverlayDirectives, IReadOnlyList<TransitionExecutionDirective> TransitionDirectives, string FallbackPolicy, string OutputPath, string MetadataOutputPath, string DebugOutputPath, int RenderPriority, bool IsThumbnailOnly, bool RendererDecisionLocked, bool IsReuseScene = false, string? ReuseSourceSceneCode = null, CinematicSceneDirection? CinematicDirection = null);
public sealed record CelestialObjectVisualPlan(string SegmentCode, string SceneCode, IReadOnlyList<string> NarrationSegmentCodes, IReadOnlyList<string> RequiredObjects, IReadOnlyList<string> ObjectDisplayNames, IReadOnlyList<string> AssetCandidates, IReadOnlyList<string> SelectedAssets, string VisualLayoutType, bool FallbackUsed, string? FallbackReason, IReadOnlyList<string> Warnings);
public sealed record SceneRenderRequestInput(string InputType, string InputCode, string Description, bool IsRequired);
public sealed record SceneRenderExpectedOutput(string OutputType, string OutputCode, string Description);
public sealed record AssetResolutionPlan(IReadOnlyList<AssetResolutionItem> Items);
public sealed record AssetResolutionItem(string AssetCode, string ObjectCode, string AssetRole, string PreferredAssetType, IReadOnlyList<string> RequiredForSceneCodes, IReadOnlyList<string> CandidateLocalPaths, string FallbackStrategy, bool IsRequired, string ResolutionStatus, int PlannedUsageCount, IReadOnlyList<string> ExpectedRendererTypes);
public sealed record StellariumRenderPlan(IReadOnlyList<StellariumRenderJob> Jobs);
public sealed record StellariumRenderJob(string JobId, string SceneCode, string RequestId, DateOnly TargetDate, DateTime? BestTimeUtc, string RegionId, double Latitude, double Longitude, string Timezone, IReadOnlyList<string> ObjectCodes, string CameraIntent, string OutputResolution, string PlannedSscPath, string PlannedCapturePath, int CaptureDurationSeconds, IReadOnlyList<string> RequiredOverlays, string CaptureType, int RenderPriority, string Status);
public sealed record OverlayRenderPlan(IReadOnlyList<OverlayRenderJob> Jobs);
public sealed record OverlayRenderJob(string JobId, string SceneCode, string OverlayType, string OverlayText, int StartSecond, int EndSecond, int ZIndex, string Animation, string SafeArea, string TypographyRole, string PlannedOverlayPath, string OutputFormat, int RenderPriority, string Status);
public sealed record TimelineRenderPlan(int TotalDurationSeconds, int SegmentCount, int TransitionCount, int OverlapCount, double LongFormCoveragePercent, IReadOnlyList<TimelineRenderSegment> TimelineSegments);
public sealed record TimelineRenderSegment(string SegmentId, string SceneCode, string RequestId, int StartSecond, int EndSecond, int DurationSeconds, IReadOnlyList<string> NarrationSegmentCodes, string TransitionIn, string TransitionOut, int TransitionInSeconds, int TransitionOutSeconds, bool HasOverlap, string OverlapReason, bool IsThumbnailOnly);
public sealed record ThumbnailRenderPlan(string ThumbnailRequestId, string RendererType, string VisualSourceType, IReadOnlyList<string> PrimaryObjects, IReadOnlyList<string> SecondaryObjects, string FocalHierarchy, string EyeFlowDirection, string EmotionalFocus, string OverlaySafeArea, string MobileSafeFraming, string ShortsCropStrategy, IReadOnlyList<string> RequiredAssets, string PlannedOutputPath, string PlannedMetadataPath, string PlannedDebugPath, string Status);
public sealed record RenderPreparationValidation(bool IsValid, bool SceneRequestsGenerated, bool AssetResolutionPlanned, bool StellariumJobsPlanned, bool OverlayJobsPlanned, bool TimelinePlanValid, bool ThumbnailPlanValid, bool WorkingDirectoryPlanValid, bool ReadyForSceneRendering, bool ReadyForRendering, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record RenderPreparationFreezeStatus(bool IsFrozen, bool IsReadyForPhase6B, IReadOnlyList<string> VerifiedChecks, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record SceneRenderingPackage(
    IReadOnlyList<SceneRenderResult> SceneRenderResults,
    IReadOnlyList<StellariumSceneRenderResult> StellariumRenderResults,
    IReadOnlyList<CelestialAssetSceneRenderResult> CelestialAssetRenderResults,
    IReadOnlyList<HybridSceneCompositeResult> HybridCompositeResults,
    IReadOnlyList<OverlayRenderResult> OverlayRenderResults,
    ThumbnailSceneRenderResult? ThumbnailRenderResult,
    SceneRenderingValidation SceneRenderingValidation,
    SceneRenderingFreezeStatus SceneRenderingFreezeStatus);
public sealed record SceneRenderResult(string RequestId, string SceneCode, string RendererType, string OutputPath, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, string? ReusedFromSceneCode = null, string? ReusedFromRequestId = null, string? ReusedOutputPath = null);
public sealed record StellariumSceneRenderResult(string JobId, string SceneCode, string RequestId, string SscPath, string OutputPath, string Status, int DurationSeconds, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record CelestialAssetSceneRenderResult(string SceneCode, string RequestId, IReadOnlyList<string> UsedAssets, string OutputPath, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record HybridSceneCompositeResult(string SceneCode, string RequestId, IReadOnlyList<string> SourceLayers, string OutputPath, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record OverlayRenderResult(string JobId, string SceneCode, string OverlayType, string OutputPath, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record ThumbnailSceneRenderResult(string RequestId, string OutputPath, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record SceneRenderingValidation(bool IsValid, bool AllSceneRequestsProcessed, bool StellariumScenesRendered, bool AssetScenesRendered, bool HybridScenesRendered, bool OverlaysRendered, bool ThumbnailRendered, bool ReadyForTimelineComposition, bool ReadyForPublishing, bool VisualAssetsResolved, bool SceneVisualsContainObjects, bool ThumbnailContainsObjects, bool BlankFrameDetected, bool DiagnosticsFallbackVisualUsed, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record SceneRenderingFreezeStatus(bool IsFrozen, IReadOnlyList<string> VerifiedChecks, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record TimelineCompositionPackage(
    LongFormTimelineResult LongFormTimelineResult,
    IReadOnlyList<SegmentCompositionResult> SegmentCompositionResults,
    IReadOnlyList<TransitionCompositionResult> TransitionCompositionResults,
    NarrationSyncResult NarrationSyncResult,
    AudioCompositionPlan AudioCompositionPlan,
    IReadOnlyList<ShortsCompositionPlan> ShortsCompositionPlans,
    TimelineCompositionValidation TimelineCompositionValidation,
    TimelineCompositionFreezeStatus TimelineCompositionFreezeStatus);
public sealed record LongFormTimelineResult(string OutputPath, int TotalDurationSeconds, string Status, bool ThumbnailExcluded, bool ReuseSceneResolved, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record SegmentCompositionResult(string SegmentId, string SceneCode, string RequestId, string SourceSceneOutputPath, int StartSecond, int EndSecond, int DurationSeconds, IReadOnlyList<string> NarrationSegmentCodes, string TransitionIn, string TransitionOut, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record TransitionCompositionResult(string TransitionId, string FromSceneCode, string ToSceneCode, string TransitionType, int StartSecond, int DurationSeconds, string Status, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record NarrationSyncResult(bool NarrationTrackPlanned, string NarrationSource, int NarrationDurationSeconds, int TargetDurationSeconds, bool AudioRendered, string SyncStatus, IReadOnlyList<NarrationSegmentSync> SegmentSync, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record NarrationSegmentSync(string SegmentCode, int StartSecond, int EndSecond, int TargetDurationSeconds, int NarrationEstimatedDurationSeconds, string Status);
public sealed record AudioCompositionPlan(string NarrationAudioPath, string BackgroundMusicPath, string FinalMixedAudioPath, bool AudioRendered, bool MusicRendered, bool MixRendered, string Status);
public sealed record ShortsCompositionPlan(string ShortCode, string Title, IReadOnlyList<string> SourceSceneCodes, IReadOnlyList<string> SourceNarrationCodes, int TargetDurationSeconds, string AspectRatio, string CropStrategy, string PlannedOutputPath, string Status);
public sealed record TimelineCompositionValidation(bool IsValid, bool LongFormTimelineComposed, int TotalDurationSeconds, int ExpectedDurationSeconds, bool TimelineHasNoGaps, bool TransitionsValid, bool ThumbnailExcluded, bool ReuseSceneResolved, bool NarrationSyncValid, bool SinglePipelineRunIdUsed, bool ReadyForFinalVideoReview, bool ReadyForPublishing, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record TimelineCompositionFreezeStatus(bool IsFrozen, bool IsReadyForPhase6D, IReadOnlyList<string> VerifiedChecks, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings);
public sealed record WeeklyExecutionValidationReport(
    bool OverlaysValidated,
    bool TransitionsValidated,
    bool TimelineValidated,
    bool RendererContractsValidated,
    bool ThumbnailContractsValidated,
    double NarrationTimelineCoveragePercent,
    IReadOnlyList<string> DuplicateSceneReuseIssues,
    IReadOnlyList<string> MissingExecutionFields,
    IReadOnlyList<string> BlockingIssues,
    IReadOnlyList<string> Warnings);
public sealed record WeeklyCinematicChoreographyPackage(
    IReadOnlyList<WeeklyCinematicScene> Scenes,
    IReadOnlyList<WeeklySceneTimeline> SceneTimeline,
    IReadOnlyList<WeeklyOverlayTimeline> OverlayTimeline,
    IReadOnlyList<WeeklyCameraTimeline> CameraTimeline,
    IReadOnlyList<WeeklyTransitionTimeline> TransitionTimeline,
    IReadOnlyList<WeeklyRenderContract> RenderContracts,
    IReadOnlyList<string> ChoreographyWarnings);
public sealed record WeeklyCinematicScene(
    string SceneCode,
    string VisualCode,
    int SceneOrder,
    int DurationSeconds,
    int StartSecond,
    int EndSecond,
    IReadOnlyList<string> NarrationSegmentCodes,
    string VisualSourceType,
    string SceneType,
    string EmotionalTone,
    string HumanTimeWindow,
    DateTime? TechnicalBestTimeUtc,
    bool RequiresStellarium,
    bool RequiresAssets,
    bool RequiresOverlayComposite,
    bool ReuseAllowed);
public sealed record WeeklyCameraTimeline(string SceneCode, int StartSecond, int EndSecond, string CameraBehavior);
public sealed record WeeklyTransitionTimeline(string FromSceneCode, string ToSceneCode, int StartSecond, int EndSecond, string TransitionType);
public interface IWeeklySkyForecastV2AssetResolver
{
    (WeeklySceneChoreographyPackage SceneChoreographyPackage, WeeklyCinematicChoreographyPackage CinematicChoreographyPackage) Resolve(WeeklyNarrationPlan narrationPlan, WeeklyHybridScenePlanPackage hybridScenePlanPackage, WeeklyVisualRequirementPackage visualRequirementPackage, string regionId);
}

public interface IWeeklySkyForecastV2NarrativeAbstractionBuilder
{
    Task<WeeklyNarrativeAbstractionPackage> BuildAsync(
        WeeklyCinematicStoryBlueprint cinematicBlueprint,
        WeeklyEditorialStoryPackage editorialPackage,
        WeeklySkyForecastV2IntelligenceResponse intelligence,
        CancellationToken cancellationToken);
}
public interface IWeeklySkyForecastV2NarrationTextGenerator
{
    Task<WeeklyGeneratedNarrationPackage> GenerateAsync(
        WeeklyNarrationPlan narrationPlan,
        WeeklyNarrativeAbstractionPackage abstractionPackage,
        CancellationToken cancellationToken);
}

public interface IWeeklySkyForecastV2NarrationPlanner
{
    Task<WeeklyNarrationPlan> BuildAsync(
        WeeklyNarrativeAbstractionPackage narrativePackage,
        WeeklyCinematicStoryBlueprint cinematicBlueprint,
        WeeklySkyForecastV2SkyfieldSummary skyfieldSummary,
        string regionId,
        DateOnly weekStartDate,
        string language,
        CancellationToken cancellationToken);
}
public interface IWeeklySkyForecastV2EditorialNormalizer
{
    Task<WeeklyNormalizedEditorialPackage> NormalizeAsync(
        WeeklySkyForecastV2IntelligenceResponse intelligence,
        WeeklyEditorialStoryPackage editorialPackage,
        WeeklyCinematicStoryBlueprint cinematicBlueprint,
        WeeklyNarrativeAbstractionPackage abstractionPackage,
        CancellationToken cancellationToken);
}

public interface IWeeklySkyForecastVisualAssetGenerationService
{
    Task<WeeklySkyForecastVisualAssetsResponse> GenerateAsync(Guid contentGenerationPlanId, WeeklySkyForecastVisualAssetsGenerateRequest request, WeeklySkyForecastProductionRequest? productionRequest, CancellationToken cancellationToken);
}

public sealed record WeeklySkyForecastSegmentVideoRenderRequest(
    bool OverwriteExisting = true,
    bool Diagnostics = true,
    bool EnableFadeInOut = true,
    double FadeDurationSeconds = 0.35d,
    bool EnableZoomPan = true);

public sealed record WeeklySkyForecastSegmentVideoRenderItem(
    string SegmentCode,
    string VideoPath,
    string AudioPath,
    string ScenePath,
    string SubtitlePath,
    double DurationSeconds,
    string Status,
    string? ErrorMessage,
    long RenderTimeMs,
    long FfmpegDurationMs);

public sealed record WeeklySkyForecastSegmentVideoRenderResponse(
    Guid ContentGenerationPlanId,
    bool Success,
    int RenderedSegments,
    int SkippedSegments,
    int FailedSegments,
    string ManifestPath,
    IReadOnlyList<WeeklySkyForecastSegmentVideoRenderItem> Segments,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CategoryProductionStepResult> StepResults);

public interface IWeeklySkyForecastSegmentVideoRenderer
{
    Task<WeeklySkyForecastSegmentVideoRenderResponse> RenderAsync(Guid contentGenerationPlanId, WeeklySkyForecastSegmentVideoRenderRequest request, CancellationToken cancellationToken);
}

public interface ICategoryProductionRunner
{
    Task<CategoryProductionPreviewResponse> RunAsync(CategoryProductionPreviewRequest request, CancellationToken cancellationToken);
}

public interface IManualCategoryPreparationOrchestrator
{
    Task<ManualCategoryPreparationResponse> RunAsync(ManualCategoryPreparationRequest request, CancellationToken cancellationToken);
}

public interface IAnalyticsIngestionService
{
    Task IngestManualAsync(IReadOnlyCollection<Astronomy.MediaFactory.Analytics.AnalyticsIngestionDto> records, CancellationToken cancellationToken);
    Task InitializeForPipelineRunAsync(AnalyticsPipelineInitializationRequest request, CancellationToken cancellationToken);
}

public interface ISafeAnalyticsExecutor
{
    Task<SafeAnalyticsExecutionResult> ExecuteInitializationAsync(AnalyticsPipelineInitializationRequest request, string outputDirectory, CancellationToken cancellationToken);
}

public sealed record SafeAnalyticsExecutionResult(
    bool AnalyticsStarted,
    bool AnalyticsCompleted,
    bool AnalyticsFailed,
    bool ScopeCreated,
    bool DbContextIsolated,
    int QueriesMaterialized,
    string? Exception,
    bool TimedOut);

public sealed record AnalyticsPipelineInitializationRequest(
    Guid PipelineRunId,
    string Language,
    string RegionId,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyCollection<string> Platforms,
    IReadOnlyCollection<string> HookTexts,
    IReadOnlyCollection<AnalyticsThumbnailSeed> Thumbnails,
    string ContentType,
    string? VideoId,
    string? VideoUrl);

public sealed record AnalyticsThumbnailSeed(string ThumbnailPath, string ThumbnailType);

public interface IAIOptimizationPipelineService
{
    Task<AIOptimizationPipelineResult> RunForPipelineAsync(AIOptimizationPipelineRequest request, CancellationToken cancellationToken);
}

public sealed record AIOptimizationPipelineRequest(
    Guid PipelineRunId,
    string OutputDirectory,
    string Language,
    string RegionId,
    DateOnly RunDate,
    string LocationName,
    string? SelectedHook,
    string? SelectedTitle,
    IReadOnlyCollection<string> Objects,
    string? LongThumbnailPath,
    string? ShortThumbnailPath,
    string EventType);

public sealed record AIOptimizationPipelineResult(
    bool Executed,
    int HookRecordsCreated,
    int PublishingRecordsCreated,
    int ThumbnailRecordsCreated,
    string[] Errors);
public interface IPromptFeedbackService
{
    Task<PromptFeedbackContext> BuildContextAsync(PromptFeedbackRequest request, CancellationToken cancellationToken);
}

public interface IFeedbackSignalExtractor
{
    void Extract(AnalyticsAggregationSummary summary, int topN, FeedbackSignalCollector collector);
}


public interface IRuntimeAssetPathResolver
{
    string BaseDirectory { get; }
    string ResolveAssetPath(string relativePath);
    string ResolveFontPath(string relativeFontPath);
    string ResolveCelestialAssetPath(string objectKey, string fileName);
    string GetAssetsRoot();
    string GetFontsRoot();
    string GetCelestialRoot();
    bool AssetExists(string relativePath);
}

public interface IThumbnailStrategyService
{
    ThumbnailPlan BuildPlan(ThumbnailGenerationRequest request);
}

public interface IThumbnailGenerationService
{
    Task<ThumbnailPlan> GenerateAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken);
}

public interface ICinematicThumbnailService : IThumbnailGenerationService
{
}

public interface ICelestialAssetPackExtractor
{
    Task<CelestialAssetPackExtractionReport> ExtractAsync(CancellationToken cancellationToken);
}

public interface IThumbnailCompositionService
{
    Task<string> ComposeAsync(ThumbnailCompositionRequest request, CancellationToken cancellationToken);
}


public interface ICelestialAssetIngestionService
{
    Task<CelestialAssetIngestionReport> RefreshAsync(CancellationToken cancellationToken);
    Task<CelestialObjectIngestionResult> RefreshObjectAsync(string objectKey, CancellationToken cancellationToken);
    Task<CelestialAssetStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<CelestialAssetObjectStatus?> GetObjectAsync(string objectKey, CancellationToken cancellationToken);
}

public interface ICelestialAssetProvider
{
    Task<CelestialAsset> GetAssetAsync(CelestialAssetRequest request, CancellationToken cancellationToken);
}

public interface ICinematicCollageComposer
{
    Task<string> ComposeAsync(CinematicCollageRequest request, CancellationToken cancellationToken);
}


public interface ICinematicThumbnailAiService
{
    Task<CinematicThumbnailAiRecommendation> RecommendAsync(CinematicThumbnailAiRequest request, CancellationToken cancellationToken);
}

public interface IThumbnailVisualHierarchyService
{
    ThumbnailVisualHierarchyResult Evaluate(ThumbnailVisualHierarchyRequest request);
}

public interface IThumbnailMoodGradingService
{
    ThumbnailMoodGradingResult SelectMood(ThumbnailMoodGradingRequest request);
}

public interface IThumbnailCandidateSelector
{
    Task<ThumbnailCandidateSelection> SelectAsync(ThumbnailGenerationRequest request, CancellationToken cancellationToken);
}

public interface IThumbnailScoringService
{
    Task<ThumbnailCandidateScore> ScoreAsync(string candidatePath, ThumbnailScoringContext context, CancellationToken cancellationToken);
}

public sealed class ThumbnailScoringContext
{
    public double MaxBlackPixelPercentage { get; init; } = 0.40;
    public double MinimumBrightnessScore { get; init; } = 0.35;
    public bool RejectDarkFrames { get; init; } = true;
    public bool EnableAstronomySceneMode { get; init; } = true;
    public string? SceneId { get; init; }
    public double TimestampSeconds { get; init; }
}

public interface IThumbnailHookService
{
    string GenerateHook(ThumbnailGenerationRequest request, int maxWords);
}

public interface IThumbnailAiOptimizationService
{
    Task<ThumbnailAiOptimizationResult> OptimizeAsync(ThumbnailAiOptimizationRequest request, CancellationToken cancellationToken);
}

public interface IThumbnailCtrScoringService
{
    ThumbnailHookScore Score(string hook, ThumbnailAiOptimizationRequest request);
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


public interface IContentCategorySettingsService
{
    Task<ContentCategorySettings?> GetSettingsAsync(ContentPipelineType type, CancellationToken cancellationToken = default);
    Task<ContentCategoryPromptSettings?> GetPromptSettingsAsync(ContentPipelineType type, string language, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ContentCategoryPublishingSettings>> GetPublishingSettingsAsync(ContentPipelineType type, CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(ContentPipelineType type, CancellationToken cancellationToken = default);
}

public sealed record ContentPipelineRunRequest(DateOnly Date, string? RegionId = null, string? Language = null, bool? PublishToYouTube = null, bool? UseTopicPlanner = null);
public sealed record ContentPipelineRunResult(ContentPipelineType PipelineType, bool Started, string Message, Guid? PipelineRunId = null);

public interface IContentCategoryPipeline
{
    ContentPipelineType PipelineType { get; }
    Task<ContentPipelineRunResult> RunAsync(ContentPipelineRunRequest request, CancellationToken ct);
}
