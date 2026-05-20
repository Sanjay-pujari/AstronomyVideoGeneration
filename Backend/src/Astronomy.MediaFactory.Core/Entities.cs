using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.Common;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineRun : EntityBase
{
    public void AssignId(Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Pipeline run id cannot be empty.", nameof(id));

        Id = id;
    }

    public DateOnly RunDate { get; set; }
    public ContentType ContentType { get; set; }
    public string? RegionId { get; set; }
    public string LocationName { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string Language { get; set; } = "en";
    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Queued;
    public string? FailureReason { get; set; }
    public bool PublishToYouTube { get; set; }
    public bool UseTopicPlanner { get; set; }
    public string? YouTubeVideoId { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string? OutputFolder { get; set; }
    public bool ResumeSupported { get; set; }
    public string? EventId { get; set; }
    public string? EventType { get; set; }
    public string? EventTitle { get; set; }
    public string? EventDescription { get; set; }
    public string? DecisionType { get; set; }
    public bool InjectedIntoDailyGuide { get; set; }
    public bool SpecialEventGuideGenerated { get; set; }
}

public sealed class PipelineStageExecution : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string StageName { get; set; } = "";
    public string Status { get; set; } = PersistentStageStatuses.Pending;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataJson { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public string? OutputPath { get; set; }
    public string? DiagnosticPath { get; set; }

    public DateTimeOffset? StartedUtc
    {
        get => StartedAt;
        set => StartedAt = value ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset? CompletedUtc
    {
        get => FinishedAt;
        set => FinishedAt = value;
    }

    public string? LastError
    {
        get => ErrorMessage;
        set => ErrorMessage = value;
    }
}

public sealed class AstronomyEvent : EntityBase
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? PeakUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? RegionId { get; set; }
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Timezone { get; set; }
    public bool GlobalVisibility { get; set; }
    public string[] VisibilityRegions { get; set; } = [];
    public string[] RelatedObjects { get; set; } = [];
    public string Source { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public double SourceConfidence { get => ConfidenceScore; set => ConfidenceScore = value; }
    public double RarityScore { get; set; }
    public double VisibilityScore { get; set; }
    public double AudienceInterestScore { get; set; }
    public double TimingUrgencyScore { get; set; }
    public double ContentOpportunityScore { get; set; }
    public string RecommendedContentType { get; set; } = "";
    public string Status { get; set; } = "Discovered";
}

public sealed class AstronomyEventGenerationHistory : EntityBase
{
    public Guid AstronomyEventId { get; set; }
    public Guid PipelineRunId { get; set; }
    public string RegionId { get; set; } = "";
    public DateOnly TargetDate { get; set; }
    public string ContentType { get; set; } = "";
    public string GenerationMode { get; set; } = "";
}

public sealed class GeneratedScript : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public ContentType ContentType { get; set; }
    public DateOnly ScriptDate { get; set; }
    public string Prompt { get; set; } = "";
    public string ScriptBody { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string TagsCsv { get; set; } = "";
    public int EstimatedDurationSeconds { get; set; }
    public string? OptimizedTitle { get; set; }
    public string? AlternateTitlesCsv { get; set; }
    public string? OptimizedDescription { get; set; }
    public string? OptimizedTagsCsv { get; set; }
    public string? OptimizedHashtagsCsv { get; set; }
    public string? ThumbnailTextSuggestionsCsv { get; set; }
    public string? HookLine { get; set; }
    public string? PromptFeedbackContextJson { get; set; }
    public string Language { get; set; } = "en";
}

public sealed class MediaAsset : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string AssetType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string? BlobPath { get; set; }
    public string? PublicUrl { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class PublishedVideo : EntityBase
{
    public Guid? PipelineRunId { get; set; }
    public string Title { get; set; } = "";
    public string? YouTubeVideoId { get; set; }
    public string? BlobUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Published";
    public string? OptimizedTitle { get; set; }
    public string? OptimizedDescription { get; set; }
    public string? OptimizedTagsCsv { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool ThumbnailUploadedToYouTube { get; set; }
    public string? EventId { get; set; }
    public string? EventType { get; set; }
    public string? EventTitle { get; set; }
    public Guid? TitleExperimentId { get; set; }
    public Guid? SelectedTitleVariantId { get; set; }
    public Guid? ThumbnailExperimentId { get; set; }
    public Guid? SelectedThumbnailVariantId { get; set; }
    public Guid? CtaExperimentId { get; set; }
    public Guid? SelectedCtaVariantId { get; set; }
}

public sealed class ShortVideo : EntityBase
{
    public Guid ParentVideoId { get; set; }
    public string? YouTubeVideoId { get; set; }
    public int Duration { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlatformPublicationRecord : EntityBase
{
    public Guid ParentShortVideoId { get; set; }
    public ShortFormPlatform Platform { get; set; }
    public string? ExternalPostId { get; set; }
    public string? ExternalUrl { get; set; }
    public PlatformPublicationStatus Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class MonetizationRecord : EntityBase
{
    public Guid? VideoId { get; set; }
    public string? YouTubeVideoId { get; set; }
    public ContentType ContentType { get; set; }
    public string AffiliateLinksJson { get; set; } = "[]";
    public string? LinkTypesCsv { get; set; }
    public string? PinnedCommentText { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PipelineJob : EntityBase
{
    public PipelineJobType JobType { get; set; }
    public Guid? ParentPipelineRunId { get; set; }
    public PipelineJobStatus Status { get; set; } = PipelineJobStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateOnly RunDate { get; set; }
    public ContentType ContentType { get; set; }
    public string? RegionId { get; set; }
    public string LocationName { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string Language { get; set; } = "en";
    public bool PublishToYouTube { get; set; }
    public bool UseTopicPlanner { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public bool IsStale { get; set; }
    public DateTimeOffset? StaleDetectedAt { get; set; }
    public string? RecoveryNotes { get; set; }
}

public sealed class VideoAnalytics : EntityBase
{
    public string VideoId { get; set; } = "";
    public long Views { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public int DurationSeconds { get; set; }
    public double? AverageViewDurationSeconds { get; set; }
    public double? CtrPercent { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    public ContentType ContentType { get; set; }
    public bool IsShort { get; set; }
    public string? ParentVideoId { get; set; }
    public string? Title { get; set; }
    public string? HookLine { get; set; }
    public Guid? PublishedVideoId { get; set; }
    public Guid? TitleExperimentId { get; set; }
    public Guid? TitleVariantId { get; set; }
    public Guid? ThumbnailExperimentId { get; set; }
    public Guid? ThumbnailVariantId { get; set; }
    public Guid? CtaExperimentId { get; set; }
    public Guid? CtaVariantId { get; set; }
    public string? EventId { get; set; }
    public string? EventType { get; set; }
    public string? EventTitle { get; set; }
    public string? DecisionType { get; set; }
    public bool InjectedIntoDailyGuide { get; set; }
    public bool SpecialEventGuideGenerated { get; set; }
}

public sealed class RecoveryOperation : EntityBase
{
    public Guid? PipelineRunId { get; set; }
    public Guid? PipelineJobId { get; set; }
    public RecoveryOperationType OperationType { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RequestedBy { get; set; } = "manual";
    public RecoveryOperationStatus Status { get; set; } = RecoveryOperationStatus.Requested;
    public string? Notes { get; set; }
    public string? ResultSummary { get; set; }
}


public sealed class ContentExperiment : EntityBase
{
    public Guid VideoId { get; set; }
    public ContentExperimentType ExperimentType { get; set; }
    public Guid? SelectedVariantId { get; set; }
    public ContentExperimentStatus Status { get; set; } = ContentExperimentStatus.Running;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ContentVariant> Variants { get; set; } = [];
}

public sealed class ContentVariant : EntityBase
{
    public Guid ContentExperimentId { get; set; }
    public ContentVariantType VariantType { get; set; }
    public string Value { get; set; } = "";
    public long Views { get; set; }
    public double? Ctr { get; set; }
    public double EngagementScore { get; set; }
    public bool IsWinner { get; set; }
}

public sealed class AlertSubscriber : EntityBase
{
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public AlertPreferredChannel PreferredChannel { get; set; } = AlertPreferredChannel.Email;
    public string RegionId { get; set; } = "";
    public string Language { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public AlertPreferences? Preferences { get; set; }
}

public sealed class AlertPreferences : EntityBase
{
    public Guid SubscriberId { get; set; }
    public string[] EventTypes { get; set; } = [];
    public string PreferredAlertTimeLocal { get; set; } = "18:00";
    public double MinimumEventScore { get; set; } = 0.65;
    public bool DailySkyGuideReminderEnabled { get; set; } = true;
    public bool SpecialEventAlertsEnabled { get; set; } = true;
}

public sealed class AlertNotification : EntityBase
{
    public Guid SubscriberId { get; set; }
    public string? EventId { get; set; }
    public string RegionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public AlertNotificationChannel Channel { get; set; } = AlertNotificationChannel.Email;
    public AlertNotificationStatus Status { get; set; } = AlertNotificationStatus.Pending;
    public DateTimeOffset ScheduledUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentUtc { get; set; }
    public string? Error { get; set; }
}

public sealed class HookOptimizationRecord : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public string Hook { get; set; } = "";
    public double CuriosityScore { get; set; }
    public double EmotionalImpactScore { get; set; }
    public double ClarityScore { get; set; }
    public double ClickProbability { get; set; }
    public double FinalScore { get; set; }
    public string RecommendationReason { get; set; } = "";
    public string Language { get; set; } = "en";
}

public sealed class ThumbnailOptimizationRecord : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public int ObjectCount { get; set; }
    public double Brightness { get; set; }
    public int TextLength { get; set; }
    public string Language { get; set; } = "en";
    public double HookIntensity { get; set; }
    public double CompositionScore { get; set; }
}

public sealed class TrendSignalRecord : EntityBase
{
    public DateOnly SignalDate { get; set; }
    public string Topic { get; set; } = "";
    public double Score { get; set; }
    public string Source { get; set; } = "internal";
}

public sealed class PublishingOptimizationRecord : EntityBase
{
    public Guid PipelineRunId { get; set; }
    public DateTimeOffset RecommendedPublishTime { get; set; }
    public string RecommendedHashtagsCsv { get; set; } = "";
    public string RecommendedTagsCsv { get; set; } = "";
    public string RecommendedAudienceType { get; set; } = "";
    public string PlatformPriorityCsv { get; set; } = "";
}


public sealed class ContentCategorySettings : EntityBase
{
    public ContentPipelineType PipelineType { get; set; }
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultRegionId { get; set; } = "india-udaipur";
    public string Frequency { get; set; } = "Daily";
    public int TargetDurationSeconds { get; set; }
    public int MaxDurationSeconds { get; set; }
    public int MaxObjects { get; set; }
    public bool GenerateLongVideo { get; set; }
    public bool GenerateShortVideo { get; set; }
    public bool GenerateThumbnail { get; set; }
    public bool PublishToYouTube { get; set; }
    public bool PublishToFacebook { get; set; }
    public bool PublishToInstagram { get; set; }
    public int Priority { get; set; }
}

public sealed class ContentCategoryPromptSettings : EntityBase
{
    public ContentPipelineType PipelineType { get; set; }
    public string ScriptPromptTemplate { get; set; } = "";
    public string HookPromptTemplate { get; set; } = "";
    public string ThumbnailTextPromptTemplate { get; set; } = "";
    public string SeoPromptTemplate { get; set; } = "";
    public string Language { get; set; } = "en";
}

public sealed class ContentCategoryPublishingSettings : EntityBase
{
    public ContentPipelineType PipelineType { get; set; }
    public string Platform { get; set; } = "YouTube";
    public bool Enabled { get; set; }
    public string ContentType { get; set; } = "Long";
    public string PrivacyStatus { get; set; } = "private";
    public string PublishTimeWindowStart { get; set; } = "18:00";
    public string PublishTimeWindowEnd { get; set; } = "23:00";
    public string HashtagTemplate { get; set; } = "#astronomy #stargazing";
}


public sealed class ContentCategoryMaster : EntityBase
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public bool SupportsLongVideo { get; set; } = true;
    public bool SupportsShortVideo { get; set; } = true;
    public bool SupportsThumbnail { get; set; } = true;
    public bool SupportsPublishing { get; set; } = true;
    public bool SupportsAiOptimization { get; set; } = true;
}

public sealed class HookStyle : EntityBase
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
}

public sealed class ThumbnailStyle : EntityBase
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
}

public sealed class NarrationStyle : EntityBase
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
}

public sealed class CelestialObject : EntityBase
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ScientificName { get; set; }
    public string ObjectType { get; set; } = "";
    public string? Description { get; set; }
    public string? FunFact { get; set; }
    public string? MythologySummary { get; set; }
    public string? BestViewingMonths { get; set; }
    public bool NakedEyeVisible { get; set; }
    public bool BestForPhotography { get; set; }
    public decimal VisibilityPriority { get; set; }
    public decimal PhotogenicScore { get; set; }
    public decimal EducationalScore { get; set; }
    public decimal ViralityScore { get; set; }
    public string? DefaultThumbnailStyleCode { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class AstronomyEventTypeMaster : EntityBase
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public decimal RarityScore { get; set; }
    public decimal ViralityScore { get; set; }
    public decimal EducationalScore { get; set; }
    public decimal MythologyRelevance { get; set; }
    public decimal PhotographyRelevance { get; set; }
    public bool Enabled { get; set; } = true;
}


public sealed class ContentVarietyRule : EntityBase
{
    public string ContentCategoryCode { get; set; } = "";
    public string RuleType { get; set; } = "";
    public string RuleKey { get; set; } = "";
    public int CooldownDays { get; set; }
    public int? MaxUsagePerWeek { get; set; }
    public int? MaxUsagePerMonth { get; set; }
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}

public sealed class ContentIdeaTemplate : EntityBase
{
    public string ContentCategoryCode { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public string TitleTemplate { get; set; } = "";
    public string TopicTemplate { get; set; } = "";
    public string? Description { get; set; }
    public string Language { get; set; } = "en";
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}

public sealed class ContentGenerationPlan : EntityBase
{
    public string ContentCategoryCode { get; set; } = "";
    public Guid? PipelineRunId { get; set; }
    public string? Title { get; set; }
    public string Language { get; set; } = "en";
    public string RegionId { get; set; } = "";
    public DateTimeOffset? ScheduledUtc { get; set; }
    public string Status { get; set; } = "Planned";
    public string? PrimaryCelestialObjectCode { get; set; }
    public string? PrimaryAstronomyEventTypeCode { get; set; }
    public string? HookStyleCode { get; set; }
    public string? NarrationStyleCode { get; set; }
    public string? ThumbnailStyleCode { get; set; }
    public bool GeneratedByAi { get; set; }
    public int Priority { get; set; } = 100;
    public string? PlanningReason { get; set; }
}

public sealed class ContentPipelineExecution : EntityBase
{
    public Guid? ContentGenerationPlanId { get; set; }
    public Guid? PipelineRunId { get; set; }
    public string ContentCategoryCode { get; set; } = "";
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? OutputFolder { get; set; }
    public string? LongVideoPath { get; set; }
    public string? ShortVideoPath { get; set; }
    public string? ThumbnailLongPath { get; set; }
    public string? ThumbnailShortPath { get; set; }
    public bool PublishingCompleted { get; set; }
    public bool AnalyticsInitialized { get; set; }
}

public sealed class ContentCategoryStyleSettings : EntityBase
{
    public string ContentCategoryCode { get; set; } = "";
    public string HookStyleCode { get; set; } = "";
    public string NarrationStyleCode { get; set; } = "";
    public string ThumbnailStyleCode { get; set; } = "";
    public string Language { get; set; } = "en";
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}
