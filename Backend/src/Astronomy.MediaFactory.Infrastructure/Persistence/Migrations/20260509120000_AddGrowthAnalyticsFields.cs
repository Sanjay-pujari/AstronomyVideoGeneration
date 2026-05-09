using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509120000_AddGrowthAnalyticsFields")]
    public partial class AddGrowthAnalyticsFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ctaVariant",
                table: "platform_content_analytics",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "affiliateBlockEnabled",
                table: "platform_content_analytics",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ctaVariant",
                table: "platform_content_analytics");

            migrationBuilder.DropColumn(
                name: "affiliateBlockEnabled",
                table: "platform_content_analytics");
        }
    }
}
