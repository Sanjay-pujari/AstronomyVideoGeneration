using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core.Common;

namespace Astronomy.MediaFactory.Core;

public sealed class PipelineRun : EntityBase
{
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
