using Astronomy.MediaFactory.Contracts;
using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Core;

public sealed class AstronomyContext
{
    public DateOnly Date { get; init; }
    public string LocationName { get; init; } = "";
    public string TimeZone { get; init; } = "Asia/Kolkata";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<AstronomyEventModel> Events { get; init; } = new();
    public List<NewsItemModel> NewsItems { get; init; } = new();
    public List<VisualIdeaModel> VisualIdeas { get; init; } = new();
    public TopicSelectionPlan? TopicSelectionPlan { get; set; }
    public PromptFeedbackContext? PromptFeedbackContext { get; set; }
    public List<SceneObservationContext> SceneObservationContexts { get; set; } = new();
    public SpecialEventContext? SpecialEvent { get; set; }
    public LocalizationContext Localization { get; set; } = LocalizationContext.English;
}

public sealed class SpecialEventContext
{
    public string EventId { get; init; } = "";
    public string EventType { get; init; } = "";
    public string EventTitle { get; init; } = "";
    public string EventDescription { get; init; } = "";
    public double ContentOpportunityScore { get; init; }
}

public sealed class AstronomyEventModel
{
    public string Category { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public string VisibilityWindow { get; init; } = "";
    public string Direction { get; init; } = "";
    public string ObservationTool { get; init; } = "";
    public string Details { get; init; } = "";
    public double Score { get; init; }
}
public sealed class NewsItemModel
{
    public string Headline { get; init; } = "";
    public string Summary { get; init; } = "";
    public string SourceName { get; init; } = "";
    public DateOnly PublishedDate { get; init; }
    public string? SourceUrl { get; init; }
}
public sealed class VisualIdeaModel
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? SourcePathOrUrl { get; init; }
}
public sealed class ScriptResult
{
    public string Prompt { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ScriptBody { get; init; } = "";
    public string[] Tags { get; init; } = Array.Empty<string>();
    public int EstimatedDurationSeconds { get; init; }
    public OptimizedVideoMetadata? OptimizedMetadata { get; init; }
    public SceneScriptSections? SceneScriptSections { get; init; }
}

public sealed class SceneScriptSections
{
    public IReadOnlyDictionary<string, string> SectionsBySceneId { get; init; } = new Dictionary<string, string>();

    public bool HasAllSections()
        => SectionsBySceneId.Count > 0 && SectionsBySceneId.Values.All(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class ShortScriptResult
{
    public string Hook { get; init; } = "";
    public string ShortScript { get; init; } = "";
    public string Title { get; init; } = "";
    public string[] Tags { get; init; } = ["shorts", "astronomy"];
    public int EstimatedDurationSeconds { get; init; }
    public OptimizedVideoMetadata? OptimizedMetadata { get; init; }
    public IReadOnlyCollection<SceneNarrationSegment> SceneNarrationSegments { get; init; } = [];
}

public sealed class MetadataOptimizationInput
{
    public required ContentType ContentType { get; init; }
    public required AstronomyContext Context { get; init; }
    public required string SourceTitle { get; init; }
    public required string SourceDescription { get; init; }
    public required IReadOnlyCollection<string> SourceTags { get; init; }
    public string? SourceScript { get; init; }
    public string? SourceHookLine { get; init; }
    public IReadOnlyCollection<string>? FeedbackKeywords { get; init; }
    public PromptFeedbackContext? FeedbackContext { get; init; }
}

public sealed class PromptFeedbackContext
{
    public ContentType ContentType { get; init; }
    public IReadOnlyCollection<string> RecommendedKeywords { get; init; } = [];
    public IReadOnlyCollection<string> AvoidKeywords { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedHookPatterns { get; init; } = [];
    public IReadOnlyCollection<string> AvoidHookPatterns { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedTitlePatterns { get; init; } = [];
    public IReadOnlyCollection<string> AvoidTitlePatterns { get; init; } = [];
    public IReadOnlyCollection<string> RecommendedToneNotes { get; init; } = [];
    public IReadOnlyCollection<string> RecentWinningTopics { get; init; } = [];
    public IReadOnlyCollection<string> RecentOverusedTopics { get; init; } = [];
    public IReadOnlyCollection<string> AvoidObjectEmphasis { get; init; } = [];
    public IReadOnlyCollection<string> ShortsHookSuggestions { get; init; } = [];
    public IReadOnlyCollection<string> MetadataOptimizationHints { get; init; } = [];
    public IReadOnlyCollection<string> ThumbnailStrategyHints { get; init; } = [];
    public string TopicSelectionRationale { get; init; } = "";
    public bool UsedFallbackDefaults { get; init; }
}

public sealed class PromptFeedbackRequest
{
    public required ContentType ContentType { get; init; }
    public TopicSelectionPlan? TopicSelectionPlan { get; init; }
    public bool IsShortForm { get; init; }
}

public sealed class OptimizedVideoMetadata
{
    public string PrimaryTitle { get; init; } = "";
    public string[] AlternateTitles { get; init; } = [];
    public string OptimizedDescription { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public string[] Hashtags { get; init; } = [];
    public string[] ThumbnailTextSuggestions { get; init; } = [];
    public string? HookLine { get; init; }
}

public enum ThumbnailLayoutType
{
    TextLeftVisualRight = 1,
    CenteredTitleOverlay = 2,
    TopBanner = 3
}

public sealed class ThumbnailPlan
{
    public string PrimaryThumbnailText { get; init; } = "";
    public string[] AlternateThumbnailTexts { get; init; } = [];
    public string? SelectedVisualPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public string? LongThumbnailPath { get; init; }
    public string? ShortThumbnailPath { get; init; }
    public IReadOnlyCollection<string> ThumbnailVariantPaths { get; init; } = [];
    public IReadOnlyCollection<ThumbnailCandidateScore> CandidateScores { get; init; } = [];
    public ThumbnailLayoutType LayoutType { get; init; } = ThumbnailLayoutType.CenteredTitleOverlay;
    public IReadOnlyCollection<ThumbnailLayoutType> LayoutCandidates { get; init; } = [ThumbnailLayoutType.CenteredTitleOverlay];
    public IReadOnlyCollection<ThumbnailVariantOption> Variants { get; init; } = [];
    public bool FallbackUsed { get; init; }
    public string Mode { get; init; } = "CinematicComposed";
    public CelestialThumbnailSelection? CelestialSelection { get; init; }
}

public sealed class ThumbnailCandidateScore
{
    public string Path { get; init; } = "";
    public string? SceneId { get; init; }
    public double TimestampSeconds { get; init; }
    public double Score { get; init; }
    public double Brightness { get; init; }
    public double BlackPixelPercentage { get; init; }
    public double Contrast { get; init; }
    public bool ObjectDetected { get; init; }
    public double ObjectVisibility { get; init; }
    public double FocalObjectScore { get; init; }
    public double GlowScore { get; init; }
    public double StarRichnessScore { get; init; }
    public double CompositionBalanceScore { get; init; }
    public double OrganicAtmosphereScore { get; init; }
    public double ProceduralAtmosphereScore { get; init; }
    public double NaturalLightingScore { get; init; }
    public double VisualArtifactPenalty { get; init; }
    public double CompositingVisibilityPenalty { get; init; }
    public double CinematicSubtletyScore { get; init; }
    public double EdgeIntegrationScore { get; init; }
    public double CompositingSeamPenalty { get; init; }
    public double AtmosphereContinuityScore { get; init; }
    public double EnvironmentalDepthScore { get; init; }
    public double SupportObjectDepthScore { get; init; }
    public double CelestialFocalSize { get; init; }
    public double ColorRichness { get; init; }
    public double TextSafeCompositionArea { get; init; }
    public double Sharpness { get; init; }
    public string? RejectionReason { get; init; }
    public bool IsRejected => !string.IsNullOrWhiteSpace(RejectionReason);
}


public sealed class CelestialThumbnailSelection
{
    public string HeroObject { get; init; } = "";
    public IReadOnlyCollection<string> SupportObjects { get; init; } = [];
    public string SelectedHook { get; init; } = "";
    public string SelectedLayout { get; init; } = "";
    public IReadOnlyCollection<CelestialAsset> AssetSources { get; init; } = [];
    public IReadOnlyCollection<object> VisibilityDataUsed { get; init; } = [];
    public bool FallbackUsed { get; init; }
    public bool SpecialEventMode { get; init; }
}

public sealed class CelestialAsset
{
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Category { get; init; } = "milky-way";
    public string LocalPath { get; init; } = "";
    public string Source { get; init; } = "LocalCache";
    public string Title { get; init; } = "";
    public string Copyright { get; init; } = "";
    public string OriginalUrl { get; init; } = "";
    public bool OldAssetIgnoredBecauseHeroExists { get; init; }
    public bool FallbackUsed { get; init; }
    public string BaseDirectory { get; init; } = "";
}


public sealed class CelestialAssetPackExtractionReport
{
    public string GeneratedAtUtc { get; init; } = "";
    public string SourceSheetPath { get; init; } = "";
    public string SourceMapPath { get; init; } = "";
    public int ObjectsProcessed { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int TransparentAssetsGenerated { get; init; }
    public IReadOnlyCollection<CelestialAssetPackExtractionItem> Items { get; init; } = [];

    public bool Enabled { get; init; }
    public string OutputRootPath { get; init; } = "";
    public string BaseDirectory { get; init; } = "";
    [JsonIgnore]
    public IReadOnlyCollection<CelestialAssetPackExtractionItem> Objects => Items;
    public IReadOnlyCollection<string> ExtractedObjects { get; init; } = [];
    public IReadOnlyCollection<string> SkippedObjects { get; init; } = [];
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
    public string ReportPath { get; init; } = "";
}

public sealed class CelestialAssetPackExtractionItem
{
    public string ObjectKey { get; init; } = "";
    public string SourceSheetPath { get; init; } = "";
    public CelestialAssetTileMapEntry? CropBox { get; init; }
    public string OutputPath { get; init; } = "";
    public string TransparentOutputPath { get; init; } = "";
    public bool Success { get; init; }
    public string? Warning { get; init; }
    public bool TransparencyApplied { get; init; }
    public bool BackgroundRemoved { get; init; }
    public int AlphaPixelsRemoved { get; init; }
    public bool AutoTrimApplied { get; init; }
    public CelestialAssetPackImageDimensions FinalDimensions { get; init; } = new();
    public bool LabelRemovalApplied { get; init; }
    public bool BorderRemovalApplied { get; init; }
}

public sealed class CelestialAssetPackImageDimensions
{
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class CelestialAssetTileMapEntry
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class CelestialAssetRequest
{
    public required string ObjectName { get; init; }
    public required string ObjectType { get; init; }
    public bool PreferPortraitSafe { get; init; }
    public bool RefreshCache { get; init; }
}

public sealed class CinematicCollageRequest
{
    public required ThumbnailGenerationRequest GenerationRequest { get; init; }
    public required CelestialThumbnailSelection Selection { get; init; }
    public required string BackgroundPath { get; init; }
    public required string OutputPath { get; init; }
}

public sealed class ThumbnailProductionQualityResult
{
    public bool IsProductionReady { get; init; }
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
    public double QualityScore { get; init; }
    public double FocalObjectScore { get; init; }
    public double TextReadabilityScore { get; init; }
    public double BlackFrameRisk { get; init; }
    public double MobileReadabilityScore { get; init; }
}


public sealed class ThumbnailGenerationRequest
{
    public required ContentType ContentType { get; init; }
    public required AstronomyContext Context { get; init; }
    public required OptimizedVideoMetadata Metadata { get; init; }
    public required IReadOnlyCollection<string> AvailableVisuals { get; init; }
    public required string OutputDirectory { get; init; }
    public bool IsShortForm { get; init; }
    public IReadOnlyCollection<RenderScene> Scenes { get; init; } = [];
    public FeedbackSignals? FeedbackSignals { get; init; }
}


public sealed class ThumbnailCandidateSelection
{
    public required ThumbnailCandidateScore SelectedCandidate { get; init; }
    public required IReadOnlyCollection<ThumbnailCandidateScore> CandidateScores { get; init; }
    public bool FallbackUsed { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = [];
}

public sealed class ThumbnailCompositionRequest
{
    public required ThumbnailGenerationRequest GenerationRequest { get; init; }
    public required ThumbnailCandidateScore SelectedCandidate { get; init; }
    public required string HookText { get; init; }
    public required string OutputPath { get; init; }
}

public sealed class SeoMetadataRequest
{
    public required IReadOnlyCollection<SceneObservationContext> SceneObservationContext { get; init; }
    public required IReadOnlyCollection<string> SelectedVisibleObjects { get; init; }
    public required string LocationName { get; init; }
    public required DateOnly TargetDate { get; init; }
    public required bool IsShortForm { get; init; }
    public IReadOnlyCollection<string> ThumbnailVariants { get; init; } = [];
    public ContentType ContentType { get; init; } = ContentType.DailySkyGuide;
    public string? EventId { get; init; }
    public string? EventType { get; init; }
    public string? EventTitle { get; init; }
    public string? EventDescription { get; init; }
    public string Language { get; init; } = "en";
    public string? RegionId { get; init; }
}

public sealed class SeoMetadataResult
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string TagsCsv { get; init; } = "";
    public string HashtagsCsv { get; init; } = "";
    public string PinnedComment { get; init; } = "";
    public GrowthMetadata? GrowthMetadata { get; init; }
}

public sealed class RenderManifest
{
    public string Title { get; set; } = "";
    public string ScriptBody { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string? IntroVisualPath { get; set; }
    public string? OutroVisualPath { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public bool EnableVerticalCrop { get; set; }
    public VideoRenderProfileKind EncodingProfile { get; set; } = VideoRenderProfileKind.Auto;
    public List<RenderScene> Scenes { get; set; } = new();
}
public sealed class RenderScene
{
    public string Caption { get; set; } = "";
    public string VisualPath { get; set; } = "";
    public int DurationSeconds { get; set; }
    public string? AudioPath { get; set; }
    public string? ObjectName { get; set; }
    public string? ObjectType { get; set; }
    public string? SceneType { get; set; }
    public string? SceneId { get; set; }
    public int? SegmentIndex { get; set; }
    public string? NarrationLanguage { get; set; }
    public string? NarrationText { get; set; }
    public string? DirectionLabel { get; set; }
    public double? AzimuthDegrees { get; set; }
}

public sealed class NarrationSegment
{
    public string Text { get; init; } = "";
    public string AudioPath { get; init; } = "";
    public int DurationSeconds { get; init; }
}

public sealed class SceneNarrationSegment
{
    public string SceneId { get; init; } = "";
    public string SceneTitle { get; init; } = "";
    public string VisualTarget { get; init; } = "";
    public string NarrationText { get; init; } = "";
    public string AudioPath { get; init; } = "";
    public int DurationSeconds { get; init; }
}

public sealed class ContentOpportunity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TitleCandidate { get; init; } = "";
    public ContentType ContentType { get; init; }
    public string EventType { get; init; } = "";
    public string ObjectName { get; init; } = "";
    public DateOnly Date { get; init; }
    public double PriorityScore { get; init; }
    public double ObservabilityScore { get; init; }
    public double TimelinessScore { get; init; }
    public double SignificanceScore { get; init; }
    public double EducationalValueScore { get; init; }
    public double GrowthPotentialScore { get; init; }
    public double DiversityScore { get; init; }
    public string Rationale { get; init; } = "";
    public bool IsShortCandidate { get; init; }
    public bool IsLongFormCandidate { get; init; }
}

public sealed class TopicSelectionRequest
{
    public DateOnly Date { get; init; }
    public string LocationName { get; init; } = "";
    public string TimeZone { get; init; } = "Asia/Kolkata";
    public ContentType? ContentType { get; init; }
    public int MaxCandidates { get; init; } = 8;
}

public sealed class TopicSelectionPlan
{
    public ContentOpportunity? PrimaryLongForm { get; init; }
    public IReadOnlyCollection<ContentOpportunity> ShortsCandidates { get; init; } = [];
    public IReadOnlyCollection<ContentOpportunity> AlternateCandidates { get; init; } = [];
    public IReadOnlyCollection<ContentOpportunity> RankedOpportunities { get; init; } = [];
    public TopicSelectionSchedulingHints SchedulingHints { get; init; } = new();
}

public sealed class TopicSelectionSchedulingHints
{
    public ContentType ContentType { get; init; } = ContentType.DailySkyGuide;
    public string PreferredCronExpression { get; init; } = "";
    public DateTimeOffset SuggestedQueueTimeUtc { get; init; }
    public string Notes { get; init; } = "";
}

public sealed class RankedTopic
{
    public ContentType ContentType { get; init; }
    public string TopicTitle { get; init; } = "";
    public string Summary { get; init; } = "";
    public double Score { get; init; }
}

public sealed class BlobUploadRequest
{
    public required string BasePath { get; init; }
    public required string VideoPath { get; init; }
    public required string AudioPath { get; init; }
    public string? ThumbnailPath { get; init; }
}

public sealed class BlobUploadResult
{
    public string? VideoUrl { get; init; }
    public string? AudioUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
}

public sealed class PublicMediaUploadResult
{
    public bool Success { get; init; }
    public string PublicUrl { get; init; } = string.Empty;
    public string BlobName { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public DateTime ExpiresUtc { get; init; }
}

public sealed class ShortVideoRenderResult
{
    public required ShortScriptResult Script { get; init; }
    public required string AudioPath { get; init; }
    public required string VideoPath { get; init; }
    public string? BlobUrl { get; init; }
    public string? YouTubeVideoId { get; init; }
    public string? ThumbnailPath { get; init; }
    public string PublishStatus { get; init; } = "Draft";
}

public sealed class YouTubeVideoAnalyticsSnapshot
{
    public required string VideoId { get; init; }
    public long Views { get; init; }
    public long Likes { get; init; }
    public long Comments { get; init; }
    public int DurationSeconds { get; init; }
    public double? AverageViewDurationSeconds { get; init; }
    public double? CtrPercent { get; init; }
    public double? EstimatedMinutesWatched { get; init; }
    public long? Impressions { get; init; }
}

public sealed class AnalyticsAggregationSummary
{
    public IReadOnlyCollection<VideoAnalytics> TopVideosByViews { get; init; } = [];
    public IReadOnlyCollection<VideoAnalytics> TopShortsByRetention { get; init; } = [];
    public IReadOnlyCollection<string> BestPerformingTitles { get; init; } = [];
    public IReadOnlyCollection<ContentTypePerformance> BestPerformingContentTypes { get; init; } = [];
}

public sealed class ContentTypePerformance
{
    public ContentType ContentType { get; init; }
    public double AverageViews { get; init; }
    public double AverageRetention { get; init; }
    public int Samples { get; init; }
}

public sealed class FeedbackSignalCollector
{
    private readonly HashSet<string> _keywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hooks = new(StringComparer.OrdinalIgnoreCase);

    public void AddKeyword(string? keyword)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
            _keywords.Add(keyword.Trim());
    }

    public void AddHook(string? hook)
    {
        if (!string.IsNullOrWhiteSpace(hook))
            _hooks.Add(hook.Trim());
    }

    public FeedbackSignals Build(int topN)
    {
        var boundedTopN = Math.Max(topN, 1);
        return new FeedbackSignals
        {
            TopKeywords = _keywords.Take(boundedTopN).ToArray(),
            BestHooks = _hooks.Take(boundedTopN).ToArray()
        };
    }
}

public sealed class FeedbackSignals
{
    public IReadOnlyCollection<string> TopKeywords { get; init; } = [];
    public IReadOnlyCollection<string> BestHooks { get; init; } = [];
}


public enum ContentExperimentType
{
    Title = 1,
    Thumbnail = 2,
    CTA = 3
}

public enum ContentVariantType
{
    TitleText = 1,
    ThumbnailTextAndLayout = 2,
    CallToActionText = 3
}

public enum ContentExperimentStatus
{
    Draft = 1,
    Running = 2,
    Completed = 3,
    Cancelled = 4
}

public sealed class VariantPerformanceMetrics
{
    public long Views { get; init; }
    public double? Ctr { get; init; }
    public double EngagementScore { get; init; }
}

public sealed class ThumbnailVariantOption
{
    public string Text { get; init; } = "";
    public ThumbnailLayoutType LayoutType { get; init; } = ThumbnailLayoutType.CenteredTitleOverlay;
    public string Value => $"{LayoutType}: {Text}";
}

public sealed class ExperimentVariantAssignment
{
    public Guid? TitleExperimentId { get; init; }
    public Guid? TitleVariantId { get; init; }
    public Guid? ThumbnailExperimentId { get; init; }
    public Guid? ThumbnailVariantId { get; init; }
    public Guid? CtaExperimentId { get; init; }
    public Guid? CtaVariantId { get; init; }
}

public sealed class ExperimentFeedbackSnapshot
{
    public IReadOnlyCollection<string> WinningTitlePatterns { get; init; } = [];
    public IReadOnlyCollection<string> WinningHooks { get; init; } = [];
    public IReadOnlyCollection<string> WinningThumbnailPatterns { get; init; } = [];
    public IReadOnlyCollection<string> WinningCallToActions { get; init; } = [];
    public IReadOnlyCollection<ExperimentFeedbackInsight> Insights { get; init; } = [];
}

public sealed class ExperimentFeedbackInsight
{
    public ContentExperimentType ExperimentType { get; init; }
    public string WinningValue { get; init; } = "";
    public string WinningPattern { get; init; } = "";
    public string WinningHook { get; init; } = "";
    public VariantPerformanceMetrics Metrics { get; init; } = new();
}


public sealed class PrePublishValidationRequest
{
    public Guid PipelineRunId { get; init; }
    public ContentType ContentType { get; init; }
    public bool IsShort { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public string FinalVideoPath { get; init; } = string.Empty;
    public IReadOnlyCollection<string> VisualPaths { get; init; } = [];
    public AstronomyContext Context { get; init; } = new();
    public ScriptResult Script { get; init; } = new();
}

public sealed class PrePublishValidationReport
{
    public Guid PipelineRunId { get; init; }
    public ContentType ContentType { get; init; }
    public bool IsShort { get; init; }
    public string FinalVideoPath { get; init; } = string.Empty;
    public bool Passed { get; set; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public DateTimeOffset CheckedAtUtc { get; init; }
}

public sealed class EventContentDecision
{
    public bool HasEvent { get; init; }
    public AstronomyEvent? PrimaryEvent { get; init; }
    public string DecisionType { get; init; } = "None";
    public IReadOnlyList<AstronomyEvent> InjectedEvents { get; init; } = [];
    public IReadOnlyList<AstronomyEvent> SpecialEventCandidates { get; init; } = [];
    public IReadOnlyList<AstronomyEvent> SkippedEvents { get; init; } = [];
    public string Reason { get; init; } = "No event selected.";
}

public sealed class CelestialAssetImageMetadata
{
    public string ObjectKey { get; init; } = "";
    public string Source { get; init; } = "NASA";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string NasaId { get; init; } = "";
    public string OriginalUrl { get; init; } = "";
    public string LocalPath { get; init; } = "";
    public DateTimeOffset DownloadedAtUtc { get; init; }
    public string LicenseNote { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public double QualityScore { get; init; }
}

public sealed class CelestialObjectIngestionResult
{
    public string ObjectKey { get; init; } = "";
    public int ImagesFound { get; init; }
    public int ImagesDownloaded { get; init; }
    public bool SkippedBecauseCached { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = [];
    public string SelectedPrimaryAsset { get; init; } = "";
}

public sealed class CelestialAssetIngestionReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyCollection<CelestialObjectIngestionResult> Objects { get; init; } = [];
}

public sealed class CelestialAssetObjectStatus
{
    public string ObjectKey { get; init; } = "";
    public string Directory { get; init; } = "";
    public int ImagesFound { get; init; }
    public int RequiredImages { get; init; }
    public bool IsSatisfied { get; init; }
    public string SelectedPrimaryAsset { get; init; } = "";
    public IReadOnlyCollection<CelestialAssetImageMetadata> Images { get; init; } = [];
    public IReadOnlyCollection<string> Errors { get; init; } = [];
}

public sealed class CelestialAssetStatusResponse
{
    public bool Enabled { get; init; }
    public string RootPath { get; init; } = "";
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyCollection<CelestialAssetObjectStatus> Objects { get; init; } = [];
}
