using Astronomy.MediaFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.MediaFactory.Infrastructure.Persistence;

public sealed class MediaFactoryDbContext : DbContext
{
    public MediaFactoryDbContext(DbContextOptions<MediaFactoryDbContext> options) : base(options) { }

    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<AstronomyEvent> AstronomyEvents => Set<AstronomyEvent>();
    public DbSet<GeneratedScript> GeneratedScripts => Set<GeneratedScript>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PublishedVideo> PublishedVideos => Set<PublishedVideo>();
    public DbSet<ShortVideo> ShortVideos => Set<ShortVideo>();
    public DbSet<PipelineJob> PipelineJobs => Set<PipelineJob>();
    public DbSet<VideoAnalytics> VideoAnalytics => Set<VideoAnalytics>();
    public DbSet<PipelineStageExecution> PipelineStageExecutions => Set<PipelineStageExecution>();
    public DbSet<RecoveryOperation> RecoveryOperations => Set<RecoveryOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PipelineRun>().ToTable("pipeline_runs").HasKey(x => x.Id);
        modelBuilder.Entity<AstronomyEvent>().ToTable("astronomy_events").HasKey(x => x.Id);
        modelBuilder.Entity<GeneratedScript>().ToTable("generated_scripts").HasKey(x => x.Id);
        modelBuilder.Entity<MediaAsset>().ToTable("media_assets").HasKey(x => x.Id);
        modelBuilder.Entity<PublishedVideo>().ToTable("published_videos").HasKey(x => x.Id);
        modelBuilder.Entity<ShortVideo>().ToTable("short_videos").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineJob>().ToTable("pipeline_jobs").HasKey(x => x.Id);
        modelBuilder.Entity<VideoAnalytics>().ToTable("video_analytics").HasKey(x => x.Id);
        modelBuilder.Entity<PipelineStageExecution>().ToTable("pipeline_stage_executions").HasKey(x => x.Id);
        modelBuilder.Entity<RecoveryOperation>().ToTable("recovery_operations").HasKey(x => x.Id);

        modelBuilder.Entity<PublishedVideo>().HasIndex(x => x.PipelineRunId);
        modelBuilder.Entity<PipelineJob>().HasIndex(x => new { x.Status, x.IsStale, x.ScheduledAt });
        modelBuilder.Entity<RecoveryOperation>().HasIndex(x => new { x.PipelineRunId, x.RequestedAt });
    }
}
