using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntityColumns_20260605 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "astronomy_content_opportunity_id",
                table: "content_generation_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "astronomy_event_intelligence_id",
                table: "content_generation_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_utc",
                table: "content_generation_plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "content_generation_plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_video_path",
                table: "content_generation_plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_status",
                table: "content_generation_plans",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Planned");

            migrationBuilder.AddColumn<string>(
                name: "planned_format",
                table: "content_generation_plans",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "planned_object_names_json",
                table: "content_generation_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "priority_score",
                table: "content_generation_plans",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_event_object_ids_json",
                table: "content_generation_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_path",
                table: "content_generation_plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_event_object_ids_json",
                table: "astronomy_content_opportunities",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selected_object_names_json",
                table: "astronomy_content_opportunities",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_astronomy_content_opportunity_id",
                table: "content_generation_plans",
                column: "astronomy_content_opportunity_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_astronomy_event_intelligence_id",
                table: "content_generation_plans",
                column: "astronomy_event_intelligence_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_plan_status",
                table: "content_generation_plans",
                column: "plan_status");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_planned_format",
                table: "content_generation_plans",
                column: "planned_format");

            migrationBuilder.AddForeignKey(
                name: "FK_content_generation_plans_astronomy_content_opportunities_as~",
                table: "content_generation_plans",
                column: "astronomy_content_opportunity_id",
                principalTable: "astronomy_content_opportunities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_content_generation_plans_astronomy_event_intelligences_astr~",
                table: "content_generation_plans",
                column: "astronomy_event_intelligence_id",
                principalTable: "astronomy_event_intelligences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_content_generation_plans_astronomy_content_opportunities_as~",
                table: "content_generation_plans");

            migrationBuilder.DropForeignKey(
                name: "FK_content_generation_plans_astronomy_event_intelligences_astr~",
                table: "content_generation_plans");

            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_astronomy_content_opportunity_id",
                table: "content_generation_plans");

            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_astronomy_event_intelligence_id",
                table: "content_generation_plans");

            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_plan_status",
                table: "content_generation_plans");

            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_planned_format",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "astronomy_content_opportunity_id",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "astronomy_event_intelligence_id",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "completed_utc",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "final_video_path",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "plan_status",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "planned_format",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "planned_object_names_json",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "priority_score",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "source_event_object_ids_json",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "thumbnail_path",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "selected_event_object_ids_json",
                table: "astronomy_content_opportunities");

            migrationBuilder.DropColumn(
                name: "selected_object_names_json",
                table: "astronomy_content_opportunities");
        }
    }
}
