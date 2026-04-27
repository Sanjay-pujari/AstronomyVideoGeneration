using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "astronomy_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    ObjectName = table.Column<string>(type: "text", nullable: false),
                    RankScore = table.Column<double>(type: "double precision", nullable: false),
                    VisibilityWindow = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    ObservationTool = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentType = table.Column<int>(type: "integer", nullable: false),
                    SelectedVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "generated_scripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    ScriptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    ScriptBody = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    TagsCsv = table.Column<string>(type: "text", nullable: false),
                    EstimatedDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    OptimizedTitle = table.Column<string>(type: "text", nullable: true),
                    AlternateTitlesCsv = table.Column<string>(type: "text", nullable: true),
                    OptimizedDescription = table.Column<string>(type: "text", nullable: true),
                    OptimizedTagsCsv = table.Column<string>(type: "text", nullable: true),
                    OptimizedHashtagsCsv = table.Column<string>(type: "text", nullable: true),
                    ThumbnailTextSuggestionsCsv = table.Column<string>(type: "text", nullable: true),
                    HookLine = table.Column<string>(type: "text", nullable: true),
                    PromptFeedbackContextJson = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generated_scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    LocalPath = table.Column<string>(type: "text", nullable: false),
                    BlobPath = table.Column<string>(type: "text", nullable: true),
                    PublicUrl = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monetization_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<Guid>(type: "uuid", nullable: true),
                    YouTubeVideoId = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    AffiliateLinksJson = table.Column<string>(type: "text", nullable: false),
                    LinkTypesCsv = table.Column<string>(type: "text", nullable: true),
                    PinnedCommentText = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monetization_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    ParentPipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    PublishToYouTube = table.Column<bool>(type: "boolean", nullable: false),
                    UseTopicPlanner = table.Column<bool>(type: "boolean", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    StaleDetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecoveryNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    LocationName = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    PublishToYouTube = table.Column<bool>(type: "boolean", nullable: false),
                    YouTubeVideoId = table.Column<string>(type: "text", nullable: true),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stage_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stage_executions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "platform_publication_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentShortVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    ExternalPostId = table.Column<string>(type: "text", nullable: true),
                    ExternalUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_publication_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "published_videos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    YouTubeVideoId = table.Column<string>(type: "text", nullable: true),
                    BlobUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OptimizedTitle = table.Column<string>(type: "text", nullable: true),
                    OptimizedDescription = table.Column<string>(type: "text", nullable: true),
                    OptimizedTagsCsv = table.Column<string>(type: "text", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "text", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    ThumbnailUploadedToYouTube = table.Column<bool>(type: "boolean", nullable: false),
                    TitleExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedTitleVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThumbnailExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedThumbnailVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CtaExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedCtaVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_videos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recovery_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PipelineJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ResultSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "short_videos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    YouTubeVideoId = table.Column<string>(type: "text", nullable: true),
                    Duration = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_short_videos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "video_analytics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<string>(type: "text", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    Likes = table.Column<long>(type: "bigint", nullable: false),
                    Comments = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    AverageViewDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    CtrPercent = table.Column<double>(type: "double precision", nullable: true),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    IsShort = table.Column<bool>(type: "boolean", nullable: false),
                    ParentVideoId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    HookLine = table.Column<string>(type: "text", nullable: true),
                    PublishedVideoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TitleExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TitleVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThumbnailExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThumbnailVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CtaExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CtaVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_analytics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentExperimentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    Ctr = table.Column<double>(type: "double precision", nullable: true),
                    EngagementScore = table.Column<double>(type: "double precision", nullable: false),
                    IsWinner = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_variants_content_experiments_ContentExperimentId",
                        column: x => x.ContentExperimentId,
                        principalTable: "content_experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_content_experiments_VideoId_ExperimentType_Status",
                table: "content_experiments",
                columns: new[] { "VideoId", "ExperimentType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_content_variants_ContentExperimentId_IsWinner",
                table: "content_variants",
                columns: new[] { "ContentExperimentId", "IsWinner" });

            migrationBuilder.CreateIndex(
                name: "IX_monetization_records_VideoId_CreatedAt",
                table: "monetization_records",
                columns: new[] { "VideoId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_jobs_Status_IsStale_ScheduledAt",
                table: "pipeline_jobs",
                columns: new[] { "Status", "IsStale", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_publication_records_ParentShortVideoId_Platform_Pu~",
                table: "platform_publication_records",
                columns: new[] { "ParentShortVideoId", "Platform", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_publication_records_Platform_ExternalPostId",
                table: "platform_publication_records",
                columns: new[] { "Platform", "ExternalPostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_videos_PipelineRunId",
                table: "published_videos",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_recovery_operations_PipelineRunId_RequestedAt",
                table: "recovery_operations",
                columns: new[] { "PipelineRunId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_video_analytics_PublishedVideoId_RetrievedAt",
                table: "video_analytics",
                columns: new[] { "PublishedVideoId", "RetrievedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_events");

            migrationBuilder.DropTable(
                name: "content_variants");

            migrationBuilder.DropTable(
                name: "generated_scripts");

            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "monetization_records");

            migrationBuilder.DropTable(
                name: "pipeline_jobs");

            migrationBuilder.DropTable(
                name: "pipeline_runs");

            migrationBuilder.DropTable(
                name: "pipeline_stage_executions");

            migrationBuilder.DropTable(
                name: "platform_publication_records");

            migrationBuilder.DropTable(
                name: "published_videos");

            migrationBuilder.DropTable(
                name: "recovery_operations");

            migrationBuilder.DropTable(
                name: "short_videos");

            migrationBuilder.DropTable(
                name: "video_analytics");

            migrationBuilder.DropTable(
                name: "content_experiments");
        }
    }
}
