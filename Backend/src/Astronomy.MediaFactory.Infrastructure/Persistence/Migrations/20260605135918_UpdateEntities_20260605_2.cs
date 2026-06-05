using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEntities_20260605_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asset_plan_json",
                table: "content_generation_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "asset_plan_status",
                table: "content_generation_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Planned");

            migrationBuilder.CreateIndex(
                name: "IX_content_generation_plans_asset_plan_status",
                table: "content_generation_plans",
                column: "asset_plan_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_generation_plans_asset_plan_status",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "asset_plan_json",
                table: "content_generation_plans");

            migrationBuilder.DropColumn(
                name: "asset_plan_status",
                table: "content_generation_plans");
        }
    }
}
