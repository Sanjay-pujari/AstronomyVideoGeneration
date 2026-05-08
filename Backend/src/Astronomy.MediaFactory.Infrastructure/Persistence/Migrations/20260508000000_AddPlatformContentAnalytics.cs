using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformContentAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_content_analytics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    PlatformContentType = table.Column<string>(type: "text", nullable: false),
                    PlatformMediaId = table.Column<string>(type: "text", nullable: false),
                    PlatformUrl = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    PublishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CollectedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: true),
                    Likes = table.Column<long>(type: "bigint", nullable: true),
                    Comments = table.Column<long>(type: "bigint", nullable: true),
                    Shares = table.Column<long>(type: "bigint", nullable: true),
                    Reach = table.Column<long>(type: "bigint", nullable: true),
                    Impressions = table.Column<long>(type: "bigint", nullable: true),
                    WatchTimeMinutes = table.Column<double>(type: "double precision", nullable: true),
                    AverageViewDurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Ctr = table.Column<double>(type: "double precision", nullable: true),
                    EngagementRate = table.Column<double>(type: "double precision", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Hashtags = table.Column<string>(type: "text", nullable: true),
                    LocationName = table.Column<string>(type: "text", nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContentCategory = table.Column<int>(type: "integer", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "text", nullable: true),
                    IsAnalyticsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_content_analytics", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_platform_content_analytics_Platform", table: "platform_content_analytics", column: "Platform");
            migrationBuilder.CreateIndex(name: "IX_platform_content_analytics_PublishedUtc", table: "platform_content_analytics", column: "PublishedUtc");
            migrationBuilder.CreateIndex(name: "IX_platform_content_analytics_PipelineRunId", table: "platform_content_analytics", column: "PipelineRunId");
            migrationBuilder.CreateIndex(name: "IX_platform_content_analytics_Platform_PlatformContentType_PlatformMediaId_CollectedUtc", table: "platform_content_analytics", columns: new[] { "Platform", "PlatformContentType", "PlatformMediaId", "CollectedUtc" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "platform_content_analytics");
        }
    }
}
