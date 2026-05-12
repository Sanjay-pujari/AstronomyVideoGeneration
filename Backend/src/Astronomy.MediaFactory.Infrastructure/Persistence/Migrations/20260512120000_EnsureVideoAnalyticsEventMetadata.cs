using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260512120000_EnsureVideoAnalyticsEventMetadata")]
    public partial class EnsureVideoAnalyticsEventMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventId" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventType" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventTitle" text;
                CREATE INDEX IF NOT EXISTS "IX_video_analytics_EventId" ON video_analytics ("EventId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_video_analytics_EventId";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventTitle";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventType";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventId";
                """);
        }
    }
}
