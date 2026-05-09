using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509110000_AddLocalizationFields")]
    public partial class AddLocalizationFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "pipeline_runs",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "generated_scripts",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_content_analytics_language",
                table: "platform_content_analytics",
                column: "language");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_platform_content_analytics_language",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "language",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "language",
                table: "generated_scripts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "platform_content_analytics");
        }
    }
}
