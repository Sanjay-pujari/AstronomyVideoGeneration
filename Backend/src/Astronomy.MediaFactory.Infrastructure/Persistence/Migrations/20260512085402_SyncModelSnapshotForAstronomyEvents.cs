using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshotForAstronomyEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "decisionType",
                table: "video_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "injectedIntoDailyGuide",
                table: "video_analytics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "specialEventGuideGenerated",
                table: "video_analytics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "decisionType",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eventId",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eventTitle",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eventType",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "injectedIntoDailyGuide",
                table: "platform_content_analytics",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "specialEventGuideGenerated",
                table: "platform_content_analytics",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decisionType",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "injectedIntoDailyGuide",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "specialEventGuideGenerated",
                table: "pipeline_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "astronomy_event_generation_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    astronomyEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    pipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    regionId = table.Column<string>(type: "text", nullable: false),
                    targetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    contentType = table.Column<string>(type: "text", nullable: false),
                    generationMode = table.Column<string>(type: "text", nullable: false),
                    createdUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_event_generation_history", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "astronomy_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    eventId = table.Column<string>(type: "text", nullable: false),
                    eventType = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    startUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    peakUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    endUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    targetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    regionId = table.Column<string>(type: "text", nullable: true),
                    locationName = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    timezone = table.Column<string>(type: "text", nullable: true),
                    globalVisibility = table.Column<bool>(type: "boolean", nullable: false),
                    visibilityRegions = table.Column<string>(type: "text", nullable: false),
                    relatedObjects = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    confidenceScore = table.Column<double>(type: "double precision", nullable: false),
                    rarityScore = table.Column<double>(type: "double precision", nullable: false),
                    visibilityScore = table.Column<double>(type: "double precision", nullable: false),
                    audienceInterestScore = table.Column<double>(type: "double precision", nullable: false),
                    timingUrgencyScore = table.Column<double>(type: "double precision", nullable: false),
                    contentOpportunityScore = table.Column<double>(type: "double precision", nullable: false),
                    recommendedContentType = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    createdUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_generation_history_astronomyEventId_regionI~",
                table: "astronomy_event_generation_history",
                columns: new[] { "astronomyEventId", "regionId", "targetDate", "contentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_events_contentOpportunityScore",
                table: "astronomy_events",
                column: "contentOpportunityScore");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_events_eventId",
                table: "astronomy_events",
                column: "eventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_events_eventType",
                table: "astronomy_events",
                column: "eventType");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_events_regionId",
                table: "astronomy_events",
                column: "regionId");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_events_targetDate",
                table: "astronomy_events",
                column: "targetDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_event_generation_history");

            migrationBuilder.DropTable(
                name: "astronomy_events");

            migrationBuilder.DropColumn(
                name: "decisionType",
                table: "video_analytics");

            migrationBuilder.DropColumn(
                name: "injectedIntoDailyGuide",
                table: "video_analytics");

            migrationBuilder.DropColumn(
                name: "specialEventGuideGenerated",
                table: "video_analytics");

            migrationBuilder.DropColumn(
                name: "decisionType",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "eventId",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "eventTitle",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "eventType",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "injectedIntoDailyGuide",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "specialEventGuideGenerated",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "decisionType",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "injectedIntoDailyGuide",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "specialEventGuideGenerated",
                table: "pipeline_runs");
        }
    }
}
