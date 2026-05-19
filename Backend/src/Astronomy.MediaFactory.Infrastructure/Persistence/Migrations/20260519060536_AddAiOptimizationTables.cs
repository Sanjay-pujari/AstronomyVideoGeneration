using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiOptimizationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hook_optimization_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hook = table.Column<string>(type: "text", nullable: false),
                    CuriosityScore = table.Column<double>(type: "double precision", nullable: false),
                    EmotionalImpactScore = table.Column<double>(type: "double precision", nullable: false),
                    ClarityScore = table.Column<double>(type: "double precision", nullable: false),
                    ClickProbability = table.Column<double>(type: "double precision", nullable: false),
                    FinalScore = table.Column<double>(type: "double precision", nullable: false),
                    RecommendationReason = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hook_optimization_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "publishing_optimization_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedPublishTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecommendedHashtagsCsv = table.Column<string>(type: "text", nullable: false),
                    RecommendedTagsCsv = table.Column<string>(type: "text", nullable: false),
                    RecommendedAudienceType = table.Column<string>(type: "text", nullable: false),
                    PlatformPriorityCsv = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publishing_optimization_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "thumbnail_optimization_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectCount = table.Column<int>(type: "integer", nullable: false),
                    Brightness = table.Column<double>(type: "double precision", nullable: false),
                    TextLength = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    HookIntensity = table.Column<double>(type: "double precision", nullable: false),
                    CompositionScore = table.Column<double>(type: "double precision", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thumbnail_optimization_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trend_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SignalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trend_signals", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hook_optimization_results");

            migrationBuilder.DropTable(
                name: "publishing_optimization_results");

            migrationBuilder.DropTable(
                name: "thumbnail_optimization_results");

            migrationBuilder.DropTable(
                name: "trend_signals");
        }
    }
}
