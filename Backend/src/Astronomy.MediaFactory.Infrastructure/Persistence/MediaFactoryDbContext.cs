using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

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

        modelBuilder.Entity<ContentExperiment>()
            .HasMany(x => x.Variants)
            .WithOne()
            .HasForeignKey(x => x.ContentExperimentId)
            .OnDelete(DeleteBehavior.Cascade);

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
        modelBuilder.Entity<VideoAnalytics>().HasIndex(x => new { x.PublishedVideoId, x.RetrievedAt });
        modelBuilder.Entity<VideoAnalytics>().HasIndex(x => x.EventId);
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.RegionId).HasColumnName("regionId");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.Language).HasColumnName("language");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.CtaVariant).HasColumnName("ctaVariant");
        modelBuilder.Entity<PlatformContentAnalytics>().Property(x => x.AffiliateBlockEnabled).HasColumnName("affiliateBlockEnabled");
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.RegionId);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.Language);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.Platform);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.PublishedUtc);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<PlatformContentAnalytics>().HasIndex(x => new { x.Platform, x.PlatformContentType, x.PlatformMediaId, x.CollectedUtc }).IsUnique();
        modelBuilder.Entity<PipelineStageExecution>().HasIndex(x => new { x.PipelineRunId, x.StageName, x.CreatedUtc });
    }
}
