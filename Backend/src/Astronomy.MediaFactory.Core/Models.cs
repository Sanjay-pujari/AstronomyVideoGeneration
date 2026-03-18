using Astronomy.MediaFactory.Contracts;
namespace Astronomy.MediaFactory.Core;

public sealed class AstronomyContext
{
    public DateOnly Date { get; init; }
    public string LocationName { get; init; } = "";
    public string TimeZone { get; init; } = "Asia/Kolkata";
    public List<AstronomyEventModel> Events { get; init; } = new();
    public List<NewsItemModel> NewsItems { get; init; } = new();
    public List<VisualIdeaModel> VisualIdeas { get; init; } = new();
    public TopicSelectionPlan? TopicSelectionPlan { get; set; }
    public PromptFeedbackContext? PromptFeedbackContext { get; set; }
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
}

public sealed class ShortScriptResult
{
    public string Hook { get; init; } = "";
    public string ShortScript { get; init; } = "";
    public string Title { get; init; } = "";
    public string[] Tags { get; init; } = ["shorts", "astronomy"];
    public int EstimatedDurationSeconds { get; init; }
    public OptimizedVideoMetadata? OptimizedMetadata { get; init; }
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
    public ThumbnailLayoutType LayoutType { get; init; } = ThumbnailLayoutType.CenteredTitleOverlay;
    public IReadOnlyCollection<ThumbnailLayoutType> LayoutCandidates { get; init; } = [ThumbnailLayoutType.CenteredTitleOverlay];
    public IReadOnlyCollection<ThumbnailVariantOption> Variants { get; init; } = [];
}

public sealed class ThumbnailGenerationRequest
{
    public required ContentType ContentType { get; init; }
    public required AstronomyContext Context { get; init; }
    public required OptimizedVideoMetadata Metadata { get; init; }
    public required IReadOnlyCollection<string> AvailableVisuals { get; init; }
    public required string OutputDirectory { get; init; }
    public bool IsShortForm { get; init; }
    public FeedbackSignals? FeedbackSignals { get; init; }
}

public sealed class RenderManifest
{
    public string Title { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string? IntroVisualPath { get; set; }
    public string? OutroVisualPath { get; set; }
    public int? OutputWidth { get; set; }
    public int? OutputHeight { get; set; }
    public bool EnableVerticalCrop { get; set; }
    public List<RenderScene> Scenes { get; set; } = new();
}
public sealed class RenderScene
{
    public string Caption { get; set; } = "";
    public string VisualPath { get; set; } = "";
    public int DurationSeconds { get; set; }
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

public sealed class ShortVideoRenderResult
{
    public required ShortScriptResult Script { get; init; }
    public required string AudioPath { get; init; }
    public required string VideoPath { get; init; }
    public string? BlobUrl { get; init; }
    public string? YouTubeVideoId { get; init; }
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
}
