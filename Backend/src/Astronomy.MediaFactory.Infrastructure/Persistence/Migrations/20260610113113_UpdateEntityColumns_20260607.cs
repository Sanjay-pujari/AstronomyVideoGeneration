using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntityColumns_20260607 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "requested_output_types_json",
                table: "content_generation_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_external_event_id",
                table: "content_generation_plans",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoGenerateAllowed",
                table: "astronomy_event_intelligences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContentStrategy",
                table: "astronomy_event_intelligences",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalEventId",
                table: "astronomy_event_intelligences",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "astronomy_event_intelligences",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "astronomy_event_intelligences",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "Verified");

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "astronomy_event_intelligences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE astronomy_event_intelligences
                SET "ExternalEventId" = COALESCE(NULLIF("EventCode", ''), "Id"::text),
                    "Year" = EXTRACT(YEAR FROM "StartUtc")::integer,
                    "Language" = 'en',
                    "VerificationStatus" = 'Verified'
                WHERE "ExternalEventId" = '' OR "Year" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_source_external_event_id",
                table: "content_generation_plans",
                column: "source_external_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_AutoGenerateAllowed",
                table: "astronomy_event_intelligences",
                column: "AutoGenerateAllowed");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_ContentStrategy",
                table: "astronomy_event_intelligences",
                column: "ContentStrategy");

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_ExternalEventId_Year_RegionId~",
                table: "astronomy_event_intelligences",
                columns: new[] { "ExternalEventId", "Year", "RegionId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_astronomy_event_intelligences_VerificationStatus",
                table: "astronomy_event_intelligences",
                column: "VerificationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_source_external_event_id",
                table: "content_generation_plans");

            migrationBuilder.DropIndex(
                name: "IX_astronomy_event_intelligences_AutoGenerateAllowed",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropIndex(
                name: "IX_astronomy_event_intelligences_ContentStrategy",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropIndex(
                name: "IX_astronomy_event_intelligences_ExternalEventId_Year_RegionId~",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropIndex(
                name: "IX_astronomy_event_intelligences_VerificationStatus",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "requested_output_types_json",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "source_external_event_id",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "AutoGenerateAllowed",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "ContentStrategy",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "ExternalEventId",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "astronomy_event_intelligences");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "astronomy_event_intelligences");
        }
    }
}
