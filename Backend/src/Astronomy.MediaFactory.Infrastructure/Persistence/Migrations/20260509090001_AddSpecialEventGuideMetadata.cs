using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509090001_AddSpecialEventGuideMetadata")]
    public partial class AddSpecialEventGuideMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventId" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventType" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventTitle" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventDescription" text;
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventId" text;
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventType" text;
                ALTER TABLE published_videos ADD COLUMN IF NOT EXISTS "EventTitle" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventId" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventType" text;
                ALTER TABLE video_analytics ADD COLUMN IF NOT EXISTS "EventTitle" text;
                CREATE INDEX IF NOT EXISTS "IX_pipeline_runs_eventId_RunDate_regionId" ON pipeline_runs ("eventId", "RunDate", "regionId");
                CREATE INDEX IF NOT EXISTS "IX_video_analytics_EventId" ON video_analytics ("EventId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_video_analytics_EventId";
                DROP INDEX IF EXISTS "IX_pipeline_runs_eventId_RunDate_regionId";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventTitle";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventType";
                ALTER TABLE video_analytics DROP COLUMN IF EXISTS "EventId";
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventTitle";
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventType";
                ALTER TABLE published_videos DROP COLUMN IF EXISTS "EventId";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventDescription";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventTitle";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventType";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventId";
                """);
        }
    }
}
