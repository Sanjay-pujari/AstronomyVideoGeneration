using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astronomy.MediaFactory.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(MediaFactoryDbContext))]
    [Migration("20260509130000_AddPipelineRunEventMetadata")]
    public partial class AddPipelineRunEventMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventId" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventType" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventTitle" text;
                ALTER TABLE pipeline_runs ADD COLUMN IF NOT EXISTS "eventDescription" text;

                CREATE INDEX IF NOT EXISTS "IX_pipeline_runs_eventId_RunDate_regionId"
                    ON pipeline_runs ("eventId", "RunDate", "regionId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_pipeline_runs_eventId_RunDate_regionId";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventDescription";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventTitle";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventType";
                ALTER TABLE pipeline_runs DROP COLUMN IF EXISTS "eventId";
                """);
        }
    }
}
