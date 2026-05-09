using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509100000_AddRegionSchedulingFields")]
    public partial class AddRegionSchedulingFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "regionId",
                table: "pipeline_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "regionId",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_regionId_RunDate_ContentType",
                table: "pipeline_runs",
                columns: new[] { "regionId", "RunDate", "ContentType" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_content_analytics_regionId",
                table: "platform_content_analytics",
                column: "regionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pipeline_runs_regionId_RunDate_ContentType",
                table: "pipeline_runs");

            migrationBuilder.DropIndex(
                name: "IX_platform_content_analytics_regionId",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "regionId",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "regionId",
                table: "platform_content_analytics");
        }
    }
}
