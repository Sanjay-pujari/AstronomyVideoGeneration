using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewEntities_20260520 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_category_prompt_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineType = table.Column<string>(type: "text", nullable: false),
                    ScriptPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    HookPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    ThumbnailTextPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    SeoPromptTemplate = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_category_prompt_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_category_publishing_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineType = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    PrivacyStatus = table.Column<string>(type: "text", nullable: false),
                    PublishTimeWindowStart = table.Column<string>(type: "text", nullable: false),
                    PublishTimeWindowEnd = table.Column<string>(type: "text", nullable: false),
                    HashtagTemplate = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_category_publishing_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_category_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineType = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultLanguage = table.Column<string>(type: "text", nullable: false),
                    DefaultRegionId = table.Column<string>(type: "text", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    TargetDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxObjects = table.Column<int>(type: "integer", nullable: false),
                    GenerateLongVideo = table.Column<bool>(type: "boolean", nullable: false),
                    GenerateShortVideo = table.Column<bool>(type: "boolean", nullable: false),
                    GenerateThumbnail = table.Column<bool>(type: "boolean", nullable: false),
                    PublishToYouTube = table.Column<bool>(type: "boolean", nullable: false),
                    PublishToFacebook = table.Column<bool>(type: "boolean", nullable: false),
                    PublishToInstagram = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_category_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_content_category_prompt_settings_PipelineType_Language",
                table: "content_category_prompt_settings",
                columns: new[] { "PipelineType", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_category_publishing_settings_PipelineType_Platform",
                table: "content_category_publishing_settings",
                columns: new[] { "PipelineType", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_category_settings_PipelineType",
                table: "content_category_settings",
                column: "PipelineType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_category_prompt_settings");

            migrationBuilder.DropTable(
                name: "content_category_publishing_settings");

            migrationBuilder.DropTable(
                name: "content_category_settings");
        }
    }
}
