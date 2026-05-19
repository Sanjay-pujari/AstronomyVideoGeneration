using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    public partial class AddAnalyticsFoundationTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            void AddCommon(string table)
            {
                migrationBuilder.CreateTable(
                    name: table,
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        createdUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        updatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                        PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                        Platform = table.Column<string>(type: "text", nullable: false),
                        ContentType = table.Column<string>(type: "text", nullable: false),
                        Language = table.Column<string>(type: "text", nullable: false),
                        RegionId = table.Column<string>(type: "text", nullable: false),
                        PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                        Impressions = table.Column<long>(type: "bigint", nullable: false),
                        Views = table.Column<long>(type: "bigint", nullable: false),
                        Ctr = table.Column<double>(type: "double precision", nullable: false),
                        AverageWatchDuration = table.Column<double>(type: "double precision", nullable: false),
                        WatchTimeMinutes = table.Column<double>(type: "double precision", nullable: false),
                        Likes = table.Column<long>(type: "bigint", nullable: false),
                        Comments = table.Column<long>(type: "bigint", nullable: false),
                        Shares = table.Column<long>(type: "bigint", nullable: false),
                        SubscribersGained = table.Column<long>(type: "bigint", nullable: false)
                    },
                    constraints: table => table.PrimaryKey($"PK_{table}", x => x.Id));
            }

            AddCommon("platform_video_analytics");
            AddCommon("platform_post_analytics");
            AddCommon("audience_analytics");
            AddCommon("thumbnail_performance");
            AddCommon("hook_performance");
            migrationBuilder.CreateTable(
                name: "daily_performance_summary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    createdUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    RegionId = table.Column<string>(type: "text", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Views = table.Column<long>(type: "bigint", nullable: false),
                    Ctr = table.Column<double>(type: "double precision", nullable: false),
                    AverageWatchDuration = table.Column<double>(type: "double precision", nullable: false),
                    WatchTimeMinutes = table.Column<double>(type: "double precision", nullable: false),
                    Likes = table.Column<long>(type: "bigint", nullable: false),
                    Comments = table.Column<long>(type: "bigint", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    SubscribersGained = table.Column<long>(type: "bigint", nullable: false),
                    summaryDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_daily_performance_summary", x => x.Id));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "platform_video_analytics");
            migrationBuilder.DropTable(name: "platform_post_analytics");
            migrationBuilder.DropTable(name: "audience_analytics");
            migrationBuilder.DropTable(name: "thumbnail_performance");
            migrationBuilder.DropTable(name: "hook_performance");
            migrationBuilder.DropTable(name: "daily_performance_summary");
        }
    }
}
