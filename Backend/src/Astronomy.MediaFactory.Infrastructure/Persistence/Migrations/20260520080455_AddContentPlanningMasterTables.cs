using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentPlanningMasterTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "astronomy_event_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RarityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ViralityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EducationalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MythologyRelevance = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PhotographyRelevance = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_astronomy_event_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "celestial_objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ScientificName = table.Column<string>(type: "text", nullable: true),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FunFact = table.Column<string>(type: "text", nullable: true),
                    MythologySummary = table.Column<string>(type: "text", nullable: true),
                    BestViewingMonths = table.Column<string>(type: "text", nullable: true),
                    NakedEyeVisible = table.Column<bool>(type: "boolean", nullable: false),
                    BestForPhotography = table.Column<bool>(type: "boolean", nullable: false),
                    VisibilityPriority = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    PhotogenicScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EducationalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ViralityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DefaultThumbnailStyleCode = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_celestial_objects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SupportsLongVideo = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsShortVideo = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsThumbnail = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsPublishing = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsAiOptimization = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_category_style_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentCategoryCode = table.Column<string>(type: "text", nullable: false),
                    HookStyleCode = table.Column<string>(type: "text", nullable: false),
                    NarrationStyleCode = table.Column<string>(type: "text", nullable: false),
                    ThumbnailStyleCode = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_category_style_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_generation_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentCategoryCode = table.Column<string>(type: "text", nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: false),
                    RegionId = table.Column<string>(type: "text", nullable: false),
                    ScheduledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PrimaryCelestialObjectCode = table.Column<string>(type: "text", nullable: true),
                    PrimaryAstronomyEventTypeCode = table.Column<string>(type: "text", nullable: true),
                    HookStyleCode = table.Column<string>(type: "text", nullable: true),
                    NarrationStyleCode = table.Column<string>(type: "text", nullable: true),
                    ThumbnailStyleCode = table.Column<string>(type: "text", nullable: true),
                    GeneratedByAi = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    PlanningReason = table.Column<string>(type: "text", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_generation_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_pipeline_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentGenerationPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentCategoryCode = table.Column<string>(type: "text", nullable: false),
                    StartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    OutputFolder = table.Column<string>(type: "text", nullable: true),
                    LongVideoPath = table.Column<string>(type: "text", nullable: true),
                    ShortVideoPath = table.Column<string>(type: "text", nullable: true),
                    ThumbnailLongPath = table.Column<string>(type: "text", nullable: true),
                    ThumbnailShortPath = table.Column<string>(type: "text", nullable: true),
                    PublishingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    AnalyticsInitialized = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_pipeline_executions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hook_styles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hook_styles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "narration_styles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narration_styles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "thumbnail_styles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thumbnail_styles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "content_categories",
                columns: new[] { "Id", "Code", "CreatedUtc", "Description", "DisplayName", "Enabled", "Priority", "SupportsAiOptimization", "SupportsLongVideo", "SupportsPublishing", "SupportsShortVideo", "SupportsThumbnail", "UpdatedUtc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "DailySkyGuide", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Daily Sky Guide", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "WeeklySkyForecast", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Weekly Sky Forecast", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "RareEventAlert", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Rare Event Alert", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "CosmicStoryShort", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Cosmic Story Short", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000005"), "AstronomyEducation", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Astronomy Education", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000006"), "AstroPhotographyGuide", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Astro Photography Guide", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000007"), "MythologySkyStory", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Mythology Sky Story", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000008"), "MonthlySkyReport", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Monthly Sky Report", true, 100, true, true, true, true, true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "hook_styles",
                columns: new[] { "Id", "Code", "CreatedUtc", "Description", "DisplayName", "Enabled", "Priority", "UpdatedUtc" },
                values: new object[,]
                {
                    { new Guid("11000000-0000-0000-0000-000000000001"), "Curiosity", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Curiosity", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000002"), "Dramatic", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Dramatic", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000003"), "Scientific", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Scientific", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000004"), "Emotional", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Emotional", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000005"), "Educational", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Educational", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000006"), "Mythological", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Mythological", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("11000000-0000-0000-0000-000000000007"), "FastPaced", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Fast Paced", true, 100, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_types_Code",
                table: "astronomy_event_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_celestial_objects_Code",
                table: "celestial_objects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_categories_Code",
                table: "content_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_ContentCategoryCode",
                table: "content_generation_plans",
                column: "ContentCategoryCode");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_Language",
                table: "content_generation_plans",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_PipelineRunId",
                table: "content_generation_plans",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_RegionId",
                table: "content_generation_plans",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_ScheduledUtc",
                table: "content_generation_plans",
                column: "ScheduledUtc");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_Status",
                table: "content_generation_plans",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_content_pipeline_executions_ContentCategoryCode",
                table: "content_pipeline_executions",
                column: "ContentCategoryCode");

            migrationBuilder.CreateIndex(
                name: "IX_content_pipeline_executions_ContentGenerationPlanId",
                table: "content_pipeline_executions",
                column: "ContentGenerationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_content_pipeline_executions_PipelineRunId",
                table: "content_pipeline_executions",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_content_pipeline_executions_StartedUtc",
                table: "content_pipeline_executions",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_content_pipeline_executions_Status",
                table: "content_pipeline_executions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_hook_styles_Code",
                table: "hook_styles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_narration_styles_Code",
                table: "narration_styles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_thumbnail_styles_Code",
                table: "thumbnail_styles",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "astronomy_event_types");

            migrationBuilder.DropTable(
                name: "celestial_objects");

            migrationBuilder.DropTable(
                name: "content_categories");

            migrationBuilder.DropTable(
                name: "content_category_style_settings");

            migrationBuilder.DropTable(
                name: "content_generation_plans");

            migrationBuilder.DropTable(
                name: "content_pipeline_executions");

            migrationBuilder.DropTable(
                name: "hook_styles");

            migrationBuilder.DropTable(
                name: "narration_styles");

            migrationBuilder.DropTable(
                name: "thumbnail_styles");
        }
    }
}
