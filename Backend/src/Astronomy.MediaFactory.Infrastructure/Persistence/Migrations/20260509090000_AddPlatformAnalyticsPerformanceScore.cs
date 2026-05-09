using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509090000_AddPlatformAnalyticsPerformanceScore")]
    public partial class AddPlatformAnalyticsPerformanceScore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PerformanceScore",
                table: "platform_content_analytics",
                type: "double precision",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformanceScore",
                table: "platform_content_analytics");
        }
    }
}
