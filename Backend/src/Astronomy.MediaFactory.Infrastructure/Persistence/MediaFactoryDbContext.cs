using System.Text.Json;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class MediaFactoryDbContext : DbContext
{
    public MediaFactoryDbContext(DbContextOptions<MediaFactoryDbContext> options) : base(options) { }

    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<GeneratedScript> GeneratedScripts => Set<GeneratedScript>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PublishedVideo> PublishedVideos => Set<PublishedVideo>();
    public DbSet<ShortVideo> ShortVideos => Set<ShortVideo>();
    public DbSet<MonetizationRecord> MonetizationRecords => Set<MonetizationRecord>();
    public DbSet<PlatformPublicationRecord> PlatformPublicationRecords => Set<PlatformPublicationRecord>();
    public DbSet<PipelineJob> PipelineJobs => Set<PipelineJob>();
    public DbSet<VideoAnalytics> VideoAnalytics => Set<VideoAnalytics>();
    public DbSet<PlatformContentAnalytics> PlatformContentAnalytics => Set<PlatformContentAnalytics>();
    public DbSet<PipelineStageExecution> PipelineStageExecutions => Set<PipelineStageExecution>();
    public DbSet<RecoveryOperation> RecoveryOperations => Set<RecoveryOperation>();
    public DbSet<ContentExperiment> ContentExperiments => Set<ContentExperiment>();
    public DbSet<ContentVariant> ContentVariants => Set<ContentVariant>();
    public DbSet<AlertSubscriber> AlertSubscribers => Set<AlertSubscriber>();
    public DbSet<AlertPreferences> AlertPreferences => Set<AlertPreferences>();
    public DbSet<AlertNotification> AlertNotifications => Set<AlertNotification>();
    public DbSet<AstronomyEvent> AstronomyEvents => Set<AstronomyEvent>();
    public DbSet<AstronomyEventGenerationHistory> AstronomyEventGenerationHistory => Set<AstronomyEventGenerationHistory>();
    public DbSet<HookOptimizationRecord> HookOptimizationResults => Set<HookOptimizationRecord>();
    public DbSet<ThumbnailOptimizationRecord> ThumbnailOptimizationResults => Set<ThumbnailOptimizationRecord>();
    public DbSet<TrendSignalRecord> TrendSignals => Set<TrendSignalRecord>();
    public DbSet<PublishingOptimizationRecord> PublishingOptimizationResults => Set<PublishingOptimizationRecord>();
    public DbSet<PlatformVideoAnalytics> PlatformVideoAnalytics => Set<PlatformVideoAnalytics>();
    public DbSet<PlatformPostAnalytics> PlatformPostAnalytics => Set<PlatformPostAnalytics>();
    public DbSet<AudienceAnalytics> AudienceAnalytics => Set<AudienceAnalytics>();
    public DbSet<ThumbnailPerformance> ThumbnailPerformance => Set<ThumbnailPerformance>();
    public DbSet<HookPerformance> HookPerformance => Set<HookPerformance>();
    public DbSet<DailyPerformanceSummary> DailyPerformanceSummary => Set<DailyPerformanceSummary>();
    public DbSet<ContentCategoryPublishingSettings> ContentCategoryPublishingSettings => Set<ContentCategoryPublishingSettings>();
    public DbSet<ContentCategoryPromptSettings> ContentCategoryPromptSettings => Set<ContentCategoryPromptSettings>();
    public DbSet<ContentCategorySettings> ContentCategorySettings => Set<ContentCategorySettings>();
    public DbSet<ContentCategoryMaster> ContentCategories => Set<ContentCategoryMaster>();
    public DbSet<HookStyle> HookStyles => Set<HookStyle>();
    public DbSet<ThumbnailStyle> ThumbnailStyles => Set<ThumbnailStyle>();
    public DbSet<NarrationStyle> NarrationStyles => Set<NarrationStyle>();
    public DbSet<CelestialObject> CelestialObjects => Set<CelestialObject>();
    public DbSet<AstronomyEventTypeMaster> AstronomyEventTypes => Set<AstronomyEventTypeMaster>();
    public DbSet<ContentGenerationPlan> ContentGenerationPlans => Set<ContentGenerationPlan>();
    public DbSet<ContentPipelineExecution> ContentPipelineExecutions => Set<ContentPipelineExecution>();
    public DbSet<ContentCategoryStyleSettings> ContentCategoryStyleSettings => Set<ContentCategoryStyleSettings>();
    public DbSet<ContentVarietyRule> ContentVarietyRules => Set<ContentVarietyRule>();
    public DbSet<ContentIdeaTemplate> ContentIdeaTemplates => Set<ContentIdeaTemplate>();

    private static readonly ValueComparer<string[]> StringArrayValueComparer = new(
        (left, right) => left != null && right != null ? left.SequenceEqual(right) : left == right,
        values => values == null ? 0 : values.Aggregate(0, (hashCode, value) => HashCode.Combine(hashCode, value == null ? 0 : StringComparer.Ordinal.GetHashCode(value))),
        values => values == null ? Array.Empty<string>() : values.ToArray());

    public override int SaveChanges()
    {
        NormalizeUtcDateTimeOffsetFields();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeUtcDateTimeOffsetFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeUtcDateTimeOffsetFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeUtcDateTimeOffsetFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PipelineRun>().ToTable("pipeline_runs").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineRun>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<PipelineRun>().Property(x => x.Language).HasColumnName("language");
        modelBuilder.Entity<PipelineRun>().Property(x => x.OutputFolder).HasColumnName("outputFolder");
        modelBuilder.Entity<PipelineRun>().Property(x => x.ResumeSupported).HasColumnName("resumeSupported");
        modelBuilder.Entity<PipelineRun>().Property(x => x.EventId).HasColumnName("eventId");
        modelBuilder.Entity<PipelineRun>().Property(x => x.EventType).HasColumnName("eventType");
        modelBuilder.Entity<PipelineRun>().Property(x => x.EventTitle).HasColumnName("eventTitle");
        modelBuilder.Entity<PipelineRun>().Property(x => x.EventDescription).HasColumnName("eventDescription");
        modelBuilder.Entity<PipelineRun>().Property(x => x.DecisionType).HasColumnName("decisionType");
        modelBuilder.Entity<PipelineRun>().Property(x => x.InjectedIntoDailyGuide).HasColumnName("injectedIntoDailyGuide");
        modelBuilder.Entity<PipelineRun>().Property(x => x.SpecialEventGuideGenerated).HasColumnName("specialEventGuideGenerated");
        modelBuilder.Entity<GeneratedScript>().ToTable("generated_scripts").HasKey(x => x.Id);
        modelBuilder.Entity<GeneratedScript>().Property(x => x.Language).HasColumnName("language");
        modelBuilder.Entity<MediaAsset>().ToTable("media_assets").HasKey(x => x.Id);
        modelBuilder.Entity<PublishedVideo>().ToTable("published_videos").HasKey(x => x.Id);
        modelBuilder.Entity<ShortVideo>().ToTable("short_videos").HasKey(x => x.Id);
        modelBuilder.Entity<MonetizationRecord>().ToTable("monetization_records").HasKey(x => x.Id);
        modelBuilder.Entity<PlatformPublicationRecord>().ToTable("platform_publication_records").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineJob>().ToTable("pipeline_jobs").HasKey(x => x.Id);
        modelBuilder.Entity<VideoAnalytics>().ToTable("video_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<PlatformContentAnalytics>().ToTable("platform_content_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineStageExecution>().ToTable("pipeline_stage_executions").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineStageExecution>().Ignore(x => x.StartedUtc);
        modelBuilder.Entity<PipelineStageExecution>().Ignore(x => x.CompletedUtc);
        modelBuilder.Entity<PipelineStageExecution>().Ignore(x => x.LastError);
        modelBuilder.Entity<RecoveryOperation>().ToTable("recovery_operations").HasKey(x => x.Id);
        modelBuilder.Entity<ContentExperiment>().ToTable("content_experiments").HasKey(x => x.Id);
        modelBuilder.Entity<ContentVariant>().ToTable("content_variants").HasKey(x => x.Id);
        modelBuilder.Entity<AlertSubscriber>().ToTable("alert_subscribers").HasKey(x => x.Id);
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.Email).HasColumnName("email");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.Phone).HasColumnName("phone");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.PreferredChannel).HasColumnName("preferredChannel").HasConversion<string>();
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.Language).HasColumnName("language");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.IsActive).HasColumnName("isActive");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.CreatedUtc).HasColumnName("createdUtc");
        modelBuilder.Entity<AlertSubscriber>().Property(x => x.UpdatedUtc).HasColumnName("updatedUtc");
        modelBuilder.Entity<AlertPreferences>().ToTable("alert_preferences").HasKey(x => x.Id);
        modelBuilder.Entity<AlertPreferences>().Property(x => x.SubscriberId).HasColumnName("subscriberId");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.EventTypes).HasColumnName("eventTypes");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.PreferredAlertTimeLocal).HasColumnName("preferredAlertTimeLocal");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.MinimumEventScore).HasColumnName("minimumEventScore");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.DailySkyGuideReminderEnabled).HasColumnName("dailySkyGuideReminderEnabled");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.SpecialEventAlertsEnabled).HasColumnName("specialEventAlertsEnabled");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.CreatedUtc).HasColumnName("createdUtc");
        modelBuilder.Entity<AlertPreferences>().Property(x => x.UpdatedUtc).HasColumnName("updatedUtc");
        modelBuilder.Entity<AstronomyEvent>().ToTable("astronomy_events").HasKey(x => x.Id);
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.EventId).HasColumnName("eventId");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.EventType).HasColumnName("eventType");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.Title).HasColumnName("title");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.Description).HasColumnName("description");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.StartUtc).HasColumnName("startUtc");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.PeakUtc).HasColumnName("peakUtc");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.EndUtc).HasColumnName("endUtc");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.TargetDate).HasColumnName("targetDate");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.LocationName).HasColumnName("locationName");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.Timezone).HasColumnName("timezone");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.GlobalVisibility).HasColumnName("globalVisibility");
        modelBuilder.Entity<AstronomyEvent>()
            .Property(x => x.VisibilityRegions)
            .HasColumnName("visibilityRegions")
            .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            .Metadata.SetValueComparer(StringArrayValueComparer);
        modelBuilder.Entity<AstronomyEvent>()
            .Property(x => x.RelatedObjects)
            .HasColumnName("relatedObjects")
            .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            .Metadata.SetValueComparer(StringArrayValueComparer);
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.Source).HasColumnName("source");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.Status).HasColumnName("status");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.ConfidenceScore).HasColumnName("confidenceScore");
        modelBuilder.Entity<AstronomyEvent>().Ignore(x => x.SourceConfidence);
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.RarityScore).HasColumnName("rarityScore");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.VisibilityScore).HasColumnName("visibilityScore");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.AudienceInterestScore).HasColumnName("audienceInterestScore");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.TimingUrgencyScore).HasColumnName("timingUrgencyScore");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.ContentOpportunityScore).HasColumnName("contentOpportunityScore");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.RecommendedContentType).HasColumnName("recommendedContentType");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.CreatedUtc).HasColumnName("createdUtc");
        modelBuilder.Entity<AstronomyEvent>().Property(x => x.UpdatedUtc).HasColumnName("updatedUtc");
        modelBuilder.Entity<AstronomyEvent>().HasIndex(x => x.EventId).IsUnique();
        modelBuilder.Entity<AstronomyEvent>().HasIndex(x => x.TargetDate);
        modelBuilder.Entity<AstronomyEvent>().HasIndex(x => x.RegionId);
        modelBuilder.Entity<AstronomyEvent>().HasIndex(x => x.EventType);
        modelBuilder.Entity<AstronomyEvent>().HasIndex(x => x.ContentOpportunityScore);
        modelBuilder.Entity<AstronomyEventGenerationHistory>().ToTable("astronomy_event_generation_history").HasKey(x => x.Id);
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.AstronomyEventId).HasColumnName("astronomyEventId");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.PipelineRunId).HasColumnName("pipelineRunId");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.TargetDate).HasColumnName("targetDate");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.ContentType).HasColumnName("contentType");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.GenerationMode).HasColumnName("generationMode");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().Property(x => x.CreatedUtc).HasColumnName("createdUtc");
        modelBuilder.Entity<AstronomyEventGenerationHistory>().HasIndex(x => new { x.AstronomyEventId, x.RegionId, x.TargetDate, x.ContentType }).IsUnique();


        modelBuilder.Entity<HookOptimizationRecord>().ToTable("hook_optimization_results").HasKey(x => x.Id);
        modelBuilder.Entity<ThumbnailOptimizationRecord>().ToTable("thumbnail_optimization_results").HasKey(x => x.Id);
        modelBuilder.Entity<TrendSignalRecord>().ToTable("trend_signals").HasKey(x => x.Id);
        modelBuilder.Entity<PublishingOptimizationRecord>().ToTable("publishing_optimization_results").HasKey(x => x.Id);
        modelBuilder.Entity<PlatformVideoAnalytics>().ToTable("platform_video_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<PlatformPostAnalytics>().ToTable("platform_post_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<AudienceAnalytics>().ToTable("audience_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<ThumbnailPerformance>().ToTable("thumbnail_performance").HasKey(x => x.Id);
        modelBuilder.Entity<HookPerformance>().ToTable("hook_performance").HasKey(x => x.Id);
        modelBuilder.Entity<DailyPerformanceSummary>().ToTable("daily_performance_summary").HasKey(x => x.Id);
        modelBuilder.Entity<DailyPerformanceSummary>().Property(x => x.SummaryDate).HasColumnName("summaryDate");


        modelBuilder.Entity<AlertNotification>().ToTable("alert_notifications").HasKey(x => x.Id);
        modelBuilder.Entity<AlertNotification>().Property(x => x.SubscriberId).HasColumnName("subscriberId");
        modelBuilder.Entity<AlertNotification>().Property(x => x.EventId).HasColumnName("eventId");
        modelBuilder.Entity<AlertNotification>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<AlertNotification>().Property(x => x.Title).HasColumnName("title");
        modelBuilder.Entity<AlertNotification>().Property(x => x.Message).HasColumnName("message");
        modelBuilder.Entity<AlertNotification>().Property(x => x.Channel).HasColumnName("channel").HasConversion<string>();
        modelBuilder.Entity<AlertNotification>().Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        modelBuilder.Entity<AlertNotification>().Property(x => x.ScheduledUtc).HasColumnName("scheduledUtc");
        modelBuilder.Entity<AlertNotification>().Property(x => x.SentUtc).HasColumnName("sentUtc");
        modelBuilder.Entity<AlertNotification>().Property(x => x.Error).HasColumnName("error");
        modelBuilder.Entity<AlertNotification>().Property(x => x.CreatedUtc).HasColumnName("createdUtc");
        modelBuilder.Entity<AlertNotification>().Property(x => x.UpdatedUtc).HasColumnName("updatedUtc");

        modelBuilder.Entity<ContentExperiment>()
            .HasMany(x => x.Variants)
            .WithOne()
            .HasForeignKey(x => x.ContentExperimentId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ContentCategorySettings>().ToTable("content_category_settings").HasKey(x => x.Id);
        modelBuilder.Entity<ContentCategorySettings>().Property(x => x.PipelineType).HasConversion<string>();
        modelBuilder.Entity<ContentCategorySettings>().HasIndex(x => x.PipelineType).IsUnique();
        modelBuilder.Entity<ContentCategoryPromptSettings>().ToTable("content_category_prompt_settings").HasKey(x => x.Id);
        modelBuilder.Entity<ContentCategoryPromptSettings>().Property(x => x.PipelineType).HasConversion<string>();
        modelBuilder.Entity<ContentCategoryPromptSettings>().HasIndex(x => new { x.PipelineType, x.Language }).IsUnique();
        modelBuilder.Entity<ContentCategoryPublishingSettings>().ToTable("content_category_publishing_settings").HasKey(x => x.Id);
        modelBuilder.Entity<ContentCategoryPublishingSettings>().Property(x => x.PipelineType).HasConversion<string>();
        modelBuilder.Entity<ContentCategoryPublishingSettings>().HasIndex(x => new { x.PipelineType, x.Platform }).IsUnique();


        modelBuilder.Entity<ContentCategoryMaster>().ToTable("content_categories").HasKey(x => x.Id);
        modelBuilder.Entity<ContentCategoryMaster>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<HookStyle>().ToTable("hook_styles").HasKey(x => x.Id);
        modelBuilder.Entity<HookStyle>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<ThumbnailStyle>().ToTable("thumbnail_styles").HasKey(x => x.Id);
        modelBuilder.Entity<ThumbnailStyle>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<NarrationStyle>().ToTable("narration_styles").HasKey(x => x.Id);
        modelBuilder.Entity<NarrationStyle>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<CelestialObject>().ToTable("celestial_objects").HasKey(x => x.Id);
        modelBuilder.Entity<CelestialObject>().Property(x => x.VisibilityPriority).HasPrecision(5, 2);
        modelBuilder.Entity<CelestialObject>().Property(x => x.PhotogenicScore).HasPrecision(5, 2);
        modelBuilder.Entity<CelestialObject>().Property(x => x.EducationalScore).HasPrecision(5, 2);
        modelBuilder.Entity<CelestialObject>().Property(x => x.ViralityScore).HasPrecision(5, 2);
        modelBuilder.Entity<CelestialObject>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<AstronomyEventTypeMaster>().ToTable("astronomy_event_types").HasKey(x => x.Id);
        modelBuilder.Entity<AstronomyEventTypeMaster>().Property(x => x.RarityScore).HasPrecision(5, 2);
        modelBuilder.Entity<AstronomyEventTypeMaster>().Property(x => x.ViralityScore).HasPrecision(5, 2);
        modelBuilder.Entity<AstronomyEventTypeMaster>().Property(x => x.EducationalScore).HasPrecision(5, 2);
        modelBuilder.Entity<AstronomyEventTypeMaster>().Property(x => x.MythologyRelevance).HasPrecision(5, 2);
        modelBuilder.Entity<AstronomyEventTypeMaster>().Property(x => x.PhotographyRelevance).HasPrecision(5, 2);
        modelBuilder.Entity<AstronomyEventTypeMaster>().HasIndex(x => x.Code).IsUnique();

        modelBuilder.Entity<ContentGenerationPlan>().ToTable("content_generation_plans").HasKey(x => x.Id);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.ContentCategoryCode);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.Language);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.RegionId);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.ScheduledUtc);
        modelBuilder.Entity<ContentGenerationPlan>().HasIndex(x => x.Status);

        modelBuilder.Entity<ContentPipelineExecution>().ToTable("content_pipeline_executions").HasKey(x => x.Id);
        modelBuilder.Entity<ContentPipelineExecution>().HasIndex(x => x.ContentGenerationPlanId);
        modelBuilder.Entity<ContentPipelineExecution>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<ContentPipelineExecution>().HasIndex(x => x.ContentCategoryCode);
        modelBuilder.Entity<ContentPipelineExecution>().HasIndex(x => x.Status);
        modelBuilder.Entity<ContentPipelineExecution>().HasIndex(x => x.StartedUtc);

        modelBuilder.Entity<ContentCategoryStyleSettings>().ToTable("content_category_style_settings").HasKey(x => x.Id);
        modelBuilder.Entity<ContentVarietyRule>().ToTable("content_variety_rules").HasKey(x => x.Id);
        modelBuilder.Entity<ContentVarietyRule>().HasIndex(x => new { x.ContentCategoryCode, x.RuleType, x.RuleKey }).IsUnique();
        modelBuilder.Entity<ContentIdeaTemplate>().ToTable("content_idea_templates").HasKey(x => x.Id);
        modelBuilder.Entity<ContentIdeaTemplate>().HasIndex(x => new { x.ContentCategoryCode, x.TemplateCode, x.Language }).IsUnique();

        var seedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        // Anonymous types: EntityBase uses protected setters, so entity instances cannot be used in HasData.
        modelBuilder.Entity<ContentCategoryMaster>().HasData(
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Code = "DailySkyGuide", DisplayName = "Daily Sky Guide", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Code = "WeeklySkyForecast", DisplayName = "Weekly Sky Forecast", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Code = "RareEventAlert", DisplayName = "Rare Event Alert", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Code = "CosmicStoryShort", DisplayName = "Cosmic Story Short", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000005"), Code = "AstronomyEducation", DisplayName = "Astronomy Education", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000006"), Code = "AstroPhotographyGuide", DisplayName = "Astro Photography Guide", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Code = "MythologySkyStory", DisplayName = "Mythology Sky Story", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("10000000-0000-0000-0000-000000000008"), Code = "MonthlySkyReport", DisplayName = "Monthly Sky Report", Description = (string?)null, Enabled = true, Priority = 100, SupportsLongVideo = true, SupportsShortVideo = true, SupportsThumbnail = true, SupportsPublishing = true, SupportsAiOptimization = true, CreatedUtc = seedUtc, UpdatedUtc = seedUtc });

        modelBuilder.Entity<HookStyle>().HasData(
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000001"), Code = "Curiosity", DisplayName = "Curiosity", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000002"), Code = "Dramatic", DisplayName = "Dramatic", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000003"), Code = "Scientific", DisplayName = "Scientific", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000004"), Code = "Emotional", DisplayName = "Emotional", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000005"), Code = "Educational", DisplayName = "Educational", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000006"), Code = "Mythological", DisplayName = "Mythological", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc },
            new { Id = Guid.Parse("11000000-0000-0000-0000-000000000007"), Code = "FastPaced", DisplayName = "Fast Paced", Description = (string?)null, Enabled = true, Priority = 100, CreatedUtc = seedUtc, UpdatedUtc = seedUtc });

        modelBuilder.Entity<PipelineRun>().HasIndex(x => new { x.RegionId, x.RunDate, x.ContentType });
        modelBuilder.Entity<PipelineRun>().HasIndex(x => new { x.EventId, x.RunDate, x.RegionId });
        modelBuilder.Entity<PublishedVideo>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<PlatformPublicationRecord>().HasIndex(x => new { x.ParentShortVideoId, x.Platform, x.PublishedAt });
        modelBuilder.Entity<PlatformPublicationRecord>().HasIndex(x => new { x.Platform, x.ExternalPostId }).IsUnique();
        modelBuilder.Entity<MonetizationRecord>().HasIndex(x => new { x.VideoId, x.CreatedAt });
        modelBuilder.Entity<PipelineJob>().HasIndex(x => new { x.Status, x.IsStale, x.ScheduledAt });
        modelBuilder.Entity<RecoveryOperation>().HasIndex(x => new { x.PipelineRunId, x.RequestedAt });
        modelBuilder.Entity<ContentExperiment>().HasIndex(x => new { x.VideoId, x.ExperimentType, x.Status });
        modelBuilder.Entity<ContentVariant>().HasIndex(x => new { x.ContentExperimentId, x.IsWinner });
        modelBuilder.Entity<AlertSubscriber>().HasIndex(x => new { x.Email, x.RegionId, x.IsActive });
        modelBuilder.Entity<AlertSubscriber>().HasOne(x => x.Preferences).WithOne().HasForeignKey<AlertPreferences>(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AlertPreferences>().HasIndex(x => x.SubscriberId).IsUnique();
        modelBuilder.Entity<AlertNotification>().HasIndex(x => new { x.SubscriberId, x.EventId, x.RegionId }).IsUnique();
        modelBuilder.Entity<AlertNotification>().HasIndex(x => new { x.Status, x.ScheduledUtc });
        modelBuilder.Entity<VideoAnalytics>().Property(x => x.DecisionType).HasColumnName("decisionType");
        modelBuilder.Entity<VideoAnalytics>().Property(x => x.InjectedIntoDailyGuide).HasColumnName("injectedIntoDailyGuide");
        modelBuilder.Entity<VideoAnalytics>().Property(x => x.SpecialEventGuideGenerated).HasColumnName("specialEventGuideGenerated");
        modelBuilder.Entity<VideoAnalytics>().HasIndex(x => new { x.PublishedVideoId, x.RetrievedAt });
        modelBuilder.Entity<VideoAnalytics>().HasIndex(x => x.EventId);
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.Language).HasColumnName("language");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.CtaVariant).HasColumnName("ctaVariant");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.AffiliateBlockEnabled).HasColumnName("affiliateBlockEnabled");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.EventId).HasColumnName("eventId");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.EventType).HasColumnName("eventType");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.EventTitle).HasColumnName("eventTitle");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.DecisionType).HasColumnName("decisionType");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.InjectedIntoDailyGuide).HasColumnName("injectedIntoDailyGuide");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.SpecialEventGuideGenerated).HasColumnName("specialEventGuideGenerated");
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.RegionId);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.Language);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.Platform);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.PublishedUtc);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => new { x.Platform, x.PlatformContentType, x.PlatformMediaId, x.CollectedUtc }).IsUnique();
        modelBuilder.Entity<PipelineStageExecution>().HasIndex(x => new { x.PipelineRunId, x.StageName, x.CreatedUtc });
    }

    private void NormalizeUtcDateTimeOffsetFields()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties.Where(p =>
                         p.Metadata.Name.EndsWith("Utc", StringComparison.Ordinal) &&
                         (p.Metadata.ClrType == typeof(DateTimeOffset) || p.Metadata.ClrType == typeof(DateTimeOffset?))))
            {
                if (property.CurrentValue is DateTimeOffset value)
                {
                    property.CurrentValue = EnsureUtc(value);
                }
            }
        }
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : value.ToUniversalTime();
}
